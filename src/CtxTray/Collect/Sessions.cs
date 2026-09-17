using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CtxTray.Collect
{
    /// <summary>Claude Desktop で開いている Code タブ 1 つ。</summary>
    internal sealed class DesktopTab
    {
        public string TabId;
        public string CliSessionId;
        public string Cwd;
        public string Model;
        public string Effort;
        public string Title;
        public long LastActivityMs;
        public long CreatedAtMs;
        public bool IsArchived;
    }

    /// <summary>実際に走っている Claude Code のプロセス 1 つ。</summary>
    internal sealed class LiveProcess
    {
        public int Pid;
        public string SessionId;
        public string Cwd;
        public string Entrypoint;   // claude-desktop / それ以外（CLI・VS Code 等）
        public string Kind;
        public string Version;
        public string Name;
        public long StartedAtMs;
        public bool Alive;

        public bool IsDesktop
        {
            get { return string.Equals(Entrypoint, "claude-desktop", StringComparison.OrdinalIgnoreCase); }
        }
    }

    internal static class Sessions
    {
        /// <summary>
        /// Desktop が管理しているタブの一覧。
        ///
        /// %APPDATA%\Claude\claude-code-sessions\&lt;account&gt;\&lt;org&gt;\local_*.json
        /// タブ 1 つにつき 1 ファイル。cliSessionId が transcript のファイル名と一致する。
        ///
        /// タブを閉じるとエントリは isArchived になるのではなく丸ごと消えるので、
        /// この一覧は常に「いま開いているタブ」を表す（実測で確認）。
        /// CLI や VS Code 拡張のセッションはここには載らない。
        /// </summary>
        public static List<DesktopTab> ReadDesktopTabs(string dataRoot, bool includeArchived, Diagnostics diag)
        {
            var result = new List<DesktopTab>();
            if (dataRoot == null) return result;

            var root = Path.Combine(dataRoot, "claude-code-sessions");
            if (!Directory.Exists(root))
            {
                diag.Add(Config.Strings.Get("diag.noTabsDir"));
                return result;
            }

            string[] files;
            try { files = Directory.GetFiles(root, "local_*.json", SearchOption.AllDirectories); }
            catch { diag.Add(Config.Strings.Get("diag.tabsUnlistable")); return result; }

            if (files.Length == 0) diag.Add(Config.Strings.Get("diag.noTabs"));

            int failed = 0;
            foreach (var f in files)
            {
                Dictionary<string, object> o = null;
                try { o = Json.ParseObject(Paths.ReadAllTextShared(f)); }
                catch { }

                if (o == null) { failed++; continue; }

                var cli = Json.Str(o, "cliSessionId");
                if (string.IsNullOrEmpty(cli)) continue;

                var archived = Json.Bool(o, "isArchived");
                if (archived && !includeArchived) continue;

                result.Add(new DesktopTab
                {
                    TabId = Json.Str(o, "sessionId"),
                    CliSessionId = cli,
                    Cwd = Json.Str(o, "cwd"),
                    Model = Json.Str(o, "model"),
                    Effort = Json.Str(o, "effort"),
                    Title = Json.Str(o, "title"),
                    LastActivityMs = Json.Long(o, "lastActivityAt"),
                    CreatedAtMs = Json.Long(o, "createdAt"),
                    IsArchived = archived,
                });
            }

            if (failed > 0) diag.Add(Config.Strings.Format("diag.tabsFailed", failed));

            result.Sort((a, b) => b.LastActivityMs.CompareTo(a.LastActivityMs));
            return result;
        }

        /// <summary>
        /// 実際に走っている Claude Code プロセスの一覧。
        ///
        /// ~/.claude/sessions/&lt;pid&gt;.json  （1 プロセス 1 ファイル）
        ///   {"pid":10320,"sessionId":"...","cwd":"...","entrypoint":"claude-desktop",
        ///    "kind":"interactive","version":"2.1.234","procStart":"1343162796855206
        ///    55","startedAt":1787154369606}
        ///
        /// Desktop のタブ登録とは別物で、補完関係にある。
        /// 実測ではタブ 6 件に対して生きているプロセスは 3 件だった。
        /// entrypoint が claude-desktop 以外なら CLI や VS Code 拡張のセッション。
        ///
        /// 終了したプロセスのファイルは残るので PID の生存を必ず確認する。
        /// PID は再利用されるため、プロセスの開始時刻 (procStart, FILETIME) も突き合わせる。
        /// </summary>
        public static List<LiveProcess> ReadProcesses(string configDir, Diagnostics diag)
        {
            var result = new List<LiveProcess>();
            if (configDir == null) return result;

            var dir = Path.Combine(configDir, "sessions");
            if (!Directory.Exists(dir))
            {
                diag.Add(Config.Strings.Get("diag.noProcDir"));
                return result;
            }

            string[] files;
            try { files = Directory.GetFiles(dir, "*.json"); }
            catch { diag.Add(Config.Strings.Get("diag.procUnlistable")); return result; }

            foreach (var f in files)
            {
                // 1 件の異常で一覧全体を失わない。
                try { ReadOneProcess(f, result); }
                catch { }
            }

            return result;
        }

        private static void ReadOneProcess(string path, List<LiveProcess> result)
        {
            Dictionary<string, object> o = null;
            try { o = Json.ParseObject(Paths.ReadAllTextShared(path)); }
            catch { }
            if (o == null) return;

            var pid = (int)Json.Long(o, "pid");
            if (pid <= 0) return;

            var p = new LiveProcess
            {
                Pid = pid,
                SessionId = Json.Str(o, "sessionId"),
                Cwd = Json.Str(o, "cwd"),
                Entrypoint = Json.Str(o, "entrypoint"),
                Kind = Json.Str(o, "kind"),
                Version = Json.Str(o, "version"),
                Name = Json.Str(o, "name"),
                StartedAtMs = Json.Long(o, "startedAt"),
            };

            long procStart;
            if (!long.TryParse(Json.Str(o, "procStart"), out procStart)) procStart = 0;
            p.Alive = IsProcessAlive(pid, procStart);

            result.Add(p);
        }

        /// <summary>
        /// PID が生きているか。procStart（FILETIME）が取れていれば
        /// プロセスの開始時刻と突き合わせて PID 再利用を弾く。
        ///
        /// ★ 全体を try で囲む。
        ///   終了済みプロセスのファイルは残るので、その PID が別のプロセスに
        ///   再利用されることがある。相手が権限の高いプロセスだと
        ///   HasExited も StartTime も Win32Exception「アクセスが拒否されました」を投げ、
        ///   収集全体が落ちる（Claude Desktop の更新後に実際に踏んだ）。
        ///
        ///   照会できないプロセスは自分が起動した claude ではないので、
        ///   生きていないものとして扱う。
        /// </summary>
        private static bool IsProcessAlive(int pid, long expectedFileTime)
        {
            try
            {
                using (var proc = Process.GetProcessById(pid))
                {
                    if (proc.HasExited) return false;
                    if (expectedFileTime <= 0) return true;

                    // 記録側と取得側で丸めが違いうるので 2 秒の許容を持たせる。
                    var actual = proc.StartTime.ToFileTime();
                    return Math.Abs(actual - expectedFileTime) < 20_000_000L;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
