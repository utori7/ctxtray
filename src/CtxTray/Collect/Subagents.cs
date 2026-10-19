using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CtxTray.Collect
{
    /// <summary>セッションの下で動いているサブエージェント 1 つ。</summary>
    internal sealed class SubagentInfo
    {
        public string AgentId;          // ファイル名の agent-(ここ).jsonl
        public string AgentType;        // agent-*.meta.json の agentType（Explore、fork など）。無ければ null
        public string Description;      // 同じく description（呼び出し側が付けた短い説明）
        public string Model;
        public string ModelName;        // 画面に出すモデル名。確かめた名前が無ければ null
        public int? Tokens;
        public int? Limit;
        public double? Pct;
        /// <summary>自動圧縮（compact_boundary）が起きた回数。</summary>
        public int Compactions;
        public DateTime LastWriteUtc;
        public bool Running;
    }

    /// <summary>
    /// サブエージェントの transcript を読む。
    ///
    /// Claude Code はサブエージェントのやり取りを、セッションの transcript とは別のファイルに書く
    /// （公式ドキュメント「Subagents」）:
    ///   ~/.claude/projects/(slug)/(sessionId)/subagents/agent-(agentId).jsonl
    /// 横に agent-(agentId).meta.json があり、agentType と description が入っている
    /// （Claude Code 2.1.299 の実物のファイルで確認、2026-10-19）。
    /// サブエージェントが立てたサブエージェントも同じフォルダーに置かれる（meta.json に parentAgentId と spawnDepth が入る）。
    /// 自動圧縮は {"type":"system","subtype":"compact_boundary"} の行として残る。
    ///
    /// ★ 「いま動いているか」はどこにも書かれていない。transcript の末尾から推し量る（JudgeRunning）。
    ///   Claude Code のエージェントの欄も終わったサブエージェントの行はすぐ消すので、パネルも動いているものだけを数える。
    /// </summary>
    internal static class Subagents
    {
        /// <summary>
        /// これより長く書き込みが無いものは、末尾がどうであれ動いていないとみなす。
        /// エージェントの欄の x で止めると、すぐに「[Request interrupted by user]」の行が書かれる（2.1.299 で確認、2026-10-19）。
        /// これは最後の行が書かれずに終わった場合の備え。
        /// Bash の 1 回の実行は最長 10 分なので、それより少し長くする。
        /// </summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

        // 末尾から見る行数。最後の user / assistant 行は普通この範囲にある。
        private const int TailLines = 60;

        /// <summary>ファイルごとに覚えておくもの。更新のたびに同じ行を読み直さないため。</summary>
        private sealed class FileState
        {
            public long Stamp;          // 更新時刻と長さ。変わったときだけ末尾と .meta.json を読み直す
            public bool TailRunning;    // 末尾の行から見て動いているか（時間による打ち切りは含まない）
            public string AgentType;
            public string Description;

            // 圧縮の回数は、前回の続きから数える（transcript は追記だけで育つ）。
            public long Offset;         // ここまで数えた（行の区切りの直後）
            public long Length;         // そのときのファイルの長さ。縮んでいたら数え直す
            public int Compactions;

            public DateTime LastUsedUtc;
        }

        private static readonly Dictionary<string, FileState> States =
            new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(1);
        private static DateTime _lastPruneUtc = DateTime.MinValue;

        private static readonly byte[] CompactMarker = Encoding.ASCII.GetBytes("compact_boundary");

        /// <summary>セッションの transcript の場所から、サブエージェントのフォルダーを割り出す。</summary>
        public static string DirFor(string transcriptPath)
        {
            if (string.IsNullOrEmpty(transcriptPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(transcriptPath);
                var session = Path.GetFileNameWithoutExtension(transcriptPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(session)) return null;
                return Path.Combine(dir, session, "subagents");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 最近書かれたサブエージェントを読む（StaleAfter より古いものは開かない）。
        /// 動いていないものも Running = false で返す（--json で確かめられるように）。
        /// </summary>
        public static List<SubagentInfo> Read(string transcriptPath, DateTime nowUtc, LimitSources modelLimits)
        {
            var result = new List<SubagentInfo>();
            var dir = DirFor(transcriptPath);
            if (dir == null || !Directory.Exists(dir)) return result;

            PruneIfDue(nowUtc);

            string[] files;
            try
            {
                // 入れ子のサブエージェントも同じフォルダーに置かれる（2.1.299 で確認）。置き場所が変わっても拾えるよう、下の階層も見る。
                files = Directory.GetFiles(dir, "agent-*.jsonl", SearchOption.AllDirectories);
            }
            catch
            {
                return result;
            }

            foreach (var path in files)
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (nowUtc - fi.LastWriteTimeUtc > StaleAfter) continue;

                    var info = ReadOne(fi, nowUtc, modelLimits);
                    if (info != null) result.Add(info);
                }
                catch { }
            }

            return result;
        }

        private static SubagentInfo ReadOne(FileInfo fi, DateTime nowUtc, LimitSources modelLimits)
        {
            var path = fi.FullName;
            var written = fi.LastWriteTimeUtc;
            var state = StateFor(path, nowUtc);

            var stamp = written.Ticks ^ (fi.Length << 1);
            if (state.Stamp != stamp)
            {
                state.Stamp = stamp;
                state.TailRunning = TailSaysRunning(Transcript.ReadTailLines(path, TailLines));
                ReadMeta(Path.ChangeExtension(path, ".meta.json"), state);
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var info = new SubagentInfo
            {
                AgentId = name.StartsWith("agent-", StringComparison.Ordinal) ? name.Substring(6) : name,
                AgentType = state.AgentType,
                Description = state.Description,
                LastWriteUtc = written,
                Running = nowUtc - written <= StaleAfter && state.TailRunning,
                Compactions = CountCompactions(path, nowUtc),
            };

            // 使用量の読み方はセッションと同じ（Transcript.ReadLatest）。
            var usage = Transcript.ReadLatest(path);
            if (usage != null)
            {
                info.Model = usage.Model;
                info.Tokens = usage.PromptTokens;
            }

            if (!string.IsNullOrEmpty(info.Model))
            {
                info.Limit = ModelLimits.Lookup(info.Model,
                    modelLimits == null ? null : modelLimits.Config,
                    modelLimits == null ? null : modelLimits.Docs);
                info.ModelName = ModelLimits.DisplayName(info.Model, modelLimits == null ? null : modelLimits.DocsNames);
            }

            if (info.Tokens.HasValue && info.Limit.HasValue && info.Limit.Value > 0)
                info.Pct = Math.Round(100.0 * info.Tokens.Value / info.Limit.Value, 1);

            return info;
        }

        private static FileState StateFor(string path, DateTime nowUtc)
        {
            FileState state;
            if (!States.TryGetValue(path, out state))
            {
                state = new FileState();
                States[path] = state;
            }
            state.LastUsedUtc = nowUtc;
            return state;
        }

        private static void ReadMeta(string path, FileState state)
        {
            try
            {
                if (!File.Exists(path)) return;
                var o = Json.ParseObject(File.ReadAllText(path, Encoding.UTF8));
                if (o == null) return;
                state.AgentType = Json.Str(o, "agentType");
                state.Description = Json.Str(o, "description");
            }
            catch { }
        }

        /// <summary>
        /// サブエージェントがまだ動いているかを、transcript の末尾から決める。
        ///
        /// 最後の user / assistant 行で見る（attachment や system の行は飛ばす）:
        ///   assistant で stop_reason が end_turn … 最後の答えを返して終わった
        ///   assistant でモデルが &lt;synthetic&gt; … エラーか中断で終わった
        ///   user で「[Request interrupted by user」… 中断された
        ///   SubagentHandback の呼び出しか、その結果 … 報告を返して終わった
        ///   それ以外（ツールの呼び出し・ツールの結果・新しい依頼）… 動いている
        /// ★ サブエージェントは（裏で動かしても前面で動かしても）end_turn で終わらず、SubagentHandback（呼び出し元へ報告を返すツール）の
        ///   呼び出しと結果で終わる（Claude Code 2.1.299 で実物を確かめた、2026-10-19）。
        ///   これを「ツールの結果」として扱うと、終わってから 15 分間「動いている」のままになっていた。
        /// SendMessage で再開されると user 行が足されるので、また「動いている」に戻る。
        /// 末尾がどうであれ、StaleAfter より長く書き込みが無ければ動いていない。
        /// </summary>
        internal static bool JudgeRunning(IList<string> tailLines, DateTime lastWriteUtc, DateTime nowUtc)
        {
            return nowUtc - lastWriteUtc <= StaleAfter && TailSaysRunning(tailLines);
        }

        private static bool TailSaysRunning(IList<string> tailLines)
        {
            if (tailLines == null) return true;

            // 最後の user 行がツールの結果なら、その呼び出しが SubagentHandback かを前の assistant 行で確かめる。
            // 呼び出しは content のブロックごとに別の行になるので、id の合う行まで遡る。
            HashSet<string> resultIds = null;

            for (var i = tailLines.Count - 1; i >= 0; i--)
            {
                var o = Json.ParseObject(tailLines[i]);
                if (o == null) continue;

                var type = Json.Str(o, "type");
                if (type == "assistant")
                {
                    var message = Json.Obj(o, "message");
                    if (resultIds != null)
                    {
                        string name;
                        if (!FindToolUse(message, resultIds, out name)) continue;
                        return !IsHandback(name);
                    }

                    if (message == null) return true;
                    if (string.Equals(Json.Str(message, "model"), "<synthetic>", StringComparison.Ordinal)) return false;
                    string called;
                    if (FindToolUse(message, null, out called) && IsHandback(called)) return false;
                    return !string.Equals(Json.Str(message, "stop_reason"), "end_turn", StringComparison.Ordinal);
                }

                if (type == "user")
                {
                    if (Json.Bool(o, "isMeta")) continue;
                    // 結果の呼び出しが見つからないまま前の依頼まで来たら、ふつうのツールの結果とみなす。
                    if (resultIds != null) return true;
                    var message = Json.Obj(o, "message");
                    if (IsInterrupt(message)) return false;
                    resultIds = ToolResultIds(message);
                    if (resultIds == null) return true;
                }
            }

            // まだ何も応答していない（立ち上がったところ）か、結果の呼び出しが末尾の範囲に無い。
            return true;
        }

        private const string HandbackTool = "SubagentHandback";

        private static bool IsHandback(string toolName)
        {
            return string.Equals(toolName, HandbackTool, StringComparison.Ordinal);
        }

        /// <summary>user 行がツールの結果だけでできていれば、その tool_use_id を返す。そうでなければ null。</summary>
        private static HashSet<string> ToolResultIds(Dictionary<string, object> message)
        {
            object content;
            if (message == null || !message.TryGetValue("content", out content)) return null;
            var blocks = content as object[];
            if (blocks == null || blocks.Length == 0) return null;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in blocks)
            {
                var b = item as Dictionary<string, object>;
                if (b == null || Json.Str(b, "type") != "tool_result") return null;
                var id = Json.Str(b, "tool_use_id");
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids.Count == 0 ? null : ids;
        }

        /// <summary>
        /// assistant 行のツールの呼び出しを探す。ids を渡したときはその id の呼び出しだけ、null なら最後の呼び出し。
        /// </summary>
        private static bool FindToolUse(Dictionary<string, object> message, HashSet<string> ids, out string name)
        {
            name = null;
            object content;
            if (message == null || !message.TryGetValue("content", out content)) return false;
            var blocks = content as object[];
            if (blocks == null) return false;

            var found = false;
            foreach (var item in blocks)
            {
                var b = item as Dictionary<string, object>;
                if (b == null || Json.Str(b, "type") != "tool_use") continue;
                if (ids != null && !ids.Contains(Json.Str(b, "id") ?? "")) continue;
                name = Json.Str(b, "name");
                found = true;
            }
            return found;
        }

        private static bool IsInterrupt(Dictionary<string, object> message)
        {
            if (message == null) return false;

            object content;
            if (!message.TryGetValue("content", out content) || content == null) return false;

            var text = content as string;
            if (text != null) return StatusWatch.IsInterrupt(text);

            var blocks = content as object[];
            if (blocks == null) return false;
            foreach (var item in blocks)
            {
                var b = item as Dictionary<string, object>;
                if (b != null && Json.Str(b, "type") == "text" && StatusWatch.IsInterrupt(Json.Str(b, "text")))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// compact_boundary の行を数える。前回数えたところから先だけを読む。
        /// 途中で終わっている行（書き込み中）は数えず、次回に回す。
        /// </summary>
        internal static int CountCompactions(string path, DateTime nowUtc)
        {
            var entry = StateFor(path, nowUtc);

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                {
                    var length = fs.Length;
                    if (length < entry.Length)
                    {
                        entry.Offset = 0;
                        entry.Compactions = 0;
                    }
                    entry.Length = length;
                    if (length <= entry.Offset) return entry.Compactions;

                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    var line = new MemoryStream();
                    var buf = new byte[64 * 1024];
                    var pos = entry.Offset;
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        var start = 0;
                        for (var i = 0; i < n; i++)
                        {
                            if (buf[i] != 0x0A) continue;
                            line.Write(buf, start, i - start);
                            if (IsCompactBoundary(line)) entry.Compactions++;
                            line.SetLength(0);
                            start = i + 1;
                            entry.Offset = pos + i + 1;
                        }
                        line.Write(buf, start, n - start);
                        pos += n;
                    }
                }
            }
            catch { }

            return entry.Compactions;
        }

        private static bool IsCompactBoundary(MemoryStream line)
        {
            // 大きなツールの出力の行もあるので、印の文字列を含む行だけを JSON として読む。
            var bytes = line.GetBuffer();
            var length = (int)line.Length;
            if (IndexOf(bytes, length, CompactMarker) < 0) return false;

            var o = Json.ParseObject(Encoding.UTF8.GetString(bytes, 0, length));
            return o != null
                && Json.Str(o, "type") == "system"
                && Json.Str(o, "subtype") == "compact_boundary";
        }

        private static int IndexOf(byte[] haystack, int length, byte[] needle)
        {
            for (var i = 0; i <= length - needle.Length; i++)
            {
                var j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static void PruneIfDue(DateTime now)
        {
            if (now - _lastPruneUtc < TimeSpan.FromMinutes(10)) return;
            _lastPruneUtc = now;

            var stale = new List<string>();
            foreach (var kv in States)
                if (now - kv.Value.LastUsedUtc > ForgetAfter) stale.Add(kv.Key);
            foreach (var key in stale) States.Remove(key);
        }
    }
}
