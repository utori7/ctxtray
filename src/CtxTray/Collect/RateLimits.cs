using System;
using System.Collections.Generic;
using System.IO;

namespace CtxTray.Collect
{
    /// <summary>plan-usage-history.json の 1 サンプル。</summary>
    internal sealed class UsageSample
    {
        public DateTime AtUtc;
        public int FiveHourPct;
        public int WeeklyPct;

        /// <summary>組織の識別子。値そのものは表示にも出力にも使わない（絞り込みだけ）。</summary>
        public string Org;
    }

    /// <summary>週間枠がリセットされた区間（この間のどこかでリセットが起きた）。</summary>
    internal sealed class ResetInterval
    {
        public DateTime FromUtc;   // 最後に高い値だったサンプルの時刻
        public DateTime ToUtc;     // 最初に低い値になったサンプルの時刻
        public int Before;
        public int After;

        public TimeSpan Width { get { return ToUtc - FromUtc; } }
    }

    internal sealed class RateLimitStatus
    {
        public int FiveHourPct;
        public int WeeklyPct;
        public DateTime SampledAtUtc;
        public int StalenessSec;

        /// <summary>5時間枠の開始（推定）。掴めなければ null。</summary>
        public DateTime? FiveHourWindowStartUtc;
        public DateTime? NextFiveHourResetUtc;
    }

    internal static class RateLimits
    {
        /// <summary>
        /// レートリミットの読み取り。
        ///
        /// %APPDATA%\Claude\plan-usage-history.json （実体は MSIX 側）
        ///   {"version":2,"samples":[{"t":1786841646425,"org":"...","u":{"fh":47,"sd":13}}]}
        ///   u.fh = 5時間枠 %  /  u.sd = 週間枠 %  /  t = epoch ミリ秒
        ///
        /// サンプル間隔は 5〜15 分。ただし Desktop を触っていない間は止まる
        /// （起動中でも止まる。docs/how-it-works.md の Rate limits）。鮮度の扱いは Snapshot 側で決める。
        /// </summary>
        public static List<UsageSample> ReadSamples(string dataRoot, Diagnostics diag)
        {
            var result = new List<UsageSample>();
            if (dataRoot == null) return result;

            var path = Path.Combine(dataRoot, "plan-usage-history.json");
            if (!File.Exists(path))
            {
                diag.Add(Config.Strings.Get("diag.noUsageFile"));
                return result;
            }

            string text;
            try { text = Paths.ReadAllTextShared(path); }
            catch { diag.Add(Config.Strings.Get("diag.usageUnreadable")); return result; }

            var root = Json.ParseObject(text);
            if (root == null) { diag.Add(Config.Strings.Get("diag.usageUnparsable")); return result; }

            var samples = Json.Arr(root, "samples");
            if (samples == null || samples.Length == 0)
            {
                diag.Add(Config.Strings.Get("diag.noSamples"));
                return result;
            }

            foreach (var item in samples)
            {
                var s = item as Dictionary<string, object>;
                if (s == null) continue;
                var u = Json.Obj(s, "u");
                if (u == null) continue;

                // 項目が無いサンプルは捨てる。0% として読むと、形式が変わったときに
                // 「使用量ゼロ」と誤って表示してしまう（不明のまま出さない方がよい）。
                if (!u.ContainsKey("fh") || !u.ContainsKey("sd") || !s.ContainsKey("t")) continue;

                result.Add(new UsageSample
                {
                    AtUtc = FromUnixMs(Json.Long(s, "t")),
                    FiveHourPct = (int)Json.Long(u, "fh"),
                    WeeklyPct = (int)Json.Long(u, "sd"),
                    Org = Json.Str(s, "org"),
                });
            }

            if (result.Count == 0)
            {
                diag.Add(Config.Strings.Get("diag.noSamples"));
                return result;
            }

            result.Sort((a, b) => a.AtUtc.CompareTo(b.AtUtc));
            return OnlyLatestOrg(result);
        }

