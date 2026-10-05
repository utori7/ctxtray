using System;
using CtxTray.Collect;
using CtxTray.Config;

namespace CtxTray.Core
{
    /// <summary>
    /// 5時間枠のリセット時刻を、パネルの行とトレイのツールチップに出すか（設定 showResets）。
    ///
    /// 既定（warn）は、5時間枠が注意の使用率を超えたときだけ出す。判定は行の色と同じ Levels を使う。
    /// リセット時刻が知りたくなるのは使用率が高いときなので、使用率で出し分ける（2026-10-03、利用者の決定）。
    /// 0.11.0 までの auto は「リセットの 30 分前から」で、使用率 95% でもリセットまで 3 時間あれば出ず、
    /// 10% でも残り 30 分を切れば出ていた。設定ファイルの auto は warn として読む。
    /// </summary>
    internal static class ResetDisplay
    {
        public const string Warn = "warn";
        public const string Always = "always";
        public const string Never = "never";

        /// <summary>設定画面のドロップダウンと同じ並び（既定値が先頭）。</summary>
        public static readonly string[] Modes = { Warn, Always, Never };

        /// <summary>設定ファイルの値を読む。大文字小文字は問わない。旧 auto と知らない値は既定の warn。</summary>
        public static string Normalize(string mode)
        {
            foreach (var m in Modes)
                if (string.Equals(mode, m, StringComparison.OrdinalIgnoreCase)) return m;
            return Warn;
        }

        public static bool ShouldShow(RateLimitStatus r, AppConfig cfg, DateTime nowUtc)
        {
            if (r == null || !r.NextFiveHourResetUtc.HasValue) return false;

            var mode = Normalize(cfg.ShowResets);
            if (mode == Never) return false;
            if (mode == Always) return true;

            // 過ぎたリセットの時刻は出さない（新しい記録が来るまで使用率は前の枠のまま）。
            return r.NextFiveHourResetUtc.Value > nowUtc
                   && Levels.ForFiveHour(r, cfg) >= Level.Warn;
        }
    }
}
