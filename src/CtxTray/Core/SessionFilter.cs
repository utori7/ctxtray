using System;
using System.Collections.Generic;
using CtxTray.Collect;
using CtxTray.Config;

namespace CtxTray.Core
{
    /// <summary>
    /// 常駐表示の対象にするセッションを絞る。
    ///
    /// HUD・トレイアイコン・ツールチップ・通知は、すべてこの結果だけを見る。
    /// HUD では隠したのに、トレイの色や通知は隠した行に反応する、という食い違いを作らないため。
    /// --json / --status は診断用なので絞り込まない（何が読めているかを全部見せる）。
    /// </summary>
    internal static class SessionFilter
    {
        public static void Apply(Snapshot snap, AppConfig config, DateTime nowUtc)
        {
            if (snap == null || config == null) return;

            var kept = new List<SessionRow>();
            var hidden = 0;
            var external = 0;

            // 並びは新しい順（SnapshotBuilder で整列済み）。上限で落ちるのは古い方になる。
            foreach (var s in snap.Sessions)
            {
                if (config.HideIdleSessions && IsIdle(s, config.IdleHours, nowUtc))
                {
                    hidden++;
                    continue;
                }

                if (s.IsExternal)
                {
                    if (external >= config.ExternalSessionsMax)
                    {
                        hidden++;
                        continue;
                    }
                    external++;
                }

                kept.Add(s);
            }

            snap.Sessions = kept;
            snap.HiddenSessionCount = hidden;
        }

        private static bool IsIdle(SessionRow s, int hours, DateTime nowUtc)
        {
            var last = s.LastActivityUtc ?? s.LastMeasuredUtc;

            // いつ使ったか分からないものは隠さない。黙って消える方が困る。
            if (!last.HasValue) return false;

            return (nowUtc - last.Value).TotalHours >= hours;
        }
    }
}
