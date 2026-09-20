using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CtxTray.Ui
{
    /// <summary>
    /// アプリそのもののアイコン（タスクバー・Alt+Tab・Explorer の exe）。
    ///
    /// 絵は「3 重リング」。外から 青＝コンテキスト / 緑＝5時間枠 / 紫＝週間枠 で、
    /// HUD・トレイと同じ値の並びと同じ識別色（Theme.IdContext / IdFiveHour / IdWeekly）。
    /// 2026-09-20 に利用者が実寸の候補から選んだ。トレイアイコンは横向きのバーなので、
    /// アイコンで縦向きのバーを使うと向きが食い違う。リングなら向きの矛盾が起きない。
    ///
    /// トレイアイコンと違い、**実際の値は反映しない**。埋まり具合は固定。
    /// 実データを映すのはトレイアイコンの役目で、タスクバーや Explorer に出るものは
    /// 「このアプリだ」と分かるための固定の目印。
    ///
    /// 地は濃紺のタイル。明るい壁紙の上でも暗いタスクバーの上でも読めるようにするため
    /// （地なしだと Explorer の白背景でリングが薄く見える）。
    /// </summary>
    internal static class AppIconRenderer
    {
        /// <summary>.ico に入れる大きさ。Windows が場面ごとに選ぶ。</summary>
        public static readonly int[] IconSizes = { 16, 20, 24, 32, 48, 64, 256 };

        /// <summary>タイルの地。Theme.Dark().Background より少し明るい濃紺。</summary>
        private static readonly Color Tile = Color.FromArgb(27, 31, 39);

        /// <summary>リングの下地。Theme.Dark().TrayTrack と同じ。</summary>
        private static readonly Color Track = Color.FromArgb(62, 68, 82);

        /// <summary>固定の埋まり具合（外・中）。値そのものに意味はない。</summary>
        private static readonly double[] Fractions = { 0.72, 0.55 };

        /// <summary>一辺 side の絵を描く。</summary>
        public static Bitmap Render(int side)
        {
            var dark = Theme.Dark();
            var colors = new[] { dark.IdContext, dark.IdFiveHour, dark.IdWeekly };

            var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // 角丸タイル。Windows 11 のアイコンに合わせて角はやや大きめ。
                using (var brush = new SolidBrush(Tile))
                using (var path = RoundedRect(new RectangleF(0, 0, side, side), side * 0.22f))
                    g.FillPath(brush, path);

                var center = side / 2f;

                // 小さい方では線を減らす。Windows のアイコンも小さい版は形を簡略化する。
                // 3 本とも描くと 16px では色の塊にしかならず、何のアイコンか分からなくなった
                // （2026-09-20、--app-icon-preview で実寸を見て決めた）。
                if (side < 22)
                {
                    // 外のリング 1 本と点だけ。下地も描かない。
                    var thin = Math.Max(2f, side * (3.6f / 32f));
                    DrawRing(g, center, side * (11.0f / 32f), thin, colors[0], Fractions[0]);
                }
                else
                {
                    // 32 を基準に決めた寸法を、そのまま比で伸ばす。
                    var stroke = Math.Max(1.6f, side * (3.0f / 32f));
                    var radii = new[] { side * (12.6f / 32f), side * (7.8f / 32f) };

                    for (var i = 0; i < radii.Length; i++)
                    {
                        // 下地は 32px 未満では省く。線が重なって輪郭がぼやけるため。
                        if (side >= 32) DrawRing(g, center, radii[i], stroke, Track, 1.0);
                        DrawRing(g, center, radii[i], stroke, colors[i], Fractions[i]);
                    }
                }

                // いちばん内側（週間枠）はリングにせず塗りつぶしの点にする。
                // 16px では半径 2px を切り、リングにすると穴が潰れて何も見えなくなるため。
                var dot = side * (side < 22 ? 3.8f : 3.2f) / 32f;
                using (var brush = new SolidBrush(colors[2]))
                    g.FillEllipse(brush, center - dot, center - dot, dot * 2, dot * 2);
            }

            return bmp;
        }

        /// <summary>真上から時計回りに fraction ぶんの弧を描く。1.0 なら円 1 周。</summary>
        private static void DrawRing(Graphics g, float center, float radius, float stroke,
                                     Color color, double fraction)
        {
            if (radius <= 0 || fraction <= 0) return;

            var box = new RectangleF(center - radius, center - radius, radius * 2, radius * 2);
            using (var pen = new Pen(color, stroke))
            {
                // 丸い端は一周のときだけ邪魔になる（始点と終点が重なって太る）。
                pen.StartCap = fraction >= 1.0 ? LineCap.Flat : LineCap.Round;
                pen.EndCap = pen.StartCap;

                if (fraction >= 1.0) g.DrawEllipse(pen, box);
                else g.DrawArc(pen, box, -90f, (float)(360.0 * fraction));
            }
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

        // --- .ico の書き出し ----------------------------------------------------

        /// <summary>
        /// 全サイズを 1 つの .ico にまとめて書き出す。
        ///
        /// Icon.Save は 1 枚しか保持できないので使わず、ICONDIR と ICONDIRENTRY を自分で組む。
        /// 各サイズは PNG のまま入れる（Windows Vista 以降が読める形式。
        /// このアプリのターゲットは Windows 10 1903 以降なので問題ない）。
        /// </summary>
        public static void WriteIco(string path)
        {
            var images = new List<byte[]>();
            foreach (var size in IconSizes)
            {
                using (var bmp = Render(size))
                using (var mem = new MemoryStream())
                {
                    bmp.Save(mem, ImageFormat.Png);
                    images.Add(mem.ToArray());
                }
            }

            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(file))
            {
                w.Write((ushort)0);                    // 予約
                w.Write((ushort)1);                    // 種別: 1 = アイコン
                w.Write((ushort)images.Count);

                // 画像の本体は、全エントリの後ろに順に置く。
                var offset = 6 + 16 * images.Count;
                for (var i = 0; i < images.Count; i++)
                {
                    var side = IconSizes[i];
                    w.Write((byte)(side >= 256 ? 0 : side));   // 0 は 256 を意味する
                    w.Write((byte)(side >= 256 ? 0 : side));
                    w.Write((byte)0);                  // 色数（32bit なので 0）
                    w.Write((byte)0);                  // 予約
                    w.Write((ushort)1);                // プレーン数
                    w.Write((ushort)32);               // ビット深度
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }

                foreach (var image in images) w.Write(image);
            }
        }

        // --- 埋め込みの読み込み --------------------------------------------------

        /// <summary>
        /// exe に埋め込んだ .ico を読む。Form.Icon に入れて使う。
        ///
        /// csproj の ApplicationIcon は exe の Win32 リソース（Explorer やショートカット用）で、
        /// .NET Framework の WinForms はそれを Form.Icon の既定値にしない
        /// （内蔵の古い既定アイコンが出る）。Icon.ExtractAssociatedIcon は 32×32 しか
        /// 返さないので、表示倍率 150% のタスクバーでぼやける。だから別に埋め込んで読む。
        /// </summary>
        public static Icon Load()
        {
            if (_loaded) return _icon;
            _loaded = true;

            try
            {
                var assembly = typeof(AppIconRenderer).Assembly;
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith("ctxtray.ico", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream != null) _icon = new Icon(stream);
                    }
                    break;
                }
            }
            catch { }

            return _icon;
        }

        private static Icon _icon;
        private static bool _loaded;
    }
}
