using System;
using System.Collections.Generic;
using System.Globalization;
using CtxTray.Collect;

namespace CtxTray.Core
{
    /// <summary>
    /// 週間枠のリセット時刻を「観測して絞り込む」。
    ///
    /// リセット時刻はどのローカルファイルにも書かれていない
    /// （plan-usage-history.json が持つのは t / fh / sd だけ）。
    /// 分かるのは「sd が減った」という事実と、その前後のサンプル時刻だけなので、
    /// 1 回の観測では「この区間のどこか」という幅が残る。
    ///
    /// 開発機（2026-09 時点）では、7 日周期で畳み込んで交差を取っても
    /// 15 時間前後の幅までしか絞れなかった。この精度で「あと何時間」と出すのは誤情報なので、
    /// 十分に狭まるまでは曜日どまりに留める（HUD には出さず、--verify-weekly でだけ見せる）。
    ///
    /// sd が減っても、リセットとは限らない。週の途中で 24% → 17% に減った記録がある（2026-09-16）。
    /// そうした観測は、先に絞り込んだ範囲と交差しなければ無視されるが、
    /// 最初の観測がそうしたものだと推定そのものが誤る。推定を表示しない理由の一つ。
    ///
    /// 絞り込みは 7 日を 5 分刻みのバケットに分け、各観測の区間で覆われた
    /// バケットの積集合を取る。円環上の区間交差を素直に書くより間違えにくい。
    /// </summary>
    internal static class WeeklyReset
    {
        private const int BucketMinutes = 5;
        private const int WeekMinutes = 7 * 24 * 60;
        private const int BucketCount = WeekMinutes / BucketMinutes;   // 2016

        private static readonly DateTime Reference = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>交差がこの幅以内に狭まったら時刻を出してよい。</summary>
        private const int PreciseMinutes = 60;

        public sealed class Estimate
        {
            public bool HasObservations;
            public int WidthMinutes;
            public DateTime? NextResetUtc;      // 十分に絞れたときだけ
            public DayOfWeek? Weekday;          // 曜日だけでも言えるとき
        }

        public static Estimate Estimate7Day(List<UsageSample> samples)
        {
            var result = new Estimate();
            var intervals = RateLimits.FindWeeklyResetIntervals(samples);
            if (intervals.Count == 0) return result;

            bool[] acc = null;

            foreach (var iv in intervals)
            {
                // 7 日以上の空白をまたいだ観測は、週のどこでも起こりうるので情報量ゼロ。
                if (iv.Width.TotalMinutes >= WeekMinutes) continue;

                var mask = ToMask(iv);
                if (acc == null)
                {
                    acc = mask;
                }
                else
                {
                    var merged = new bool[BucketCount];
                    var any = false;
                    for (var i = 0; i < BucketCount; i++)
                    {
                        merged[i] = acc[i] && mask[i];
                        if (merged[i]) any = true;
                    }
                    // 交差が空になるのは、周期が 7 日でないか観測が汚れている場合。
                    // 誤った時刻を出すより、絞り込みを諦めて既存の推定を保つ。
                    if (any) acc = merged;
                }
            }

            if (acc == null) return result;

            result.HasObservations = true;

            int start, length;
            if (!LongestRun(acc, out start, out length)) return result;

            result.WidthMinutes = length * BucketMinutes;

            var centerPhase = ((start + length / 2.0) * BucketMinutes) % WeekMinutes;
            var representative = Reference.AddMinutes(centerPhase).ToLocalTime();
            result.Weekday = representative.DayOfWeek;

            if (result.WidthMinutes <= PreciseMinutes)
                result.NextResetUtc = NextOccurrenceUtc(centerPhase);

            return result;
        }

        /// <summary>
        /// HUD に出す短い文字列。
        /// 絞り込めていなければ曜日だけ、観測が無ければ null（何も出さない）。
        /// </summary>
        public static string Hint(Snapshot snap)
        {
            if (snap == null) return null;
            var est = Estimate7Day(snap.Samples);
            if (!est.HasObservations || !est.Weekday.HasValue) return null;

            var day = Config.Strings.Weekday(est.Weekday.Value);

            if (est.NextResetUtc.HasValue)
                return day + " " + est.NextResetUtc.Value.ToLocalTime()
                                      .ToString("HH:mm", CultureInfo.InvariantCulture);

            return Config.Strings.Format("hud.estimating", day);
        }

        private static bool[] ToMask(ResetInterval iv)
        {
            var mask = new bool[BucketCount];

            var from = Phase(iv.FromUtc);
            var to = Phase(iv.ToUtc);

            var startBucket = (int)Math.Floor(from / BucketMinutes);
            var endBucket = (int)Math.Ceiling(to / BucketMinutes);

            var count = endBucket - startBucket;
            if (count < 0) count += BucketCount;      // 週境界をまたぐ区間
            if (count <= 0) count = 1;
            if (count > BucketCount) count = BucketCount;

            for (var i = 0; i <= count; i++)
                mask[((startBucket + i) % BucketCount + BucketCount) % BucketCount] = true;

            return mask;
        }

        private static double Phase(DateTime utc)
        {
            var minutes = (utc - Reference).TotalMinutes;
            var phase = minutes % WeekMinutes;
            return phase < 0 ? phase + WeekMinutes : phase;
        }

        /// <summary>円環上で最も長い連続領域を返す。</summary>
        private static bool LongestRun(bool[] mask, out int start, out int length)
        {
            start = 0;
            length = 0;

            var all = true;
            foreach (var b in mask) if (!b) { all = false; break; }
            if (all) { length = BucketCount; return true; }

            var bestStart = -1;
            var bestLen = 0;
            var curStart = -1;
            var curLen = 0;

            for (var i = 0; i < BucketCount * 2; i++)
            {
                var idx = i % BucketCount;
                if (mask[idx])
                {
                    if (curLen == 0) curStart = idx;
                    curLen++;
                    if (curLen > bestLen && curLen <= BucketCount) { bestLen = curLen; bestStart = curStart; }
                }
                else
                {
                    curLen = 0;
                }
            }

            if (bestStart < 0) return false;
            start = bestStart;
            length = bestLen;
            return true;
        }

        private static DateTime NextOccurrenceUtc(double phaseMinutes)
        {
            var now = DateTime.UtcNow;
            var nowPhase = Phase(now);
            var delta = phaseMinutes - nowPhase;
            if (delta <= 0) delta += WeekMinutes;
            return now.AddMinutes(delta);
        }

    }
}
