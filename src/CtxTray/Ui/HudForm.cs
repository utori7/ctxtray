using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CtxTray.Collect;
using CtxTray.Config;
using CtxTray.Core;
using CtxTray.Native;

namespace CtxTray.Ui
{
    /// <summary>
    /// 常時最前面の小型 HUD。
    ///
    /// 実測で並走セッションが 3〜6 個あるので、トレイアイコン 1 個では要件を満たせない。
    /// レート枠とセッションを「同じ格子」に載せ、名前・バー・% が縦に揃うようにする。
    /// 数字は固定幅の列に右寄せする（GDI+ からは等幅数字の OpenType 機能を使えないため、
    /// 書体ではなく列で桁を揃える）。
    ///
    /// 推測にもとづく値（残りターン、週間枠リセットの曜日）は出さない。
    /// 検証できていない数字を常に目に入る場所に置くと、確かな値と区別がつかないため
    /// （2026-09-15 に削除。5時間枠のリセット時刻は公式値と照合済みなので残している）。
    /// </summary>
    internal sealed class HudForm : Form
    {
        private const int HotkeyId = 0xC5A7;

        /// <summary>
        /// Desktop のタブでないセッション（ターミナル・VS Code）の名前の前に付ける印。
        /// 以前は ⌘ だったが、Mac のキー記号で Windows では意味が伝わりにくいので、
        /// ターミナルの入力待ちの形にした（実寸の描画を見比べて利用者が選んだ、2026-09-16）。
        /// </summary>
        private const string ExternalMark = ">_ ";

        /// <summary>名前の列の最小幅（96 DPI・標準の文字サイズでの値）。これより狭いと名前が読めない。</summary>
        private const int MinNameWidth = 70;

        private AppConfig _config;
        private Theme _theme;
        private Snapshot _snapshot;

        public event EventHandler HotkeyPressed;

        public HudForm(AppConfig config)
        {
            _config = config;
            _theme = Theme.Resolve(config.Theme);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = _theme.Background;
            Opacity = Clamp(config.Opacity, 0.2, 1.0);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            MouseDown += OnDragStart;
        }

        /// <summary>フォーカスを奪わない。Alt+Tab にも出さない。</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                if (_config != null && _config.ClickThrough)
                    cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _dpiScale = Dpi.ScaleFor(Handle);
            RegisterHotkey();
            ApplyRoundedCorners();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            base.OnHandleDestroyed(e);
        }

        /// <summary>
        /// 角丸は DWM に任せる。自前で Region を作るとアンチエイリアスが効かず
        /// 角がギザギザになるため。Windows 11 (build 22000+) でのみ効き、
        /// それ以外では失敗コードが返るだけで害はない。
        /// </summary>
        private void ApplyRoundedCorners()
        {
            try
            {
                var pref = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(
                    Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                var handler = HotkeyPressed;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }

            // モニタ間を移動したときに倍率を取り直す。
            if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                _dpiScale = Dpi.ScaleFor(Handle);
                Relayout();
                Invalidate();
            }

            // ライト/ダークの切り替えはこれで飛んでくる。ポーリングは要らない。
            if (m.Msg == NativeMethods.WM_SETTINGCHANGE && m.LParam != IntPtr.Zero)
            {
                string area = null;
                try { area = Marshal.PtrToStringAuto(m.LParam); }
                catch { }

                if (string.Equals(area, "ImmersiveColorSet", StringComparison.Ordinal))
                    ReloadTheme();
            }

            base.WndProc(ref m);
        }

        private void RegisterHotkey()
        {
            uint mods, vk;
            if (!HotkeyParser.TryParse(_config.Hotkey, out mods, out vk)) return;
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            NativeMethods.RegisterHotKey(Handle, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk);
        }

        public Theme CurrentTheme { get { return _theme; } }

