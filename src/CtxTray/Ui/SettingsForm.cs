using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CtxTray.Config;
using CtxTray.Native;

namespace CtxTray.Ui
{
    /// <summary>
    /// 設定ダイアログ。4 つのタブ（HUD／トレイアイコン／しきい値と通知／全般）に分ける。
    ///
    /// 以前は全項目を縦 1 列に並べていたが、長くてスクロールが要り、
    /// どこに何があるか探しにくかった（利用者の指摘、2026-09-15）。
    ///
    /// OK を押すと AppConfig.Save() するだけ。既存の設定ファイル監視がそれを拾って
    /// 反映するので、反映経路を二重に持たない。
    /// 例外は「右下の隅に戻す」で、設定というより操作なので押した時点で動かす。
    ///
    /// ★ 実行中の設定オブジェクトは書き換えない。OK の時点の最新の設定を複製し、
    ///   この画面が受け持つ値だけを載せて保存する（AppConfig.Clone の説明を参照）。
    ///   開いている間にパネルを動かしたりメニューで透過を切り替えたりしても、古い値で上書きしない。
    ///
    /// 圧縮点は観測で較正される値なので編集させず、現在値だけを見せる。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        /// <summary>行ラベルの列の幅（96 DPI 基準）。</summary>
        private const int LabelColumn = 180;

        /// <summary>補助説明のラベルに付ける印。配色で淡くするものを見分ける。</summary>
        private const string HintTag = "hint";

        /// <summary>
        /// この画面が基準にしている設定の複製。画面に入れる値と、表示だけの値（圧縮点など）に使う。
        /// 最初は開いた時点のもので、「適用」で保存するたび、保存したもので置き換える
        /// （言語・配色が変わったかの判定を、実際に反映済みの値と比べるため）。
        /// </summary>
        private AppConfig _config;

        /// <summary>実行中の最新の設定。OK の時点でこれを複製して保存する。</summary>
        private readonly Func<AppConfig> _current;

        private readonly Func<string, TrayGauge> _gaugeFor;
        /// <summary>そのホットキーが空いているかを確かめる（HudForm.IsHotkeyAvailable）。</summary>
        private readonly Func<string, bool> _hotkeyAvailable;
        /// <summary>Windows のライト/ダークが切り替わると入れ替わるので readonly にしない。</summary>
        private Theme _theme;

        /// <summary>
        /// 部品の大きさに掛ける倍率。窓を作る前に決まっている必要があるので、
        /// 開く場所（マウスのあるモニタ）から引く。プライマリの倍率で固定していた頃は、
        /// 倍率の違う 2 枚目で開くと大きさが合わなかった（2026-09-20）。
        /// 開いたあとに別のモニタへ動かしたときは作り直さない（利用者の決定）。
        /// </summary>
        private readonly float _s;

        /// <summary>開いたモニタ。大きさを決めたあとの中央寄せも、この画面の中で行う。</summary>
        private readonly Screen _screen;

        /// <summary>「右下の隅に戻す」が押された。</summary>
        public event EventHandler ResetHudPositionRequested;

        private int S(double v) { return (int)Math.Round(v * _s); }
        private Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        // --- タブ ---
        /// <summary>組み立ての根。言語を切り替えたときは、これごと捨てて組み直す。</summary>
        private TableLayoutPanel _root;
        private FlowLayoutPanel _tabStrip;
        private Panel _pageHost;
        private Control _buttons;
        private readonly List<RadioButton> _tabs = new List<RadioButton>();
        private readonly List<ScrollPage> _pages = new List<ScrollPage>();
        private readonly List<ThinScrollBar> _bars = new List<ThinScrollBar>();
        private readonly List<TableLayoutPanel> _grids = new List<TableLayoutPanel>();

        /// <summary>
        /// 入力欄とその角丸の枠の対応。値の出し入れは部品に対して行い、
        /// 画面に置くときだけ枠に差し替える（Place）。
        /// </summary>
        private readonly Dictionary<Control, FieldFrame> _frames = new Dictionary<Control, FieldFrame>();
        private TableLayoutPanel _grid;   // 組み立て中のタブの格子

        // --- HUD ---
        private CheckBox _hudRate, _hudSessions, _hideIdle, _hideStopped, _external;
        private CheckBox _showBar, _showTokens, _clickThrough, _showAtStartup, _hideFullscreen;
        private NumericUpDown _idleHours, _externalMax, _hudWidth;
        private ComboBox _showResets, _textSize;
        private TrackBar _opacity;
        private Label _opacityValue;
        private TextBox _hotkey;
        private Label _hotkeyWarning;

        // --- トレイアイコン ---
        private RadioButton _modeMulti, _modeSingle;
        private CheckBox _showContext, _showFiveHour, _showWeekly;
        private readonly Dictionary<string, Label> _swatches = new Dictionary<string, Label>();
        private Label _labelHeading;
        private RadioButton _labelLetters, _labelGlyphs;
        private PictureBox _preview;

        // --- しきい値と通知 ---
        private NumericUpDown _ctxWarn, _ctxDanger, _fhWarn, _fhDanger, _wkWarn, _wkDanger;
        private NumericUpDown _hysteresis, _minRepeat;
        private Label _ctxHint;
        private Label _thresholdOrderWarning;
        private CheckBox _levelMarks;
        private CheckBox _notifyContext, _notifyFh, _notifyWk;

        // --- 全般 ---
        private ComboBox _themeCombo, _language;
        private NumericUpDown _poll;

        public SettingsForm(Func<AppConfig> current, Func<string, TrayGauge> gaugeFor,
                            Func<string, bool> hotkeyAvailable)
        {
            _current = current;
            _config = current().Clone();
            _gaugeFor = gaugeFor;
            _hotkeyAvailable = hotkeyAvailable;
            _theme = Theme.Resolve(_config.Theme);

            // 倍率と置き場所は、窓を作る前にマウスのあるモニタから決める。
            var origin = Cursor.Position;
            _s = Dpi.ScaleForPoint(origin);
            _screen = Screen.FromPoint(origin);

            // 題名に版を出す。不具合の報告でどの版か分かるように（2026-09-18）。
            Text = Strings.Get("set.title") + " — " + AppVersion.Display;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            // 中央寄せは自分で行う（CenterScreen だと開くモニタを選べない）。
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            // タスクバー・Alt+Tab に出るアイコン。指定しないと WinForms 内蔵の
            // 古い既定アイコンになる（利用者の指摘、2026-09-20）。
            Icon = AppIconRenderer.Load();

            // 倍率は自分で掛ける。WinForms の自動スケールに任せると、
            // .NET Framework では DeviceDpi が 96 のままなので効かない（Dpi.cs 参照）。
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font(Theme.FontFamily, 9f * _s);
            // 幅 560 では英語のラベルや「注意／危険」の行が右端で切れたので広げた。
            ClientSize = new Size(S(600), S(600));
            // 作った時点から開くモニタの上に置く（あとで FitToContent が高さを決めて置き直す）。
            CenterOnScreen();

            Build();
            Load_(_config);
            ApplyTheme();
            SelectTab(0);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitToContent();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 窓枠（タイトルバー・枠線）は Windows が描くので、中身とは別に色を渡す。
            WindowChrome.Apply(Handle, _theme);
        }

