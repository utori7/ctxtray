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
        /// コンテキストの「圧縮点までの到達率」。
        /// 1.0 で auto-compact が走る想定。分母不明なら null。
        /// </summary>
        public static double? ContextReach(SessionRow s, AppConfig cfg)
        {
            if (s == null || !s.ContextTokens.HasValue || !s.ContextLimit.HasValue) return null;
            if (s.ContextLimit.Value <= 0) return null;

            var compactPoint = cfg.CompactThreshold * s.ContextLimit.Value;
            if (compactPoint <= 0) return null;

            return s.ContextTokens.Value / compactPoint;
        }

        // slackPts は通知の「下がった」判定（ヒステリシス）でだけ使う。
        // 閾値をこのポイント数だけ下げて判定し、それでも下のレベルなら「下がった」とみなす。
        // 表示の色は常に slackPts = 0 で判定する。

        public static Level ForContext(SessionRow s, AppConfig cfg, double slackPts = 0)
        {
            var reach = ContextReach(s, cfg);
            if (!reach.HasValue) return Level.Normal;

            // 到達率は 1.0 = 100% で持つので、ポイントは 1/100 にして下げる。
            var slack = slackPts / 100.0;
            return ForPct(reach.Value, cfg.ContextWarn - slack, cfg.ContextDanger - slack);
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