        /// <summary>テーマを読み直す。設定変更と OS のテーマ変更の両方から呼ばれる。</summary>
        public void ReloadTheme()
        {
            _theme = Theme.Resolve(_config.Theme);
            BackColor = _theme.Background;
            Invalidate();

            var handler = ThemeChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public event EventHandler ThemeChanged;

        /// <summary>
        /// 設定を反映する。
        ///
        /// TrayApp は再読込のたびに新しい AppConfig を作るので、参照ごと差し替える。
        /// 以前は生成時のオブジェクトを握ったままで、一度再読込が起きると
        /// HUD だけが古い設定を見続けていた。
        /// </summary>
        public void ApplyConfig(AppConfig config)
        {
            var clickThroughChanged = config.ClickThrough != _config.ClickThrough;
            _config = config;

            Opacity = Clamp(_config.Opacity, 0.2, 1.0);

            // クリック透過はウィンドウの拡張スタイルなので、作り直さないと変わらない。
            // 作り直すと OnHandleCreated でホットキーも登録し直される。
            if (clickThroughChanged && IsHandleCreated) RecreateHandle();
            else RegisterHotkey();

            ReloadTheme();
            Relayout();
            Invalidate();
        }

        public void SetSnapshot(Snapshot snapshot)
        {
            _snapshot = snapshot;
            Relayout();
            EnsureTopMost();
            Invalidate();
        }

        /// <summary>
        /// 最前面に押し戻す。TopMost を立てるだけでは、他アプリが最前面を取ったあとに
        /// 後ろへ回されることがある（WS_EX_NOACTIVATE を付けていると特に）。
        /// </summary>
        public void EnsureTopMost()
        {
            if (!IsHandleCreated || !Visible) return;
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                                       NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                                       | NativeMethods.SWP_NOACTIVATE);
        }

        // --- 寸法 ---------------------------------------------------------------
        //
        // 96 DPI・標準の文字サイズを基準にした値に Factor を掛けて使う。
        // フォントも Pixel 指定にして同じ係数を掛ける（Point 指定だと DPI 対応プロセスでは二重に拡大される）。
        // 文字の大きさの設定も同じ係数に含めるので、文字を大きくすると列と窓の幅も同じ比率で広がる。

        private float _dpiScale = Dpi.SystemScale;

        private float Factor { get { return _dpiScale * (float)_config.HudTextScale; } }

        private int S(double v) { return (int)Math.Round(v * Factor); }

        private int PadX { get { return S(14); } }
        private int PadY { get { return S(12); } }
        private int RowHeight { get { return S(22); } }
        private int CapHeight { get { return S(17); } }
        private int SectionGap { get { return S(9); } }
        private int Gap { get { return S(8); } }

        private bool ShowRates { get { return _config.HudShowRateLimits; } }

        /// <summary>両方オフにされても空の箱にはしない。そのときはセッションを出す。</summary>
        private bool ShowSessions { get { return _config.HudShowSessions || !_config.HudShowRateLimits; } }

        private int BarW { get { return _config.HudShowBar ? S(84) : 0; } }
        private int PctW { get { return S(44); } }
        private int TokensW { get { return _config.HudShowTokens ? S(60) : 0; } }

        private int PanelWidth
        {
            get
            {
                // 設定の幅は標準の文字サイズでの値。出す列の合計より狭くはしない。
                var minimum = 14 * 2 + MinNameWidth + 8 + 44
                            + (_config.HudShowBar ? 84 + 8 : 0)
                            + (_config.HudShowTokens ? 8 + 60 : 0);
                return S(Math.Max(minimum, _config.HudWidth));
            }
        }

        private int NameW
        {
            get
            {
                var used = PadX * 2 + Gap + PctW;
                if (BarW > 0) used += BarW + Gap;
                if (TokensW > 0) used += Gap + TokensW;
                return Math.Max(S(MinNameWidth), PanelWidth - used);
            }
        }

        private void Relayout()
        {
            var sessions = _snapshot == null ? 1 : Math.Max(1, _snapshot.Sessions.Count);

            var height = PadY * 2;
            if (ShowRates) height += CapHeight + RowHeight * 2;
            if (ShowRates && ShowSessions) height += SectionGap;
            if (ShowSessions) height += CapHeight + RowHeight * sessions;

            var size = new Size(PanelWidth, height);
            if (Size != size) Size = size;

            if (_config.HudX < 0 && _config.HudY < 0) MoveToDefaultCorner();
        }

