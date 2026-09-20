using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using CtxTray.Core;

namespace CtxTray.Ui
{
    /// <summary>
    /// バーに付ける、色以外の手がかり。
    ///
    /// レベルはこれまで色だけで示していた（普段は値ごとの色、注意・危険だけ黄・赤）。
    /// 赤と緑の区別が付きにくい人には、5時間枠が「通常」なのか「危険」なのかが分からない。
    /// バーの長さも手がかりにはなるが、通知領域の 16px では長さ自体が読み取りにくい。
    ///
    /// 色の規則を Theme.ColorFor の 1 か所で決めているのと同じく、形もここだけで決める。
    /// HUD（HudForm.DrawBar）と通知領域（TrayIconRenderer.DrawBar）が同じ規則を見る。
    ///
    /// 描き方は斜めの縞。実寸の見本（切れ目案・色だけの案と並べたもの）を見て利用者が選んだ
    /// （2026-09-20）。16px でも、普段の塗りつぶしと見分けられることを確かめてある。
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
