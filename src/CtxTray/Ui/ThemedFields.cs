using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CtxTray.Ui
{
    /// <summary>
    /// 入力欄まわりの描画を 1 か所に集める小道具。
    ///
    /// WinForms の標準の見た目（立体的な上下ボタン、四角い枠、小さな三角のボタン）は
    /// 配色に追従せず古く見える（利用者の指摘、2026-09-20）。
    /// タブ見出しを標準の TabControl で作らず、スクロールバーも自前で描いているのと同じ理由で、
    /// 入力欄も角丸の枠と細い山形で描き直す。
    /// </summary>
    internal static class Fields
    {
        /// <summary>角丸の四角。枠も地もこれで描く。</summary>
        public static GraphicsPath RoundedRect(RectangleF r, float radius)
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

        /// <summary>枠の色。入力中は青、マウスを乗せているときは一段はっきり。</summary>
        public static Color EdgeColor(Theme theme, bool focused, bool hot)
        {
            if (focused) return theme.Normal;
            if (hot) return theme.TextSecondary;
            return theme.FieldBorder;
        }

        /// <summary>下向きの山形（∨）。ドロップダウンの印。</summary>
        public static void Chevron(Graphics g, RectangleF box, Color color, float scale, bool up)
        {
            var w = 3.8f * scale;
            var h = 2.2f * scale;
            var cx = box.X + box.Width / 2f;
            var cy = box.Y + box.Height / 2f;
            var dy = up ? -h : h;

            using (var pen = new Pen(color, Math.Max(1.2f, 1.4f * scale)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - w, cy - dy / 2f),
                    new PointF(cx, cy + dy / 2f),
                    new PointF(cx + w, cy - dy / 2f),
                });
            }
        }
    }

    /// <summary>
    /// 入力欄の角丸の枠。中の部品は枠なしにして、この入れ物が枠と地を描く。
    ///
    /// 枠を部品自身に描かせられないため（TextBox と NumericUpDown の枠は
    /// Windows が窓枠として描くので色を指定できない）、1 枚かぶせる。
    /// </summary>
    internal sealed class FieldFrame : Panel
    {
        private readonly float _s;
        private readonly Control _inner;

        private Theme _theme;
        private bool _focused;
        private bool _hot;

        public FieldFrame(Control inner, float scale)
        {
            _s = scale;
            _inner = inner;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;

            // 置かれ方（余白）は中の部品のものを引き継ぐ。
            Margin = inner.Margin;
            inner.Margin = new Padding(0);
            Controls.Add(inner);
            Fit();

            inner.SizeChanged += (s, e) => Fit();
            // Enter / Leave は中の部品のさらに子（NumericUpDown の入力欄）まで見てくれる。
            inner.Enter += (s, e) => { _focused = true; Invalidate(); };
            inner.Leave += (s, e) => { _focused = false; Invalidate(); };
            inner.MouseEnter += (s, e) => Hot(true);
            inner.MouseLeave += (s, e) => Hot(false);
            MouseEnter += (s, e) => Hot(true);
            MouseLeave += (s, e) => Hot(false);
        }

        /// <summary>中の部品。値の出し入れはこちらに対して行う。</summary>
        public Control Inner { get { return _inner; } }

        public Theme Theme
        {
            set { _theme = value; Invalidate(); }
        }

        /// <summary>
        /// 枠と中身のすき間。ここに枠線が乗る。
        /// 広げると行が高くなり、HUD タブが画面に収まらなくなるので詰めてある。
        /// </summary>
        private int Inset { get { return Math.Max(2, (int)Math.Round(2 * _s)); } }

        private void Hot(bool on)
        {
            if (_hot == on) return;
            _hot = on;
            Invalidate();
        }

        /// <summary>中の部品の大きさに合わせる。欄の高さは揃えたいので下限を置く。</summary>
        private void Fit()
        {
            var h = Math.Max(_inner.Height + Inset * 2, (int)Math.Round(22 * _s));
            Size = new Size(_inner.Width + Inset * 2, h);
            _inner.Location = new Point(Inset, (h - _inner.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_theme == null) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Fields.RoundedRect(r, 4f * _s))
            {
                using (var brush = new SolidBrush(_theme.Field))
                    e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(Fields.EdgeColor(_theme, _focused, _hot), 1f))
                    e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// 数値の欄。上下ボタンを細い山形に描き直す。
    ///
    /// 標準の上下ボタンは ControlPaint が描く立体的な三角で、配色に追従しない。
    /// ボタンは NumericUpDown の子の部品なので、その Paint に重ねて塗り直す。
    /// 枠は FieldFrame に任せるので、ここでは枠なしにする。
    /// </summary>
    internal sealed class ThemedNumeric : NumericUpDown
    {
        private readonly float _s;
        private readonly Control _buttons;
        private readonly Control _edit;

        private Theme _theme;
        private bool _insetting;
        /// <summary>マウスが乗っているのは上下どちらか（0=なし、1=上、2=下）。</summary>
        private int _hotHalf;

        public ThemedNumeric(float scale)
        {
            _s = scale;
            BorderStyle = BorderStyle.None;

            foreach (Control c in Controls)
            {
                if (c.GetType().Name == "UpDownButtons") _buttons = c;
                else if (c is TextBox) _edit = c;
            }

            if (_buttons == null) return;

            _buttons.Paint += DrawButtons;
            _buttons.MouseMove += (s, e) => HotHalf(e.Y < _buttons.Height / 2 ? 1 : 2);
            _buttons.MouseLeave += (s, e) => HotHalf(0);
        }

        public Theme Theme
        {
            set
            {
                _theme = value;
                BackColor = value.Field;
                ForeColor = value.TextPrimary;
                if (_buttons != null) _buttons.Invalidate();
            }
        }

        private void HotHalf(int half)
        {
            if (_hotHalf == half) return;
            _hotHalf = half;
            if (_buttons != null) _buttons.Invalidate();
        }

        /// <summary>
        /// 枠なしにすると数字が左端に貼り付くので、入力欄だけ少し右へ寄せる。
        /// NumericUpDown は枠の幅ぶんしか中身を寄せない作りなので、並べ直した後に動かす。
        /// </summary>
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_edit == null || _insetting) return;

            var pad = (int)Math.Round(4 * _s);
            if (_edit.Left >= pad) return;

            _insetting = true;
            try { _edit.SetBounds(pad, _edit.Top, Math.Max(1, _edit.Width - pad), _edit.Height); }
            finally { _insetting = false; }
        }

        /// <summary>
        /// 標準の描画の上に重ねて塗る。地ごと塗り直すので、下の三角は見えなくなる。
        /// </summary>
        private void DrawButtons(object sender, PaintEventArgs e)
        {
            if (_theme == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var all = _buttons.ClientRectangle;
            using (var brush = new SolidBrush(_theme.Field))
                g.FillRectangle(brush, all);

            var half = all.Height / 2f;
            var top = new RectangleF(all.X, all.Y, all.Width, half);
            var bottom = new RectangleF(all.X, all.Y + half, all.Width, all.Height - half);

            // マウスの乗っている側だけ、押せることが分かるように薄く敷く。
            if (_hotHalf != 0)
            {
                using (var brush = new SolidBrush(_theme.IsDark
                                                  ? ControlPaint.Light(_theme.Field, 0.35f)
                                                  : ControlPaint.Dark(_theme.Field, 0.04f)))
                    g.FillRectangle(brush, _hotHalf == 1 ? top : bottom);
            }

            Fields.Chevron(g, top, _hotHalf == 1 ? _theme.TextPrimary : _theme.TextSecondary, _s, true);
            Fields.Chevron(g, bottom, _hotHalf == 2 ? _theme.TextPrimary : _theme.TextSecondary, _s, false);
        }
    }

    /// <summary>
    /// ドロップダウン。閉じているときの見た目を丸ごと自前で描く。
    ///
    /// 標準（FlatStyle.Flat）は右端に小さな四角いボタンと三角を描き、配色にも角丸にも合わない。
    /// 一覧の側は OnDrawItem で塗る。
    /// </summary>
    internal sealed class ThemedCombo : ComboBox
    {
        private const int WM_PAINT = 0x000F;

        private readonly float _s;
        private Theme _theme;
        private bool _hot;

        public ThemedCombo(float scale)
        {
            _s = scale;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = (int)Math.Round(24 * _s);
        }

        public Theme Theme
        {
            set
            {
                _theme = value;
                // 角丸の外側（四隅）に出る地。ページと同じ色にしておくと角が溶ける。
                BackColor = value.Background;
                ForeColor = value.TextPrimary;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = false;
            Invalidate();
        }

        /// <summary>一覧の 1 行。選ばれている行は青で敷く。</summary>
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (_theme == null || e.Index < 0) { base.OnDrawItem(e); return; }

            // 閉じているときの欄は後から全部描き直すので、ここでは地だけ塗る。
            var inEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            var picked = !inEdit && (e.State & DrawItemState.Selected) != 0;

            using (var brush = new SolidBrush(picked ? _theme.Normal : _theme.Field))
                e.Graphics.FillRectangle(brush, e.Bounds);

            if (inEdit) return;

            var ink = picked ? (_theme.IsDark ? _theme.Background : Color.White) : _theme.TextPrimary;
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, e.Bounds, ink,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.NoPrefix | TextFormatFlags.LeftAndRightPadding);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // 標準の描画が終わったところで、閉じているときの欄を丸ごと描き直す。
            if (m.Msg != WM_PAINT || _theme == null) return;

            using (var g = Graphics.FromHwnd(Handle))
                Decorate(g);
        }

        private void Decorate(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 標準の四角い枠を先に消す。消さないと、角丸の外側に四隅だけが残る。
            using (var back = new SolidBrush(_theme.Background))
                g.FillRectangle(back, ClientRectangle);

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Fields.RoundedRect(r, 4f * _s))
            {
                using (var brush = new SolidBrush(_theme.Field))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Fields.EdgeColor(_theme, Focused, _hot), 1f))
                    g.DrawPath(pen, path);
            }

            var arrow = (int)Math.Round(22 * _s);
            var text = new Rectangle((int)Math.Round(7 * _s), 0,
                                     Math.Max(1, Width - arrow - (int)Math.Round(7 * _s)), Height);

            if (SelectedIndex >= 0)
            {
                TextRenderer.DrawText(g, Items[SelectedIndex].ToString(), Font, text, _theme.TextPrimary,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                      | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            Fields.Chevron(g, new RectangleF(Width - arrow, 0, arrow, Height),
                           _theme.TextSecondary, _s, false);
        }
    }
}