        private void MoveToDefaultCorner()
        {
            if (_config.HudX >= 0 && _config.HudY >= 0)
            {
                Location = new Point(_config.HudX, _config.HudY);
                return;
            }

            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - S(16), area.Bottom - Height - S(16));
        }

        public void RestorePosition()
        {
            if (_config.HudX >= 0 && _config.HudY >= 0 &&
                IsOnAnyScreen(new Point(_config.HudX, _config.HudY)))
            {
                Location = new Point(_config.HudX, _config.HudY);
            }
            else
            {
                MoveToDefaultCorner();
            }
        }

        /// <summary>
        /// 既定の位置（主モニタの右下）に戻す。設定画面のボタンから呼ばれる。
        /// 位置を「未設定」に戻すので、以後は行数が変わっても右下に張り付く。
        /// </summary>
        public void ResetPosition()
        {
            _config.HudX = -1;
            _config.HudY = -1;
            MoveToDefaultCorner();
            _config.Save();
        }

        /// <summary>
        /// 保存位置が今のモニタ構成に無ければ既定位置に戻す。
        /// 外部モニタを外したときに画面外へ消えるのを防ぐ。
        /// </summary>
        private static bool IsOnAnyScreen(Point p)
        {
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.Contains(p)) return true;
            return false;
        }

        private void SavePosition()
        {
            _config.HudX = Location.X;
            _config.HudY = Location.Y;
            _config.Save();
        }

        /// <summary>
        /// ドラッグで移動する。タイトルバーを掴んだことにして、移動は Windows に任せる。
        ///
        /// ★ 位置の保存は SendMessage が戻った直後に行う。
        ///   SendMessage は移動が終わるまで戻らず、その間の移動ループがボタンを離す操作を
        ///   受け取ってしまうので、MouseUp は届かない。以前は MouseUp で保存していたため
        ///   位置が保存されず、次の更新で右下の隅へ戻されていた（Relayout は位置が未設定なら隅に置く）。
        ///   クリックしただけ（動いていない）なら保存しない。未設定のまま隅に張り付かせておくため。
        /// </summary>
        private void OnDragStart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            var before = Location;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                                      (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);

            if (Location != before) SavePosition();
        }

        // --- 描画 ---------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_theme.Background);

            using (var border = new Pen(_theme.Border))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            using (var body = new Font(Theme.FontFamily, 12.5f * Factor, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bold = new Font(Theme.FontFamily, 12.5f * Factor, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var cap = new Font(Theme.FontFamily, 10.5f * Factor, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var capNote = new Font(Theme.FontFamily, 10.5f * Factor, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var y = PadY;

                if (_snapshot == null)
                {
                    DrawLeft(g, body, Strings.Get("hud.loading"), _theme.TextSecondary, PadX, y, Width - PadX * 2);
                    return;
                }

                if (ShowRates)
                {
                    DrawCaption(g, cap, capNote, Strings.Get("hud.capLimits"), null, y);
                    y += CapHeight;
                    y = DrawRateRows(g, body, bold, y);
                }

                if (ShowRates && ShowSessions)
                {
                    y += SectionGap - RowHeight / 4;
                    using (var sep = new Pen(_theme.Separator))
                        g.DrawLine(sep, PadX, y, Width - PadX, y);
                    y += RowHeight / 4;
                }

                if (ShowSessions)
                {
                    // 隠した行があることは見出しに小さく出す。
                    // 何も書かないと「セッションが消えた」と受け取られるため。
                    var hidden = _snapshot.HiddenSessionCount > 0
                        ? Strings.Format("hud.hidden", _snapshot.HiddenSessionCount)
                        : null;

                    DrawCaption(g, cap, capNote, Strings.Get("hud.capContext"), hidden, y);
                    y += CapHeight;
                    DrawSessionRows(g, body, bold, y);
                }
            }
        }

        private void DrawCaption(Graphics g, Font cap, Font noteFont, string text, string note, int y)
        {
            DrawLeft(g, cap, text, _theme.TextSecondary, PadX, y, Width - PadX * 2);
            if (!string.IsNullOrEmpty(note))
                DrawRight(g, noteFont, note, _theme.TextSecondary, PadX, y, Width - PadX * 2);
        }

        private int DrawRateRows(Graphics g, Font body, Font bold, int y)
        {
            var r = _snapshot.RateLimits;
            if (r == null)
            {
                DrawLeft(g, body, Strings.Get("hud.rateUnavail"), _theme.Danger, PadX, y, Width - PadX * 2);
                return y + RowHeight * 2;
            }

            // Desktop 未起動や長時間更新なしのときは参考値なので淡く出す。
            var dimmed = _snapshot.Freshness == RateFreshness.Reference;

            DrawRow(g, body, bold, y,
                    Strings.Get("hud.fiveHour"), FiveHourResetText(r), false,
                    r.FiveHourPct / 100.0, Levels.ForFiveHour(r, _config), dimmed,
                    FormatPercent(r.FiveHourPct), null);
            y += RowHeight;

            DrawRow(g, body, bold, y,
                    Strings.Get("hud.weekly"), null, false,
                    r.WeeklyPct / 100.0, Levels.ForWeekly(r, _config), dimmed,
                    FormatPercent(r.WeeklyPct), null);
            y += RowHeight;

            return y;
        }

        private void DrawSessionRows(Graphics g, Font body, Font bold, int y)
        {
            if (_snapshot.Sessions.Count == 0)
            {
                // 読み取りに失敗しているなら、その理由をここに出す。
                // 「セッションなし」とだけ出すと、使っていないだけなのか
                // 壊れているのかが利用者に判別できない。
                var broken = !_snapshot.Diag.IsEmpty;
                DrawLeft(g, body,
                         broken ? _snapshot.Diag.Summary : Strings.Get("hud.noSessions"),
                         broken ? _theme.Danger : _theme.TextSecondary,
                         PadX, y, Width - PadX * 2);
                return;
            }

            foreach (var s in _snapshot.Sessions)
            {
                var name = string.IsNullOrEmpty(s.Title) ? Strings.Get("hud.untitled") : s.Title;
                // Desktop のタブでないものは印を付ける。
                if (s.IsExternal) name = ExternalMark + name;

                string pct;
                double fraction;
                if (s.ModelKnown && s.ContextPct.HasValue)
                {
                    // 表示するのはウィンドウに対する消費率。公式インジケーターと同じ値。
                    pct = s.ContextPct.Value.ToString("0", CultureInfo.InvariantCulture) + "%";
                    fraction = s.ContextPct.Value / 100.0;
                }
                else
                {
                    pct = "—";
                    fraction = 0;
                }

                var tokens = _config.HudShowTokens && s.ContextTokens.HasValue
                    ? Compact(s.ContextTokens.Value) + "/" + Compact(s.ContextLimit ?? 0)
                    : null;

                DrawRow(g, body, bold, y, name, null, s.IsActive,
                        fraction, Levels.ForContext(s, _config), !s.ProcessAlive,
                        pct, tokens);

                y += RowHeight;
            }
        }

        /// <summary>
        /// 1 行を描く。レート枠もセッションも同じ格子に載せることで列が縦に揃う。
        ///
        /// note（5時間枠のリセット時刻）は名前の列の右端に寄せる。
        /// 専用の列を足すと、セッションの行まで名前が狭くなるため。
        /// </summary>
        private void DrawRow(Graphics g, Font body, Font bold, int y,
                             string name, string note, bool emphasise,
                             double fraction, Level level, bool dimmed,
                             string pct, string tokens)
        {
            var nameColor = (dimmed && !emphasise) ? _theme.TextSecondary : _theme.TextPrimary;
            var nameW = NameW;

            if (!string.IsNullOrEmpty(note))
            {
                var noteW = Math.Min(nameW / 2,
                    TextRenderer.MeasureText(g, note, body, new Size(int.MaxValue, RowHeight),
                                             TextFormatFlags.NoPrefix).Width);
                DrawRight(g, body, note, _theme.TextSecondary, PadX + nameW - noteW, y, noteW);
                nameW -= noteW + Gap;
            }

            DrawLeft(g, emphasise ? bold : body, name, nameColor, PadX, y, nameW);
            var x = PadX + NameW + Gap;

            if (BarW > 0)
            {
                var barH = S(6);
                DrawBar(g, x, y + (RowHeight - barH) / 2, BarW, barH, fraction, level, dimmed);
                x += BarW + Gap;
            }

            DrawRight(g, bold, pct, dimmed ? _theme.TextSecondary : _theme.For(level), x, y, PctW);
            x += PctW;

            if (TokensW > 0)
                DrawRight(g, body, tokens, _theme.TextSecondary, x + Gap, y, TokensW);
        }

        private void DrawBar(Graphics g, int x, int y, int w, int h, double fraction,
                             Level level, bool dimmed)
        {
            using (var track = new SolidBrush(_theme.BarTrack))
            using (var path = RoundedRect(x, y, w, h))
                g.FillPath(track, path);

            if (fraction <= 0) return;
            if (fraction > 1) fraction = 1;

            var filled = (int)Math.Round(w * fraction);
            // 丸端がつぶれない最小幅を確保する。
            if (filled < h) filled = h;

            var color = dimmed ? _theme.TextSecondary : _theme.For(level);
            using (var brush = new SolidBrush(color))
            using (var path = RoundedRect(x, y, filled, h))
                g.FillPath(brush, path);
        }

        private static GraphicsPath RoundedRect(int x, int y, int w, int h)
        {
            var r = Math.Max(1, h / 2);
            var path = new GraphicsPath();
            if (w <= h)
            {
                path.AddEllipse(x, y, h, h);
                return path;
            }

            path.AddArc(x, y, r * 2, h, 90, 180);
            path.AddArc(x + w - r * 2, y, r * 2, h, 270, 180);
            path.CloseFigure();
            return path;
        }

        private void DrawLeft(Graphics g, Font font, string text, Color color, int x, int y, int w)
        {
            if (string.IsNullOrEmpty(text) || w <= 0) return;
            TextRenderer.DrawText(g, text, font, new Rectangle(x, y, w, RowHeight), color,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void DrawRight(Graphics g, Font font, string text, Color color, int x, int y, int w)
        {
            if (string.IsNullOrEmpty(text) || w <= 0) return;
            TextRenderer.DrawText(g, text, font, new Rectangle(x, y, w, RowHeight), color,
                                  TextFormatFlags.Right | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // --- 文言 ---------------------------------------------------------------

        /// <summary>
        /// 以前は「サンプル以降に稼働があった＝実際はこれより上」の意味で末尾に "+" を付けていたが、
        /// 利用者に意味が伝わらなかったので付けない（2026-09-15）。鮮度は --json の freshness で見られる。
        /// </summary>
        private static string FormatPercent(int pct)
        {
            return pct.ToString(CultureInfo.InvariantCulture) + "%";
        }

        private string FiveHourResetText(RateLimitStatus r)
        {
            if (!ShouldShowReset(r)) return null;
            return "→" + r.NextFiveHourResetUtc.Value.ToLocalTime()
                          .ToString("H:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// auto のときはリセットが近いときだけ出す。
        /// 常時出すと、残りの数時間はただ行が伸びるだけになる。
        /// </summary>
        private bool ShouldShowReset(RateLimitStatus r)
        {
            if (r == null || !r.NextFiveHourResetUtc.HasValue) return false;

            var mode = (_config.ShowResets ?? "auto").ToLowerInvariant();
            if (mode == "never") return false;
            if (mode == "always") return true;

            var remain = r.NextFiveHourResetUtc.Value - DateTime.UtcNow;
            return remain.TotalSeconds > 0 && remain.TotalMinutes <= _config.ResetLeadFiveHourMinutes;
        }

        private static string Compact(int tokens)
        {
            if (tokens >= 1000000)
                return (tokens / 1000000.0).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            if (tokens >= 1000)
                return (tokens / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "k";
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
