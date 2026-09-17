using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CtxTray.Collect
{
    /// <summary>
    /// レートリミット表示値の信頼度。
    ///
    /// 経過時間ではなく「サンプル以降に稼働があったか」で決める。
    /// 実測では、直近 15 分以内に応答があったティックに限れば
    /// 20 分超の古さは 0.0%（中央値 7.7 分）だった。つまり古さは待機時間の影であって、
    /// 作業中の値は信用してよい。ただし fh は最大 2.8 %/分 で伸びるので、
    /// サンプル以降に稼働があれば表示値は「下限」である。
    /// </summary>
    internal enum RateFreshness
    {
        /// <summary>サンプル以降に稼働なし。現在値とみなしてよい。</summary>
        Current,

        /// <summary>サンプル以降に稼働あり。表示値は下限で、実際はこれより上。</summary>
        Behind,

        /// <summary>Desktop 未起動、または 24 時間以上更新なし。参考値。</summary>
        Reference,
    }

    internal sealed class SessionRow
    {
        public string Title;
        public string CliSessionId;
        public string Cwd;
        public string Model;
        public bool ModelKnown;
        public string Effort;

        public int? ContextTokens;
        public int? ContextLimit;
        public double? ContextPct;

        public DateTime? LastMeasuredUtc;
        public DateTime? LastActivityUtc;
        public string TranscriptPath;

        public bool IsActive;
        public bool IsExternal;      // Desktop のタブではない（CLI / VS Code 等）
        public bool ProcessAlive;
        public int Pid;
        public string Entrypoint;
    }

    internal sealed class Snapshot
    {
        public DateTime GeneratedAtUtc;
        public RateLimitStatus RateLimits;
        public RateFreshness Freshness;
        public bool DesktopRunning;

        // どこを読んだか。E-0（MSIX のパスリダイレクト）で嵌まったときに
        // 「空なのか読めていないのか」を切り分けられるよう、必ず出す。
        public string DataRoot;
        public string ConfigDir;

        /// <summary>レートリミットの全サンプル。週間枠のリセット推定に使う。</summary>
        public List<UsageSample> Samples = new List<UsageSample>();
        public List<SessionRow> Sessions = new List<SessionRow>();

        /// <summary>
        /// 常駐表示で隠したセッションの数（Core/SessionFilter.cs）。
        /// HUD の見出しに「ほか N 件は非表示」と出すために持つ。--json / --status では常に 0。
        /// </summary>
        public int HiddenSessionCount;

        public Diagnostics Diag = new Diagnostics();
    }

    internal static class SnapshotBuilder
    {
        /// <summary>
        /// 状態を 1 回分そろえる。
        ///
        /// ★ ここは例外を投げない。
        ///   1 か所の読み取り失敗で全体が落ちると、表示層には「何も来ない」としか
        ///   分からず、HUD が「読み取り中…」のまま固まる（実際に、古い PID が
        ///   権限の高いプロセスに再利用されて Process.HasExited が
        ///   「アクセスが拒否されました」を投げ、この症状を起こした）。
        ///   失敗は diag に残し、読めたところまでを返す。
        /// </summary>
        /// <param name="modelLimits">設定の modelLimits。組み込みの分母表より優先する。</param>
        public static Snapshot Build(bool includeExternal = true, bool includeArchived = false,
                                     IDictionary<string, int> modelLimits = null)
        {
            var snap = new Snapshot { GeneratedAtUtc = DateTime.UtcNow };
            try
            {
                Collect(snap, includeExternal, includeArchived, modelLimits);
            }
            catch (Exception ex)
            {
                snap.Diag.Add(ex.Message);
            }
            return snap;
        }

        private static void Collect(Snapshot snap, bool includeExternal, bool includeArchived,
                                    IDictionary<string, int> modelLimits)
        {
            var diag = snap.Diag;

            var dataRoot = Paths.FindClaudeDataRoot(diag);
            var configDir = Paths.FindClaudeCodeConfigDir(diag);
            snap.DataRoot = dataRoot;
            snap.ConfigDir = configDir;

            var samples = RateLimits.ReadSamples(dataRoot, diag);
            snap.Samples = samples;
            snap.RateLimits = RateLimits.Current(samples, snap.GeneratedAtUtc, diag);

            var tabs = Sessions.ReadDesktopTabs(dataRoot, includeArchived, diag);
            var procs = Sessions.ReadProcesses(configDir, diag);

            // sessionId -> 生きているプロセス（同じ会話に 2 つあれば Desktop 側）
            var aliveBySession = Sessions.AliveBySession(procs);

            var latestTranscriptWriteUtc = DateTime.MinValue;

            // --- Desktop のタブ -------------------------------------------------
            foreach (var tab in tabs)
            {
                var path = Transcript.Find(configDir, tab.Cwd, tab.CliSessionId);

                // transcript が実在しないものは短命な幽霊セッション。表示しない
                // （docs/how-it-works.md の 3 節）。
                if (path == null) continue;

                TrackWriteTime(path, ref latestTranscriptWriteUtc);

                var row = BuildRow(path, tab.Title, tab.CliSessionId, tab.Cwd, tab.Model, tab.Effort, modelLimits);
                row.LastActivityUtc = tab.LastActivityMs > 0
                    ? RateLimits.FromUnixMs(tab.LastActivityMs)
                    : row.LastMeasuredUtc;

                LiveProcess proc;
                if (aliveBySession.TryGetValue(tab.CliSessionId, out proc))
                {
                    row.ProcessAlive = true;
                    row.Pid = proc.Pid;
                    row.Entrypoint = proc.Entrypoint;
                }

                snap.Sessions.Add(row);
            }

            // --- Desktop 以外で走っているセッション -------------------------------
            if (includeExternal)
            {
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in snap.Sessions) known.Add(r.CliSessionId);

                foreach (var p in procs)
                {
                    if (!p.Alive || p.IsDesktop) continue;
                    if (string.IsNullOrEmpty(p.SessionId) || known.Contains(p.SessionId)) continue;

                    var path = Transcript.Find(configDir, p.Cwd, p.SessionId);
                    if (path == null) continue;

                    TrackWriteTime(path, ref latestTranscriptWriteUtc);

                    var title = !string.IsNullOrEmpty(p.Name) ? p.Name : LeafOf(p.Cwd);
                    var row = BuildRow(path, title, p.SessionId, p.Cwd, null, null, modelLimits);
                    row.IsExternal = true;
                    row.ProcessAlive = true;
                    row.Pid = p.Pid;
                    row.Entrypoint = p.Entrypoint;
                    row.LastActivityUtc = row.LastMeasuredUtc
                        ?? (p.StartedAtMs > 0 ? RateLimits.FromUnixMs(p.StartedAtMs) : (DateTime?)null);

                    snap.Sessions.Add(row);
                    known.Add(p.SessionId);
                }
            }

            // 直近に動いたものをアクティブ扱いにする。
            snap.Sessions.Sort((a, b) =>
                Nullable.Compare(b.LastActivityUtc, a.LastActivityUtc));
            if (snap.Sessions.Count > 0) snap.Sessions[0].IsActive = true;

            snap.DesktopRunning = IsDesktopRunning();
            snap.Freshness = JudgeFreshness(snap, latestTranscriptWriteUtc);
        }

        private static SessionRow BuildRow(string transcriptPath, string title, string sessionId,
                                           string cwd, string tabModel, string effort,
                                           IDictionary<string, int> modelLimits)
        {
            var usage = Transcript.ReadLatest(transcriptPath);

            // transcript に書かれたモデルを優先する。タブ登録側は切り替え直後にずれうる。
            var model = (usage != null && !string.IsNullOrEmpty(usage.Model)) ? usage.Model : tabModel;
            var limit = ModelLimits.Lookup(model, modelLimits);

            var row = new SessionRow
            {
                Title = title,
                CliSessionId = sessionId,
                Cwd = cwd,
                Model = model,
                ModelKnown = limit.HasValue,
                Effort = effort,
                TranscriptPath = transcriptPath,
                ContextLimit = limit,
            };

            if (usage != null)
            {
                row.ContextTokens = usage.PromptTokens;
                row.LastMeasuredUtc = usage.AtUtc == DateTime.MinValue ? (DateTime?)null : usage.AtUtc;

                // 分母が分からないモデルでは % を出さない。
                if (limit.HasValue && limit.Value > 0)
                    row.ContextPct = Math.Round(100.0 * usage.PromptTokens / limit.Value, 1);
            }

            return row;
        }

        private static void TrackWriteTime(string path, ref DateTime latest)
        {
            try
            {
                var t = File.GetLastWriteTimeUtc(path);
                if (t > latest) latest = t;
            }
            catch { }
        }

        private static RateFreshness JudgeFreshness(Snapshot snap, DateTime latestTranscriptWriteUtc)
        {
            if (snap.RateLimits == null) return RateFreshness.Reference;

            if (!snap.DesktopRunning) return RateFreshness.Reference;
            if (snap.RateLimits.StalenessSec > 24 * 3600) return RateFreshness.Reference;

            // サンプル以降に transcript が書かれていれば、その分だけ値は遅れている。
            if (latestTranscriptWriteUtc > snap.RateLimits.SampledAtUtc)
                return RateFreshness.Behind;

            return RateFreshness.Current;
        }

        /// <summary>
        /// Claude Desktop が起動しているか。
        ///
        /// ★ プロセス名だけでは決められない。Claude Code 本体も claude.exe という名前で動く
        ///   （Desktop 同梱の %APPDATA%\Claude\claude-code\&lt;版&gt;\claude.exe、
        ///   ネイティブ版の ~\.local\bin\claude.exe）。名前だけで判定すると、
        ///   Desktop を閉じてターミナルで Claude Code を使っている間も「起動中」になり、
        ///   更新の止まったレート枠が参考値（灰色）にならない。
        ///   実行ファイルの製品名が "Claude Code" のもの、パスが claude-code フォルダの下のものを除く。
        ///   パスも製品名も読めないものは、従来どおり Desktop とみなす。
        /// </summary>
        private static bool IsDesktopRunning()
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName("Claude"); }
            catch { return false; }

            var found = false;
            foreach (var p in procs)
            {
                try
                {
                    if (!found && !IsClaudeCodeBinary(p.Id)) found = true;
                }
                catch
                {
                    found = true;
                }
                finally
                {
                    p.Dispose();
                }
            }
            return found;
        }

        // 実行ファイルのパス → Claude Code 本体か。更新のたびに版情報を読み直さないよう覚えておく
        // （同じ exe のプロセスが十数個並ぶ）。パスの種類は数個なので捨てなくてよい。
        private static readonly Dictionary<string, bool> ClaudeCodeByPath =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static bool IsClaudeCodeBinary(int pid)
        {
            var path = Native.NativeMethods.ProcessImagePath(pid);
            if (string.IsNullOrEmpty(path)) return false;

            bool known;
            if (ClaudeCodeByPath.TryGetValue(path, out known)) return known;

            var result = path.IndexOf(@"\claude-code\", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!result)
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    result = string.Equals(info.ProductName, "Claude Code", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    result = false;
                }
            }

            ClaudeCodeByPath[path] = result;
            return result;
        }

        private static string LeafOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return Config.Strings.Get("hud.untitled");
            try { return new DirectoryInfo(path.TrimEnd('\\', '/')).Name; }
            catch { return path; }
        }
    }
}
