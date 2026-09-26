using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CtxTray.Collect
{
    /// <summary>transcript の 1 ターン分の使用量。</summary>
    internal sealed class UsagePoint
    {
        public DateTime AtUtc;
        public string Model;
        public string Effort;        // その応答のエフォート（行のトップレベルの effort）。古い版の行には無い
        public int PromptTokens;     // そのリクエストで実際に送ったプロンプト長
        public int OutputTokens;     // 参考。コンテキスト量には足さない
    }

    internal static class Transcript
    {
        // 末尾から読む行数。1 行 = JSONL の 1 レコードなので、
        // assistant 行はこの範囲にほぼ確実に含まれる。
        private const int TailLines = 400;
        private const int TailLinesFallback = 4000;

        // 末尾読みで確保する最大バイト数の上限（巨大なツール出力対策）。
        private const long TailBytesCap = 16L * 1024 * 1024;

        private sealed class CacheEntry
        {
            public long Stamp;
            public List<UsagePoint> Points;
            public DateTime LastUsedUtc;
        }

        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        // 全体検索の結果。見つかった場所と、見つからなかった時刻を覚える。
        // transcript がまだ無いセッション（開いただけのタブ、短命な幽霊セッション）のために、
        // 更新のたびに projects 全体を再帰検索しないようにする。
        private static readonly Dictionary<string, string> FoundBySearch =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> MissedAt =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan RetrySearchAfter = TimeSpan.FromSeconds(30);

        // 使われなくなった記録（閉じたセッション）を捨てる間隔と、捨てるまでの時間。
        private static readonly TimeSpan PruneEvery = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(1);
        private static DateTime _lastPruneUtc = DateTime.MinValue;

        /// <summary>
        /// cwd と sessionId から transcript の場所を割り出す。
        ///
        /// ~/.claude/projects/(cwd の slug)/(cliSessionId).jsonl
        /// slug は cwd の "\" "/" ":" "_" を "-" に置換したもの。ただしこの規則は内部仕様なので、
        /// まず推測で当て、外したらファイル名で全体検索に落とす。
        /// </summary>
        public static string Find(string configDir, string cwd, string sessionId)
        {
            if (configDir == null || string.IsNullOrEmpty(sessionId)) return null;

            var projects = Path.Combine(configDir, "projects");
            if (!Directory.Exists(projects)) return null;

            if (!string.IsNullOrEmpty(cwd))
            {
                var slug = cwd.Replace('\\', '-').Replace('/', '-').Replace(':', '-').Replace('_', '-');
                var guess = Path.Combine(projects, slug, sessionId + ".jsonl");
                if (File.Exists(guess)) return guess;
            }

            var now = DateTime.UtcNow;
            PruneIfDue(now);

            string known;
            if (FoundBySearch.TryGetValue(sessionId, out known) && File.Exists(known)) return known;

            DateTime missed;
            if (MissedAt.TryGetValue(sessionId, out missed) && now - missed < RetrySearchAfter) return null;

            try
            {
                var hits = Directory.GetFiles(projects, sessionId + ".jsonl", SearchOption.AllDirectories);
                if (hits.Length > 0)
                {
                    FoundBySearch[sessionId] = hits[0];
                    MissedAt.Remove(sessionId);
                    return hits[0];
                }
            }
            catch { }

            MissedAt[sessionId] = now;
            return null;
        }

        private static void PruneIfDue(DateTime now)
        {
            if (now - _lastPruneUtc < PruneEvery) return;
            _lastPruneUtc = now;

            var staleMisses = new List<string>();
            foreach (var kv in MissedAt)
                if (now - kv.Value > ForgetAfter) staleMisses.Add(kv.Key);
            foreach (var key in staleMisses) MissedAt.Remove(key);

            var staleFound = new List<string>();
            foreach (var kv in FoundBySearch)
                if (!File.Exists(kv.Value)) staleFound.Add(kv.Key);
            foreach (var key in staleFound) FoundBySearch.Remove(key);

            var staleCache = new List<string>();
            foreach (var kv in Cache)
                if (now - kv.Value.LastUsedUtc > ForgetAfter) staleCache.Add(kv.Key);
            foreach (var key in staleCache) Cache.Remove(key);
        }

        /// <summary>
        /// 末尾から使用量の系列を取る（時系列順で返す）。
        ///
        /// コンテキスト量 = input_tokens + cache_creation_input_tokens + cache_read_input_tokens
        ///
        /// ★ output_tokens は足さない。
        ///   公式インジケーターが 169k を表示していた時刻の実測で、
        ///   この式が 169,046 と一致した。output を足すと 170,534 で過大になる。
        ///   インジケーターが出しているのは
        ///   「直近リクエストで送ったプロンプト長」そのもの。
        ///
        /// 更新時刻とサイズが前回と同じなら再解析しない。
        /// </summary>
        public static List<UsagePoint> ReadSeries(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            FileInfo fi;
            try
            {
                fi = new FileInfo(path);
                if (!fi.Exists) return null;
            }
            catch { return null; }

            var stamp = fi.LastWriteTimeUtc.Ticks ^ (fi.Length << 1);

            var now = DateTime.UtcNow;
            PruneIfDue(now);

            CacheEntry cached;
            if (Cache.TryGetValue(path, out cached) && cached.Stamp == stamp)
            {
                cached.LastUsedUtc = now;
                return cached.Points;
            }

            var points = Parse(path, TailLines);
            // 末尾 400 行に assistant 行が無い場合だけ、範囲を広げてもう一度だけ試す。
            if (points.Count == 0)
                points = Parse(path, TailLinesFallback);

            Cache[path] = new CacheEntry { Stamp = stamp, Points = points, LastUsedUtc = now };
            return points;
        }

        public static UsagePoint ReadLatest(string path)
        {
            var series = ReadSeries(path);
            if (series == null || series.Count == 0) return null;
            return series[series.Count - 1];
        }

        private static List<UsagePoint> Parse(string path, int maxLines)
        {
            var points = new List<UsagePoint>();

            List<string> lines;
            try { lines = ReadTailLines(path, maxLines); }
            catch { return points; }

            foreach (var line in lines)
            {
                // 全行を JSON 解析すると重いので、usage を持つ行だけに絞る。
                if (line.IndexOf("\"cache_read_input_tokens\"", StringComparison.Ordinal) < 0) continue;

                var o = Json.ParseObject(line);
                if (o == null) continue;

                var message = Json.Obj(o, "message");
                if (message == null) continue;

                var usage = Json.Obj(message, "usage");
                if (usage == null) continue;

                var model = Json.Str(message, "model");

                // "<synthetic>" は中断やエラー時に混ざる行で、usage が全部 0 になっている。
                // これを拾うとコンテキストが 0% と表示される。
                if (string.Equals(model, "<synthetic>", StringComparison.Ordinal)) continue;

                var prompt = (int)(Json.Long(usage, "input_tokens")
                                 + Json.Long(usage, "cache_creation_input_tokens")
                                 + Json.Long(usage, "cache_read_input_tokens"));

                if (prompt <= 0) continue;

                points.Add(new UsagePoint
                {
                    AtUtc = ParseTimestamp(Json.Str(o, "timestamp")),
                    Model = model,
                    // message の中ではなく行のトップレベルにある（2026-09-26、Desktop・CLI・VS Code の実データで確認）。
                    Effort = Json.Str(o, "effort"),
                    PromptTokens = prompt,
                    OutputTokens = (int)Json.Long(usage, "output_tokens"),
                });
            }

            return points;
        }

        private static DateTime ParseTimestamp(string s)
        {
            DateTime dt;
            if (!string.IsNullOrEmpty(s) &&
                DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                return dt;
            return DateTime.MinValue;
        }

        /// <summary>
        /// ファイル末尾から maxLines 行を読む。
        ///
        /// transcript は数 MB まで育つので全体は読まない。
        /// Claude Code が書き込み中でも読めるように共有指定する。
        /// </summary>
        private static List<string> ReadTailLines(string path, int maxLines)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
            {
                const int ChunkSize = 64 * 1024;
                var chunks = new List<byte[]>();
                long pos = fs.Length;
                long taken = 0;
                int newlines = 0;
                bool reachedStart = false;

                while (pos > 0 && newlines <= maxLines && taken < TailBytesCap)
                {
                    int size = (int)Math.Min(ChunkSize, pos);
                    pos -= size;
                    fs.Seek(pos, SeekOrigin.Begin);

                    var buf = new byte[size];
                    int read = 0;
                    while (read < size)
                    {
                        int n = fs.Read(buf, read, size - read);
                        if (n <= 0) break;
                        read += n;
                    }

                    chunks.Insert(0, buf);
                    taken += read;
                    for (int i = 0; i < read; i++)
                        if (buf[i] == 0x0A) newlines++;

                    if (pos == 0) reachedStart = true;
                }

                var all = new byte[taken];
                int offset = 0;
                foreach (var c in chunks)
                {
                    Buffer.BlockCopy(c, 0, all, offset, c.Length);
                    offset += c.Length;
                }

                var text = new UTF8Encoding(false).GetString(all);
                var split = text.Split('\n');

                var lines = new List<string>(split.Length);
                // 先頭要素は行の途中から始まっている可能性がある(UTF-8 の文字境界も割れうる)。
                // ファイル先頭まで読み切った場合を除いて捨てる。
                int start = reachedStart ? 0 : 1;
                for (int i = start; i < split.Length; i++)
                {
                    var line = split[i].TrimEnd('\r');
                    if (line.Length > 0) lines.Add(line);
                }

                if (lines.Count > maxLines)
                    lines.RemoveRange(0, lines.Count - maxLines);

                return lines;
            }
        }
    }
}
