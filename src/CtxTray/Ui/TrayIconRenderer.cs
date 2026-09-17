using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using CtxTray.Core;
using CtxTray.Native;

namespace CtxTray.Ui
{
    /// <summary>トレイアイコンに描く 1 つの値。</summary>
    internal sealed class TrayGauge
    {
        /// <summary>値の名前（context / fiveHour / weekly）。目印と識別色を選ぶのに使う。</summary>
        public string Value;

        /// <summary>0〜1。バーの埋まり具合。</summary>
        public double Fraction;

        /// <summary>色を決めるレベル。値ごとに独立して判定する。</summary>
        public Level Level = Level.Normal;

        /// <summary>%。null なら「値が読めていない」として下地だけ描く。</summary>
        public double? Percent;

        public bool HasValue { get { return Percent.HasValue; } }
    }

    /// <summary>
    /// 通知領域のアイコンを描く。
    ///
    /// 数字は描かない。量はバーの長さで大まかに示し、正確な値はツールチップと HUD に任せる。
    /// 16〜24px の数字は大きくしても読むのに一瞬かかり、3 つ並べると何の数字かも
    /// 分かりにくかった。数字の代わりに「何の値か」を示す方が親切、という利用者の判断（2026-09-16）。
    ///
    /// スタイルは 3 つ:
    ///   bars    … 1 個にまとめるモード。選んだ値 1 つにつき横のバー 1 本（上から context / fiveHour / weekly）
    ///   letters … 値ごとに分けるモード。何の値かを英字（C / 5h / W）で示し、下にバー
    ///   glyphs  … 同上。英字の代わりに絵記号（吹き出し / 時計 / カレンダー）
    ///
    /// 色の規則はすべて共通で、HUD とも同じ（Theme.ColorFor）: 普段は値ごとの色、注意・危険になった値だけ黄・赤。
    /// 以前の「危険時は背景を塗る（反転）」はやめた。1 個にまとめるモードでは
    /// どの値が危ないのかが分からなくなり、両モードで規則を揃えられないため。
    /// </summary>
    internal static class TrayIconRenderer
    {
        public const string BarsStyle = "bars";
        public const string LettersStyle = "letters";
        public const string GlyphsStyle = "glyphs";

        /// <summary>
        /// 呼び出し側は差し替え後に古い Icon を Dispose すること
        /// （GDI ハンドルは自動では解放されない）。
        /// </summary>
        /// <param name="sideOverride">
        /// 0 以外を渡すとその一辺で描く。設定画面の見本と、各表示倍率での見え方を
        /// 実寸で確かめるための入口。通常は 0 で、画面の DPI から決める。
        /// </param>
        public static Icon Render(IList<TrayGauge> gauges, string style, Theme theme, int sideOverride = 0)
        {
            var side = sideOverride > 0 ? sideOverride : CanvasSide();

            using (var bmp = new Bitmap(side, side))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // 整数座標の矩形がにじまないよう、画素の中心ではなく角に合わせる。
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    // 背景が透明なので ClearType は使わない（文字の縁に色が付く）。
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    if (IsStyle(style, BarsStyle))
                    {
                        DrawBars(g, side, gauges, theme);
                    }
                    else
                    {
                        var gauge = (gauges != null && gauges.Count > 0) ? gauges[0] : null;
                        DrawMarked(g, side, gauge, IsStyle(style, GlyphsStyle), theme);
                    }
                }

