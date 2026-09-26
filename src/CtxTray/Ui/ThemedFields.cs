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

        /// <summary>a を b へ amount（0〜1）だけ寄せた色。使えない欄の文字を地に溶かして淡くするのに使う。</summary>
        public static Color Blend(Color a, Color b, float amount)
        {
            Func<int, int, int> mix = (x, y) => (int)Math.Round(x + (y - x) * amount);
            return Color.FromArgb(mix(a.R, b.R), mix(a.G, b.G), mix(a.B, b.B));
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
    /// 枠を部品自身に描かせられないため（TextBox の枠は
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
            // Enter / Leave は中の部品のさらに子（数値の欄の入力欄）まで見てくれる。
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
    /// 数値の欄。枠なしの入力欄と、自前で描く上下ボタンの組み合わせ。
    ///
    /// ★ NumericUpDown を継承しない。0.4.0 までは NumericUpDown の上下ボタンが標準の三角を描いたあとに
    ///   Paint で塗りつぶしていたので、マウスを動かして描き直されるたびに標準の三角が一瞬見え、ちらついた
    ///   （2026-09-26、利用者の指摘）。上から塗る方法では、先に描かれる分をなくせない。
    ///   上下ボタンを自分で最後まで描く部品に置き換え、二度描きそのものをやめた。
    ///
    /// 設定画面で使う分だけを持つ（整数の Minimum・Maximum・Value・ValueChanged）。
    /// 枠は FieldFrame に任せる。
    /// </summary>
    internal sealed class ThemedNumeric : Control
    {
        private readonly float _s;
        private readonly TextBox _edit;
        private readonly SpinButtons _buttons;

        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private Theme _theme;

        public event EventHandler ValueChanged;

        public ThemedNumeric(float scale)
        {
            _s = scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // フォーカスは中の入力欄に渡す。
            SetStyle(ControlStyles.Selectable, false);

            _edit = new TextBox
            {
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Left,
                Text = "0",
            };
            _buttons = new SpinButtons(this);
            Controls.Add(_edit);
            Controls.Add(_buttons);

            _edit.KeyDown += OnEditKeyDown;
            _edit.KeyPress += (s, e) =>
            {
                // 数字と操作用の文字（BackSpace など）だけ受ける。どの欄も 0 以上なので符号は要らない。
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            _edit.Leave += (s, e) => Commit();
            _edit.MouseWheel += OnEditWheel;

            // 枠（FieldFrame）の「マウスが乗っている」表示のため、子の出入りを自分の出入りとして伝える。
            foreach (Control child in new Control[] { _edit, _buttons })
            {
                child.MouseEnter += (s, e) => OnMouseEnter(e);
                child.MouseLeave += (s, e) => { if (!IsPointerInside()) OnMouseLeave(e); };
            }
        }

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; if (_maximum < value) _maximum = value; SetValue(_value); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; if (_minimum > value) _minimum = value; SetValue(_value); }
        }

        /// <summary>
        /// いまの値。入力欄に打ちかけの数字があれば、それを確定してから返す
        /// （Enter で OK を押したときは入力欄からフォーカスが外れないので、Leave を待てない）。
        /// </summary>
        public int Value
        {
            get { Commit(); return _value; }
            set { SetValue(value); }
        }

        public Theme Theme
        {
            set
            {
                _theme = value;
                BackColor = value.Field;
                _edit.BackColor = value.Field;
                _edit.ForeColor = value.TextPrimary;
                _buttons.Invalidate();
            }
        }

        internal Theme CurrentTheme { get { return _theme; } }
        internal float UiScale { get { return _s; } }

        /// <summary>1 段上げる・下げる。上下ボタン・矢印キー・ホイールから。</summary>
        internal void Step(int delta)
        {
            Commit();
            SetValue(_value + delta);
        }

        internal void FocusEdit()
        {
            if (!_edit.Focused) _edit.Focus();
        }

        private void SetValue(int v)
        {
            v = Math.Max(_minimum, Math.Min(_maximum, v));
            var text = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_edit.Text != text) _edit.Text = text;
            if (v == _value) return;

            _value = v;
            var handler = ValueChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>打った数字を値にする。読めなければ元の値に戻す。範囲外は端に寄せる。</summary>
        private void Commit()
        {
            int typed;
            if (int.TryParse(_edit.Text, System.Globalization.NumberStyles.None,
                             System.Globalization.CultureInfo.InvariantCulture, out typed))
                SetValue(typed);
            else
                SetValue(_value);
        }

        private void OnEditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) Commit();
        }

        /// <summary>
        /// ホイールで値を変えるのは、この欄に入力しているときだけ。
        /// 乗せただけで変えると、ページをホイールで送る途中で値が変わってしまう。
        /// 変えないときは親（ページ）に回して、そのままスクロールさせる。
        /// </summary>
        private void OnEditWheel(object sender, MouseEventArgs e)
        {
            if (!_edit.Focused) return;
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            if (e.Delta != 0) Step(e.Delta > 0 ? 1 : -1);
        }

        private bool IsPointerInside()
        {
            return IsHandleCreated && ClientRectangle.Contains(PointToClient(Cursor.Position));
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            // 高さは入力欄の文字の高さで決まる（NumericUpDown と同じ）。
            Height = _edit.PreferredHeight;
            PerformLayout();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _buttons.Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            // 枠なしにすると数字が左端に貼り付くので、少し右へ寄せる。
            var pad = (int)Math.Round(4 * _s);
            var buttonWidth = (int)Math.Round(16 * _s);
            _buttons.SetBounds(Math.Max(0, Width - buttonWidth), 0, buttonWidth, Height);
            _edit.SetBounds(pad, Math.Max(0, (Height - _edit.Height) / 2),
                            Math.Max(1, Width - buttonWidth - pad), _edit.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(_theme != null ? _theme.Field : BackColor))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        /// <summary>
        /// 上下ボタン。地も山形も自分で描き、二重バッファで 1 回で出す。
        /// 押し続けると、少し待ってから繰り返す（NumericUpDown と同じ操作感）。
        /// </summary>
        private sealed class SpinButtons : Control
        {
            private readonly ThemedNumeric _owner;
            private readonly Timer _repeat = new Timer();

            /// <summary>マウスが乗っているのは上下どちらか（0=なし、1=上、2=下）。</summary>
            private int _hotHalf;
            /// <summary>押しているのは上下どちらか（0=なし）。</summary>
            private int _pressed;

            public SpinButtons(ThemedNumeric owner)
            {
                _owner = owner;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                         | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Cursor = Cursors.Default;

                _repeat.Tick += (s, e) =>
                {
                    // 最初の 1 回だけ長めに待ち、あとは速く繰り返す。
                    _repeat.Interval = 60;
                    if (_pressed != 0 && Half(PointToClient(Cursor.Position)) == _pressed) Step();
                };
            }

            private int Half(Point p)
            {
                if (!ClientRectangle.Contains(p)) return 0;
                return p.Y < Height / 2 ? 1 : 2;
            }

            private void Step()
            {
                _owner.Step(_pressed == 1 ? 1 : -1);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var half = Half(e.Location);
                if (half == _hotHalf) return;
                _hotHalf = half;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hotHalf == 0) return;
                _hotHalf = 0;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || !Enabled) return;

                _owner.FocusEdit();
                _pressed = Half(e.Location);
                if (_pressed == 0) return;
                Step();
                _repeat.Interval = 400;
                _repeat.Start();
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _repeat.Stop();
                _pressed = 0;
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                // 入力欄と同じ扱い（入力中だけ値を変え、そうでなければページに回す）。
                var handled = e as HandledMouseEventArgs;
                if (!_owner._edit.Focused) { base.OnMouseWheel(e); return; }
                if (handled != null) handled.Handled = true;
                if (e.Delta != 0) _owner.Step(e.Delta > 0 ? 1 : -1);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var theme = _owner.CurrentTheme;
                if (theme == null) return;

                var s = _owner.UiScale;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var all = ClientRectangle;
                using (var brush = new SolidBrush(theme.Field))
                    g.FillRectangle(brush, all);

                var half = all.Height / 2f;
                var top = new RectangleF(all.X, all.Y, all.Width, half);
                var bottom = new RectangleF(all.X, all.Y + half, all.Width, all.Height - half);

                // マウスの乗っている側だけ、押せることが分かるように薄く敷く。押している間は少し濃く。
                var shaded = _pressed != 0 ? _pressed : _hotHalf;
                if (shaded != 0 && Enabled)
                {
                    var amount = _pressed != 0 ? 0.5f : 0.35f;
                    using (var brush = new SolidBrush(theme.IsDark
                                                      ? ControlPaint.Light(theme.Field, amount)
                                                      : ControlPaint.Dark(theme.Field, _pressed != 0 ? 0.08f : 0.04f)))
                        g.FillRectangle(brush, shaded == 1 ? top : bottom);
                }

                Fields.Chevron(g, top, Ink(theme, 1), s, true);
                Fields.Chevron(g, bottom, Ink(theme, 2), s, false);
            }

            private Color Ink(Theme theme, int half)
            {
                // 使えないときは地に半分溶かして淡くする（明暗どちらの配色でも淡く見えるように）。
                if (!Enabled) return Fields.Blend(theme.TextSecondary, theme.Field, 0.5f);
                return _hotHalf == half || _pressed == half ? theme.TextPrimary : theme.TextSecondary;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _repeat.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    /// <summary>
    /// ドロップダウン。閉じているときの見た目を丸ごと自前で描く。
    ///
    /// 標準（FlatStyle.Flat）は右端に小さな四角いボタンと三角を描き、配色にも角丸にも合わない。
    /// 一覧の側は OnDrawItem で塗る。
    ///
    /// ★ 閉じているときの欄は、Windows に一切描かせない（WM_PAINT を自分で受けて、裏で描いてから 1 回で出す）。
    ///   0.4.0 までは標準の描画が終わったあとに上から描き直していたので、マウスが乗るたびの描き直しで
    ///   標準の三角が一瞬見え、ちらついた（2026-09-26、利用者の指摘）。
    /// </summary>
    internal sealed class ThemedCombo : ComboBox
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PRINTCLIENT = 0x0318;

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

        // 枠の色（入力中は青）と表示する項目が変わる。描くのは WM_PAINT だけなので、描き直しを頼む。
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        /// <summary>一覧の 1 行。選ばれている行は青で敷く。</summary>
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (_theme == null || e.Index < 0) { base.OnDrawItem(e); return; }

            // 閉じているときの欄（フォーカスの出入りなどで Windows が WM_PAINT を通さずに頼んでくる）は描かない。
            // 描くとその場で地が塗られ、次の WM_PAINT までの一瞬、文字の消えた欄が見える。欄は Decorate だけが描く。
            if ((e.State & DrawItemState.ComboBoxEdit) != 0) return;

            var picked = (e.State & DrawItemState.Selected) != 0;

            using (var brush = new SolidBrush(picked ? _theme.Normal : _theme.Field))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var ink = picked ? (_theme.IsDark ? _theme.Background : Color.White) : _theme.TextPrimary;
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, e.Bounds, ink,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.NoPrefix | TextFormatFlags.LeftAndRightPadding);
        }

        protected override void WndProc(ref Message m)
        {
            if (_theme != null)
            {
                if (m.Msg == WM_ERASEBKGND)
                {
                    // 地も Decorate が塗る。先に消すとその一瞬が見える。
                    m.Result = (IntPtr)1;
                    return;
                }

                if (m.Msg == WM_PAINT || m.Msg == WM_PRINTCLIENT)
                {
                    PaintSelf(ref m);
                    return;
                }
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// 閉じているときの欄を描く。裏の画像に描いてから 1 回で画面に出す。
        /// wParam に描き先が渡されたとき（WM_PRINTCLIENT など）はそこへ描く。
        /// </summary>
        private void PaintSelf(ref Message m)
        {
            var given = m.WParam;
            var ps = new Native.NativeMethods.PAINTSTRUCT();
            var hdc = given != IntPtr.Zero ? given : Native.NativeMethods.BeginPaint(Handle, ref ps);
            try
            {
                using (var target = Graphics.FromHdc(hdc))
                using (var buffer = BufferedGraphicsManager.Current.Allocate(target, ClientRectangle))
                {
                    Decorate(buffer.Graphics);
                    buffer.Render(target);
                }
            }
            finally
            {
                if (given == IntPtr.Zero) Native.NativeMethods.EndPaint(Handle, ref ps);
            }
            m.Result = IntPtr.Zero;
        }

        private void Decorate(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 角丸の外側（四隅）はページと同じ色。
            using (var back = new SolidBrush(_theme.Background))
                g.FillRectangle(back, ClientRectangle);

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Fields.RoundedRect(r, 4f * _s))
            {
                using (var brush = new SolidBrush(_theme.Field))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Fields.EdgeColor(_theme, Focused, _hot && Enabled), 1f))
                    g.DrawPath(pen, path);
            }

            var arrow = (int)Math.Round(22 * _s);
            var text = new Rectangle((int)Math.Round(7 * _s), 0,
                                     Math.Max(1, Width - arrow - (int)Math.Round(7 * _s)), Height);

            // 使えないときは文字と山形を淡くする（出し方の選択など、ほかの設定で使えなくなる欄がある）。
            var ink = Enabled ? _theme.TextPrimary : Fields.Blend(_theme.TextSecondary, _theme.Field, 0.5f);
            if (SelectedIndex >= 0)
            {
                TextRenderer.DrawText(g, Items[SelectedIndex].ToString(), Font, text, ink,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                      | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            Fields.Chevron(g, new RectangleF(Width - arrow, 0, arrow, Height),
                           Enabled ? _theme.TextSecondary : ink, _s, false);
        }
    }
}
