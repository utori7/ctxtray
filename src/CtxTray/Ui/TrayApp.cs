using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CtxTray.Collect;
using CtxTray.Config;
using CtxTray.Core;
using CtxTray.Notify;

namespace CtxTray.Ui
{
    /// <summary>
    /// 常駐本体。トレイアイコンと HUD をまとめて面倒を見る。
    ///
    /// 開発初期の反省: 手動で起動するコンソール窓では、実際に動いていた時間が 10.4% しかなかった。
    /// 「常時可視」は常駐が前提条件なので、ここは落ちない・窓を出さないことを優先する。
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        /// <summary>
        /// 通知領域のアイコン。値ごとに 1 スロットで、並びは AppConfig.AllTrayValues と同じ
        /// （context / fiveHour / weekly）。
        ///
        /// ★ 使わないスロットも必ず生成する。
        ///   WinForms は NotifyIcon の生成順に uID を振り、Windows は
        ///   「タスクバーに出す」設定を 実行ファイル＋uID で覚える。
        ///   条件によって生成をやめると uID がずれ、利用者が出したアイコンの設定が
        ///   別の値に付いてしまう。表示・非表示は Visible で切り替える。
        /// </summary>
        private readonly NotifyIcon[] _trays;
        private readonly Icon[] _icons;

        private readonly Timer _timer = new Timer();
        private readonly ThresholdNotifier _notifier;
        private readonly ToolStripMenuItem _hudItem;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ContextMenuStrip _menu;

        private AppConfig _config;
        private HudForm _hud;
        private Snapshot _lastSnapshot;
        private SettingsForm _settings;

        private FileSystemWatcher _dataWatcher;
        private FileSystemWatcher _projectWatcher;
        private FileSystemWatcher _configWatcher;

        private volatile bool _dirty = true;
        private volatile bool _configDirty;
        private DateTime _lastRefreshUtc = DateTime.MinValue;

        // 稼働率を自分で測る。手動起動では 10.4% しか出なかったという実測があるので、
        // 常駐できているかを利用者が確認できるようにする。
        private readonly DateTime _startedUtc = DateTime.UtcNow;

        // 想定外の例外の記録（ReportError）。
        private const int ErrorNotifyIntervalMinutes = 30;
        private string _lastError;
        private DateTime _lastErrorUtc;
        private DateTime _lastErrorNotifiedUtc = DateTime.MinValue;

