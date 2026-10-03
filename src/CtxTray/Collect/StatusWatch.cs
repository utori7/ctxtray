using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CtxTray.Collect
{
    /// <summary>監視している Claude Code のプロセス 1 つの、ある時点の様子。</summary>
    internal sealed class WatchedSession
    {
        public int Pid;
        public string ShortId;       // sessionId の先頭 8 文字。ほかの行と突き合わせるため
        public string Entrypoint;
        public string Version;
        public string Status;        // ~/.claude/sessions/<pid>.json の status。無い版では null
        public long StatusUpdatedAtMs;

        /// <summary>status 以外の項目。値は列挙値のような短いものだけ（Sanitize を通したもの）。</summary>
        public SortedDictionary<string, string> Fields = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>transcript の末尾の要約（SummarizeTail）。transcript が無ければ null。</summary>
        public string Tail;
    }

    /// <summary>
    /// ID らしい値を、監視 1 回の間だけ通じる番号（id#1, id#2, ...）に置き換える。
    /// 値そのもの（claude.ai のセッション ID など）は貼り付け先に出したくないが、
    /// 「変わったか」「ほかの項目と同じ値か」は状態の突き合わせに使える。
    /// </summary>
    internal sealed class IdAliases
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

        public string For(string id)
        {
            string alias;
            if (!_map.TryGetValue(id, out alias))
            {
                alias = "id#" + (_map.Count + 1).ToString(CultureInfo.InvariantCulture);
                _map[id] = alias;
            }
            return alias;
        }
    }

    /// <summary>
    /// --watch-status: セッションの状態が変わるたびに 1 行ずつ書く診断用の監視。
    ///
    /// ~/.claude/sessions/&lt;pid&gt;.json には、新しめの Claude Code（2.1.286・2.1.288 で確認）が
    ///   "status":"busy", "statusUpdatedAt":1790998068316
    /// を書いている。Desktop の Code タブ（2.1.286）で確かめた値は次のとおり。
    ///   busy     応答を作っている
    ///   waiting  利用者を待っている（質問への回答、ツールの承認）。この間だけ waitingFor に何を待っているかの文が付く
    ///   idle     応答が終わった。数秒後にタブの記録の postTurnSummary.status_category が completed / blocked になる
    /// ほかの場面（ターミナル版、VS Code 拡張、中断など）の値は、表示に使う前にこのモードで集める。
    ///
    /// 突き合わせのために、同じ時点の transcript の末尾（最後の発言の種類やツール名）と、
    /// Desktop のタブの記録（local_*.json）の項目も並べて出す。
    ///
    /// ★ 出力は Issue やチャットにそのまま貼れるようにする。
    ///   セッション名、フォルダーのパス、会話の本文は出さない。値は列挙値のような
    ///   短いもの（英数字と . _ : - だけ）に限り、それ以外は長さだけを書く。
    ///   ID らしい値（UUID、session_ のあとの長い英数字など）は id#N に置き換える。
    ///   未知の項目も同じ規則で出すので、新しく増えた項目にも気付ける。
    /// </summary>
    internal sealed class StatusWatch
    {
        // 出さない項目。名前・パス・ID（人に見せられない、または意味が無い）。
        private static readonly HashSet<string> SkippedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "sessionId", "cliSessionId", "cwd", "name", "title", "messagingSocketPath", "pidDomain",
        };

        // プロセスの記録で、行の別の場所に出す項目。タブの記録では同じ名前でも項目として出す
        // （タブ側に status があれば、それも知りたい）。
        private static readonly HashSet<string> ProcessKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "pid", "procStart", "version", "entrypoint", "status",
        };

        private static readonly Regex TokenPattern = new Regex(@"^[A-Za-z0-9_.:\-]{1,64}$", RegexOptions.CultureInvariant);

        // 文でも中身を出す項目。waitingFor は「何を待っているか」の定型文で、
        // 回答待ちと承認待ちを見分けるのに要る。英字と空白だけの短い文に限る
        // （数字・記号・パスが入ったら文字数だけにする）。会話の要約（status_detail など）は対象にしない。
        private static readonly HashSet<string> PhraseKeys = new HashSet<string>(StringComparer.Ordinal) { "waitingFor" };
        private static readonly Regex PhrasePattern = new Regex(@"^[A-Za-z][A-Za-z ']{0,39}$", RegexOptions.CultureInvariant);

        // ID らしい値: UUID を含むもの、または英字と数字が混ざった 16 文字以上の並びを含むもの
        // （session_0143Mhty... や、ハッシュ値）。claude-opus-5-5 のような名前は区切りごとに短いので当たらない。
        private static readonly Regex UuidPattern = new Regex(
            @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}", RegexOptions.CultureInvariant);
        private static readonly Regex LongRunPattern = new Regex(
            @"(?=[A-Za-z0-9]*[0-9])(?=[A-Za-z0-9]*[A-Za-z])[A-Za-z0-9]{16,}", RegexOptions.CultureInvariant);

        // transcript の末尾から見る行数。最後の user / assistant 行は普通この範囲にある。
        private const int TailLines = 60;

        private readonly string _configDir;
        private readonly string _dataRoot;
        private readonly IdAliases _ids = new IdAliases();

        // 終了したプロセスのファイルは残り続けるので、一度死んでいると分かったファイルは
        // 書き換わるまで調べ直さない（PID は再利用されるが、そのときはファイルも書き換わる）。
        private readonly Dictionary<string, long> _deadStamps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, KeyValuePair<long, string>> _tailCache =
            new Dictionary<string, KeyValuePair<long, string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, KeyValuePair<long, Dictionary<string, object>>> _tabCache =
            new Dictionary<string, KeyValuePair<long, Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

        public StatusWatch(string configDir, string dataRoot)
        {
            _configDir = configDir;
            _dataRoot = dataRoot;
        }

        // --- 読み取り -----------------------------------------------------------

        /// <summary>
        /// いま生きているプロセスを pid ごとに読む。
        /// 書き込みの途中で読めなかったファイルは、前回の様子（previous）を引き継ぐ。
        /// そうしないと、一瞬だけ「終了」して「見つかった」ように出る。
        /// </summary>
        public Dictionary<int, WatchedSession> Poll(Dictionary<int, WatchedSession> previous)
        {
            var result = new Dictionary<int, WatchedSession>();
            if (_configDir == null) return result;

            var dir = Path.Combine(_configDir, "sessions");
            string[] files;
            try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : new string[0]; }
            catch { return result; }

            Dictionary<string, Dictionary<string, object>> tabs = null;

            foreach (var f in files)
            {
                try
                {
                    var stamp = Stamp(f);
                    long dead;
                    if (_deadStamps.TryGetValue(f, out dead) && dead == stamp) continue;

                    var o = Json.ParseObject(Paths.ReadAllTextShared(f));
                    if (o == null)
                    {
                        int filePid;
                        WatchedSession last;
                        if (previous != null && int.TryParse(Path.GetFileNameWithoutExtension(f), out filePid) &&
                            previous.TryGetValue(filePid, out last))
                            result[filePid] = last;
                        continue;
                    }

                    var pid = (int)Json.Long(o, "pid");
                    if (pid <= 0) continue;

                    long procStart;
                    if (!long.TryParse(Json.Str(o, "procStart"), out procStart)) procStart = 0;
                    if (!Sessions.IsProcessAlive(pid, procStart))
                    {
                        _deadStamps[f] = stamp;
                        continue;
                    }
                    _deadStamps.Remove(f);

                    var s = FromRecord(o, _ids);

                    var sessionId = Json.Str(o, "sessionId");
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        if (tabs == null) tabs = ReadTabs();
                        Dictionary<string, object> tab;
                        if (tabs.TryGetValue(sessionId, out tab)) Sanitize(tab, "tab.", s.Fields, _ids);

                        s.Tail = ReadTail(Transcript.Find(_configDir, Json.Str(o, "cwd"), sessionId));
                    }

                    result[pid] = s;
                }
                catch
                {
                    // 1 件の異常で監視全体を止めない。
                }
            }

            return result;
        }

        /// <summary>sessions/&lt;pid&gt;.json の 1 件を、出してよい形に直す。</summary>
        public static WatchedSession FromRecord(Dictionary<string, object> o, IdAliases ids = null)
        {
            var sessionId = Json.Str(o, "sessionId") ?? "";
            var s = new WatchedSession
            {
                Pid = (int)Json.Long(o, "pid"),
                ShortId = sessionId.Length > 8 ? sessionId.Substring(0, 8) : sessionId,
                Entrypoint = Token(Json.Str(o, "entrypoint")),
                Version = Token(Json.Str(o, "version")),
                Status = o.ContainsKey("status") ? Value(o["status"], ids) : null,
                StatusUpdatedAtMs = Json.Long(o, "statusUpdatedAt"),
            };
            Sanitize(o, "", s.Fields, ids);
            return s;
        }

        private Dictionary<string, Dictionary<string, object>> ReadTabs()
        {
            var result = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            if (_dataRoot == null) return result;

            var root = Path.Combine(_dataRoot, "claude-code-sessions");
            string[] files;
            try { files = Directory.Exists(root) ? Directory.GetFiles(root, "local_*.json", SearchOption.AllDirectories) : new string[0]; }
            catch { return result; }

            foreach (var f in files)
            {
                try
                {
                    var stamp = Stamp(f);
                    KeyValuePair<long, Dictionary<string, object>> cached;
                    Dictionary<string, object> o;
                    if (_tabCache.TryGetValue(f, out cached) && cached.Key == stamp) o = cached.Value;
                    else
                    {
                        o = Json.ParseObject(Paths.ReadAllTextShared(f));
                        if (o == null) continue;
                        _tabCache[f] = new KeyValuePair<long, Dictionary<string, object>>(stamp, o);
                    }

                    var cli = Json.Str(o, "cliSessionId");
                    if (!string.IsNullOrEmpty(cli)) result[cli] = o;
                }
                catch { }
            }
            return result;
        }

        private string ReadTail(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var stamp = Stamp(path);
                KeyValuePair<long, string> cached;
                if (_tailCache.TryGetValue(path, out cached) && cached.Key == stamp) return cached.Value;

                var summary = SummarizeTail(Transcript.ReadTailLines(path, TailLines));
                _tailCache[path] = new KeyValuePair<long, string>(stamp, summary);
                return summary;
            }
            catch
            {
                return null;
            }
        }

        private static long Stamp(string path)
        {
            var fi = new FileInfo(path);
            return fi.LastWriteTimeUtc.Ticks ^ (fi.Length << 1);
        }

        // --- 出してよい形に直す --------------------------------------------------

        /// <summary>
        /// 記録の項目を「キー → 短い値」に直して into に足す。
        ///
        /// 名前・パス・時刻の項目は出さない。時刻は数値で、キーが At / Since / Ms で終わるもの
        /// （updatedAt、startedAt、nameSince、lastActivityAt など）。毎回変わるので、
        /// 出すと状態の変化の行が埋もれる。
        ///
        /// 入れ子の項目は 1 段だけ開いて「親.子」で出す。タブの記録の postTurnSummary
        /// （needs_action・status_category を持つ）のように、状態そのものが入れ子の中にあるため。
        /// 2 段目より深いものはキーだけを出す。
        /// </summary>
        public static void Sanitize(Dictionary<string, object> o, string prefix, SortedDictionary<string, string> into,
                                    IdAliases ids = null)
        {
            Sanitize(o, prefix, into, ids, true);
        }

        private static void Sanitize(Dictionary<string, object> o, string prefix, SortedDictionary<string, string> into,
                                     IdAliases ids, bool expand)
        {
            if (o == null) return;
            foreach (var kv in o)
            {
                if (SkippedKeys.Contains(kv.Key)) continue;
                if (prefix.Length == 0 && ProcessKeys.Contains(kv.Key)) continue;
                if (IsTimestamp(kv.Key, kv.Value)) continue;

                var key = prefix + (Token(kv.Key) ?? "?");
                var inner = kv.Value as Dictionary<string, object>;
                var phrase = kv.Value as string;
                if (expand && inner != null && inner.Count > 0) Sanitize(inner, key + ".", into, ids, false);
                else if (phrase != null && PhraseKeys.Contains(kv.Key) && PhrasePattern.IsMatch(phrase) && Token(phrase) == null)
                    into[key] = "\"" + phrase + "\"";
                else into[key] = Value(kv.Value, ids);
            }
        }

        private static bool IsTimestamp(string key, object value)
        {
            if (!(value is int || value is long || value is decimal || value is double)) return false;
            return key.EndsWith("At", StringComparison.Ordinal)
                || key.EndsWith("Since", StringComparison.Ordinal)
                || key.EndsWith("Ms", StringComparison.Ordinal);
        }

        /// <summary>
        /// 値を短い文字列にする。列挙値のようなもの以外は中身を出さない。
        /// ID らしい値は ids の番号に、ids が無ければ &lt;id&gt; にする。
        /// </summary>
        public static string Value(object v, IdAliases ids = null)
        {
            if (v == null) return "null";
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is int || v is long || v is decimal || v is double)
                return Convert.ToString(v, CultureInfo.InvariantCulture);

            var s = v as string;
            if (s != null)
            {
                if (Token(s) == null) return "<text " + s.Length + ">";
                return LooksLikeId(s) ? (ids != null ? ids.For(s) : "<id>") : s;
            }

            var arr = v as object[];
            if (arr != null)
            {
                var parts = new List<string>();
                foreach (var item in arr)
                {
                    var t = item as string;
                    if (t == null || Token(t) == null) return "<array " + arr.Length + ">";
                    parts.Add(Value(t, ids));
                }
                return "[" + string.Join(",", parts.ToArray()) + "]";
            }

            var obj = v as Dictionary<string, object>;
            if (obj != null)
            {
                var keys = new List<string>();
                foreach (var k in obj.Keys) keys.Add(Token(k) ?? "?");
                keys.Sort(StringComparer.Ordinal);
                return "{" + string.Join(",", keys.ToArray()) + "}";
            }

            return "<" + v.GetType().Name + ">";
        }

        /// <summary>英数字と . _ : - だけの短い文字列ならそのまま、それ以外は null。</summary>
        public static string Token(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return TokenPattern.IsMatch(s) ? s : null;
        }

        public static bool LooksLikeId(string s)
        {
            return !string.IsNullOrEmpty(s) && (UuidPattern.IsMatch(s) || LongRunPattern.IsMatch(s));
        }

        // --- transcript の末尾 --------------------------------------------------

        /// <summary>
        /// transcript の末尾を 1 行に要約する。例:
        ///   assistant[thinking,tool_use:Bash] stop=tool_use
        ///   user[tool_result]  +attachment
        ///   user[interrupt]
        ///
        /// 最後の user / assistant 行を要約し、その後ろに付いている別の種類の行
        /// （attachment、system など）を + で並べる。本文は出さない。
        /// </summary>
        public static string SummarizeTail(IList<string> lines)
        {
            if (lines == null || lines.Count == 0) return "empty";

            var after = new List<string>();
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var o = Json.ParseObject(lines[i]);
                if (o == null) continue;

                var type = Token(Json.Str(o, "type")) ?? "?";
                if (type == "user" || type == "assistant")
                {
                    var sb = new StringBuilder();
                    if (Json.Bool(o, "isSidechain")) sb.Append("side:");
                    sb.Append(type).Append('[').Append(Blocks(o, type)).Append(']');

                    var message = Json.Obj(o, "message");
                    var stop = message != null ? Json.Str(message, "stop_reason") : null;
                    if (stop != null) sb.Append(" stop=").Append(Token(stop) ?? "?");

                    if (after.Count > 0) sb.Append("  +").Append(string.Join(",", after.ToArray()));
                    return sb.ToString();
                }

                var label = type;
                var sub = Subtype(o);
                if (sub != null) label += ":" + sub;
                if (!after.Contains(label)) after.Insert(0, label);
            }

            return after.Count > 0 ? "+" + string.Join(",", after.ToArray()) : "empty";
        }

        private static string Subtype(Dictionary<string, object> o)
        {
            var sub = Token(Json.Str(o, "subtype"));
            if (sub != null) return sub;

            foreach (var key in new[] { "attachment", "data" })
            {
                var inner = Json.Obj(o, key);
                if (inner == null) continue;
                sub = Token(Json.Str(inner, "type"));
                if (sub != null) return sub;
            }
            return null;
        }

        private static string Blocks(Dictionary<string, object> o, string type)
        {
            if (type == "user" && Json.Bool(o, "isMeta")) return "meta";

            var message = Json.Obj(o, "message");
            if (message == null) return "";

            object content;
            if (!message.TryGetValue("content", out content) || content == null) return "";

            var text = content as string;
            if (text != null) return type == "user" ? (IsInterrupt(text) ? "interrupt" : "prompt") : "text";

            var blocks = content as object[];
            if (blocks == null) return "";

            var parts = new List<string>();
            foreach (var item in blocks)
            {
                var b = item as Dictionary<string, object>;
                if (b == null) continue;

                var kind = Token(Json.Str(b, "type")) ?? "?";
                string part = kind;
                if (kind == "tool_use")
                    part = "tool_use:" + (Token(Json.Str(b, "name")) ?? "?");
                else if (kind == "tool_result" && Json.Bool(b, "is_error"))
                    part = "tool_result:error";
                else if (kind == "text" && type == "user")
                    part = IsInterrupt(Json.Str(b, "text")) ? "interrupt" : "prompt";

                if (!parts.Contains(part)) parts.Add(part);
            }
            return string.Join(",", parts.ToArray());
        }

        private static bool IsInterrupt(string text)
        {
            return text != null && text.StartsWith("[Request interrupted by user", StringComparison.Ordinal);
        }

        // --- 変化の行 -----------------------------------------------------------

        /// <summary>
        /// 前回と今回の違いを、行の本体（時刻と pid を除いた部分）にして返す。変化が無ければ空。
        /// status が同じでも statusUpdatedAt が変わっていれば出す。
        /// 監視の間隔より短い間に別の値を挟んで戻ったことが分かるように。
        /// </summary>
        public static List<string> Changes(WatchedSession before, WatchedSession after, DateTime nowUtc)
        {
            var lines = new List<string>();
            if (before == null || after == null) return lines;

            if (before.ShortId != after.ShortId)
                lines.Add("session " + before.ShortId + " -> " + after.ShortId);

            if (before.Status != after.Status || before.StatusUpdatedAtMs != after.StatusUpdatedAtMs)
            {
                var sb = new StringBuilder();
                sb.Append("status ").Append(before.Status ?? "(none)").Append(" -> ").Append(after.Status ?? "(none)");
                if (before.Status == after.Status) sb.Append(" (set again)");
                if (before.StatusUpdatedAtMs > 0 && after.StatusUpdatedAtMs > before.StatusUpdatedAtMs)
                    sb.Append("  after ").Append(Seconds(after.StatusUpdatedAtMs - before.StatusUpdatedAtMs));
                var lag = Lag(after.StatusUpdatedAtMs, nowUtc);
                if (lag != null) sb.Append("  seen ").Append(lag).Append(" later");
                sb.Append("  | tail ").Append(after.Tail ?? "(none)");
                lines.Add(sb.ToString());
            }
            else if (before.Tail != after.Tail)
            {
                lines.Add("tail " + (after.Tail ?? "(none)") + "  | status " + (after.Status ?? "(none)"));
            }

            foreach (var kv in after.Fields)
            {
                string old;
                if (!before.Fields.TryGetValue(kv.Key, out old)) lines.Add("field " + kv.Key + " = " + kv.Value + " (new)");
                else if (old != kv.Value) lines.Add("field " + kv.Key + " " + old + " -> " + kv.Value);
            }
            foreach (var kv in before.Fields)
            {
                if (!after.Fields.ContainsKey(kv.Key)) lines.Add("field " + kv.Key + " removed (was " + kv.Value + ")");
            }

            return lines;
        }

        /// <summary>初めて見つけたプロセスの行の本体。</summary>
        public static string Describe(WatchedSession s)
        {
            var sb = new StringBuilder();
            sb.Append("found ").Append(s.Entrypoint ?? "?")
              .Append(" v").Append(s.Version ?? "?")
              .Append(" session ").Append(s.ShortId)
              .Append("  status ").Append(s.Status ?? "(none)")
              .Append("  | tail ").Append(s.Tail ?? "(none)");
            if (s.Fields.Count > 0)
            {
                sb.Append("  | ");
                var first = true;
                foreach (var kv in s.Fields)
                {
                    if (!first) sb.Append(' ');
                    sb.Append(kv.Key).Append('=').Append(kv.Value);
                    first = false;
                }
            }
            return sb.ToString();
        }

        private static string Lag(long recordedMs, DateTime nowUtc)
        {
            if (recordedMs <= 0) return null;
            var nowMs = (long)(nowUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var lag = nowMs - recordedMs;
            if (lag < 0 || lag > 3600_000) return null;   // 時計がずれているか、古い記録
            return Seconds(lag);
        }

        private static string Seconds(long ms)
        {
            return (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }
    }
}
