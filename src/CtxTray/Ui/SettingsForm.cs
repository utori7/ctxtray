using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CtxTray.Config;

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
    /// 圧縮点は観測で較正される値なので編集させず、現在値だけを見せる。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        /// <summary>行ラベルの列の幅（96 DPI 基準）。</summary>
        private const int LabelColumn = 180;

        /// <summary>補助説明のラベルに付ける印。配色で淡くするものを見分ける。</summary>
        private const string HintTag = "hint";

        private readonly AppConfig _config;
        private readonly Func<string, TrayGauge> _gaugeFor;
        private readonly Theme _theme;
        private readonly float _s = Dpi.SystemScale;

        /// <summary>「右下の隅に戻す」が押された。</summary>
        public event EventHandler ResetHudPositionRequested;

        private int S(double v) { return (int)Math.Round(v * _s); }
        private Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        // --- タブ ---
        private FlowLayoutPanel _tabStrip;
        private Panel _pageHost;
        private Control _buttons;
        private readonly List<RadioButton> _tabs = new List<RadioButton>();
        private readonly List<Panel> _pages = new List<Panel>();
        private readonly List<TableLayoutPanel> _grids = new List<TableLayoutPanel>();
        private TableLayoutPanel _grid;   // 組み立て中のタブの格子

        // --- HUD ---
        private CheckBox _hudRate, _hudSessions, _hideIdle, _external;
        private CheckBox _showBar, _showTokens, _clickThrough, _showAtStartup;
        private NumericUpDown _idleHours, _externalMax, _hudWidth;
        private ComboBox _showResets, _textSize;
        private TrackBar _opacity;
        private Label _opacityValue;
        private TextBox _hotkey;

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
        private CheckBox _notifyContext, _notifyFh, _notifyWk;

        // --- 全般 ---
        private ComboBox _themeCombo, _language;
        private NumericUpDown _poll;

        public SettingsForm(AppConfig config, Func<string, TrayGauge> gaugeFor)
        {
            _config = config;
            _gaugeFor = gaugeFor;
            _theme = Theme.Resolve(config.Theme);

            Text = Strings.Get("set.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            // 倍率は自分で掛ける。WinForms の自動スケールに任せると、
            // .NET Framework では DeviceDpi が 96 のままなので効かない（Dpi.cs 参照）。
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font(Theme.FontFamily, 9f * _s);
            // 幅 560 では英語のラベルや「注意／危険」の行が右端で切れたので広げた。
            ClientSize = new Size(S(600), S(600));

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
            var limit = (int)(Screen.FromControl(this).WorkingArea.Height * 0.9);
            ClientSize = new Size(ClientSize.Width, Math.Min(wanted, limit));
            CenterToScreen();
        }

        // --- 組み立て -----------------------------------------------------------

        private void Build()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0),
            };
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
            _hotkey = new TextBox { Width = S(160), ReadOnly = true, Cursor = Cursors.Hand };
            _hotkey.KeyDown += OnHotkeyKeyDown;
            Row("set.hotkey", _hotkey);
            Hint("set.hotkeyHint");

            _showAtStartup = Check("set.showAtStartup");
            Full(_showAtStartup);

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
            var ok = Button("set.ok", (s, e) =>
            {
                // 保存できなかったのに閉じると、変更が消えたことに気づけない。
                if (Save()) { Close(); return; }
                MessageBox.Show(this, Strings.Format("set.saveFailed", AppConfig.FilePath), Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            var cancel = Button("set.cancel", (s, e) => Close());
            var reset = Button("set.reset", (s, e) => ResetToDefaults());

            // WrapContents を切らないと、高さを測るときにボタンを縦に積んだ前提で計算され、
            // ボタン列の下に大きな空きができる（実画面で確認）。
            var right = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0),
            };
            right.Controls.Add(ok);
            right.Controls.Add(cancel);

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

            var page = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
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

            page.Controls.Add(_grid);
            _pageHost.Controls.Add(page);
            _pages.Add(page);
            _grids.Add(_grid);
        }

        private void SelectTab(int index)
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == index;
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
            _grid.Controls.Add(control, 1, row);
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
            _grid.Controls.Add(control, 0, _grid.RowCount);
            _grid.SetColumnSpan(control, 2);
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
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300) };
            foreach (var item in items) c.Items.Add(item);
            return c;
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
            return new NumericUpDown { Minimum = min, Maximum = max, Width = S(60) };
        }

        private NumericUpDown Percent()
        {
            return new NumericUpDown { Minimum = 0, Maximum = 100, Width = S(56) };
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

        private static Control Flow(params Control[] controls)
        {
            var p = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
            };
            foreach (var c in controls)
                if (c != null) p.Controls.Add(c);
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
            _showAtStartup.Checked = c.HudShowAtStartup;

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

            _notifyContext.Checked = c.NotifyContext;
            _notifyFh.Checked = c.NotifyFiveHour;
            _notifyWk.Checked = c.NotifyWeekly;
            _hysteresis.Value = Clamp(c.NotifyHysteresisPts, 0, 50);
            _minRepeat.Value = Clamp(c.NotifyMinRepeatMinutes, 0, 1440);

            _themeCombo.SelectedIndex = IndexOf(c.Theme, "auto", "light", "dark");
            _language.SelectedIndex = IndexOf(c.Language, "auto", "ja", "en");
            _poll.Value = Clamp(c.PollSeconds, 1, 600);
        }

        /// <summary>画面の値を設定に入れて保存する。書けなかったら false。</summary>
        private bool Save()
        {
            _config.HudShowRateLimits = _hudRate.Checked;
            _config.HudShowSessions = _hudSessions.Checked;
            _config.HideIdleSessions = _hideIdle.Checked;
            _config.IdleHours = (int)_idleHours.Value;
            _config.ExternalSessionsEnabled = _external.Checked;
            _config.ExternalSessionsMax = (int)_externalMax.Value;

            _config.HudShowBar = _showBar.Checked;
            _config.HudShowTokens = _showTokens.Checked;
            _config.ShowResets = Pick(_showResets.SelectedIndex, "always", "auto", "never");

            _config.HudTextSize = Pick(_textSize.SelectedIndex, AppConfig.TextSizes);
            _config.HudWidth = (int)_hudWidth.Value;
            _config.Opacity = _opacity.Value / 100.0;
            _config.ClickThrough = _clickThrough.Checked;

            uint mods, vk;
            if (HotkeyParser.TryParse(_hotkey.Text, out mods, out vk))
                _config.Hotkey = _hotkey.Text;
            _config.HudShowAtStartup = _showAtStartup.Checked;

            _config.TrayMode = _modeMulti.Checked ? "multi" : "single";
            _config.TrayLabel = SelectedLabelStyle();
            _config.TrayValues = BuildTrayValues();

            _config.ContextWarn = (double)_ctxWarn.Value / 100.0;
            _config.ContextDanger = (double)_ctxDanger.Value / 100.0;
            _config.FiveHourWarn = (int)_fhWarn.Value;
            _config.FiveHourDanger = (int)_fhDanger.Value;
            _config.WeeklyWarn = (int)_wkWarn.Value;
            _config.WeeklyDanger = (int)_wkDanger.Value;

            _config.NotifyContext = _notifyContext.Checked;
            _config.NotifyFiveHour = _notifyFh.Checked;
            _config.NotifyWeekly = _notifyWk.Checked;
            _config.NotifyHysteresisPts = (int)_hysteresis.Value;
            _config.NotifyMinRepeatMinutes = (int)_minRepeat.Value;

            _config.Theme = Pick(_themeCombo.SelectedIndex, "auto", "light", "dark");
            _config.Language = Pick(_language.SelectedIndex, "auto", "ja", "en");
            _config.PollSeconds = (int)_poll.Value;

            return _config.Save();
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
        /// </summary>
        private void UpdateContextHint()
        {
            var point = _config.CompactThreshold * 1000000.0;
            _ctxHint.Text = Strings.Format("set.ctxHint",
                point * (double)_ctxWarn.Value / 100.0,
                point * (double)_ctxDanger.Value / 100.0);
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

            if (_modeMulti.Checked)
            {
                var style = SelectedLabelStyle();
                foreach (var value in values)
                    icons.Add(TrayIconRenderer.Render(new List<TrayGauge> { PreviewGauge(value) },
                                                      style, theme, side));
            }
            else
            {
                var gauges = new List<TrayGauge>();
                foreach (var value in values) gauges.Add(PreviewGauge(value));
                icons.Add(TrayIconRenderer.Render(gauges, TrayIconRenderer.BarsStyle, theme, side));
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

            UpdateSwatches();
            // Paint_ が全ラベルの色を塗り直すので、淡色の指定をやり直す。
            UpdateTrayEnabled();
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

                if (c is TextBox || c is NumericUpDown || c is ComboBox)
                {
                    c.BackColor = _theme.IsDark ? ControlPaint.Light(_theme.Background, 0.35f)
                                                : Color.White;
                    var combo = c as ComboBox;
                    if (combo != null) combo.FlatStyle = FlatStyle.Flat;
                    var num = c as NumericUpDown;
                    if (num != null) num.BorderStyle = BorderStyle.FixedSingle;
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
