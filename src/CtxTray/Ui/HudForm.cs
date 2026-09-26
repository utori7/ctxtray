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
    /// 並びは上からコンテキスト（セッション）→ 5時間枠 → 週間枠。Claude Desktop の Claude Code の表示に合わせ、
    /// トレイアイコン・設定画面と揃えた。色もトレイと同じ規則（Theme.ColorFor）で、
    /// 普段は値ごとの色、注意・危険の値だけ黄・赤（2026-09-17、利用者の決定）。
    ///
    /// 推測にもとづく値（残りターン、週間枠リセットの曜日）は出さない。
    /// 検証できていない数字を常に目に入る場所に置くと、確かな値と区別がつかないため
    /// （2026-09-15 に削除。5時間枠のリセット時刻は公式値と照合済みなので残している）。
    /// </summary>
    /// <summary>ホットキーの登録結果。使えないときに理由を利用者へ伝えるために持つ。</summary>
    internal enum HotkeyState
    {
        /// <summary>登録できた。</summary>
        Ok,

        /// <summary>キーを決めていない（「なし」。クリック透過のキーの既定）。</summary>
        None,

        /// <summary>もう一方のキー（表示／非表示）と同じ。同じキーは 2 つに登録できない。</summary>
        Duplicate,

        /// <summary>設定の文字列を読み取れない（手で書き換えた場合）。</summary>
        Unparsable,

        /// <summary>ほかのアプリが同じキーを登録している。</summary>
        Taken,
    }

    internal sealed class HudForm : Form
    {
        private const int HotkeyId = 0xC5A7;

        /// <summary>設定画面から「そのキーが空いているか」を試すときの ID。本番の登録とぶつけない。</summary>
        private const int HotkeyProbeId = 0xC5A8;

        /// <summary>クリック透過の切り替えのキー（2026-09-26）。</summary>
        private const int ClickThroughHotkeyId = 0xC5A9;

        /// <summary>
        /// Desktop のタブでないセッション（ターミナル・VS Code）の名前の前に付ける印。
        /// 以前は ⌘ だったが、Mac のキー記号で Windows では意味が伝わりにくいので、
        /// ターミナルの入力待ちの形にした（実寸の描画を見比べて利用者が選んだ、2026-09-16）。
        /// </summary>
        private const string ExternalMark = ">_ ";

        private AppConfig _config;
        private Theme _theme;
        private Snapshot _snapshot;

        /// <summary>
        /// ドラッグ中は位置を計算し直さない。
        /// 移動ループの中でもタイマーは動くので、そこで位置を決めると利用者の手と取り合いになる。
        /// </summary>
        private bool _dragging;

        public event EventHandler HotkeyPressed;

        /// <summary>クリック透過の切り替えのキーが押された。</summary>
        public event EventHandler ClickThroughHotkeyPressed;

        public HudForm(AppConfig config)
        {
            _config = config;
            _theme = Theme.Resolve(config.Theme);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = _theme.Background;
            Opacity = WantedOpacity(config);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            MouseDown += OnDragStart;
            MouseMove += OnHoverRow;
            // 「外れた」は、窓の出入りなどでも飛んでくる。本当に外にいるときだけ消す。
            MouseLeave += (s, e) => { if (!Bounds.Contains(Cursor.Position)) ClearTip(); };
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
            RegisterHotkeys();
            ApplyRoundedCorners();
            EnsureLayeredAttributes();
        }

        /// <summary>
        /// 実際に使う不透明度。
        ///
        /// コントラストテーマのときは設定によらず不透明にする。後ろが透けると
        /// 文字と地の差が縮み、コントラストテーマを選んだ意味が無くなるため。
        /// </summary>
        private double WantedOpacity(AppConfig config)
        {
            if (_theme != null && _theme.IsHighContrast) return 1.0;
            return Clamp(config.Opacity, 0.2, 1.0);
        }

        /// <summary>いまの窓にクリック透過の拡張スタイルが付いているか。</summary>
        private bool HasClickThroughStyle()
        {
            var ex = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            return (ex & NativeMethods.WS_EX_TRANSPARENT) != 0;
        }

        /// <summary>
        /// 不透明度 100% でクリック透過にしたときの手当て。
        ///
        /// クリック透過には WS_EX_LAYERED が要るので CreateParams で付けている。
        /// 不透明度が 100% 未満なら WinForms が透明度を設定するが、100% のときは何もしない。
        /// 透明度を一度も設定しない LAYERED の窓は画面に描かれないので、ここで「不透明」を設定する。
        /// </summary>
        private void EnsureLayeredAttributes()
        {
            if (Opacity < 1.0) return;

            var ex = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((ex & NativeMethods.WS_EX_LAYERED) == 0) return;

            NativeMethods.SetLayeredWindowAttributes(Handle, 0, 255, NativeMethods.LWA_ALPHA);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            NativeMethods.UnregisterHotKey(Handle, ClickThroughHotkeyId);
            // 空きを調べる登録が残っていても困らないが、念のため外す。
            NativeMethods.UnregisterHotKey(Handle, HotkeyProbeId);
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

            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == ClickThroughHotkeyId)
            {
                var handler = ClickThroughHotkeyPressed;
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

            // コントラストテーマの入り切りは別の合図で来る。
            if (m.Msg == NativeMethods.WM_THEMECHANGED) ReloadTheme();

            base.WndProc(ref m);
        }

        /// <summary>
        /// ホットキー（表示／非表示と、決めてあればクリック透過）を登録する。
        ///
        /// ★ 戻り値を捨てない。ほかのアプリが同じキーを取っていると登録は失敗し、
        ///   以前はそれを黙って見逃していたので、押しても何も起きない理由が利用者に分からなかった
        ///   （2026-09-18）。結果は HotkeyStatus に残し、TrayApp が通知で知らせる。
        /// </summary>
        private void RegisterHotkeys()
        {
            // 表示／非表示のキーも「なし」にできる（2026-09-26、利用者の決定）。そのときはトレイアイコンで出し入れする。
            if (string.IsNullOrWhiteSpace(_config.Hotkey))
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _hotkeyState = HotkeyState.None;
            }
            else
            {
                _hotkeyState = Register(HotkeyId, _config.Hotkey);
            }

            var clickThrough = _config.ClickThroughHotkey;
            if (string.IsNullOrWhiteSpace(clickThrough))
            {
                NativeMethods.UnregisterHotKey(Handle, ClickThroughHotkeyId);
                _clickThroughHotkeyState = HotkeyState.None;
            }
            else if (HotkeyParser.Same(clickThrough, _config.Hotkey))
            {
                // 同じキーは 2 つに登録できない。「ほかのアプリが使っている」と誤って伝えないよう分けておく。
                NativeMethods.UnregisterHotKey(Handle, ClickThroughHotkeyId);
                _clickThroughHotkeyState = HotkeyState.Duplicate;
            }
            else
            {
                _clickThroughHotkeyState = Register(ClickThroughHotkeyId, clickThrough);
            }
        }

        /// <summary>その ID で登録し直す。読めない文字列なら前の登録も外す（古いキーが効き続けないように）。</summary>
        private HotkeyState Register(int id, string combo)
        {
            NativeMethods.UnregisterHotKey(Handle, id);

            uint mods, vk;
            if (!HotkeyParser.TryParse(combo, out mods, out vk)) return HotkeyState.Unparsable;

            return NativeMethods.RegisterHotKey(Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk)
                ? HotkeyState.Ok
                : HotkeyState.Taken;
        }

        private HotkeyState _hotkeyState = HotkeyState.Ok;
        private HotkeyState _clickThroughHotkeyState = HotkeyState.None;

        public HotkeyState HotkeyStatus { get { return _hotkeyState; } }

        /// <summary>クリック透過のキーの登録結果。決めていなければ None。</summary>
        public HotkeyState ClickThroughHotkeyStatus { get { return _clickThroughHotkeyState; } }

        /// <summary>
        /// 設定画面から呼ぶ。そのキーがいま登録できるか。
        ///
        /// 自分がすでに登録しているキーは「使える」と答える。自分の登録のせいで
        /// RegisterHotKey が失敗し、いま使えているキーを「使えない」と見せてしまうため。
        /// </summary>
        public bool IsHotkeyAvailable(string combo)
        {
            uint mods, vk;
            if (!HotkeyParser.TryParse(combo, out mods, out vk)) return false;

            if (_hotkeyState == HotkeyState.Ok && HotkeyParser.Same(_config.Hotkey, combo)) return true;
            if (_clickThroughHotkeyState == HotkeyState.Ok
                && HotkeyParser.Same(_config.ClickThroughHotkey, combo)) return true;

            // 窓がまだ無ければ確かめようがない。使えないと決めつけない。
            if (!IsHandleCreated) return true;

            if (!NativeMethods.RegisterHotKey(Handle, HotkeyProbeId,
                                              mods | NativeMethods.MOD_NOREPEAT, vk))
                return false;

            NativeMethods.UnregisterHotKey(Handle, HotkeyProbeId);
            return true;
        }

        public Theme CurrentTheme { get { return _theme; } }

        /// <summary>テーマを読み直す。設定変更と OS のテーマ変更の両方から呼ばれる。</summary>
        public void ReloadTheme()
        {
            _theme = Theme.Resolve(_config.Theme);
            BackColor = _theme.Background;
            // コントラストテーマに入る／出ると、使ってよい不透明度が変わる。
            Opacity = WantedOpacity(_config);
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
            _config = config;

            Opacity = WantedOpacity(_config);

            // クリック透過はウィンドウの拡張スタイルなので、作り直さないと変わらない。
            // 作り直すと OnHandleCreated でホットキーも登録し直される。
            //
            // ★ 切り替わったかは、設定どうしではなく「窓に実際に付いているか」と比べて決める。
            //   トレイのメニューも設定画面も、HUD が握っている設定オブジェクトそのものを書き換えてから
            //   ここへ来る（再読込でも同じ値が届く）。以前は前後の設定を比べていたので常に「変わっていない」になり、
            //   窓が作り直されず、再起動するまでクリック透過が効かなかった（2026-09-19、利用者の指摘で発覚）。
            if (IsHandleCreated && HasClickThroughStyle() != _config.ClickThrough) RecreateHandle();
            else RegisterHotkeys();

            // 不透明度だけを 100% に変えた場合も、LAYERED の窓に前の透明度が残らないようにする。
            if (IsHandleCreated) EnsureLayeredAttributes();

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

            // ★ ここで詳細を消さない。
            //   transcript の書き込みで更新は数秒ごとに来るので、消すと出る前に取り消されて
            //   いつまでも出なかった（実機で確認、2026-09-18）。
            //   出している間に値が変わったときは、描き直した後に文言だけ差し替える（OnPaint の最後）。
        }

        // --- 行の詳細（マウスを乗せたときに出す）--------------------------------
        //
        // 行は幅が限られていて名前も省略されるので、確かな値の詳細はここに出す
        // （2026-09-18、利用者が見本から「全部入り」を選んだ）。
        // 推測値は出さない方針は変えない。レート枠の「表示値は下限」の注記も出さない（利用者の判断）。

        private sealed class TipRow
        {
            public int Top;
            public int Bottom;
            public System.Collections.Generic.List<TipLine> Lines;

            /// <summary>中身が変わったかを見るための、行をつないだ文字列。</summary>
            public string Text;
        }

        private readonly System.Collections.Generic.List<TipRow> _tipRows =
            new System.Collections.Generic.List<TipRow>();

        /// <summary>
        /// 隠した行も出している最中か。
        ///
        /// 見出しの「ほか N 件を表示」を押すと立つ。設定（hideIdleSessions など）は書き換えない —
        /// ここで出すのはその場の一時的な表示で、次に開き直すまで続く。
        /// 絞り込み自体は SessionFilter のままなので、トレイ・ツールチップ・通知の対象は変わらない。
        /// </summary>
        private bool _showHidden;

        /// <summary>見出しのメモ（「ほか N 件を表示」／「隠す」）の当たり判定。空なら押せない。</summary>
        private Rectangle _hiddenToggleArea = Rectangle.Empty;

        /// <summary>
        /// パネルの上に一時的に出す案内。null なら出さない。
        ///
        /// 1. 初めての起動のときの操作の案内。通知（バルーン）だけだと、集中モード中や通知を切っている環境では
        ///    一度も出ない。パネル自体は既定で出るので、ここにも同じ案内を置く（2026-09-20）。
        ///    設定ファイルには何も足さない。初回に設定を保存するので、次の起動では出ない。
        /// 2. クリック透過を切り替えたときの知らせ（2026-09-26）。キーで切り替えると他に手がかりが無く、
        ///    オンにすると右クリックも効かなくなるので、戻し方をここで伝える。
        /// </summary>
        private string _notice;

        /// <summary>案内を自分で消す時刻。触らないまま残り続けないようにする。</summary>
        private DateTime _noticeUntilUtc = DateTime.MinValue;

        /// <summary>案内が占める高さ（本文 2 行＋区切り）。★ Relayout と OnPaint で同じ値を使う。</summary>
        private int NoticeHeight { get { return _notice == null ? 0 : RowHeight * 2 + SectionGap; } }

        /// <summary>案内を出す。前の案内が出ていれば置き換える。</summary>
        public void ShowNotice(string text, int seconds)
        {
            _notice = string.IsNullOrEmpty(text) ? null : text;
            _noticeUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
            Relayout();
            Invalidate();
        }

        /// <summary>案内を消す。読み終わったとき（操作したとき）と、時間が過ぎたときに呼ぶ。</summary>
        public void DismissNotice()
        {
            if (_notice == null) return;
            _notice = null;
            Relayout();
            Invalidate();
        }

        /// <summary>
        /// 詳細の札。Windows 標準の ToolTip はこの窓（一度も前面にならない最前面の窓）では
        /// 出なかったので、自前の窓で描く（Ui/TipForm.cs）。
        /// </summary>
        private TipForm _tip;


        private int _tipRow = -1;
        private bool _tipShowing;
        private int _tipShownRow = -1;
        private string _tipShownText;

        /// <summary>
        /// 札を出すまでの待ち。パネルの上をマウスが通り過ぎただけで出さないため。
        ///
        /// ★ この窓では WinForms のタイマー（WM_TIMER）が発火しない（2026-09-18 実機で確認）ので、
        ///   待ちは自分で計れない。TrayApp の 500ms のタイマーから PumpTip を呼んでもらう。
        ///   そのため実際に出るまでは、この値から 500ms 遅れるまでの幅がある。
        /// </summary>
        private const int TipDelayMs = 300;

        /// <summary>いまの行にマウスが乗った時刻。待ち時間の起点。</summary>
        private DateTime _tipHoverUtc = DateTime.MinValue;

        private void OnHoverRow(object sender, MouseEventArgs e)
        {
            var row = -1;
            for (var i = 0; i < _tipRows.Count; i++)
            {
                if (e.Y < _tipRows[i].Top || e.Y >= _tipRows[i].Bottom) continue;
                row = i;
                break;
            }

            if (row == _tipRow) return;

            _tipRow = row;
            _tipHoverUtc = DateTime.UtcNow;

            // 行から外れたら、待たずに消す。
            if (row < 0) { HideTip(); return; }

            // すでに札が出ているなら、隣の行へ移ったときは待たせない
            // （読んでいる最中に一度消えてから出し直すと、かえって読みにくい）。
            if (_tipShowing) ShowTip();
        }

        /// <summary>
        /// 時間で決まることをまとめて進める。TrayApp のタイマー（500ms）から呼ばれる。
        /// この窓では WinForms のタイマーが発火しないので、時計はここから借りる。
        /// </summary>
        public void Pump()
        {
            if (_notice != null && DateTime.UtcNow >= _noticeUntilUtc) DismissNotice();
            PumpTip();
        }

        /// <summary>待ち時間が過ぎていれば札を出す。</summary>
        private void PumpTip()
        {
            if (_tipShowing || _tipRow < 0) return;
            if ((DateTime.UtcNow - _tipHoverUtc).TotalMilliseconds < TipDelayMs) return;

            // 待っている間にマウスが外へ出ていることがある（外に出た合図が届かない経路がある）。
            if (!Bounds.Contains(Cursor.Position)) { ClearTip(); return; }

            ShowTip();
        }

        private void ShowTip()
        {
            if (_tipRow < 0 || _tipRow >= _tipRows.Count)
            {
                HideTip();
                return;
            }

            var row = _tipRows[_tipRow];

            // ★ 同じ行の同じ内容なら出し直さない。
            //   出し直しを繰り返すと、札が描かれる前に作り直されて、いつまでも見えないままになる
            //   （2026-09-18、実機で確認。何かの拍子に「外れた・入った」が連続することがある）。
            if (_tipShowing && _tipShownRow == _tipRow
                && string.Equals(_tipShownText, row.Text, StringComparison.Ordinal)) return;

            _tipShowing = true;
            _tipShownRow = _tipRow;
            _tipShownText = row.Text;

            if (_tip == null || _tip.IsDisposed) _tip = new TipForm();
            // 札はパネルの外に出す（パネルは最前面へ押し戻しているので、重ねると裏に隠れる）。
            _tip.ShowLines(row.Lines, _theme, Factor, Bounds, PointToScreen(new Point(0, row.Top)).Y);
        }

        /// <summary>
        /// 出している詳細の文言を、描き直した後の値に合わせる（古い数字を出したままにしない）。
        /// 描画の途中で窓を触らないよう、描き終わってから呼ぶ。
        /// </summary>
        private void RefreshTip()
        {
            if (!_tipShowing) return;

            if (_tipRow < 0 || _tipRow >= _tipRows.Count) { HideTip(); return; }
            if (string.Equals(_tipRows[_tipRow].Text, _tipShownText, StringComparison.Ordinal)) return;

            BeginInvoke(new Action(ShowTip));
        }

        private void HideTip()
        {
            if (!_tipShowing) return;
            _tipShowing = false;
            _tipShownRow = -1;
            _tipShownText = null;
            if (_tip != null && !_tip.IsDisposed) _tip.Hide();
        }

        private void ClearTip()
        {
            _tipRow = -1;
            HideTip();
        }

        private void AddTipRow(int y, System.Collections.Generic.List<TipLine> lines, int height = 0)
        {
            if (lines == null || lines.Count == 0) return;

            var joined = new System.Text.StringBuilder();
            foreach (var line in lines) joined.Append(line.Text).Append('\n');

            _tipRows.Add(new TipRow
            {
                Top = y,
                Bottom = y + (height > 0 ? height : RowHeight),
                Lines = lines,
                Text = joined.ToString(),
            });
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

        private int BarW { get { return _config.HudShowBar ? S(HudLayout.Bar) : 0; } }
        private int PctW { get { return S(HudLayout.Pct); } }
        private int TokensW { get { return _config.HudShowTokens ? S(HudLayout.Tokens) : 0; } }

        // --- モデルとエフォート（設定 showModel / showEffort / modelLayout）-----------
        //
        // column  … 名前の後ろに専用の列（その分パネルを広げる。名前の幅は変えない）
        // twoLine … 名前の下に小さく 2 段目（幅は変えない。セッションの行が高くなる）
        // 名前の列の右端に寄せる案は、名前が 3〜5 文字に縮んで読めなくなるので採らなかった
        // （2026-09-26、実寸の見本で利用者が判断）。

        private bool ModelRowOn { get { return _config.HudShowModel || _config.HudShowEffort; } }

        private bool TwoLine
        {
            get { return ModelRowOn && string.Equals(_config.HudModelLayout, "twoLine", StringComparison.Ordinal); }
        }

        /// <summary>行に出す文字列。出さない設定なら null。</summary>
        private string ModelRowText(SessionRow s)
        {
            if (!ModelRowOn) return null;
            return ModelText.Format(s, _config.HudShowModel, _config.HudShowEffort);
        }

        /// <summary>専用の列の幅（標準の文字サイズでの値。名前の下に出すときは 0）。</summary>
        private int ModelColBase
        {
            get
            {
                if (!ModelRowOn || TwoLine) return 0;
                return HudLayout.ModelColumn(_config.HudShowModel, _config.HudShowEffort);
            }
        }

        private int ModelColW { get { return S(ModelColBase); } }

        /// <summary>2 段目の高さ。★ Relayout・DrawSessionRows・札の当たり判定で同じ値を使う。</summary>
        private int SubLineH { get { return TwoLine ? S(15) : 0; } }

        private int SessionRowHeight { get { return RowHeight + SubLineH; } }

        /// <summary>
        /// パネルの幅は、設定した名前の幅とオンにした列の合計（Core/HudLayout.cs）。
        /// どの列もオンにした分だけ広がり、名前は縮まない。
        /// </summary>
        private int PanelWidth
        {
            get
            {
                return S(HudLayout.PanelWidth(_config.HudNameWidth, _config.HudShowBar,
                                              _config.HudShowTokens, ModelColBase));
            }
        }

        /// <summary>
        /// 名前の欄。倍率を掛けた後の丸めで列の合計とパネルの幅が 1px ずれないよう、
        /// パネルの幅から他の列を引いて求める。
        /// </summary>
        private int NameW
        {
            get
            {
                var used = PadX * 2 + Gap + PctW;
                if (BarW > 0) used += BarW + Gap;
                if (TokensW > 0) used += Gap + TokensW;
                if (ModelColW > 0) used += ModelColW + Gap;
                return Math.Max(S(HudLayout.MinNameWidth), PanelWidth - used);
            }
        }

        private void Relayout()
        {
            var sessions = VisibleRowCount;

            var height = PadY * 2 + NoticeHeight;
            if (ShowRates) height += CapHeight + RowHeight * 2;
            if (ShowRates && ShowSessions) height += SectionGap;
            // 行が無いときの案内は 1 段（DrawSessionRows と同じ）。
            if (ShowSessions)
                height += CapHeight + (HasSessionRows ? SessionRowHeight * sessions : RowHeight);

            var size = new Size(PanelWidth, height);
            if (Size != size) Size = size;

            ApplyPosition();
        }

        /// <summary>
        /// いまの大きさに合わせて位置を決め直す。
        ///
        /// 高さはセッションの数で変わるので、大きさを変えるたびに呼ぶ。
        /// 保存した位置が無ければ右下の隅。ドラッグ中は触らない。
        /// </summary>
        private void ApplyPosition()
        {
            if (_dragging) return;

            if (!HasSavedPosition)
            {
                MoveToDefaultCorner();
                return;
            }

            var work = Screen.FromRectangle(SavedBounds()).WorkingArea;
            Location = HudPlacement.Place(_config.HudX, _config.HudY,
                                          _config.HudAnchorBottom, Size, work);
        }

        /// <summary>位置が保存されているか（-1 は未設定＝右下の隅）。</summary>
        private bool HasSavedPosition { get { return _config.HudX >= 0 && _config.HudY >= 0; } }

        /// <summary>保存された座標といまの大きさから、置こうとしている範囲。</summary>
        private Rectangle SavedBounds()
        {
            var top = _config.HudAnchorBottom ? _config.HudY - Height : _config.HudY;
            return new Rectangle(_config.HudX, top, Width, Height);
        }

        private void MoveToDefaultCorner()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - S(16), area.Bottom - Height - S(16));
        }

        /// <summary>
        /// 起動時に位置を戻す。
        ///
        /// 保存位置が今のモニタ構成から外れていても（外部モニタを外した、画面を縦向きにしたなど）、
        /// ApplyPosition が作業領域の内側へ押し戻すので画面外には残らない。
        /// 設定は書き換えないので、元の構成に戻せば元の位置に戻る。
        /// </summary>
        public void RestorePosition()
        {
            ApplyPosition();
        }

        /// <summary>
        /// 既定の位置（主モニタの右下）に戻す。設定画面のボタンから呼ばれる。
        /// 位置を「未設定」に戻すので、以後は行数が変わっても右下に張り付く。
        /// </summary>
        public void ResetPosition()
        {
            _config.HudX = -1;
            _config.HudY = -1;
            _config.HudAnchorBottom = false;
            MoveToDefaultCorner();
            _config.Save();
        }

        /// <summary>
        /// 位置を保存する。画面の下半分に置かれていれば下端の座標を覚える（HudPlacement）。
        /// 上端だけを覚えていた頃は、行が増えると下へ伸びて画面の外に出ていた。
        ///
        /// 位置は実行中の状態そのものなので、ここだけは実行中の設定を直接書き換える
        /// （保存に失敗しても、動かした位置はそのまま使う。再読込でも実行中の値が優先される）。
        /// </summary>
        private void SavePosition()
        {
            var work = Screen.FromRectangle(Bounds).WorkingArea;
            var anchorBottom = HudPlacement.AnchorBottom(Bounds, work);
            var anchor = HudPlacement.Anchor(Bounds, anchorBottom);

            _config.HudX = anchor.X;
            _config.HudY = anchor.Y;
            _config.HudAnchorBottom = anchorBottom;
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

            // 触ったなら案内は読み終えたとみなす（押す場所を選ばせない）。
            DismissNotice();

            // 見出しの「ほか N 件を表示」はドラッグに渡さない。
            // 押した場所がそこなら、移動ではなく展開の切り替えにする。
            if (!_hiddenToggleArea.IsEmpty && _hiddenToggleArea.Contains(e.Location))
            {
                ToggleHiddenRows();
                return;
            }

            var before = Location;
            // 移動ループの中でもタイマーは動く。その間 ApplyPosition が働くと、
            // 保存済みの位置へ引き戻されて動かせない。
            _dragging = true;
            try
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                                          (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            }
            finally
            {
                _dragging = false;
            }

            if (Location != before) SavePosition();
        }

        /// <summary>
        /// 隠した行を出す／しまう。設定は書き換えない（その場の表示だけ）。
        /// 行数が変わるので、大きさを取り直してから描き直す。
        /// </summary>
        private void ToggleHiddenRows()
        {
            if (!_showHidden && HiddenRows.Count == 0) return;

            _showHidden = !_showHidden;
            // 行が入れ替わるので、いま出している札は当てにならない。
            ClearTip();
            Relayout();
            Invalidate();
        }

        // --- 描画 ---------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_theme.Background);

            // 行の詳細と押せる範囲は描くたびに組み直す
            // （行の位置も内容も、そのとき描いたものと一致させる）。
            _tipRows.Clear();
            _hiddenToggleArea = Rectangle.Empty;

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

                if (_notice != null)
                {
                    // 1 行では収まらないので折り返す（高さは NoticeHeight と揃える）。
                    TextRenderer.DrawText(g, _notice, body,
                        new Rectangle(PadX, y, Width - PadX * 2, RowHeight * 2), _theme.TextSecondary,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                        | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                    var lineY = y + RowHeight * 2 + SectionGap / 2;
                    using (var sep = new Pen(_theme.Separator))
                        g.DrawLine(sep, PadX, lineY, Width - PadX, lineY);

                    y += NoticeHeight;
                }

                if (ShowSessions)
                {
                    // 隠した行があることは見出しに小さく出す。
                    // 何も書かないと「セッションが消えた」と受け取られるため。
                    // 押すとその場で出せる（設定は変えない。2026-09-20）。
                    string hidden = null;
                    if (_showHidden && HiddenRows.Count > 0) hidden = Strings.Get("hud.collapse");
                    else if (_snapshot.HiddenSessionCount > 0)
                        hidden = Strings.Format("hud.hidden", _snapshot.HiddenSessionCount);

                    _hiddenToggleArea =
                        DrawCaption(g, cap, capNote, Strings.Get("hud.capContext"), hidden, y);
                    y += CapHeight;
                    y = DrawSessionRows(g, body, bold, y);
                }

                if (ShowRates && ShowSessions)
                {
                    y += SectionGap - RowHeight / 4;
                    using (var sep = new Pen(_theme.Separator))
                        g.DrawLine(sep, PadX, y, Width - PadX, y);
                    y += RowHeight / 4;
                }

                if (ShowRates)
                {
                    // 淡い行の理由は見出しに言葉で出す。灰色なだけでは「壊れている」と読まれ、
                    // 理由は行にマウスを乗せないと分からなかった（2026-09-20）。
                    // 記号は付けない（▲ も + も意味が伝わらなかった、2026-09-15）。
                    var reference = _snapshot.Freshness == RateFreshness.Reference
                        ? Strings.Get("hud.capReference")
                        : null;

                    DrawCaption(g, cap, capNote, Strings.Get("hud.capLimits"), reference, y);
                    y += CapHeight;
                    DrawRateRows(g, body, bold, y);
                }
            }

            // 詳細を出している最中に値が変わっていたら、新しい文言に差し替える。
            RefreshTip();
        }

        /// <summary>見出しを描き、右のメモが占めた範囲を返す（メモが無ければ空）。押せる範囲に使う。</summary>
        private Rectangle DrawCaption(Graphics g, Font cap, Font noteFont, string text, string note, int y)
        {
            DrawLeft(g, cap, text, _theme.TextSecondary, PadX, y, Width - PadX * 2);
            if (string.IsNullOrEmpty(note)) return Rectangle.Empty;

            DrawRight(g, noteFont, note, _theme.TextSecondary, PadX, y, Width - PadX * 2);

            var w = TextRenderer.MeasureText(g, note, noteFont, new Size(int.MaxValue, RowHeight),
                                             TextFormatFlags.NoPrefix).Width;
            return new Rectangle(Width - PadX - w, y, w, CapHeight);
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
                    r.FiveHourPct / 100.0, "fiveHour", Levels.ForFiveHour(r, _config), dimmed,
                    FormatPercent(r.FiveHourPct), null);
            AddTipRow(y, RateTip(r, true));
            y += RowHeight;

            DrawRow(g, body, bold, y,
                    Strings.Get("hud.weekly"), null, false,
                    r.WeeklyPct / 100.0, "weekly", Levels.ForWeekly(r, _config), dimmed,
                    FormatPercent(r.WeeklyPct), null);
            AddTipRow(y, RateTip(r, false));
            y += RowHeight;

            return y;
        }

        /// <summary>
        /// 隠した行。展開していないときは空（Relayout と DrawSessionRows が同じ数を見るための入口）。
        /// </summary>
        private System.Collections.Generic.IList<SessionRow> HiddenRows
        {
            get
            {
                if (_snapshot == null || _snapshot.HiddenSessions == null)
                    return new SessionRow[0];
                return _snapshot.HiddenSessions;
            }
        }

        /// <summary>いま描くセッションの行数。★ Relayout の高さ計算と必ず同じ数にする。</summary>
        private int VisibleRowCount
        {
            get
            {
                if (_snapshot == null) return 1;
                var n = _snapshot.Sessions.Count;
                if (_showHidden) n += HiddenRows.Count;
                return Math.Max(1, n);   // 0 件でも案内の 1 行を出す
            }
        }

        /// <summary>描くセッションの行があるか（無ければ案内の 1 行）。★ Relayout と DrawSessionRows で同じ判定。</summary>
        private bool HasSessionRows
        {
            get
            {
                return _snapshot != null
                    && (_snapshot.Sessions.Count > 0 || (_showHidden && HiddenRows.Count > 0));
            }
        }

        /// <summary>セッションの行を描き、次の行の y を返す（行が無くても案内の 1 行分進める。Relayout と同じ）。</summary>
        private int DrawSessionRows(Graphics g, Font body, Font bold, int y)
        {
            if (!HasSessionRows)
            {
                // 読み取りに失敗しているなら、その理由をここに出す。
                // 「セッションなし」とだけ出すと、使っていないだけなのか
                // 壊れているのかが利用者に判別できない。
                var broken = !_snapshot.Diag.IsEmpty;
                DrawLeft(g, body,
                         broken ? _snapshot.Diag.Summary : Strings.Get("hud.noSessions"),
                         broken ? _theme.Danger : _theme.TextSecondary,
                         PadX, y, Width - PadX * 2);
                return y + RowHeight;
            }

            var rows = new System.Collections.Generic.List<SessionRow>(_snapshot.Sessions);
            // 展開中は、絞り込みで落とした行を後ろに続ける（設定は変えない）。
            if (_showHidden) rows.AddRange(HiddenRows);

            foreach (var s in rows)
            {
                var name = string.IsNullOrEmpty(s.Title) ? Strings.Get("hud.untitled") : s.Title;
                // Desktop のタブでないものは印を付ける。
                if (s.IsExternal) name = ExternalMark + name;

                string pct;
                double fraction;
                if (s.ModelKnown && s.ContextPct.HasValue)
                {
                    // 表示するのはウィンドウに対する消費率。公式インジケーターと同じ値。
                    pct = PercentText.Format(s.ContextPct.Value) + "%";
                    fraction = s.ContextPct.Value / 100.0;
                }
                else if (s.ContextTokens.HasValue)
                {
                    // 量は分かるが分母（モデルの上限）が分からない。「—」だと読めていないのと
                    // 区別が付かないので、分からないのは分母だと示す。理由は札で説明する。
                    pct = "?%";
                    fraction = 0;
                }
                else
                {
                    pct = "—";
                    fraction = 0;
                }

                var tokens = _config.HudShowTokens && s.ContextTokens.HasValue
                    ? Compact(s.ContextTokens.Value) + "/"
                      + (s.ContextLimit.HasValue ? Compact(s.ContextLimit.Value) : "?")
                    : null;

                var modelText = ModelRowText(s);

                DrawRow(g, body, bold, y, name, null, s.IsActive,
                        fraction, "context", Levels.ForContext(s, _config), !s.ProcessAlive,
                        pct, tokens, modelText);

                if (TwoLine && modelText != null)
                {
                    using (var small = new Font(Theme.FontFamily, 10.5f * Factor, FontStyle.Regular, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, modelText, small,
                            new Rectangle(PadX, y + RowHeight - S(4), NameW, SubLineH + S(2)), _theme.TextSecondary,
                            TextFormatFlags.Left | TextFormatFlags.Top
                            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }

                AddTipRow(y, SessionTip(s), SessionRowHeight);

                y += SessionRowHeight;
            }

            return y;
        }

        /// <summary>
        /// 1 行を描く。レート枠もセッションも同じ格子に載せることで列が縦に揃う。
        ///
        /// note（5時間枠のリセット時刻）は名前の列の右端に寄せる。
        /// 専用の列を足すと、セッションの行まで名前が狭くなるため。
        ///
        /// value は値の名前（context / fiveHour / weekly）。バーと % の色を決めるのに使う。
        /// </summary>
        private void DrawRow(Graphics g, Font body, Font bold, int y,
                             string name, string note, bool emphasise,
                             double fraction, string value, Level level, bool dimmed,
                             string pct, string tokens, string model = null)
        {
            // 淡く出す行（止まっているセッション、参考値のレート枠）は色で区別しない。
            var valueColor = dimmed ? _theme.TextSecondary : _theme.ColorFor(value, level);

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

            // 専用の列（レート枠の行では空）。
            if (ModelColW > 0)
            {
                DrawLeft(g, body, model, _theme.TextSecondary, x, y, ModelColW);
                x += ModelColW + Gap;
            }

            if (BarW > 0)
            {
                var barH = S(6);
                // 淡い行は色で区別していないので、形の印も付けない（レベルを主張させない）。
                DrawBar(g, x, y + (RowHeight - barH) / 2, BarW, barH, fraction, valueColor,
                        dimmed ? Level.Normal : level);
                x += BarW + Gap;
            }

            DrawRight(g, bold, pct, valueColor, x, y, PctW);
            x += PctW;

            if (TokensW > 0)
                DrawRight(g, body, tokens, _theme.TextSecondary, x + Gap, y, TokensW);
        }

        private void DrawBar(Graphics g, int x, int y, int w, int h, double fraction, Color color,
                             Level level)
        {
            using (var track = new SolidBrush(_theme.BarTrack))
            using (var path = RoundedRect(x, y, w, h))
                g.FillPath(track, path);

            if (fraction <= 0) return;
            if (fraction > 1) fraction = 1;

            var filled = (int)Math.Round(w * fraction);
            // 丸端がつぶれない最小幅を確保する。
            if (filled < h) filled = h;

            using (var brush = new SolidBrush(color))
            using (var path = RoundedRect(x, y, filled, h))
                g.FillPath(brush, path);

            // 注意・危険は色以外でも分かるようにする。規則はトレイと共通（Ui/LevelMark.cs）。
            LevelMark.Decorate(g, new RectangleF(x, y, filled, h), level, _theme.BarTrack,
                               _config.LevelMarks);
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

        /// <summary>
        /// セッションの行の詳細。読み取れた事実だけを並べる（推測は入れない）。
        /// 名前は行では省略されるので、ここでは全部出す。
        /// </summary>
        /// <summary>分母が分からない理由と、利用者にできること。</summary>
        private static string NoLimitReason(SessionRow s)
        {
            switch (s.DocsState)
            {
                case DocsLookupState.Pending:
                    return Strings.Get("hud.tipNoLimitPending");
                case DocsLookupState.NotFound:
                    // 再確認は 24 時間後。時刻だけ出すと今日のことに読めるので「明日」と書く。
                    return Strings.Get("hud.tipNoLimitNotFound");
                default:
                    return Strings.Get("hud.tipNoLimitOff");
            }
        }

        private System.Collections.Generic.List<TipLine> SessionTip(SessionRow s)
        {
            var lines = new System.Collections.Generic.List<TipLine>();

            // 名前は行では省略されるので、ここで全部出す（見出しなので太字）。
            lines.Add(new TipLine(
                string.IsNullOrEmpty(s.Title) ? Strings.Get("hud.untitled") : s.Title, false, true));

            if (s.IsExternal)
            {
                // 分かっている入口だけ名前で出す。知らない値のときは何も言わない。
                if (string.Equals(s.Entrypoint, "cli", StringComparison.OrdinalIgnoreCase))
                    lines.Add(new TipLine(Strings.Get("hud.tipTerminal"), true, false));
                else if (string.Equals(s.Entrypoint, "claude-vscode", StringComparison.OrdinalIgnoreCase))
                    lines.Add(new TipLine(Strings.Get("hud.tipVsCode"), true, false));
            }

            // モデルとエフォートを 1 行に。表示名が無いモデルは ID のまま出る。
            // 表示名の下に ID も出していたが、重複して不要と利用者の判断で外した（2026-09-26）。
            var modelLine = ModelText.Format(s);
            if (modelLine != null) lines.Add(new TipLine(modelLine, false, false));

            if (s.ContextTokens.HasValue)
            {
                if (s.ModelKnown && s.ContextLimit.HasValue && s.ContextPct.HasValue)
                {
                    lines.Add(new TipLine(Strings.Format("hud.tipTokens",
                        Thousands(s.ContextTokens.Value), Thousands(s.ContextLimit.Value),
                        PercentText.Format(s.ContextPct.Value)), false, false));

                    // 圧縮点は設定の compactThreshold（公式の既定値）。通知の本文と同じ根拠。
                    var left = (int)Math.Round(s.ContextLimit.Value * _config.CompactThreshold
                                               - s.ContextTokens.Value);
                    if (left > 0)
                        lines.Add(new TipLine(Strings.Format("hud.tipToCompact", Thousands(left)), false, false));

                    // 組み込みの表以外から得た分母は、出どころを添える（確かめる手がかり）。
                    if (s.LimitSource == LimitSource.Docs)
                        lines.Add(new TipLine(Strings.Get("hud.tipLimitDocs"), true, false));
                    else if (s.LimitSource == LimitSource.Config)
                        lines.Add(new TipLine(Strings.Get("hud.tipLimitConfig"), true, false));
                }
                else
                {
                    lines.Add(new TipLine(
                        Strings.Format("hud.tipTokensOnly", Thousands(s.ContextTokens.Value)), false, false));
                    // % が出ない理由と、利用者にできることを状況ごとに言い分ける。
                    // 「上限が分からない」だけでは、壊れているのか待てば出るのか分からない。
                    lines.Add(new TipLine(Strings.Get("hud.tipNoLimitHead"), false, true));
                    lines.Add(new TipLine(NoLimitReason(s), true, false));
                }
            }

            // 応答が記録された時刻（transcript の usage）と、タブ側の最終操作の新しい方を出す。
            // タブ側の記録は遅れることがあり、それだけを見ると「1 時間前」のように古く出る（実機で確認）。
            var last = Later(s.LastMeasuredUtc, s.LastActivityUtc);
            if (last.HasValue)
                lines.Add(new TipLine(Strings.Format("hud.tipLastReply", Ago(last.Value)), true, false));

            // 淡い行の理由をはっきり書く（色だけでは意味が伝わらない）。
            if (!s.ProcessAlive) lines.Add(new TipLine(Strings.Get("hud.tipStopped"), true, false));

            return lines;
        }

        /// <summary>
        /// レート枠の行の詳細。いつ記録された値かを書く（画面の値は Desktop が最後に記録したもの）。
        /// 「記録の後にも使用があったので実際はこれより高い」は出さない（2026-09-18、利用者の判断）。
        /// </summary>
        private System.Collections.Generic.List<TipLine> RateTip(RateLimitStatus r, bool fiveHour)
        {
            if (r == null) return null;

            var lines = new System.Collections.Generic.List<TipLine>();
            lines.Add(new TipLine(Strings.Format(fiveHour ? "notify.fiveHourBody" : "notify.weeklyBody",
                                                 fiveHour ? r.FiveHourPct : r.WeeklyPct), false, true));

            if (r.SampledAtUtc != DateTime.MinValue)
                lines.Add(new TipLine(Strings.Format("hud.tipSampled", Ago(r.SampledAtUtc)), true, false));

            if (_snapshot != null && _snapshot.Freshness == RateFreshness.Reference)
                lines.Add(new TipLine(Strings.Get("hud.tipReference"), true, false));

            // 行には残り 30 分からしか出さないリセット見込みも、ここでは常に出す
            //（自分で見に来たときだけなので、雑音にならない）。「表示しない」設定のときは出さない。
            if (fiveHour && r.NextFiveHourResetUtc.HasValue
                && !string.Equals(_config.ShowResets, "never", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new TipLine(Strings.Format("hud.tipReset",
                    r.NextFiveHourResetUtc.Value.ToLocalTime()
                     .ToString("H:mm", CultureInfo.InvariantCulture)), false, false));
            }

            return lines;
        }

        private static DateTime? Later(DateTime? a, DateTime? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return a.Value >= b.Value ? a : b;
        }

        private static string Thousands(int value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>どれくらい前か。秒・分・時間で言い方を変える。</summary>
        private static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalSeconds < 60)
                return Strings.Format("cli.secondsAgo", Math.Max(0, (int)span.TotalSeconds));
            if (span.TotalMinutes < 60)
                return Strings.Format("cli.minutesAgo", (int)span.TotalMinutes);
            return Strings.Format("cli.hoursAgo", (int)span.TotalHours);
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

        /// <summary>パネルを隠したら札も引っ込める（札だけ画面に残らないように）。</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) ClearTip();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tip != null && !_tip.IsDisposed) _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
