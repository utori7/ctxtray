using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using CtxTray.Core;

namespace CtxTray.Ui
{
    /// <summary>
    /// パネルのバーに付ける、色以外の手がかり。
    ///
    /// レベルはこれまで色だけで示していた（普段は値ごとの色、注意・危険だけ黄・赤）。
    /// 赤と緑の区別が付きにくい人には、5時間枠が「通常」なのか「危険」なのかが分からない。
    /// そこで注意・危険には斜めの縞も重ねる（注意は粗く、危険は細かく）。
    /// 実寸の見本（切れ目案・色だけの案と並べたもの）を見て利用者が選んだ（2026-09-20）。
    ///
    /// ★ 通知領域のアイコンには入れない（2026-09-20、利用者の決定）。
    ///   タスクバー（150% で 24px、目印付きだとバーは 22×5px）では、どの描き方も
    ///   はっきり見えるか・うるさくならないかのどちらかを満たせなかった。
    ///   実寸で 3 案（細い斜め・太い斜め・縦の切れ目）を見比べた結果、
    ///   アイコンは色だけのままにして、模様はパネルの役目とした。
    ///   ここを「HUD とトレイで規則が違う」と見て揃えに行かないこと。
    /// </summary>
    internal static class LevelMark
    {
        /// <summary>
        /// 塗り終わったバーに縞を重ねる。注意は粗く、危険は細かく。
        /// </summary>
        /// <param name="filled">塗った部分（バーの左端から埋まっているところまで）。</param>
        /// <param name="track">バーの下地の色。縞はこの色で入れる（削れて見せるため）。</param>
        /// <param name="enabled">設定（thresholds.marks）。オフなら何もしない。</param>
        public static void Decorate(Graphics g, RectangleF filled, Level level, Color track, bool enabled)
        {
            if (!enabled || level == Level.Normal) return;
            if (filled.Width <= 0 || filled.Height <= 0) return;

            // 危険は細かく、注意は粗く。深刻さの順に読めるようにする。
            var gap = filled.Height * (level == Level.Danger ? 1.1f : 2.0f);
            if (gap <= 0.5f) return;

            // バーからはみ出さないよう、塗った部分で切り取ってから描く。
            var clip = g.Clip;
            try
            {
                g.SetClip(filled, CombineMode.Intersect);
                using (var pen = new Pen(track, Math.Max(1f, filled.Height * 0.26f)))
                {
                    // 右上がりの線を、バーの高さぶんずらしながら並べる。
                    for (var x = filled.Left - filled.Height; x < filled.Right + filled.Height; x += gap)
                        g.DrawLine(pen, x, filled.Bottom, x + filled.Height, filled.Top);
                }
            }
            finally
            {
                g.Clip = clip;
            }
        }
    }
}
