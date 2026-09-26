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

            // ★ 落とした行も取っておく（HUD の「ほか N 件は非表示」を押したときに描く）。
            //   Sessions には入れないので、トレイ・ツールチップ・通知の対象は今までどおり。
            var hiddenRows = new List<SessionRow>();
            var external = 0;

            // 並びは新しい順（SnapshotBuilder で整列済み）。上限で落ちるのは古い方になる。
            foreach (var s in snap.Sessions)
            {
                if (config.HideIdleSessions && IsIdle(s, config.IdleHours, nowUtc))
                {
                    hiddenRows.Add(s);
                    continue;
                }

                // 止まっている行（HUD で淡く出るもの）を出さない設定。既定はオフ。
                // トレイ・ツールチップ・通知は元から動いている行しか見ないので、
                // これは HUD の縦の長さを抑えるための設定（2026-09-18）。
                if (config.HideStoppedSessions && !IsRunning(s))
                {
                    hiddenRows.Add(s);
                    continue;
                }

                if (s.IsExternal)
                {
                    if (external >= config.ExternalSessionsMax)
                    {
                        hiddenRows.Add(s);
                        continue;
                    }
                    external++;
                }

                kept.Add(s);
            }

            snap.Sessions = kept;
            snap.HiddenSessions = hiddenRows;
            snap.HiddenSessionCount = hiddenRows.Count;
        }

        /// <summary>トレイ・ツールチップ・通知の対象か（HUD で淡く出ない行か）。</summary>
        public static bool IsRunning(SessionRow s)
        {
            return s != null && s.ProcessAlive;
        }

        /// <summary>
        /// トレイとツールチップに出す、動いている中で最も圧縮に近いセッション。無ければ null。
        ///
        /// 選ぶ基準はトークン数ではなくウィンドウに対する割合。分母の違うモデル (1M と 200k) が
        /// 混ざっても、上限に近い方を選べるようにしておく（圧縮点は全モデル共通の割合なので、
        /// 圧縮に近い順とも同じ）。
        /// </summary>
        public static SessionRow MostPressed(Snapshot snap, AppConfig config)
        {
            if (snap == null || config == null) return null;

            SessionRow worst = null;
            var worstRatio = double.MinValue;
            foreach (var s in snap.Sessions)
            {
                if (!IsRunning(s)) continue;

                var ratio = Levels.ContextRatio(s);
                if (!ratio.HasValue) continue;
                if (ratio.Value > worstRatio) { worstRatio = ratio.Value; worst = s; }
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
