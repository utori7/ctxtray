using System;
using CtxTray.Collect;
using CtxTray.Config;

namespace CtxTray.Core
{
    internal enum Level
    {
        Normal = 0,
        Warn = 1,
        Danger = 2,
    }

    /// <summary>
    /// 閾値の判定を 1 か所に集める。
    ///
    /// HUD の行の色、トレイアイコンの色、通知の発火は、すべてこの結果を使う。
    /// 「色は 60%、通知は 70%」のように別々の数字を持つと、片方だけ直したときに
    /// 食い違う。設定 (AppConfig) にも閾値は 1 組しか無い。
    /// </summary>
    internal static class Levels
    {
        /// <summary>
        /// コンテキストの「ウィンドウに対する割合」（1.0 = 上限）。分母不明なら null。
        ///
        /// トレイ・HUD・通知に出す % と同じ基準。閾値もこれと比べる。
        /// 以前は「圧縮点までの到達率」で比べていたが、設定の 75 がパネルでは 73 で色が変わり、
        /// 分かりにくかった（2026-09-26 利用者の提案で変更）。
        /// </summary>
        public static double? ContextRatio(SessionRow s)
        {
            if (s == null || !s.ContextTokens.HasValue || !s.ContextLimit.HasValue) return null;
            if (s.ContextLimit.Value <= 0) return null;

            return (double)s.ContextTokens.Value / s.ContextLimit.Value;
        }

        // slackPts は通知の「下がった」判定（ヒステリシス）でだけ使う。
        // 閾値をこのポイント数だけ下げて判定し、それでも下のレベルなら「下がった」とみなす。
        // 表示の色は常に slackPts = 0 で判定する。

        public static Level ForContext(SessionRow s, AppConfig cfg, double slackPts = 0)
        {
            var ratio = ContextRatio(s);
            if (!ratio.HasValue) return Level.Normal;

            // 割合は 1.0 = 100% で持つので、ポイントは 1/100 にして下げる。
            var slack = slackPts / 100.0;
            return ForPct(ratio.Value, cfg.ContextWarn - slack, cfg.ContextDanger - slack);
        }

        public static Level ForPct(double pct, double warn, double danger)
        {
            if (pct >= danger) return Level.Danger;
            if (pct >= warn) return Level.Warn;
            return Level.Normal;
        }

        public static Level ForFiveHour(RateLimitStatus r, AppConfig cfg, double slackPts = 0)
        {
            if (r == null) return Level.Normal;
            return ForPct(r.FiveHourPct, cfg.FiveHourWarn - slackPts, cfg.FiveHourDanger - slackPts);
        }

        public static Level ForWeekly(RateLimitStatus r, AppConfig cfg, double slackPts = 0)
        {
            if (r == null) return Level.Normal;
            return ForPct(r.WeeklyPct, cfg.WeeklyWarn - slackPts, cfg.WeeklyDanger - slackPts);
        }

        public static Level Max(Level a, Level b)
        {
            return (Level)Math.Max((int)a, (int)b);
        }
    }
}
