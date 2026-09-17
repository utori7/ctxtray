using System;
using System.Collections.Generic;
using CtxTray.Collect;

namespace CtxTray.Core
{
    /// <summary>
    /// 圧縮の検出。
    ///
    /// 以前はここで「圧縮まであと何ターンか」を予測して HUD に出していたが、
    /// 実際の圧縮と突き合わせた検証が一度もできておらず、前提の圧縮点 (0.92) も
    /// 未観測の仮置きだったため削除した（2026-09-15、利用者の判断）。
    /// 「推測で埋めない」という設計原則に合わせた。
    ///
    /// DetectCompactions は圧縮点の較正（calibration.json、未実装）で使うために残している。
    /// </summary>
    internal static class Forecast
    {
        /// <summary>前ターンからこの割合以上減っていたら圧縮が起きたとみなす。</summary>
        private const double CompactionDropRatio = 0.30;

        /// <summary>
        /// 圧縮が起きた形跡を探す。
        ///
        /// 圧縮点 (CompactThreshold) は調査中に一度も観測できなかったため、
        /// 出荷時の 0.92 はあくまで仮置き。実際に圧縮を観測したら、
        /// その直前のピーク ÷ 分母 を記録して置き換える。
        /// 「まだ観測していない」ことを暫定値で覆い隠さないための仕組み。
        /// </summary>
        public static List<CompactionEvent> DetectCompactions(List<UsagePoint> series, int limit)
        {
            var events = new List<CompactionEvent>();
            if (series == null || limit <= 0) return events;

            for (var i = 1; i < series.Count; i++)
            {
                var before = series[i - 1].PromptTokens;
                var after = series[i].PromptTokens;
                if (before <= 0) continue;

                var drop = (before - after) / (double)before;
                if (drop < CompactionDropRatio) continue;

                events.Add(new CompactionEvent
                {
                    AtUtc = series[i].AtUtc,
                    Model = series[i - 1].Model,
                    PeakTokens = before,
                    Limit = limit,
                    Ratio = before / (double)limit,
                });
            }

            return events;
        }
    }

    internal sealed class CompactionEvent
    {
        public DateTime AtUtc;
        public string Model;
        public int PeakTokens;
        public int Limit;
        public double Ratio;
    }
}