        /// <summary>
        /// 最新のサンプルと同じ組織（org）のサンプルだけを残す。
        ///
        /// サンプルは組織ごとに記録される。アカウントや組織を切り替えて使うと、
        /// 別の組織の値が混ざり、% が行き来したり、5時間枠の開始や週間枠のリセットを
        /// 誤って検出したりする。表示するのは「いま使っている組織」の値なので、最新に合わせる。
        /// org が書かれていない形式なら絞り込まない。
        /// </summary>
        private static List<UsageSample> OnlyLatestOrg(List<UsageSample> sorted)
        {
            var org = sorted[sorted.Count - 1].Org;
            if (string.IsNullOrEmpty(org)) return sorted;

            var kept = new List<UsageSample>(sorted.Count);
            foreach (var s in sorted)
                if (string.Equals(s.Org, org, StringComparison.Ordinal)) kept.Add(s);
            return kept;
        }

        public static RateLimitStatus Current(List<UsageSample> samples, DateTime nowUtc, Diagnostics diag)
        {
            if (samples == null || samples.Count == 0) return null;

            var last = samples[samples.Count - 1];
            var status = new RateLimitStatus
            {
                FiveHourPct = last.FiveHourPct,
                WeeklyPct = last.WeeklyPct,
                SampledAtUtc = last.AtUtc,
                StalenessSec = (int)Math.Max(0, (nowUtc - last.AtUtc).TotalSeconds),
            };

            var start = FindFiveHourWindowStart(samples, nowUtc);
            if (start.HasValue)
            {
                status.FiveHourWindowStartUtc = start;
                status.NextFiveHourResetUtc = start.Value.AddHours(5);
            }

            return status;
        }

        /// <summary>
        /// 5時間枠の開始時刻を推定する。
        ///
        /// 枠は「その枠で最初に使った時刻」から 5 時間で切れる。したがって
        /// fh が 0 → 正 に変わった箇所が枠の開始。サンプル間隔ぶんの幅があるので
        /// 安全側（早め）に倒して「最後に 0 だったサンプル」を採る。
        ///
        /// ★「fh が 0 に落ちた時刻を探す」実装は誤り。
        ///   Desktop 停止中はサンプルが空くため、0 の期間をまたいで前日の日時を掴む。
        ///   開発中の試作で実際に踏んだ。
        ///
        /// 開発中に 1 回、実際のリセット時刻と照合して 1 分差だった。
        /// ただし仕組み上、推定はサンプル間隔（5〜15 分）ぶん早めにずれうる。
        /// </summary>
        private static DateTime? FindFiveHourWindowStart(List<UsageSample> samples, DateTime nowUtc)
        {
            for (int i = samples.Count - 1; i >= 1; i--)
            {
                if (samples[i].FiveHourPct > 0 && samples[i - 1].FiveHourPct == 0)
                {
                    var start = samples[i - 1].AtUtc;
                    // 推定したリセットが既に過ぎているなら枠は切り替わっている＝推定不能。
                    return start.AddHours(5) > nowUtc ? start : (DateTime?)null;
                }
            }
            return null;
        }

        /// <summary>
        /// 週間枠がリセットされた区間を全部拾う。
        ///
        /// リセット時刻はどのファイルにも書かれていないので、
        /// 「sd が減った」区間を観測として集め、7 日周期で畳み込んで絞り込む（Core/WeeklyReset.cs）。
        /// 1 回の観測ではサンプルの空白ぶんの幅が残るため、幅が十分狭まるまでは
        /// 時刻を表示しない。
        /// </summary>
        public static List<ResetInterval> FindWeeklyResetIntervals(List<UsageSample> samples)
        {
            var result = new List<ResetInterval>();
            if (samples == null) return result;

            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].WeeklyPct < samples[i - 1].WeeklyPct)
                {
                    result.Add(new ResetInterval
                    {
                        FromUtc = samples[i - 1].AtUtc,
                        ToUtc = samples[i].AtUtc,
                        Before = samples[i - 1].WeeklyPct,
                        After = samples[i].WeeklyPct,
                    });
                }
            }
            return result;
        }

        public static DateTime FromUnixMs(long ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
        }
    }
}