                var handle = bmp.GetHicon();
                try
                {
                    // Icon.FromHandle はハンドルを所有しないので、複製して返す。
                    using (var tmp = Icon.FromHandle(handle))
                        return (Icon)tmp.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private static bool IsStyle(string style, string name)
        {
            return string.Equals(style, name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 通知領域のアイコンは表示倍率に追従させないとぼやける。
        /// SystemInformation.SmallIconSize が 96dpi 相当を返すことがあるので、
        /// 画面の実 DPI から求めた値と大きい方を採る。
        /// </summary>
        public static int CanvasSide()
        {
            var fromDpi = 16;
            try
            {
                using (var screen = Graphics.FromHwnd(IntPtr.Zero))
                    fromDpi = (int)Math.Round(16 * screen.DpiX / 96f);
            }
            catch { }

            return Math.Max(Math.Max(16, fromDpi), SystemInformation.SmallIconSize.Width);
        }

        /// <summary>
        /// 目印とバーの色。値が読めていなければ下地の色、読めていれば HUD と同じ規則（Theme.ColorFor）。
        /// </summary>
        private static Color ColorFor(TrayGauge gauge, Theme theme)
        {
            if (gauge == null || !gauge.HasValue) return theme.TrayTrack;
            return theme.ColorFor(gauge.Value, gauge.Level);
        }

        // --- bars（1 個にまとめるモード） ---------------------------------------

        private static void DrawBars(Graphics g, int side, IList<TrayGauge> gauges, Theme theme)
        {
            var count = gauges == null ? 0 : Math.Min(3, gauges.Count);
            if (count == 0) return;

            int thickness, gap;
            BarLayout(side, count, out thickness, out gap);

            // バーの束を縦の中央に置く。長さは左右 1px を残して横いっぱい。
            var total = thickness * count + gap * (count - 1);
            var top = (int)Math.Round((side - total) / 2.0);
            var radius = Math.Min(thickness / 2f, side * 0.1f);

            for (var i = 0; i < count; i++)
            {
                var rect = new RectangleF(1, top + i * (thickness + gap), side - 2, thickness);
                DrawBar(g, rect, gauges[i], theme, radius);
            }
        }

        /// <summary>
        /// バーの太さと間隔。16px と 24px は実寸のモックアップで決めた値、
        /// それ以外（表示倍率 125%・200% など）は 24px と同じ比率で求める。
        /// </summary>
        private static void BarLayout(int side, int count, out int thickness, out int gap)
        {
            if (side <= 16)
            {
                if (count >= 3) { thickness = 4; gap = 1; return; }
                if (count == 2) { thickness = 6; gap = 2; return; }
                thickness = 7; gap = 0;
                return;
            }

            var spacing = Math.Max(1, (int)Math.Round(side * 0.083));
            if (count >= 3)
            {
                thickness = (int)Math.Round(side * 0.25);
                gap = spacing;
                return;
            }

            thickness = (int)Math.Round(side * 0.42);
            gap = count == 2 ? spacing : 0;
        }

        private static void DrawBar(Graphics g, RectangleF rect, TrayGauge gauge, Theme theme, float radius)
        {
            using (var brush = new SolidBrush(theme.TrayTrack))
            using (var path = RoundedRect(rect, radius))
                g.FillPath(brush, path);

            if (gauge == null || !gauge.HasValue) return;

            var fraction = Math.Max(0.0, Math.Min(1.0, gauge.Fraction));
            // 丸い端が作れる最小の長さは描く。0% でも「値はある」ことが分かるように。
            var filled = Math.Max(radius * 2, (float)(rect.Width * fraction));

            using (var brush = new SolidBrush(ColorFor(gauge, theme)))
            using (var path = RoundedRect(new RectangleF(rect.X, rect.Y, filled, rect.Height), radius))
                g.FillPath(brush, path);
        }

        // --- letters / glyphs（値ごとに分けるモード） ---------------------------

        /// <summary>
        /// 上に目印、下にバー。寸法は実寸のモックアップ（24px: 目印の枠 高さ約 14・バー 5）に合わせ、
        /// 他の大きさは同じ比率で求める。
        /// </summary>
        private static void DrawMarked(Graphics g, int side, TrayGauge gauge, bool glyphs, Theme theme)
        {
            var barH = Math.Max(3, (int)Math.Round(side * 0.22));
            var barY = side - 1 - barH;
            DrawBar(g, new RectangleF(1, barY, side - 2, barH), gauge, theme, barH / 2f);

            var gap = Math.Max(1.5f, side * 0.1f);
            var pad = side <= 16 ? 0.5f : 1f;
            var box = new RectangleF(pad, pad, side - pad * 2, barY - gap - pad);

            var color = ColorFor(gauge, theme);
            var value = gauge == null ? null : gauge.Value;
            if (glyphs) DrawGlyph(g, value, box, color, side);
            else FillFittedText(g, LetterFor(value), box, color);
        }

        /// <summary>
        /// 英字の目印。表示言語によらず同じにする（ツールチップと設定画面に意味を書いてある）。
        /// </summary>
        private static string LetterFor(string value)
        {
            if (Is(value, "context")) return "C";
            if (Is(value, "fiveHour")) return "5h";
            if (Is(value, "weekly")) return "W";
            return "?";
        }

        private static bool Is(string value, string name)
        {
            return string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 絵記号の目印。高さ 14 の枠を基準に作った形を、枠の高さに合わせて伸縮する。
        ///   コンテキスト … 吹き出し（会話の量）
        ///   5時間枠     … 時計
        ///   週間枠       … カレンダー
        /// </summary>
        private static void DrawGlyph(Graphics g, string value, RectangleF box, Color color, int side)
        {
            var k = box.Height / 14f;
            var cx = box.X + box.Width / 2f;
            var cy = box.Y + box.Height / 2f;
            var lineWidth = Math.Max(1.3f, side * 0.075f);

            using (var pen = new Pen(color, lineWidth))
            using (var brush = new SolidBrush(color))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                if (Is(value, "fiveHour"))
                {
                    var r = 6.2f * k;
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx, cy - r * 0.62f),
                        new PointF(cx, cy),
                        new PointF(cx + r * 0.5f, cy + r * 0.2f),
                    });
                }
                else if (Is(value, "weekly"))
                {
                    var w = 14f * k;
                    var h = 12f * k;
                    var x = cx - w / 2f;
                    var y = cy - h / 2f;
                    using (var path = RoundedRect(new RectangleF(x, y, w, h), 2f * k))
                        g.DrawPath(pen, path);
                    g.FillRectangle(brush, x, y, w, 3.5f * k);
                    var cell = 2.4f * k;
                    g.FillRectangle(brush, x + 3f * k, y + 6f * k, cell, cell);
                    g.FillRectangle(brush, x + 6.8f * k, y + 6f * k, cell, cell);
                }
                else
                {
                    var w = 15f * k;
                    var h = 10f * k;
                    var x = cx - w / 2f;
                    var y = cy - h / 2f - 1f * k;
                    using (var path = RoundedRect(new RectangleF(x, y, w, h), 2.5f * k))
                        g.DrawPath(pen, path);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(x + 3.5f * k, y + h),
                        new PointF(x + 3f * k, y + h + 3.5f * k),
                        new PointF(x + 7f * k, y + h),
                    });
                    var line = 1.3f * k;
                    g.FillRectangle(brush, x + 3f * k, y + 3f * k, w - 6f * k, line);
                    g.FillRectangle(brush, x + 3f * k, y + 5.8f * k, w - 8f * k, line);
                }
            }
        }

        /// <summary>
        /// 文字を輪郭（パス）にして、実際にインクが乗る範囲を枠に合わせて塗る。
        ///
        /// MeasureString の高さは書体の上下の余白まで含むので、高さで合わせると
        /// 文字が 3 割ほど小さくなる。輪郭の外接矩形なら文字そのものの大きさで合わせられる。
        /// 目印は枠の高さで決まるので、1 文字（C）と 2 文字（5h）で大きさが揃う。
        /// </summary>
        private static void FillFittedText(Graphics g, string text, RectangleF box, Color color)
        {
            if (box.Width <= 0 || box.Height <= 0) return;

            using (var path = new GraphicsPath())
            {
                using (var family = OpenBoldFamily())
                {
                    var style = family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
                    path.AddString(text, family, (int)style, 100f, PointF.Empty, StringFormat.GenericTypographic);
                }

                var b = path.GetBounds();
                if (b.Width <= 0 || b.Height <= 0) return;

                var scale = Math.Min(box.Width / b.Width, box.Height / b.Height);
                var w = b.Width * scale;
                var h = b.Height * scale;

                using (var m = new Matrix())
                {
                    // 後から足した変換ほど先に効く（既定の MatrixOrder.Prepend）。
                    // 原点へ寄せる → 拡大 → 枠の中央へ、の順になる。
                    m.Translate(box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f);
                    m.Scale(scale, scale);
                    m.Translate(-b.X, -b.Y);
                    path.Transform(m);
                }

                using (var brush = new SolidBrush(color))
                    g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// 太字を選べる書体を開く。可変フォントは GDI+ から太字を選べない場合があるので、
        /// 選べなければ Segoe UI、それも無ければ既定のサンセリフに落とす。
        /// </summary>
        private static FontFamily OpenBoldFamily()
        {
            foreach (var name in new[] { Theme.FontFamily, "Segoe UI" })
            {
                try
                {
                    var family = new FontFamily(name);
                    if (family.IsStyleAvailable(FontStyle.Bold)) return family;
                    family.Dispose();
                }
                catch { }
            }
            return new FontFamily(GenericFontFamilies.SansSerif);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
