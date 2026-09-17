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
    ///
    /// HUD は Claude Code のプロセスが止まっている行も淡く出すが、トレイ・ツールチップ・通知は
    /// 動いている行（IsRunning）だけを見る。止まっているセッションは消費量が増えず、
    /// 知らせても対処できないため。以前は止まっている行の値がトレイに出て、
    /// 作業中のセッションの値が見えなかった。
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

        /// <summary>トレイ・ツールチップ・通知の対象か（HUD で淡く出ない行か）。</summary>
        public static bool IsRunning(SessionRow s)
        {
            return s != null && s.ProcessAlive;
        }

        /// <summary>
        /// トレイとツールチップに出す、動いている中で最も圧縮に近いセッション。無ければ null。
        ///
        /// 選ぶ基準は % ではなく圧縮点への到達率。分母の違うモデル (1M と 200k) が
        /// 混ざっても、圧縮に近い方を選べるようにしておく。
        /// </summary>
        public static SessionRow MostPressed(Snapshot snap, AppConfig config)
        {
            if (snap == null || config == null) return null;

            SessionRow worst = null;
            var worstReach = double.MinValue;
            foreach (var s in snap.Sessions)
            {
                if (!IsRunning(s)) continue;

                var reach = Levels.ContextReach(s, config);
                if (!reach.HasValue) continue;
                if (reach.Value > worstReach) { worstReach = reach.Value; worst = s; }
            }
            return worst;
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