        protected override void WndProc(ref Message m)
        {
            // Windows のライト/ダークの切り替え。この画面は非モーダルで出しっぱなしにできるので、
            // HUD と同じように開いたまま追従させる（2026-09-20、利用者の決定）。
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
        /// 配色を読み直して塗り直す。
        /// 設定のテーマが light / dark 固定なら Theme.Resolve が同じ色を返すので、
        /// Windows の切り替えでは見た目が変わらない（意図どおり）。
        /// </summary>
        private void ReloadTheme()
        {
            _theme = Theme.Resolve(_config.Theme);

            ApplyTheme();
            WindowChrome.Apply(Handle, _theme);
            foreach (var bar in _bars) bar.Theme = _theme;
            Invalidate(true);
        }

        /// <summary>
        /// 窓の高さを、いちばん長いタブが収まる高さに合わせる。
        /// 画面より高くなる場合だけ画面の 9 割で止め、タブの中をスクロールさせる。
        /// </summary>
        private void FitToContent()
        {
            PerformLayout();

            var inner = ClientSize.Width - S(16) * 2;
            var tallest = 0;
            foreach (var grid in _grids)
                tallest = Math.Max(tallest, grid.GetPreferredSize(new Size(inner, 0)).Height);

            var wanted = _tabStrip.Height + _buttons.Height + tallest + S(4 + 12) + S(8);
            var limit = (int)(_screen.WorkingArea.Height * 0.9);
            ClientSize = new Size(ClientSize.Width, Math.Min(wanted, limit));
            CenterOnScreen();

            // 高さが決まってから、ページと自前のスクロールバーを合わせる。
            PerformLayout();
            LayoutPages();
        }

        /// <summary>
        /// 開いたモニタの作業領域の中央へ置く。
        /// CenterToScreen はいま窓が載っているモニタを見るので、
        /// 別のモニタで開いた直後に呼ぶと意図しない画面へ寄る。
        /// </summary>
        private void CenterOnScreen()
        {
            var work = _screen.WorkingArea;
            Location = new Point(work.Left + (work.Width - Width) / 2,
                                 work.Top + Math.Max(0, (work.Height - Height) / 2));
        }

        // --- 組み立て -----------------------------------------------------------

        private void Build()
        {
            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0),
            };
            var root = _root;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _tabStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Padding = P(12, 10, 12, 0),
                Margin = new Padding(0),
            };
            _pageHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            // 窓の高さが決まった後や、表示倍率が変わった後にも追随させる。
            _pageHost.Resize += (s, e) => LayoutPages();

            BuildHudPage();
            BuildTrayPage();
            BuildThresholdPage();
            BuildGeneralPage();

            _buttons = BuildButtons();

            root.Controls.Add(_tabStrip, 0, 0);
            root.Controls.Add(_pageHost, 0, 1);
            root.Controls.Add(_buttons, 0, 2);
            Controls.Add(root);
        }

        private void BuildHudPage()
        {
            BeginPage("set.tabHud");

            // HUD と同じ並び（セッションごとのコンテキスト → レート枠）。
            Section("set.secHudContent");
            _hudSessions = Check("set.hudShowSessions");
            _hudRate = Check("set.hudShowRate");
            // 両方外すと HUD が空になるので、最後の 1 つは外させない。
            _hudRate.CheckedChanged += (s, e) => { if (!_hudRate.Checked && !_hudSessions.Checked) _hudRate.Checked = true; };
            _hudSessions.CheckedChanged += (s, e) => { if (!_hudRate.Checked && !_hudSessions.Checked) _hudSessions.Checked = true; };
            Full(_hudSessions);
            Full(_hudRate);

            Section("set.secHudSessions");
            _hideIdle = new CheckBox { AutoSize = true, Margin = BareCheckMargin };
            _idleHours = Number(1, 8760);
            _hideIdle.CheckedChanged += (s, e) => _idleHours.Enabled = _hideIdle.Checked;
            Full(Flow(_hideIdle, Toggles(Text_("set.hideIdlePre"), _hideIdle), _idleHours,
                      Toggles(Text_("set.hideIdlePost"), _hideIdle)));

            _hideStopped = Check("set.hideStopped");
            Full(_hideStopped);

            _external = Check("set.external");
            _externalMax = Number(1, 99);
            _external.CheckedChanged += (s, e) => _externalMax.Enabled = _external.Checked;
            Full(_external);
            Full(Flow(Indent(), Text_("set.externalMaxPre"), _externalMax, Text_("set.externalMaxPost")));

            Section("set.secHudColumns");
            _showBar = Check("set.showBar");
            Full(_showBar);
            _showTokens = Check("set.showTokens");
            Full(_showTokens);
            _showResets = Combo(Strings.Get("set.always"),
                                Strings.Format("set.autoNear", _config.ResetLeadFiveHourMinutes),
                                Strings.Get("set.never"));
            Row("set.showResets", _showResets);

            Section("set.secHudLook");
            _textSize = Combo(Strings.Get("set.sizeSmall"), Strings.Get("set.sizeNormal"),
                              Strings.Get("set.sizeLarge"), Strings.Get("set.sizeXLarge"));
            _textSize.Width = S(120);
            Row("set.textSize", _textSize);

            _hudWidth = Number(AppConfig.MinHudWidth, AppConfig.MaxHudWidth);
            Row("set.hudWidth", Flow(_hudWidth, Unit(Strings.Format("set.hudWidthUnit", AppConfig.DefaultHudWidth))));

            _opacity = new TrackBar
            {
                Minimum = 20, Maximum = 100, TickFrequency = 10,
                SmallChange = 5, LargeChange = 10, Width = S(200), AutoSize = false, Height = S(34),
            };
            _opacityValue = new Label { AutoSize = true, Padding = P(4, 8, 0, 0) };
            _opacity.ValueChanged += (s, e) => _opacityValue.Text = _opacity.Value + "%";
            Row("set.opacity", Flow(_opacity, _opacityValue));
            Hint("set.opacityHint");

            // チェックボックスの文は折り返せないので、長い注意書きは補足の行に分ける。
            _clickThrough = Check("set.clickThrough");
            Full(_clickThrough);
            Hint("set.clickThroughHint");

            Section("set.secHudPlace");
            _hotkey = Framed(new TextBox
            {
                Width = S(160),
                ReadOnly = true,
                Cursor = Cursors.Hand,
                // 枠は FieldFrame が描く。
                BorderStyle = BorderStyle.None,
            });
            _hotkey.KeyDown += OnHotkeyKeyDown;
            Row("set.hotkey", _hotkey);
            Hint("set.hotkeyHint");
            // ほかのアプリが使っているキーを選んだときだけ出す。
            _hotkeyWarning = Hint("set.hotkeyTaken");
            _hotkeyWarning.Visible = false;

            _showAtStartup = Check("set.showAtStartup");
            Full(_showAtStartup);

            _hideFullscreen = Check("set.hideFullscreen");
            Full(_hideFullscreen);
            Hint("set.hideFullscreenHint");

            Row("set.position", Button("set.resetPosition", (s, e) =>
            {
                var handler = ResetHudPositionRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }));
        }

        /// <summary>
        /// トレイアイコンのタブ。値の選び方は両モード共通にした（2026-09-16）。
        /// 以前は 1 個にまとめるモードだけ「形・外側の輪・内側の輪」を別に選ばせていた。
        /// </summary>
        private void BuildTrayPage()
        {
            BeginPage("set.tabTray");

            Section("set.secTrayCount");
            _modeMulti = Radio("set.modeMulti");
            _modeSingle = Radio("set.modeSingle");
            _modeMulti.CheckedChanged += (s, e) => UpdateTrayEnabled();
            // 同じ親に入れたラジオボタンだけが排他になる（タブ列や目印の選択とは別の親）。
            Full(Stack(_modeMulti, _modeSingle));

            Section("set.secTrayValues");
            _showContext = IdentityCheck("set.valContext", "context");
            _showFiveHour = IdentityCheck("set.valFiveHour", "fiveHour");
            _showWeekly = IdentityCheck("set.valWeekly", "weekly");

            _labelHeading = Section("set.secTrayLabel");
            _labelLetters = Radio("set.labelLetters");
            _labelGlyphs = Radio("set.labelGlyphs");
            _labelLetters.CheckedChanged += (s, e) => InvalidatePreview();
            Full(Stack(_labelLetters, _labelGlyphs));
            Hint("set.labelHint");

            Hint("set.trayOverflowHint");

            Section("set.secTrayPreview");
            _preview = new PictureBox { Width = S(360), Height = S(56), Margin = P(0, 2, 0, 2) };
            _preview.Paint += PaintPreview;
            Full(_preview);
            Hint("set.previewHint");
        }

        private void BuildThresholdPage()
        {
            BeginPage("set.tabThresholds");

            Section("set.secThreshold");
            _ctxWarn = Percent();
            _ctxDanger = Percent();
            _ctxWarn.ValueChanged += (s, e) => UpdateContextHint();
            _ctxDanger.ValueChanged += (s, e) => UpdateContextHint();
            Row("set.ctxThreshold", WarnDanger(_ctxWarn, _ctxDanger));
            _ctxHint = Hint(null);

            _fhWarn = Percent();
            _fhDanger = Percent();
            Row("set.fhThreshold", WarnDanger(_fhWarn, _fhDanger));

            _wkWarn = Percent();
            _wkDanger = Percent();
            Row("set.wkThreshold", WarnDanger(_wkWarn, _wkDanger));

            // 注意が危険より大きいと、判定が危険を先に見るので注意が一度も起きない。
            // ホットキーの赤字と同じ作りで、その場で知らせる（値は勝手に直さない）。
            _thresholdOrderWarning = Hint("set.thresholdOrder");

            // 色だけだと、赤と緑の区別が付きにくい人には通常と危険が見分けられない。
            _levelMarks = Check("set.levelMarks");
            _levelMarks.CheckedChanged += (s, e) => InvalidatePreview();
            Full(_levelMarks);
            Hint("set.levelMarksHint");
            foreach (var box in new[] { _ctxWarn, _ctxDanger, _fhWarn, _fhDanger, _wkWarn, _wkDanger })
                box.ValueChanged += (s, e) => UpdateThresholdOrderWarning();

            Section("set.secNotify");
            _notifyContext = Check("set.notifyContext");
            Full(_notifyContext);
            _notifyFh = Check("set.notifyFh");
            Full(_notifyFh);
            _notifyWk = Check("set.notifyWk");
            Full(_notifyWk);

            _hysteresis = Number(0, 50);
            Row("set.hysteresis", Flow(Text_("set.hysteresisPre"), _hysteresis, Text_("set.hysteresisUnit")));
            _minRepeat = Number(0, 1440);
            Row("set.minRepeat", Flow(_minRepeat, Text_("set.minutes")));
        }

        private void BuildGeneralPage()
        {
            BeginPage("set.tabGeneral");

            Section("set.secGeneral");
            _themeCombo = Combo(Strings.Get("set.auto"), Strings.Get("set.light"), Strings.Get("set.dark"));
            // 見本はタスクバーの配色で描くので、配色を変えたら描き直す。
            _themeCombo.SelectedIndexChanged += (s, e) => { UpdateSwatches(); InvalidatePreview(); };
            Row("set.theme", _themeCombo);

            _language = Combo(Strings.Get("set.langAuto"), "日本語", "English");
            Row("set.language", _language);

            _poll = Number(1, 600);
            Row("set.poll", Flow(_poll, Text_("set.seconds")));

            Row("set.compactPoint", new Label
            {
                AutoSize = true,
                Padding = P(0, 7, 0, 0),
                // 設定ファイルで変えた値を「既定値」と書かない。
                Text = Strings.Format(
                    Math.Abs(_config.CompactThreshold - AppConfig.DefaultCompactThreshold) < 1e-9
                        ? "set.compactValue" : "set.compactCustom",
                    _config.CompactThreshold),
            });
            Hint("set.compactHint");

            Row("set.configFile", Button("set.open", (s, e) => OpenFile()));
        }

        private Control BuildButtons()
        {
            // 保存できなかったのに閉じると、変更が消えたことに気づけない。
            var ok = Button("set.ok", (s, e) => { if (ApplyOrReport(false)) Close(); });
            var cancel = Button("set.cancel", (s, e) => Close());
            // 幅・文字の大きさ・不透明度は、出して見ながら決めるもの。
            // 「適用」が無いと、OK → 閉じる → 見る → 開き直す、の往復が要った（2026-09-20）。
            // Windows の慣例どおり、適用した分は「キャンセル」では戻らない。
            var apply = Button("set.apply", (s, e) => ApplyOrReport(true));
            var reset = Button("set.reset", (s, e) => ResetToDefaults());

            // WrapContents を切らないと、高さを測るときにボタンを縦に積んだ前提で計算され、
            // ボタン列の下に大きな空きができる（実画面で確認）。
            var right = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0),
            };
            // 右寄せの流し込みなので、先に足したものほど右端に来る。
            // 左から読んで OK・キャンセル・適用（Windows の並び）になるよう、適用から足す。
            right.Controls.Add(apply);
            right.Controls.Add(cancel);
            right.Controls.Add(ok);

            var left = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0),
            };
            left.Controls.Add(reset);

            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
                Padding = P(16, 8, 16, 12),
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bar.Controls.Add(left, 0, 0);
            bar.Controls.Add(right, 1, 0);

            AcceptButton = ok;
            CancelButton = cancel;
            return bar;
        }

        // --- タブ ---------------------------------------------------------------

        /// <summary>
        /// タブを 1 枚足す。
        ///
        /// WinForms 標準の TabControl はダーク配色でもタブ見出しの地が白く残るので使わない。
        /// ボタン風の RadioButton を横に並べ、下の Panel を切り替える。
        /// </summary>
        private void BeginPage(string titleKey)
        {
            var index = _pages.Count;

            var tab = new RadioButton
            {
                Text = Strings.Get(titleKey),
                Appearance = Appearance.Button,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Padding = P(10, 4, 10, 4),
                Margin = P(0, 0, 4, 0),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            tab.FlatAppearance.BorderSize = 0;
            tab.CheckedChanged += (s, e) => { if (tab.Checked) SelectTab(index); };
            _tabStrip.Controls.Add(tab);
            _tabs.Add(tab);

            // Dock = Fill にせず、_pageHost.Resize で大きさを合わせる（LayoutPages）。
            // 標準のスクロールバーを _pageHost のクリップ範囲の外へ押し出すために、
            // このパネルだけ横幅を広く取るため。
            var page = new ScrollPage
            {
                Padding = P(16, 4, 16, 12),
                Visible = false,
            };

            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(LabelColumn)));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // 自前の細いスクロールバー。Windows 標準のものは配色に追従せず古く見える
            // （利用者の指摘、2026-09-20）。タブ見出しを標準の TabControl で作らないのと同じ理由。
            var bar = new ThinScrollBar { Visible = false };
            bar.Attach(page);

            page.Controls.Add(_grid);
            _pageHost.Controls.Add(page);
            _pageHost.Controls.Add(bar);
            bar.BringToFront();

            _pages.Add(page);
            _bars.Add(bar);
            _grids.Add(_grid);
        }

        /// <summary>
        /// ページと自前のスクロールバーの位置・大きさを合わせる。
        ///
        /// ページは _pageHost より標準スクロールバーの幅だけ横に広くする。こうすると
        /// 標準のスクロールバーは _pageHost のクリップ範囲の外に出て見えなくなり、
        /// ホイール・キー操作・Tab での自動スクロールだけが残る。
        ///
        /// 広げるのはスクロールが要るタブだけ。いつも広げると、スクロールの要らないタブで
        /// 中身がその幅ぶん広がって右端が切れる。スクロールの要否で場合分けすると、
        /// どちらでも中身の幅は _pageHost の幅ちょうどで揃う。
        /// </summary>
        private void LayoutPages()
        {
            if (_pageHost == null || _pages.Count == 0) return;

            var host = _pageHost.ClientSize;
            if (host.Width <= 0 || host.Height <= 0) return;

            var slot = SystemInformation.VerticalScrollBarWidth;

            for (var i = 0; i < _pages.Count; i++)
            {
                var page = _pages[i];
                var inner = Math.Max(1, host.Width - page.Padding.Horizontal);
                var wanted = _grids[i].GetPreferredSize(new Size(inner, 0)).Height
                             + page.Padding.Vertical;

                var scrolls = wanted > host.Height;
                page.Bounds = new Rectangle(0, 0, host.Width + (scrolls ? slot : 0), host.Height);

                var bar = _bars[i];
                bar.Bounds = new Rectangle(host.Width - bar.Width - S(2), S(2),
                                           bar.Width, Math.Max(1, host.Height - S(4)));
                bar.BringToFront();
                bar.Sync();
            }
        }

        private void SelectTab(int index)
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == index;
                // バーはページの兄弟なので、選ばれているタブのものだけ出す。
                _bars[i].Active = i == index;
                _tabs[i].ForeColor = i == index ? _theme.TextPrimary : _theme.TextSecondary;
            }
            if (!_tabs[index].Checked) _tabs[index].Checked = true;
        }

        // --- 組み立ての小道具 ---------------------------------------------------

        private Label Section(string key)
        {
            var label = new Label
            {
                Text = Strings.Get(key),
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                // 節の間隔は詰めめにする。HUD タブが 150% 表示の画面の高さに収まらなかったため。
                Padding = P(0, _grid.RowCount == 0 ? 6 : 10, 0, 2),
            };
            AddFull(label);
            return label;
        }

        /// <summary>ラベル付きの行。ラベルを返す（淡色にするときに使う）。</summary>
        private Label Row(string labelKey, Control control)
        {
            var row = _grid.RowCount;
            var label = new Label
            {
                Text = Strings.Get(labelKey),
                AutoSize = true,
                // 英語のラベルは長いので、列からはみ出さず折り返させる。
                MaximumSize = new Size(S(LabelColumn - 8), 0),
                Padding = P(0, 7, 8, 0),
            };
            _grid.Controls.Add(label, 0, row);
            _grid.Controls.Add(Place(control), 1, row);
            _grid.RowCount++;
            return label;
        }

        /// <summary>ラベルの列を使わず、行いっぱいに置く。</summary>
        private void Full(Control control)
        {
            AddFull(control);
        }

        private void AddFull(Control control)
        {
            var placed = Place(control);
            _grid.Controls.Add(placed, 0, _grid.RowCount);
            _grid.SetColumnSpan(placed, 2);
            _grid.RowCount++;
        }

        private Label Hint(string key)
        {
            var label = new Label
            {
                Text = key == null ? string.Empty : Strings.Get(key),
                AutoSize = true,
                MaximumSize = new Size(S(540), 0),
                Padding = P(0, 0, 0, 4),
                Tag = HintTag,
            };
            AddFull(label);
            return label;
        }

        private ComboBox Combo(params string[] items)
        {
            var c = new ThemedCombo { Width = S(300) };
            foreach (var item in items) c.Items.Add(item);
            return c;
        }

        /// <summary>
        /// 入力欄を角丸の枠に入れる。枠は画面に置くときだけ使い、
        /// 値の出し入れは今までどおり部品に対して行う。
        /// </summary>
        private T Framed<T>(T inner) where T : Control
        {
            _frames[inner] = new FieldFrame(inner);
            return inner;
        }

        /// <summary>画面に置く実体。枠を持つ部品なら枠を返す。</summary>
        private Control Place(Control control)
        {
            FieldFrame frame;
            if (control != null && _frames.TryGetValue(control, out frame)) return frame;
            return control;
        }

        private CheckBox Check(string key)
        {
            return new CheckBox { Text = Strings.Get(key), AutoSize = true, Padding = P(0, 1, 0, 1) };
        }

        private RadioButton Radio(string key)
        {
            return new RadioButton { Text = Strings.Get(key), AutoSize = true, Margin = P(0, 2, 0, 2) };
        }

        /// <summary>縦に積む入れ物。ラジオボタンの組を作るのに使う。</summary>
        private static Control Stack(params Control[] controls)
        {
            var p = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0),
            };
            foreach (var c in controls) p.Controls.Add(c);
            return p;
        }

        /// <summary>
        /// 文字を持たないチェックボックスを、横に並べたラベルの文字と同じ高さに揃える外側の余白。
        /// ラベルは上に 6 の余白を持つので、それより少し下げないと四角が文字より上に浮く（実画面で確認）。
        /// </summary>
        private Padding BareCheckMargin { get { return P(0, 11, 0, 0); } }

        /// <summary>
        /// 識別色の見本付きのチェックボックス。トレイで目印とバーに付く色と同じ色を並べ、
        /// どの色がどの値かを設定画面で覚えられるようにする。
        /// </summary>
        private CheckBox IdentityCheck(string key, string value)
        {
            var box = new CheckBox { AutoSize = true, Margin = BareCheckMargin };
            var swatch = new Label { Text = "■", AutoSize = true, Padding = P(0, 6, 2, 0), Margin = new Padding(0) };
            var text = Unit(Strings.Get(key));
            text.Padding = P(0, 6, 0, 0);
            text.Margin = new Padding(0);

            Toggles(swatch, box);
            Toggles(text, box);
            _swatches[value] = swatch;

            box.CheckedChanged += (s, e) => InvalidatePreview();
            Full(Flow(box, swatch, text));
            return box;
        }

        // 数値欄は 4 桁（8760 時間）まで入れば足りる。70 だと「注意／危険」の行が右端からはみ出した。
        private NumericUpDown Number(int min, int max)
        {
            return Framed(new ThemedNumeric { Minimum = min, Maximum = max, Width = S(60) });
        }

        private NumericUpDown Percent()
        {
            return Framed(new ThemedNumeric { Minimum = 0, Maximum = 100, Width = S(56) });
        }

        /// <summary>数値の前後に置く短い文字。空文字なら何も置かない（語順が日英で違うため）。</summary>
        private Label Unit(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return new Label { Text = text, AutoSize = true, Padding = P(4, 6, 4, 0) };
        }

        private Label Text_(string key)
        {
            return Unit(Strings.Get(key));
        }

        /// <summary>チェックボックスの横の文字を押しても切り替わるようにする。</summary>
        private static Label Toggles(Label label, CheckBox box)
        {
            if (label != null) label.Click += (s, e) => { if (box.Enabled) box.Checked = !box.Checked; };
            return label;
        }

        private Control Indent()
        {
            return new Label { AutoSize = false, Width = S(18), Height = 1, Margin = new Padding(0) };
        }

        private Control WarnDanger(NumericUpDown warn, NumericUpDown danger)
        {
            return Flow(Unit(Strings.Get("set.warn")), warn, Unit("%"),
                        Unit(Strings.Get("set.danger")), danger, Unit("%"));
        }

        private Control Flow(params Control[] controls)
        {
            var p = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
            };
            foreach (var c in controls)
                if (c != null) p.Controls.Add(Place(c));
            return p;
        }

        private Button Button(string key, EventHandler onClick)
        {
            var b = new Button { Text = Strings.Get(key), AutoSize = true, Padding = P(10, 3, 10, 3) };
            b.Click += onClick;
            return b;
        }

        // --- 値の出し入れ -------------------------------------------------------

        /// <summary>設定を画面に入れる。「既定に戻す」では新しい AppConfig を渡す。</summary>
        private void Load_(AppConfig c)
        {
            // 「最後の 1 つは外させない」処理が途中で働かないよう、セッションを先に入れる。
            _hudSessions.Checked = c.HudShowSessions || !c.HudShowRateLimits;
            _hudRate.Checked = c.HudShowRateLimits;

            _hideIdle.Checked = c.HideIdleSessions;
            _idleHours.Value = Clamp(c.IdleHours, 1, 8760);
            _idleHours.Enabled = c.HideIdleSessions;
            _hideStopped.Checked = c.HideStoppedSessions;
            _external.Checked = c.ExternalSessionsEnabled;
            _externalMax.Value = Clamp(c.ExternalSessionsMax, 1, 99);
            _externalMax.Enabled = c.ExternalSessionsEnabled;

            _showBar.Checked = c.HudShowBar;
            _showTokens.Checked = c.HudShowTokens;
            _showResets.SelectedIndex = IndexOf(c.ShowResets, "always", "auto", "never");

            _textSize.SelectedIndex = TextSizeIndex(c.HudTextSize);
            _hudWidth.Value = Clamp(c.HudWidth, AppConfig.MinHudWidth, AppConfig.MaxHudWidth);
            _opacity.Value = Clamp((int)Math.Round(c.Opacity * 100), 20, 100);
            _opacityValue.Text = _opacity.Value + "%";
            _clickThrough.Checked = c.ClickThrough;

            _hotkey.Text = c.Hotkey;
            UpdateHotkeyWarning();
            _showAtStartup.Checked = c.HudShowAtStartup;
            _hideFullscreen.Checked = c.HideWhenFullscreen;

            if (c.TrayMultiMode) _modeMulti.Checked = true;
            else _modeSingle.Checked = true;
            if (string.Equals(c.TrayLabel, TrayIconRenderer.GlyphsStyle, StringComparison.OrdinalIgnoreCase))
                _labelGlyphs.Checked = true;
            else
                _labelLetters.Checked = true;
            _showContext.Checked = HasValue(c, "context");
            _showFiveHour.Checked = HasValue(c, "fiveHour");
            _showWeekly.Checked = HasValue(c, "weekly");
            UpdateTrayEnabled();

            _ctxWarn.Value = Clamp((int)Math.Round(c.ContextWarn * 100), 0, 100);
            _ctxDanger.Value = Clamp((int)Math.Round(c.ContextDanger * 100), 0, 100);
            _fhWarn.Value = Clamp(c.FiveHourWarn, 0, 100);
            _fhDanger.Value = Clamp(c.FiveHourDanger, 0, 100);
            _wkWarn.Value = Clamp(c.WeeklyWarn, 0, 100);
            _wkDanger.Value = Clamp(c.WeeklyDanger, 0, 100);
            UpdateContextHint();
            UpdateThresholdOrderWarning();
            _levelMarks.Checked = c.LevelMarks;

            _notifyContext.Checked = c.NotifyContext;
            _notifyFh.Checked = c.NotifyFiveHour;
            _notifyWk.Checked = c.NotifyWeekly;
            _hysteresis.Value = Clamp(c.NotifyHysteresisPts, 0, 50);
            _minRepeat.Value = Clamp(c.NotifyMinRepeatMinutes, 0, 1440);

            _themeCombo.SelectedIndex = IndexOf(c.Theme, "auto", "light", "dark");
            _language.SelectedIndex = IndexOf(c.Language, "auto", "ja", "en");
            _poll.Value = Clamp(c.PollSeconds, 1, 600);
        }

        /// <summary>
        /// 画面の値を載せた設定を組む。保存はしない。
        ///
        /// 呼ばれた時点の最新の設定を複製し、この画面が受け持つ値だけを載せる。
        /// 実行中の設定は書き換えないので、保存に失敗しても半端に反映されない。
        /// パネルの位置など、この画面が受け持たない値は最新のものがそのまま残る。
        /// </summary>
        private AppConfig FromScreen()
        {
            var c = _current().Clone();

            c.HudShowRateLimits = _hudRate.Checked;
            c.HudShowSessions = _hudSessions.Checked;
            c.HideIdleSessions = _hideIdle.Checked;
            c.IdleHours = (int)_idleHours.Value;
            c.HideStoppedSessions = _hideStopped.Checked;
            c.ExternalSessionsEnabled = _external.Checked;
            c.ExternalSessionsMax = (int)_externalMax.Value;

            c.HudShowBar = _showBar.Checked;
            c.HudShowTokens = _showTokens.Checked;
            c.ShowResets = Pick(_showResets.SelectedIndex, "always", "auto", "never");

            c.HudTextSize = Pick(_textSize.SelectedIndex, AppConfig.TextSizes);
            c.HudWidth = (int)_hudWidth.Value;
            c.Opacity = _opacity.Value / 100.0;
            c.ClickThrough = _clickThrough.Checked;

            uint mods, vk;
            if (HotkeyParser.TryParse(_hotkey.Text, out mods, out vk))
                c.Hotkey = _hotkey.Text;
            c.HudShowAtStartup = _showAtStartup.Checked;
            c.HideWhenFullscreen = _hideFullscreen.Checked;

            c.TrayMode = _modeMulti.Checked ? "multi" : "single";
            c.TrayLabel = SelectedLabelStyle();
            c.TrayValues = BuildTrayValues();

            c.ContextWarn = (double)_ctxWarn.Value / 100.0;
            c.ContextDanger = (double)_ctxDanger.Value / 100.0;
            c.FiveHourWarn = (int)_fhWarn.Value;
            c.FiveHourDanger = (int)_fhDanger.Value;
            c.WeeklyWarn = (int)_wkWarn.Value;
            c.WeeklyDanger = (int)_wkDanger.Value;
            c.LevelMarks = _levelMarks.Checked;

            c.NotifyContext = _notifyContext.Checked;
            c.NotifyFiveHour = _notifyFh.Checked;
            c.NotifyWeekly = _notifyWk.Checked;
            c.NotifyHysteresisPts = (int)_hysteresis.Value;
            c.NotifyMinRepeatMinutes = (int)_minRepeat.Value;

            c.Theme = Pick(_themeCombo.SelectedIndex, "auto", "light", "dark");
            c.Language = Pick(_language.SelectedIndex, "auto", "ja", "en");
            c.PollSeconds = (int)_poll.Value;

            return c;
        }

        /// <summary>
        /// 画面の値を保存し、この画面自身にも反映する。書けなかったら false。
        ///
        /// 保存するだけなら反映は設定ファイルの監視に任せられるが、
        /// 「適用」で閉じずに続けるときは、この画面だけが古い言語・配色のまま残る。
        /// そこで保存できたときだけ、ここで配色を読み直し、言語が変わっていれば組み直す（2026-09-20）。
        /// </summary>
        /// <param name="refreshUi">
        /// この画面を作り直す・塗り直す。OK はこの直後に閉じるので false
        /// （閉じる直前に組み直すと、一瞬ちらつくだけで意味がない）。
        /// </param>
        private bool Apply(bool refreshUi)
        {
            var next = FromScreen();
            if (!next.Save()) return false;

            var languageChanged = refreshUi && !string.Equals(_config.Language, next.Language,
                                                              StringComparison.OrdinalIgnoreCase);
            var themeChanged = refreshUi && !string.Equals(_config.Theme, next.Theme,
                                                           StringComparison.OrdinalIgnoreCase);

            // 以後の「変わったか」の比較と、表示だけの値（圧縮点）は、いま保存したものを基準にする。
            _config = next;

            if (languageChanged)
            {
                // Strings は全体で 1 つ。常駐側も次の再読込で同じ値にする。
                Strings.Apply(next.Language);
                Rebuild();
            }
            else if (themeChanged)
            {
                ReloadTheme();
            }

            return true;
        }

        /// <summary>
        /// 保存し、書けなかったらその場で知らせる。OK と「適用」で同じ扱いにする。
        /// 保存できなかったのに閉じると、変更が消えたことに気づけない。
        /// </summary>
        private bool ApplyOrReport(bool refreshUi)
        {
            if (Apply(refreshUi)) return true;

            MessageBox.Show(this, Strings.Format("set.saveFailed", AppConfig.FilePath), Text,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        /// <summary>
        /// 言語が変わったときに画面を組み直す。
        ///
        /// 文言は部品を作るときに埋め込まれるので、1 つずつ入れ替えるより作り直す方が
        /// 取りこぼしが無い（ドロップダウンの項目や、数値の前後に置く短い文字まで含むため）。
        /// 値は保存済みの設定から入れ直すので、画面の内容は変わらない。
        /// </summary>
        private void Rebuild()
        {
            var tab = SelectedTab();

            SuspendLayout();
            try
            {
                Controls.Remove(_root);
                _root.Dispose();

                _tabs.Clear();
                _pages.Clear();
                _bars.Clear();
                _grids.Clear();
                _frames.Clear();
                _swatches.Clear();

                Text = Strings.Get("set.title") + " — " + AppVersion.Display;
                Build();
                Load_(_config);
                // 言語と配色を一度に変えた場合もあるので、配色と窓枠はここで付け直す。
                ReloadTheme();
                SelectTab(tab);
            }
            finally
            {
                ResumeLayout(true);
            }

            FitToContent();
        }

        /// <summary>いま選ばれているタブ。組み直したあとも同じタブを開いたままにする。</summary>
        private int SelectedTab()
        {
            for (var i = 0; i < _tabs.Count; i++)
                if (_tabs[i].Checked) return i;
            return 0;
        }

        /// <summary>
        /// トレイのメニューでクリック透過を切り替えたときに呼ばれる。
        /// 画面のチェックを合わせておかないと、OK で切り替える前の値に戻してしまう。
        /// </summary>
        public void SyncClickThrough(bool on)
        {
            if (_clickThrough != null) _clickThrough.Checked = on;
        }

        private string SelectedLabelStyle()
        {
            return _labelGlyphs.Checked ? TrayIconRenderer.GlyphsStyle : TrayIconRenderer.LettersStyle;
        }

        /// <summary>
        /// 表示する値の並び。両モードとも、チェックされた値を正式な並び順で。
        /// 全部外されたらアイコンに何も描かれなくなるので、最低 1 つは残す。
        /// </summary>
        private List<string> BuildTrayValues()
        {
            var picked = new List<string>();
            if (_showContext.Checked) picked.Add("context");
            if (_showFiveHour.Checked) picked.Add("fiveHour");
            if (_showWeekly.Checked) picked.Add("weekly");

            if (picked.Count == 0) picked.Add("context");
            return picked;
        }

        private static bool HasValue(AppConfig c, string value)
        {
            if (c.TrayValues == null) return true;
            foreach (var v in c.TrayValues)
                if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int TextSizeIndex(string value)
        {
            for (var i = 0; i < AppConfig.TextSizes.Length; i++)
                if (string.Equals(AppConfig.TextSizes[i], value, StringComparison.OrdinalIgnoreCase)) return i;
            return 1;   // 知らない値なら「標準」
        }

        /// <summary>
        /// 目印は値ごとに分けるときだけ意味を持つので、1 個にまとめるときは淡色にする。
        /// 隠さず淡くするのは、何が選べるのかを見せておくため。
        ///
        /// Enabled は切らない。暗い配色で無効にしたラジオボタンは、Windows が文字を
        /// 暗い灰色で描くのでほとんど読めなかった（実画面で確認）。押せても害はなく、
        /// 選んだ目印は値ごとに分けたときに使われる。
        /// </summary>
        private void UpdateTrayEnabled()
        {
            if (_modeMulti == null || _labelLetters == null) return;

            var dim = !_modeMulti.Checked;
            Dim(_labelHeading, dim);
            Dim(_labelLetters, dim);
            Dim(_labelGlyphs, dim);

            InvalidatePreview();
        }

        private void Dim(Control control, bool dim)
        {
            if (control != null) control.ForeColor = dim ? _theme.TextSecondary : _theme.TextPrimary;
        }

        /// <summary>
        /// 圧縮点への到達率は分かりにくいので、1M モデルでの実トークン数を添える。
        ///
        /// あわせて、パネルに出る % も書く。パネルの % は「ウィンドウに対する消費率」で
        /// ここで入れる % と分母が違うため、「75 にしたのに 73% で色が変わった」と見えていた（2026-09-20）。
        /// </summary>
        private void UpdateContextHint()
        {
            var point = _config.CompactThreshold * 1000000.0;
            _ctxHint.Text = Strings.Format("set.ctxHint",
                point * (double)_ctxWarn.Value / 100.0,
                point * (double)_ctxDanger.Value / 100.0,
                (double)_ctxWarn.Value * _config.CompactThreshold,
                (double)_ctxDanger.Value * _config.CompactThreshold);
        }

        /// <summary>
        /// 「注意」が「危険」より大きい組があれば赤字で知らせる。
        /// Levels.ForPct は危険から先に判定するので、その状態では注意が一度も起きない。
        /// Paint_ がラベルの色を塗り直すので、配色を適用した後にも呼ぶ。
        /// </summary>
        private void UpdateThresholdOrderWarning()
        {
            if (_thresholdOrderWarning == null || _ctxWarn == null) return;

            var inverted = _ctxWarn.Value > _ctxDanger.Value
                        || _fhWarn.Value > _fhDanger.Value
                        || _wkWarn.Value > _wkDanger.Value;

            _thresholdOrderWarning.Visible = inverted;
            if (inverted) _thresholdOrderWarning.ForeColor = _theme.Danger;
        }

        /// <summary>
        /// 画面の値だけを既定に戻す。OK を押すまで保存しない。
        /// HUD の位置はこの画面では戻さない（設定を戻したら HUD が飛んでいく、という驚きを避ける）。
        /// </summary>
        private void ResetToDefaults()
        {
            Load_(new AppConfig());
        }

        private void OpenFile()
        {
            try
            {
                if (!System.IO.File.Exists(AppConfig.FilePath)) _config.Save();
                Process.Start(new ProcessStartInfo(AppConfig.FilePath) { UseShellExecute = true });
            }
            catch { }
        }

        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            var key = e.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return;

            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            if (parts.Count == 0) return;   // 修飾キー無しは他アプリと衝突しやすいので受けない

            parts.Add(key.ToString());
            _hotkey.Text = string.Join("+", parts.ToArray());
            UpdateHotkeyWarning();
        }

        /// <summary>
        /// 選んだキーがほかのアプリに取られていないかを確かめ、駄目なら赤字で知らせる。
        ///
        /// 主に効くのは画面を開いたとき（いま設定されているキーが、後から起動したアプリに
        /// 取られていた場合など）。ほかのアプリが先に取っているキーは、押してもこの欄に届かないので、
        /// キーを押した時点での確認はまず働かない（RegisterHotKey で予約されたキーは、前面の窓に届かない）。
        /// その場合の案内は set.hotkeyHint に書いた。
        /// なお、キーフックで先取りするアプリは RegisterHotKey を妨げないので、ここでも通知でも検出できない。
        /// Paint_ がラベルの色を塗り直すので、配色を適用した後にも呼ぶ。
        /// </summary>
        private void UpdateHotkeyWarning()
        {
            if (_hotkeyWarning == null || _hotkey == null) return;

            var taken = _hotkeyAvailable != null && !_hotkeyAvailable(_hotkey.Text);
            _hotkeyWarning.Visible = taken;
            if (taken) _hotkeyWarning.ForeColor = _theme.Danger;
        }

        // --- トレイアイコンの見本 -----------------------------------------------

        private string ThemeKey()
        {
            return Pick(_themeCombo == null ? 0 : _themeCombo.SelectedIndex, "auto", "light", "dark");
        }

        private void InvalidatePreview()
        {
            if (_preview != null) _preview.Invalidate();
        }

        /// <summary>
        /// 実物と同じ描画関数を、今の値と画面で選んでいる設定（OK 前）で呼ぶ。
        /// 保存する前に見た目を確かめられるようにするため。左が実寸、右が 2 倍。
        /// </summary>
        private void PaintPreview(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var theme = Theme.ResolveForTray(ThemeKey());

            // タスクバーの地色に近い色を敷く（--icon-preview と同じ値）。
            g.Clear(theme.IsDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            var side = TrayIconRenderer.CanvasSide();
            var icons = PreviewIcons(theme, side);
            try
            {
                var x = S(10);
                foreach (var icon in icons)
                {
                    using (var bmp = icon.ToBitmap())
                        g.DrawImage(bmp, x, (_preview.Height - side) / 2, side, side);
                    x += side + S(6);
                }

                x += S(18);
                foreach (var icon in icons)
                {
                    using (var bmp = icon.ToBitmap())
                        g.DrawImage(bmp, x, (_preview.Height - side * 2) / 2, side * 2, side * 2);
                    x += side * 2 + S(8);
                }
            }
            finally
            {
                foreach (var icon in icons) icon.Dispose();
            }
        }

        private List<Icon> PreviewIcons(Theme theme, int side)
        {
            var icons = new List<Icon>();
            if (_gaugeFor == null || _modeMulti == null) return icons;

            var values = BuildTrayValues();

            // 形の手がかりも画面のチェックに合わせる（OK を押す前に見え方が分かるように）。
            var marks = _levelMarks == null || _levelMarks.Checked;

            if (_modeMulti.Checked)
            {
                var style = SelectedLabelStyle();
                foreach (var value in values)
                    icons.Add(TrayIconRenderer.Render(new List<TrayGauge> { PreviewGauge(value) },
                                                      style, theme, marks, side));
            }
            else
            {
                var gauges = new List<TrayGauge>();
                foreach (var value in values) gauges.Add(PreviewGauge(value));
                icons.Add(TrayIconRenderer.Render(gauges, TrayIconRenderer.BarsStyle, theme, marks, side));
            }

            return icons;
        }

        private TrayGauge PreviewGauge(string value)
        {
            var gauge = _gaugeFor(value) ?? new TrayGauge();
            gauge.Value = value;
            return gauge;
        }

        // --- 配色 ---------------------------------------------------------------

        private void ApplyTheme()
        {
            BackColor = _theme.Background;
            ForeColor = _theme.TextPrimary;
            Paint_(this);

            var raised = _theme.IsDark ? ControlPaint.Light(_theme.Background, 0.25f)
                                       : Color.FromArgb(228, 232, 238);
            foreach (var tab in _tabs)
            {
                tab.BackColor = _theme.Background;
                tab.FlatAppearance.CheckedBackColor = raised;
                tab.FlatAppearance.MouseOverBackColor = raised;
            }

            // Paint_ は配色を持たない部品しか塗らないので、自前のバーには直接渡す。
            foreach (var bar in _bars) bar.Theme = _theme;

            UpdateSwatches();
            // Paint_ が全ラベルの色を塗り直すので、淡色と赤字の指定をやり直す。
            UpdateTrayEnabled();
            UpdateHotkeyWarning();
            UpdateThresholdOrderWarning();
        }

        private void UpdateSwatches()
        {
            var tray = Theme.ResolveForTray(ThemeKey());
            foreach (var kv in _swatches)
                kv.Value.ForeColor = tray.IdentityFor(kv.Key);
        }

        /// <summary>
        /// WinForms はダークモードに標準対応しないので、色を手で流し込む。
        /// 入力欄まで塗らないと、暗い背景に白い箱が並んで余計に不格好になる。
        /// </summary>
        private void Paint_(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                c.ForeColor = _theme.TextPrimary;

                // 入力欄は自前で描く部品に置き換えてある（Ui/ThemedFields.cs）。配色はそちらに渡す。
                if (c is ThemedCombo) { ((ThemedCombo)c).Theme = _theme; }
                else if (c is ThemedNumeric) { ((ThemedNumeric)c).Theme = _theme; }
                else if (c is FieldFrame)
                {
                    // 角丸の外側（四隅）に出る地。ページと同じ色にしておくと角が溶ける。
                    c.BackColor = _theme.Background;
                    ((FieldFrame)c).Theme = _theme;
                }
                else if (c is TextBox)
                {
                    c.BackColor = _theme.Field;
                }
                else if (c is Button)
                {
                    var b = (Button)c;
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = _theme.Border;
                    b.BackColor = _theme.IsDark ? ControlPaint.Light(_theme.Background, 0.25f)
                                                : Color.White;
                }
                else
                {
                    c.BackColor = _theme.Background;
                }

                // 補助テキストは控えめに。
                if (c is Label && HintTag.Equals(c.Tag))
                    c.ForeColor = _theme.TextSecondary;

                if (c.Controls.Count > 0) Paint_(c);
            }
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static int IndexOf(string value, params string[] options)
        {
            for (var i = 0; i < options.Length; i++)
                if (string.Equals(value, options[i], StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        private static string Pick(int index, params string[] options)
        {
            if (index < 0 || index >= options.Length) return options[0];
            return options[index];
        }
    }
}