        public TrayApp()
        {
            string problem;
            _config = AppConfig.Load(out problem);

            // uID を固定するため、モードによらず常に同じ順序で全スロットを作る。
            _trays = new NotifyIcon[AppConfig.AllTrayValues.Length];
            _icons = new Icon[_trays.Length];
            for (var i = 0; i < _trays.Length; i++) _trays[i] = new NotifyIcon();

            // 通知は「いま表示されているアイコン」から出す。値ごとに分けるモードで
            // コンテキストを外すと 1 個目のアイコンは非表示になり、そこからは出せないため。
            _notifier = new ThresholdNotifier(VisibleTray);

            // 描画などで想定外の例外が起きても、.NET の例外ダイアログを出さずに動き続ける。
            Application.ThreadException += OnThreadException;

            Strings.Apply(_config.Language);

            // HUD の項目は表示状態で文言が変わるので、Tag を付けず UpdateMenuState で付ける。
            _hudItem = new ToolStripMenuItem(Strings.Get("menu.showHud"), null, (s, e) => ToggleHud());
            _autoStartItem = MenuItem("menu.autoStart", ToggleAutoStart);

            _menu = new ContextMenuStrip();
            // 開くたびにチェックを付け直す。ショートカットを利用者が消したり、exe を移したりしても
            // 古い表示のままにならないように。
            _menu.Opening += (s, e) => UpdateMenuState();
            _menu.Items.Add(_hudItem);
            _menu.Items.Add(_autoStartItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(MenuItem("menu.settings", OpenSettings));
            _menu.Items.Add(MenuItem("menu.refresh", () => { _dirty = true; Tick(null, null); }));
            _menu.Items.Add(MenuItem("menu.openConfig", OpenConfig));
            _menu.Items.Add(MenuItem("menu.uptime", ShowUptime));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(MenuItem("menu.exit", ExitApp));

            // メニューとダブルクリックは全スロットで共有する。
            foreach (var tray in _trays)
            {
                tray.ContextMenuStrip = _menu;
                tray.Text = "ctxtray";
                tray.Icon = SystemIcons.Application;
                tray.DoubleClick += (s, e) => ToggleHud();
            }
            // 最初の描画までは主アイコンだけ出す。
            _trays[0].Visible = true;

            _hud = CreateHud();
            if (_config.HudShowAtStartup) _hud.Show();

            SetupWatchers();

            _timer.Interval = 500;
            _timer.Tick += Tick;
            _timer.Start();

            if (!string.IsNullOrEmpty(problem))
                _notifier.ShowInfo("ctxtray", problem);

            UpdateMenuState();
            Tick(null, null);
        }

        private HudForm CreateHud()
        {
            var hud = new HudForm(_config);
            hud.HotkeyPressed += (s, e) => ToggleHud();
            // OS のテーマが変わったら、トレイアイコンも描き直す。
            hud.ThemeChanged += (s, e) => { if (_lastSnapshot != null) SetTrayIcon(_lastSnapshot); };
            hud.RestorePosition();

            // ホットキーは HUD のウィンドウに登録される。起動時に HUD を出さない設定でも
            // ホットキーで出せるよう、表示せずにハンドルだけ作っておく。
            var handle = hud.Handle;
            GC.KeepAlive(handle);

            return hud;
        }

        // --- 更新 ---------------------------------------------------------------

        /// <summary>
        /// 0.5 秒ごとの更新。常駐用途なので、1 回の失敗で落とさず次のティックで取り直す。
        ///
        /// ★ ここで例外を逃がすと WinForms の既定動作で例外ダイアログが出る。
        ///   タイマーはダイアログの裏でも動き続けるので、同じ例外が続くとダイアログが重なる。
        /// </summary>
        private void Tick(object sender, EventArgs e)
        {
            try
            {
                TickCore();
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        private void TickCore()
        {
            if (_configDirty)
            {
                _configDirty = false;
                ReloadConfig();
            }

            // 待たせている通知は、収集を挟まなくても一定間隔で出す。
            _notifier.Pump();

            var due = (DateTime.UtcNow - _lastRefreshUtc).TotalSeconds >= _config.PollSeconds;
            if (!_dirty && !due) return;

            _dirty = false;
            _lastRefreshUtc = DateTime.UtcNow;

            // 収集は例外を投げない（失敗は snap.Diag に残る）。
            var snap = SnapshotBuilder.Build(_config.ExternalSessionsEnabled, false, _config.ModelLimits);

            // 表示の対象を絞る。ここで一度だけ絞り、HUD・トレイ・ツールチップ・通知が
            // すべて同じ結果を見る（HUD で隠した行にトレイや通知が反応しないように）。
            SessionFilter.Apply(snap, _config, DateTime.UtcNow);

            _lastSnapshot = snap;

            SetTrayIcon(snap);
            // どのアイコンをホバーしても全体が分かるよう、同じ 3 行を全部に付ける。
            SetTooltip(BuildTooltip(snap));

            if (_hud != null && !_hud.IsDisposed)
                _hud.SetSnapshot(snap);

            _notifier.Check(snap, _config);
        }

        private void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            ReportError(e.Exception);
        }

        /// <summary>
        /// 想定外の例外を記録する。ツールチップに理由を出し、稼働状況の画面で最後の 1 件を見られるようにする。
        /// 通知は、同じ失敗が続いたときに連投しないよう 30 分に 1 回まで。
        /// </summary>
        private void ReportError(Exception ex)
        {
            try
            {
                var message = ex == null ? "?" : ex.GetType().Name + ": " + ex.Message;
                _lastError = message;
                _lastErrorUtc = DateTime.UtcNow;

                var tip = "ctxtray - " + Strings.Format("tip.error", message);
                SetTooltip(tip.Length > 63 ? tip.Substring(0, 63) : tip);

                if ((DateTime.UtcNow - _lastErrorNotifiedUtc).TotalMinutes >= ErrorNotifyIntervalMinutes)
                {
                    _lastErrorNotifiedUtc = DateTime.UtcNow;
                    _notifier.ShowInfo("ctxtray", Strings.Format("app.error", message));
                }
            }
            catch
            {
                // 報告のための処理で、さらに例外を増やさない。
            }
        }

        /// <summary>通知の出し先。表示中のアイコンのうち最初のもの。</summary>
        private NotifyIcon VisibleTray()
        {
            foreach (var tray in _trays)
                if (tray.Visible) return tray;
            return null;
        }

        /// <summary>
        /// 最も圧縮に近いセッション。
        ///
        /// 選ぶ基準は % ではなく圧縮点への到達率。分母の違うモデル (1M と 200k) が
        /// 混ざると、% が大きい方が圧縮に近いとは限らない。
        /// </summary>
        private SessionRow MostPressed(Snapshot snap)
        {
            SessionRow worst = null;
            var worstReach = double.MinValue;
            foreach (var s in snap.Sessions)
            {
                var reach = Levels.ContextReach(s, _config);
                if (!reach.HasValue) continue;
                if (reach.Value > worstReach) { worstReach = reach.Value; worst = s; }
            }
            return worst;
        }

        /// <summary>
        /// ツールチップ。何の値かをラベルで明示する。
        ///
        /// NotifyIcon.Text は 63 文字が上限。行の途中で切れたものを見せたくないので、
        /// 先に 2・3 行目を組んでから、余った文字数にセッション名を詰める。
        /// </summary>
        private string BuildTooltip(Snapshot snap)
        {
            const int Limit = 63;

            var r = snap.RateLimits;

            var tail = new List<string>();
            if (r == null)
            {
                tail.Add(Strings.Get("tip.rateUnknown"));
            }
            else
            {
                var fh = r.FiveHourPct + "%";
                var reset = FiveHourResetSuffix(r);
                if (reset != null) fh += " " + reset;
                tail.Add(Strings.Format("tip.fiveHour", fh));

                tail.Add(Strings.Format("tip.weekly", r.WeeklyPct + "%"));
            }

            var worst = MostPressed(snap);
            if (worst == null || !worst.ContextPct.HasValue)
                return Join(new List<string> { Strings.Get("tip.noSessions") }, tail);

            var pct = (int)Math.Round(worst.ContextPct.Value);
            var head = Strings.Format("tip.context", pct, string.Empty).TrimEnd();

            // 残りの文字数にセッション名を詰める。入らなければ名前を落とす。
            var budget = Limit - Length(tail) - head.Length - 2;
            var title = string.IsNullOrEmpty(worst.Title) ? string.Empty : worst.Title;
            if (budget > 2 && title.Length > 0)
            {
                if (title.Length > budget) title = title.Substring(0, Math.Max(1, budget - 1)) + "…";
                head = Strings.Format("tip.context", pct, title);
            }

            return Join(new List<string> { head }, tail);
        }

        private static int Length(List<string> lines)
        {
            var n = 0;
            foreach (var l in lines) n += l.Length + 1;   // 改行ぶん
            return n;
        }

        private static string Join(List<string> head, List<string> tail)
        {
            var all = new List<string>(head);
            all.AddRange(tail);

            // それでも溢れるなら、後ろの行から落とす（途中で切らない）。
            while (all.Count > 1 && string.Join("\n", all.ToArray()).Length > 63)
                all.RemoveAt(all.Count - 1);

            var text = string.Join("\n", all.ToArray());
            return text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private string FiveHourResetSuffix(RateLimitStatus r)
        {
            if (r == null || !r.NextFiveHourResetUtc.HasValue) return null;

            var mode = (_config.ShowResets ?? "auto").ToLowerInvariant();
            if (mode == "never") return null;

            var remain = r.NextFiveHourResetUtc.Value - DateTime.UtcNow;
            if (mode == "auto" && (remain.TotalSeconds <= 0 ||
                                   remain.TotalMinutes > _config.ResetLeadFiveHourMinutes))
                return null;

            return "→" + r.NextFiveHourResetUtc.Value.ToLocalTime()
                          .ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void SetTooltip(string text)
        {
            foreach (var tray in _trays)
                if (tray.Visible) tray.Text = text;
        }

        /// <summary>
        /// アイコンを描く。色の規則は両モード共通（普段は値ごとの色、注意・危険の値だけ黄・赤）。
        ///   single … 主アイコン 1 個に、選んだ値を横のバーで並べる
        ///   multi  … 値ごとにアイコンを分け、目印（英字か絵記号）とバーで描く
        /// </summary>
        private void SetTrayIcon(Snapshot snap)
        {
            var theme = Theme.ResolveForTray(_config.Theme);
            var worst = MostPressed(snap);

            if (_config.TrayMultiMode) RenderMulti(snap, worst, theme);
            else RenderSingle(snap, worst, theme);
        }

        private void RenderSingle(Snapshot snap, SessionRow worst, Theme theme)
        {
            var gauges = new List<TrayGauge>();
            foreach (var value in _config.OrderedTrayValues())
                gauges.Add(Gauge(value, snap, worst, theme));

            Apply(0, TrayIconRenderer.Render(gauges, TrayIconRenderer.BarsStyle, theme));

            for (var i = 1; i < _trays.Length; i++) Hide(i);
        }

        private void RenderMulti(Snapshot snap, SessionRow worst, Theme theme)
        {
            for (var slot = 0; slot < _trays.Length; slot++)
            {
                var value = AppConfig.AllTrayValues[slot];
                if (!ContainsValue(_config.TrayValues, value)) { Hide(slot); continue; }

                Apply(slot, TrayIconRenderer.Render(new List<TrayGauge> { Gauge(value, snap, worst, theme) },
                                                    _config.TrayLabel, theme));
            }
        }

        /// <summary>描画用のゲージ。値の名前（目印を選ぶため）と識別色を添える。</summary>
        private TrayGauge Gauge(string value, Snapshot snap, SessionRow worst, Theme theme)
        {
            var gauge = BuildGauge(value, snap, worst);
            gauge.Value = value;
            gauge.Identity = theme.IdentityFor(value);
            return gauge;
        }

        private static bool ContainsValue(List<string> list, string value)
        {
            foreach (var v in list)
                if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void Apply(int slot, Icon icon)
        {
            _trays[slot].Icon = icon;
            _trays[slot].Visible = true;

            // 先に差し替えてから古いものを捨てる。GDI ハンドルは自動で解放されない。
            if (_icons[slot] != null) _icons[slot].Dispose();
            _icons[slot] = icon;
        }

        private void Hide(int slot)
        {
            if (!_trays[slot].Visible) return;
            _trays[slot].Visible = false;
        }

        private TrayGauge BuildGauge(string value, Snapshot snap, SessionRow worst)
        {
            var r = snap.RateLimits;

            if (string.Equals(value, "fiveHour", StringComparison.OrdinalIgnoreCase))
            {
                if (r == null) return new TrayGauge();
                return new TrayGauge
                {
                    Fraction = r.FiveHourPct / 100.0,
                    Level = Levels.ForFiveHour(r, _config),
                    Percent = r.FiveHourPct,
                };
            }

            if (string.Equals(value, "weekly", StringComparison.OrdinalIgnoreCase))
            {
                if (r == null) return new TrayGauge();
                return new TrayGauge
                {
                    Fraction = r.WeeklyPct / 100.0,
                    Level = Levels.ForWeekly(r, _config),
                    Percent = r.WeeklyPct,
                };
            }

            // 既定は context。バーが表す割合は画面に出す % と同じ値にする
            // （公式インジケーターと食い違わせない）。
            if (worst == null || !worst.ContextPct.HasValue) return new TrayGauge();
            return new TrayGauge
            {
                Fraction = worst.ContextPct.Value / 100.0,
                Level = Levels.ForContext(worst, _config),
                Percent = worst.ContextPct.Value,
            };
        }

        /// <summary>設定画面の見本用。最新の値で 1 つ分のゲージを組む。</summary>
        private TrayGauge PreviewGauge(string value)
        {
            if (_lastSnapshot == null) return new TrayGauge();
            return BuildGauge(value, _lastSnapshot, MostPressed(_lastSnapshot));
        }

        // --- 監視 ---------------------------------------------------------------

        private void SetupWatchers()
        {
            var diag = new Diagnostics();
            var dataRoot = Paths.FindClaudeDataRoot(diag);
            var configDir = Paths.FindClaudeCodeConfigDir(diag);

            _dataWatcher = TryWatch(dataRoot, "*.json", false);
            if (configDir != null)
                _projectWatcher = TryWatch(Path.Combine(configDir, "projects"), "*.jsonl", true);

            // 設定ファイルの変更を拾って、保存した瞬間に反映する。
            try
            {
                Directory.CreateDirectory(AppConfig.Dir);
                _configWatcher = new FileSystemWatcher(AppConfig.Dir, "config.json");
                _configWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                _configWatcher.Changed += (s, e) => _configDirty = true;
                _configWatcher.Created += (s, e) => _configDirty = true;
                _configWatcher.Renamed += (s, e) => _configDirty = true;
                // 削除も拾う。消したら既定値に戻る、が期待される動きなので。
                _configWatcher.Deleted += (s, e) => _configDirty = true;
                _configWatcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private FileSystemWatcher TryWatch(string path, string filter, bool recursive)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return null;
            try
            {
                var w = new FileSystemWatcher(path, filter)
                {
                    IncludeSubdirectories = recursive,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                };
                // 監視スレッドから UI を触らない。フラグだけ立てて次のティックで拾う。
                w.Changed += (s, e) => _dirty = true;
                w.Created += (s, e) => _dirty = true;
                w.Deleted += (s, e) => _dirty = true;
                w.Renamed += (s, e) => _dirty = true;
                w.EnableRaisingEvents = true;
                return w;
            }
            catch { return null; }
        }

        private void ReloadConfig()
        {
            string problem;
            bool failed;
            var loaded = AppConfig.Load(out problem, out failed);

            // 実行中に書き換えた設定が読めないときは、既定値に切り替えず直前の設定のまま動く。
            // 手で編集している途中の書き損じで、アイコンの出し方や言語が急に変わらないように。
            if (failed)
            {
                _notifier.ShowInfo("ctxtray", Strings.Get("config.brokenKept"));
                return;
            }

            // 位置は実行中の値を優先する（ドラッグ直後に設定ファイルで巻き戻さない）。
            loaded.HudX = _config.HudX;
            loaded.HudY = _config.HudY;
            _config = loaded;
            Strings.Apply(_config.Language);
            RefreshMenuTexts();

            // HUD にも新しい設定オブジェクトを渡す。渡さないと HUD だけ古い設定を見続ける。
            if (_hud != null && !_hud.IsDisposed) _hud.ApplyConfig(_config);
            if (!string.IsNullOrEmpty(problem)) _notifier.ShowInfo("ctxtray", problem);

            _dirty = true;
        }

        // --- メニュー -----------------------------------------------------------

        /// <summary>
        /// HUD の表示・非表示。状態は保存しない。
        /// 起動時に出すかどうかは設定（起動したときに HUD を表示する）で決める。
        /// </summary>
        private void ToggleHud()
        {
            if (_hud == null || _hud.IsDisposed) _hud = CreateHud();

            if (_hud.Visible) _hud.Hide();
            else { _hud.Show(); _dirty = true; }

            UpdateMenuState();
        }

        private void ToggleAutoStart()
        {
            var enabled = !AutoStart.IsEnabled;
            AutoStart.SetEnabled(enabled);
            UpdateMenuState();
        }

        /// <summary>
        /// 言語を切り替えたときにメニューの文言を付け直す。
        /// 各項目の Tag に文言のキーを持たせてある。
        /// </summary>
        private void RefreshMenuTexts()
        {
            foreach (ToolStripItem item in _menu.Items)
            {
                var key = item.Tag as string;
                if (key != null) item.Text = Strings.Get(key);
            }
            UpdateMenuState();
        }

        private ToolStripMenuItem MenuItem(string key, Action onClick)
        {
            return new ToolStripMenuItem(Strings.Get(key), null, (s, e) => onClick()) { Tag = key };
        }

        private void UpdateMenuState()
        {
            _hudItem.Text = Strings.Get((_hud != null && _hud.Visible) ? "menu.hideHud" : "menu.showHud");
            _hudItem.Checked = _hud != null && _hud.Visible;
            _autoStartItem.Checked = AutoStart.IsEnabled;
        }

        /// <summary>
        /// 設定ダイアログ。OK で AppConfig.Save() され、既存の設定ファイル監視が
        /// それを拾って反映する。反映経路を二重に持たない。
        /// </summary>
        private void OpenSettings()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                return;
            }

            _settings = new SettingsForm(_config, PreviewGauge);
            _settings.ResetHudPositionRequested += (s, e) =>
            {
                if (_hud == null || _hud.IsDisposed) _hud = CreateHud();
                _hud.ResetPosition();
            };
            _settings.FormClosed += (s, e) => { _settings = null; _configDirty = true; };
            _settings.Show();
        }

        private void OpenConfig()
        {
            try
            {
                if (!File.Exists(AppConfig.FilePath)) _config.Save();
                Process.Start(new ProcessStartInfo(AppConfig.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _notifier.ShowInfo("ctxtray", Strings.Format("config.openFailed", ex.Message));
            }
        }

        private void ShowUptime()
        {
            var up = DateTime.UtcNow - _startedUtc;
            // 時間は切り捨てる（TotalHours をそのまま "0" で書式化すると 1.6 時間が「2 時間 36 分」になる）。
            var text = Strings.Format("uptime.body", (int)up.TotalHours, up.Minutes, _config.PollSeconds, AppConfig.FilePath);
            if (_lastError != null)
                text += Strings.Format("uptime.lastError", _lastError,
                    _lastErrorUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));

            MessageBox.Show(text, "ctxtray", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExitApp()
        {
            _timer.Stop();
            foreach (var tray in _trays) tray.Visible = false;
            if (_hud != null && !_hud.IsDisposed) _hud.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.ThreadException -= OnThreadException;
                if (_dataWatcher != null) _dataWatcher.Dispose();
                if (_projectWatcher != null) _projectWatcher.Dispose();
                if (_configWatcher != null) _configWatcher.Dispose();
                _timer.Dispose();

                foreach (var tray in _trays) tray.Dispose();
                foreach (var icon in _icons) if (icon != null) icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
