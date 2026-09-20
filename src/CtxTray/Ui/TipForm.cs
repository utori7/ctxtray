using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CtxTray.Native;

namespace CtxTray.Ui
{
    /// <summary>詳細の札の 1 行。淡く出す行（補足）と、太字の行（見出し）を区別する。</summary>
    internal struct TipLine
    {
        public string Text;
        public bool Dim;
        public bool Bold;

        public TipLine(string text, bool dim, bool bold)
        {
            Text = text;
            Dim = dim;
            Bold = bold;
        }
    }

    /// <summary>
    /// HUD の行にマウスを乗せたときに出す札。
    ///
    /// ★ Windows 標準の ToolTip は使えなかった。
    ///   HUD は一度も前面にならない最前面の窓（WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW）なので、
    ///   ToolTip.Show を呼んでも画面に出ない（2026-09-18、実機で確認。行の上に重ねても、
    ///   パネルの外に出しても現れなかった）。そこで HUD と同じ要領で自分で描く。
    ///   配色・書体も HUD と同じものを渡すので、見た目が揃う。
    ///
    /// 札はパネルの外（左隣、入らなければ右隣）に出す。パネルは更新のたびに最前面へ押し戻すので、
    /// 重ねて出すとその裏に隠れてしまう。
    /// </summary>
    internal sealed class TipForm : Form
    {
        /// <summary>行の折り返し幅（96 DPI・標準の文字サイズでの値）。長い注意書きはここで折り返す。</summary>
        private const int MaxTextWidth = 300;

        private readonly List<TipLine> _lines = new List<TipLine>();
        private Theme _theme = Theme.Dark();
        private float _scale = 1f;

        public TipForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // 札はただの表示なので、マウスも受け取らない（受け取ると、下の行から
                // マウスが外れたことになって出たり消えたりする）。
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE
                            | NativeMethods.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var pref = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(
                    Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { }
        }

        /// <param name="avoid">パネルの範囲（画面座標）。ここに重ならない位置に出す。</param>
        /// <param name="anchorTop">合わせる行の上端（画面座標）。</param>
        public void ShowLines(IList<TipLine> lines, Theme theme, float scale,
                              Rectangle avoid, int anchorTop)
        {
            if (lines == null || lines.Count == 0) { Hide(); return; }

            _theme = theme;
            _scale = scale;
            _lines.Clear();
            for (var i = 0; i < lines.Count; i++) _lines.Add(lines[i]);

            BackColor = _theme.Background;
            Size = Measure();
            Location = PlaceNextTo(avoid, anchorTop);

            if (!Visible) Show();

            // パネルと同じ「最前面」なので、出すたびに前へ出しておく。焦点は取らない。
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                                       NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                                       | NativeMethods.SWP_NOACTIVATE);
            Invalidate();
        }

        private int S(double v) { return (int)Math.Round(v * _scale); }

        private int PadX { get { return S(9); } }
        private int PadY { get { return S(7); } }
        private int LineGap { get { return S(3); } }

        private Size Measure()
        {
            var width = 0;
            var height = PadY * 2;

            using (var body = Body())
            using (var bold = Bold())
            {
                for (var i = 0; i < _lines.Count; i++)
                {
                    var size = TextRenderer.MeasureText(_lines[i].Text,
                        _lines[i].Bold ? bold : body,
                        new Size(S(MaxTextWidth), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

                    if (size.Width > width) width = size.Width;
                    height += size.Height;
                    if (i > 0) height += LineGap;
                }
            }

            return new Size(width + PadX * 2, height);
        }

        private Point PlaceNextTo(Rectangle avoid, int anchorTop)
        {
            var work = Screen.FromRectangle(avoid).WorkingArea;
            var gap = S(6);

            // 左隣が第一候補。入らなければ右隣、どちらも狭ければ作業領域の内側へ寄せる。
            var x = avoid.Left - Width - gap;
            if (x < work.Left) x = avoid.Right + gap;
            if (x + Width > work.Right) x = Math.Max(work.Left, work.Right - Width);

            var y = anchorTop;
            if (y + Height > work.Bottom) y = work.Bottom - Height;
            if (y < work.Top) y = work.Top;

            return new Point(x, y);
        }

        private Font Body()
        {
            return new Font(Theme.FontFamily, 12.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private Font Bold()
        {
            return new Font(Theme.FontFamily, 12.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_theme.Background);

            using (var border = new Pen(_theme.Border))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            using (var body = Body())
            using (var bold = Bold())
            {
                var y = PadY;
                var w = Width - PadX * 2;

                for (var i = 0; i < _lines.Count; i++)
                {
                    var line = _lines[i];
                    var font = line.Bold ? bold : body;
                    var height = TextRenderer.MeasureText(line.Text, font,
                        new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

                    TextRenderer.DrawText(g, line.Text, font, new Rectangle(PadX, y, w, height),
                        line.Dim ? _theme.TextSecondary : _theme.TextPrimary,
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

                    y += height + LineGap;
                }
            }
        }
    }
}
