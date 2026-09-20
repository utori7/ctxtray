using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CtxTray.Ui
{
    /// <summary>
    /// スクロールする中身を載せるパネル。表示位置か中身の高さが変わったら知らせる。
    ///
    /// AutoScroll は残す。捨てて自前で中身をずらすと、Tab キーで画面外の項目へ
    /// 移ったときの自動スクロール（ScrollControlIntoView）や、ホイール・キー操作まで
    /// 作り直すことになるため。標準のスクロールバーは
    /// SettingsForm 側で親のクリップ範囲の外へ押し出して見えなくしている。
    /// </summary>
    internal sealed class ScrollPage : Panel
    {
        /// <summary>表示位置か中身の高さが変わった。</summary>
        public event EventHandler ViewChanged;

        private int _lastY = int.MinValue;
        private int _lastHeight = -1;
        private bool _checking;

        public ScrollPage()
        {
            AutoScroll = true;
        }

        /// <summary>いまのスクロール量（0 以上）。</summary>
        public int Offset { get { return -DisplayRectangle.Y; } }

        /// <summary>中身の高さ。</summary>
        public int ContentHeight { get { return DisplayRectangle.Height; } }

        /// <summary>見えている高さ。</summary>
        public int ViewportHeight { get { return ClientSize.Height; } }

        /// <summary>スクロール量を変える。範囲外は WinForms 側で丸められる。</summary>
        public void ScrollTo(int offset)
        {
            if (offset < 0) offset = 0;
            // AutoScrollPosition は読むと負、書くときは正の値で渡す（WinForms の仕様）。
            AutoScrollPosition = new Point(0, offset);
        }

        // ホイール・キー・ドラッグ・レイアウトのどれで動いても同じ場所で拾えるよう、
        // メッセージを処理した後に位置を見る。個々の操作を列挙すると取りこぼす。
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            Check();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Check();
        }

        private void Check()
        {
            if (_checking || IsDisposed || !IsHandleCreated) return;

            _checking = true;
            try
            {
                var rect = DisplayRectangle;
                if (rect.Y == _lastY && rect.Height == _lastHeight) return;

                _lastY = rect.Y;
                _lastHeight = rect.Height;

                var handler = ViewChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            catch { }
            finally { _checking = false; }
        }
    }

    /// <summary>
    /// 細い縦スクロールバー。トラックは描かず、つまみだけを角丸で描く。
    ///
    /// Windows 標準のスクロールバーは配色に追従せず、見た目も古い（利用者の指摘、2026-09-20）。
    /// 設定画面のタブ見出しを標準の TabControl でなくボタン風の RadioButton で作ったのと同じ理由で、
    /// ここも自前で描く。
    /// </summary>
    internal sealed class ThinScrollBar : Control
    {
        private readonly float _s = Dpi.SystemScale;

        private ScrollPage _page;
        private Theme _theme;

        private bool _hot;
        private bool _dragging;
        /// <summary>つまみのどこをつかんだか（つまみの上端からの距離）。</summary>
        private int _grab;

        public ThinScrollBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // 自分にフォーカスを持たせない。Tab の巡回に入ると設定項目の間に割り込む。
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Width = S(10);
            Visible = false;
        }

        private int S(double v) { return (int)Math.Round(v * _s); }

        /// <summary>つまみの幅。バーの幅より細くして、両側に余白を残す。</summary>
        private int ThumbWidth { get { return S(6); } }

        /// <summary>つまみの最短の長さ。短すぎるとつかめない。</summary>
        private int MinThumb { get { return S(24); } }

        /// <summary>上下の余白。</summary>
        private int Margin_ { get { return S(2); } }

        public Theme Theme
        {
            set { _theme = value; Invalidate(); }
        }

        /// <summary>
        /// 見張っているページがいま選ばれているタブか。
        /// バーはページの兄弟として置くので、これが無いと 4 タブぶんが同時に出てしまう。
        /// </summary>
        public bool Active
        {
            get { return _active; }
            set { _active = value; Sync(); }
        }

        private bool _active;

        /// <summary>見張る相手を決める。</summary>
        public void Attach(ScrollPage page)
        {
            if (_page != null) _page.ViewChanged -= OnPageViewChanged;
            _page = page;
            if (_page != null) _page.ViewChanged += OnPageViewChanged;
            Sync();
        }

        private void OnPageViewChanged(object sender, EventArgs e)
        {
            Sync();
        }

        /// <summary>中身が収まっていればバーを消す。出ていれば描き直す。</summary>
        public void Sync()
        {
            if (_page == null || _page.IsDisposed || !_active) { Visible = false; return; }

            var needed = _page.ContentHeight > _page.ViewportHeight;
            if (Visible != needed) Visible = needed;
            if (needed) Invalidate();
        }

        // --- 寸法 ---------------------------------------------------------------

        private int TrackLength { get { return Math.Max(0, Height - Margin_ * 2); } }

        private int ThumbLength
        {
            get
            {
                var content = _page.ContentHeight;
                var view = _page.ViewportHeight;
                if (content <= 0) return MinThumb;

                var len = (int)Math.Round((double)TrackLength * view / content);
                return Math.Max(MinThumb, Math.Min(TrackLength, len));
            }
        }

        /// <summary>スクロールできる量。0 なら動かせない。</summary>
        private int ScrollRange
        {
            get { return Math.Max(0, _page.ContentHeight - _page.ViewportHeight); }
        }

        private int ThumbTop
        {
            get
            {
                var slack = TrackLength - ThumbLength;
                if (slack <= 0 || ScrollRange <= 0) return Margin_;
                return Margin_ + (int)Math.Round((double)slack * _page.Offset / ScrollRange);
            }
        }

        // --- 描画 ---------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_page == null || _theme == null || ScrollRange <= 0) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var x = (Width - ThumbWidth) / 2f;
            var rect = new RectangleF(x, ThumbTop, ThumbWidth, ThumbLength);
            var color = (_hot || _dragging) ? _theme.ScrollThumbHot : _theme.ScrollThumb;

            using (var brush = new SolidBrush(color))
            using (var path = RoundedRect(rect, ThumbWidth / 2f))
                e.Graphics.FillPath(brush, path);
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

        // --- 操作 ---------------------------------------------------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_page == null || e.Button != MouseButtons.Left || ScrollRange <= 0) return;

            var top = ThumbTop;
            var len = ThumbLength;

            if (e.Y >= top && e.Y < top + len)
            {
                _dragging = true;
                _grab = e.Y - top;
                Capture = true;
            }
            else
            {
                // つまみの外を押したら 1 画面ぶん送る。
                var page = Math.Max(1, _page.ViewportHeight - S(24));
                _page.ScrollTo(_page.Offset + (e.Y < top ? -page : page));
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_page == null) return;

            if (!_dragging)
            {
                var top = ThumbTop;
                var hot = e.Y >= top && e.Y < top + ThumbLength;
                if (hot != _hot) { _hot = hot; Invalidate(); }
                return;
            }

            var slack = TrackLength - ThumbLength;
            if (slack <= 0) return;

            var wanted = e.Y - _grab - Margin_;
            if (wanted < 0) wanted = 0;
            if (wanted > slack) wanted = slack;

            _page.ScrollTo((int)Math.Round((double)ScrollRange * wanted / slack));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;

            _dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging || !_hot) return;

            _hot = false;
            Invalidate();
        }

        /// <summary>バーの上でホイールを回しても中身が動くようにする。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_page == null || ScrollRange <= 0) return;

            var step = Math.Max(S(40), _page.ViewportHeight / 6);
            _page.ScrollTo(_page.Offset - e.Delta / 120 * step);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _page != null)
            {
                _page.ViewChanged -= OnPageViewChanged;
                _page = null;
            }
            base.Dispose(disposing);
        }
    }
}
