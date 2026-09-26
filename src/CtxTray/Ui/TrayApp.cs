using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CtxTray.Collect;
using CtxTray.Config;
using CtxTray.Core;
using CtxTray.Native;
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
        ///   なお Windows 11 25H2 では記録は実行ファイルごとに 1 件だけで、1 つをタスクバーへ出すと
        ///   3 つとも出た（2026-09-17 実測）。他の版は確かめていないので、生成順の固定は続ける。
        /// </summary>
        private readonly NotifyIcon[] _trays;
        private readonly Icon[] _icons;

        private readonly Timer _timer = new Timer();
        private readonly ThresholdNotifier _notifier;
        private readonly ToolStripMenuItem _hudItem;
        private readonly ToolStripMenuItem _clickThroughItem;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ContextMenuStrip _menu;

        private AppConfig _config;
        private HudForm _hud;
        private Snapshot _lastSnapshot;
        private SettingsForm _settings;

        /// <summary>
        /// 上限が分からないモデルを公式ドキュメントで調べる係。通信するのは設定でオンにしたときだけ
        /// （fetchModelLimits）。取得済みの値の読み出しはオフでも使う（ローカルの記録ファイル）。
        /// </summary>
        private readonly ModelDocsFetcher _modelDocs =
            new ModelDocsFetcher(AppConfig.Dir, "ctxtray/" + AppVersion.Full);

        /// <summary>この実行中に、上限が分からなかったモデル（設定画面の一覧に出す）。</summary>
        private readonly HashSet<string> _unknownModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        // 全画面のアプリのために隠したか。自分で隠したときだけ出し直す
        // （利用者が隠した HUD を勝手に出さない）。
        private bool _hiddenForFullscreen;

        // 全画面と続けて何回判定したか（一瞬のブレで隠さないため）。
        private int _fullscreenTicks;

        // ホットキーが使えないことを知らせたキー。同じキーで繰り返し知らせない。
        private string _hotkeyWarnedFor;

        // 初めての起動のときにパネルへ出す案内を、何秒出しておくか。
        // 通知（5〜6 秒）より長く置く。読む前に消えるのを避けるため。
        private const int WelcomeSeconds = 120;

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
            _clickThroughItem = MenuItem("menu.clickThrough", ToggleClickThrough);
            _autoStartItem = MenuItem("menu.autoStart", ToggleAutoStart);

            _menu = new ContextMenuStrip();
            // 透過をオンにすると HUD がマウスを一切受け取らなくなる。押す前に分かるよう、
            // この項目にだけ説明を出す（ToolStripDropDownMenu は既定で説明を出さない）。
            _menu.ShowItemToolTips = true;
            // 開くたびにチェックを付け直す。ショートカットを利用者が消したり、exe を移したりしても
            // 古い表示のままにならないように。
            _menu.Opening += (s, e) => UpdateMenuState();
            _menu.Items.Add(_hudItem);
            _menu.Items.Add(_clickThroughItem);
            _menu.Items.Add(_autoStartItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(MenuItem("menu.settings", OpenSettings));
            _menu.Items.Add(MenuItem("menu.refresh", () => { _dirty = true; Tick(null, null); }));
            _menu.Items.Add(MenuItem("menu.openConfig", OpenConfig));
            _menu.Items.Add(MenuItem("menu.about", ShowAbout));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(MenuItem("menu.exit", ExitApp));

            // メニューとクリックの扱いは全スロットで共有する。
            foreach (var tray in _trays)
            {
                tray.ContextMenuStrip = _menu;
                tray.Text = "ctxtray";
                // 最初の描画までの仮のアイコン。Windows の汎用アイコンだと、
                // 起動直後の一瞬だけ別のアプリに見える。
                tray.Icon = AppIconRenderer.Load() ?? SystemIcons.Application;

                // ★ 1 回クリックで切り替える（Windows 11 のほかのアイコンと同じ操作、2026-09-18）。
                //   WinForms はダブルクリックの 2 回目ではクリックの処理を呼ばないので、
                //   慣れでダブルクリックしても切り替わるのは 1 回で済む。
                tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleHud(); };

                // 通知をクリックしたら HUD を出す（以前は何も起きなかった）。
                // 動作を持つ通知（上限が分からないモデル → 設定の「モデル」タブ）はそちらを優先する。
                tray.BalloonTipClicked += (s, e) =>
                {
                    var action = _notifier.TakeClickAction();
                    if (action != null) action();
                    else ShowHud();
                };
            }
            // ここではまだどのアイコンも出さない。値ごとに分けるモードでは Windows に登録する順が
            // 並びを決めるので（RenderMulti）、先に 1 個目だけ出すと並びが崩れる。

            _hud = CreateHud();
            if (_config.HudShowAtStartup) _hud.Show();

            SetupWatchers();

            _timer.Interval = 500;
            _timer.Tick += Tick;
            _timer.Start();

            if (!string.IsNullOrEmpty(problem))
                _notifier.ShowInfo("ctxtray", problem);

            // 初めての起動（設定ファイルが無かった）だけ、操作の案内を出す。
            // 常駐してアイコンが出るだけでは、ホットキーや右クリックに気づけない。
            if (_config.WasMissing)
            {
                var welcome = Strings.Format("app.welcome", _config.Hotkey);
                _notifier.ShowInfo("ctxtray", welcome);

                // 通知は集中モード中や通知を切っている環境では出ないので、パネルにも同じ案内を出す。
                // しばらくすると自分で消える（HudForm.Pump）。
                if (_hud != null && !_hud.IsDisposed) _hud.ShowWelcome(welcome, WelcomeSeconds);

                // 設定ファイルを作って、次の起動では出さないようにする。
                _config.Save();
            }

            // ホットキーを登録できたか（他のアプリが同じキーを取っていると失敗する）。
            CheckHotkey();

            UpdateMenuState();
            Tick(null, null);

            // 最初の描画に失敗しても、アイコンが 1 つも無い（メニューも開けない）状態にはしない。
            if (VisibleTray() == null) _trays[0].Visible = true;
        }

        private HudForm CreateHud()
        {
            var hud = new HudForm(_config);
            hud.HotkeyPressed += (s, e) => ToggleHud();
            // OS のテーマが変わったら、トレイアイコンも描き直す。
            hud.ThemeChanged += (s, e) => { if (_lastSnapshot != null) SetTrayIcon(_lastSnapshot); };
            hud.RestorePosition();

            // トレイと同じメニューを HUD の右クリックでも出す。
            // アイコンが「隠れているインジケーター」に入っていると、設定や終了に届きにくいため（2026-09-18）。
            hud.ContextMenuStrip = _menu;

            // ホットキーは HUD のウィンドウに登録される。起動時に HUD を出さない設定でも
            // ホットキーで出せるよう、表示せずにハンドルだけ作っておく。
            var handle = hud.Handle;
            GC.KeepAlive(handle);

            return hud;
        }

        /// <summary>
        /// ホットキーが登録できたかを確かめ、駄目なら理由を知らせる。
        /// 押しても何も起きない状態を黙って放置しないため（2026-09-18）。
        /// 同じキーで何度も知らせない（設定を読み直すたびに通知が出ないように）。
        /// </summary>
        private void CheckHotkey()
        {
            if (_hud == null || _hud.IsDisposed) return;

            if (_hud.HotkeyStatus == HotkeyState.Ok)
            {
                _hotkeyWarnedFor = null;
                return;
            }

            var key = _config.Hotkey ?? string.Empty;
            if (string.Equals(_hotkeyWarnedFor, key, StringComparison.Ordinal)) return;
            _hotkeyWarnedFor = key;

            _notifier.ShowInfo("ctxtray", Strings.Format(
                _hud.HotkeyStatus == HotkeyState.Taken ? "hotkey.taken" : "hotkey.unparsable", key));
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

            // 行の詳細の待ち時間と、初回案内を消す時刻もここで計る
            // （HUD の窓では WinForms のタイマーが発火しない）。
            if (_hud != null && !_hud.IsDisposed && _hud.Visible) _hud.Pump();

            // 全画面のアプリの出入りは、収集の間隔とは関係なく追いかける。
            ApplyFullscreenRule();

            // 公式ドキュメントの取得が済んだら、待たずに分母を反映する。
            if (_modelDocs.TakeChanged()) _dirty = true;

            var due = (DateTime.UtcNow - _lastRefreshUtc).TotalSeconds >= _config.PollSeconds;
            if (!_dirty && !due) return;

            _dirty = false;
            _lastRefreshUtc = DateTime.UtcNow;

            // 収集は例外を投げない（失敗は snap.Diag に残る）。
            var fetch = _config.FetchModelLimits;
            var snap = SnapshotBuilder.Build(_config.ExternalSessionsEnabled, false, new LimitSources
            {
                Config = _config.ModelLimits,
                Docs = _modelDocs.Limits(),
                DocsNames = _modelDocs.Names(),
                DocsState = m => _modelDocs.StateFor(m, fetch),
            });

            // 表示の対象を絞る。ここで一度だけ絞り、HUD・トレイ・ツールチップ・通知が
            // すべて同じ結果を見る（HUD で隠した行にトレイや通知が反応しないように）。
            SessionFilter.Apply(snap, _config, DateTime.UtcNow);

            HandleUnknownModels(snap);

            _lastSnapshot = snap;

            SetTrayIcon(snap);
            // どのアイコンをホバーしても全体が分かるよう、同じ 3 行を全部に付ける。
            SetTooltip(BuildTooltip(snap));

            if (_hud != null && !_hud.IsDisposed)
                _hud.SetSnapshot(snap);

            _notifier.Check(snap, _config);
        }

        // --- 上限が分からないモデル ---------------------------------------------

        /// <summary>
        /// 上限（分母）が分からないモデルの後始末。
        /// 取得がオンなら公式ドキュメントへ確認に出し、分からないままなら 1 回だけ知らせる。
        ///
        /// 知らせるのは動いているセッションのモデルだけ（止まっている古いタブで起動のたびに鳴らさない。
        /// 閾値の通知と同じ考え方）。取得がオンのときは、確認して見つからなかった後に知らせる
        /// （数秒で分かるかもしれないのに先に「分からない」と言わない）。
        /// </summary>
        private void HandleUnknownModels(Snapshot snap)
        {
            var unknown = new List<string>();
            var nameless = new List<string>();
            foreach (var list in new[] { snap.Sessions, snap.HiddenSessions })
                foreach (var s in list)
                {
                    if (!s.ModelKnown && !string.IsNullOrEmpty(s.Model) && !unknown.Contains(s.Model))
                        unknown.Add(s.Model);
                    // 0.2.0 で上限だけ取得したモデルは名前の記録が無い。取得がオンなら 1 回だけ読み直す。
                    if (s.LimitSource == LimitSource.Docs && s.ModelName == null && !nameless.Contains(s.Model))
                        nameless.Add(s.Model);
                }

            if (_config.FetchModelLimits) _modelDocs.RequestNames(nameless);

            foreach (var m in unknown) _unknownModels.Add(m);
            if (unknown.Count == 0) return;

            if (_config.FetchModelLimits) _modelDocs.Request(unknown, false);

            if (!_config.NotifyContext) return;
            foreach (var s in snap.Sessions)
            {
                if (s.ModelKnown || string.IsNullOrEmpty(s.Model) || !SessionFilter.IsRunning(s)) continue;
                if (s.DocsState == DocsLookupState.Pending) continue;
                if (!_modelDocs.MarkNotified(s.Model)) continue;

                _notifier.ShowInfo(Strings.Get("notify.unknownModel"),
                                   Strings.Format("notify.unknownModelBody", s.Model),
                                   () => OpenSettings(true));
            }
        }

        /// <summary>設定画面の「モデル」タブの一覧。組み込み・取得済み・設定・分からなかったもの。</summary>
        private IList<ModelEntry> ModelEntries()
        {
            var config = _config.ModelLimits;
            var docs = _modelDocs.Limits();
            var list = new List<ModelEntry>();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in config)
            {
                list.Add(new ModelEntry { Id = kv.Key, Limit = kv.Value, Source = LimitSource.Config });
                listed.Add(kv.Key);
            }
            foreach (var kv in ModelLimits.BuiltIn)
            {
                if (!listed.Add(kv.Key)) continue;
                list.Add(new ModelEntry { Id = kv.Key, Limit = kv.Value, Source = LimitSource.BuiltIn });
            }
            foreach (var e in _modelDocs.Entries())
            {
                if (!listed.Add(e.Id)) continue;
                list.Add(e);
            }

            var unknown = new HashSet<string>(_unknownModels, StringComparer.OrdinalIgnoreCase);
            foreach (var m in _modelDocs.UnknownSoFar()) unknown.Add(m);
            foreach (var m in unknown)
            {
                if (listed.Contains(m) || ModelLimits.Lookup(m, config, docs).HasValue) continue;
                listed.Add(m);
                list.Add(new ModelEntry { Id = m, Source = LimitSource.Unknown });
            }
            return list;
        }

        /// <summary>「今すぐ確認」。分からないモデルを、24 時間を待たずに確かめる。</summary>
        private void CheckModelsNow()
        {
            var models = new List<string>(_unknownModels);
            models.AddRange(_modelDocs.UnknownSoFar());
            _modelDocs.Request(models, true);
            _dirty = true;
        }

        private void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            ReportError(e.Exception);
        }

        // --- 全画面のアプリ -----------------------------------------------------

        /// <summary>
        /// 全画面のアプリ（動画・発表・ゲーム）を使っている間は HUD を隠す。
        ///
        /// 自分で隠したときだけ印を立て、全画面が終わったら出し直す。
        /// 利用者が自分で隠した HUD を勝手に出さないため。
        /// </summary>
        private void ApplyFullscreenRule()
        {
            if (!_config.HideWhenFullscreen)
            {
                // 設定を切ったときは、隠していたものを出し直す。
                if (_hiddenForFullscreen)
                {
                    _hiddenForFullscreen = false;
                    ShowHudIfHidden();
                }
                return;
            }

            // ★ 1 回の判定では隠さない。
            //   起動直後などに一瞬だけ「全画面」と返ることがあり、それで HUD が消えた（実機で確認、2026-09-18）。
            //   続けて 2 回（約 1 秒）そうなら隠す。戻すのは 1 回で。
            var fullscreen = IsFullscreen();
            _fullscreenTicks = fullscreen ? _fullscreenTicks + 1 : 0;
            fullscreen = _fullscreenTicks >= 2;

            if (fullscreen && !_hiddenForFullscreen)
            {
                if (_hud == null || _hud.IsDisposed || !_hud.Visible) return;
                _hiddenForFullscreen = true;
                _hud.Hide();
                UpdateMenuState();
                return;
            }

            if (!fullscreen && _hiddenForFullscreen)
            {
                _hiddenForFullscreen = false;
                ShowHudIfHidden();
            }
        }

        /// <summary>
        /// いま全画面のアプリが使われているか。
        ///
        /// Windows の「通知を出してよい状態か」を借りて判定する。最大化しただけの窓は含まれない。
        /// ただし前面が Claude Desktop なら全画面とみなさない。
        /// Desktop を全画面で使っているときこそ、コンテキストの残量が見えている必要があるため。
        /// </summary>
        private static bool IsFullscreen()
        {
            int state;
            try
            {
                if (NativeMethods.SHQueryUserNotificationState(out state) != 0) return false;
            }
            catch
            {
                return false;
            }

            if (state != NativeMethods.QUNS_BUSY
                && state != NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
                && state != NativeMethods.QUNS_PRESENTATION_MODE)
                return false;

            return !ForegroundIsClaudeDesktop();
        }

        private static bool ForegroundIsClaudeDesktop()
        {
            try
            {
                var window = NativeMethods.GetForegroundWindow();
                if (window == IntPtr.Zero) return false;

                int pid;
                NativeMethods.GetWindowThreadProcessId(window, out pid);
                if (pid == 0) return false;

                return SnapshotBuilder.IsDesktopProcess(pid);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 想定外の例外を記録する。ツールチップに理由を出し、「ctxtray について」の画面で最後の 1 件を見られるようにする。
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

            var worst = SessionFilter.MostPressed(snap, _config);
            if (worst == null || !worst.ContextPct.HasValue)
            {
                // 行はあるが全部止まっている（HUD では淡い行だけ）ときは、そう書く。
                // 「セッションなし」だと HUD の表示と食い違って見える。
                // 止まっている行を隠す設定では行が 0 件になるが、それも「動いているものが無い」状態。
                var none = snap.Sessions.Count > 0 || snap.HiddenSessionCount > 0
                    ? "tip.noRunning" : "tip.noSessions";
                return Join(new List<string> { Strings.Get(none) }, tail);
            }

            // HUD とアイコンの数字と同じ丸めにする（Math.Round の偶数丸めだと x.5 で 1 ずれる）。
            var pct = PercentText.Format(worst.ContextPct.Value);
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
            // 動いているセッションが無ければ null になり、コンテキストのバーは空で描かれる。
            var worst = SessionFilter.MostPressed(snap, _config);

            if (_config.TrayMultiMode) RenderMulti(snap, worst, theme);
            else RenderSingle(snap, worst, theme);
        }

        private void RenderSingle(Snapshot snap, SessionRow worst, Theme theme)
        {
            var gauges = new List<TrayGauge>();
            foreach (var value in _config.OrderedTrayValues())
                gauges.Add(Gauge(value, snap, worst));

            Apply(0, TrayIconRenderer.Render(gauges, TrayIconRenderer.BarsStyle, theme));

            for (var i = 1; i < _trays.Length; i++) Hide(i);
        }

        /// <summary>
        /// 値ごとに分けるモード。
        ///
        /// ★ Windows に登録する（Visible を立てる）順はスロットの逆（週間枠 → 5時間枠 → コンテキスト）。
        ///   Windows 11 は登録されたアイコンを順に左端へ入れるので、スロット順に登録すると
        ///   左から 週間枠・5時間枠・コンテキスト と逆に並んだ。この並びは起動のたびに登録順で決まり、
        ///   タスクバーでも隠れているインジケーターでも同じだった
        ///   （2026-09-17、Windows 11 25H2 で実測。ドラッグで並べ替えても再起動で戻った）。
        ///   uID（NotifyIcon の生成順）は変えない。
        /// </summary>
        private void RenderMulti(Snapshot snap, SessionRow worst, Theme theme)
        {
            for (var slot = _trays.Length - 1; slot >= 0; slot--)
            {
                var value = AppConfig.AllTrayValues[slot];
                if (!ContainsValue(_config.TrayValues, value)) { Hide(slot); continue; }

                Apply(slot, TrayIconRenderer.Render(new List<TrayGauge> { Gauge(value, snap, worst) },
                                                    _config.TrayLabel, theme));
            }
        }

        /// <summary>描画用のゲージ。値の名前（目印と識別色を選ぶため）を添える。</summary>
        private TrayGauge Gauge(string value, Snapshot snap, SessionRow worst)
        {
            var gauge = BuildGauge(value, snap, worst);
            gauge.Value = value;
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
            return BuildGauge(value, _lastSnapshot, SessionFilter.MostPressed(_lastSnapshot, _config));
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
            // 位置の基準（上端／下端）も位置の一部なので一緒に持ち越す。
            loaded.HudX = _config.HudX;
            loaded.HudY = _config.HudY;
            loaded.HudAnchorBottom = _config.HudAnchorBottom;
            _config = loaded;
            Strings.Apply(_config.Language);
            RefreshMenuTexts();

            // HUD にも新しい設定オブジェクトを渡す。渡さないと HUD だけ古い設定を見続ける。
            if (_hud != null && !_hud.IsDisposed) _hud.ApplyConfig(_config);
            // 設定でキーを変えたときは、新しいキーで登録できたかを確かめ直す。
            CheckHotkey();
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

            // 出し入れができたなら、初回の案内はもう読まなくてよい。
            _hud.DismissWelcome();

            // 利用者の操作を優先する。全画面のために隠した印は消す
            // （利用者が出したものを次のティックで引っ込めない／隠したものを出し直さない）。
            _hiddenForFullscreen = false;

            UpdateMenuState();
        }

        /// <summary>HUD を出す（切り替えではない）。通知をクリックしたときに使う。</summary>
        private void ShowHud()
        {
            if (_hud == null || _hud.IsDisposed) _hud = CreateHud();
            _hiddenForFullscreen = false;
            ShowHudIfHidden();
        }

        private void ShowHudIfHidden()
        {
            if (_hud == null || _hud.IsDisposed) return;
            if (!_hud.Visible) { _hud.Show(); _dirty = true; }
            UpdateMenuState();
        }

        /// <summary>
        /// クリック透過の切り替え。透過中は HUD をドラッグできず右クリックも届かないので、
        /// 設定画面を開かずにトレイのメニューから戻せるようにする。
        ///
        /// 実行中の設定は書き換えず、複製を保存して再読込で反映する（設定画面の OK と同じ経路。
        /// AppConfig.Clone の説明を参照）。保存できなければ何も変えずに知らせる。
        /// </summary>
        private void ToggleClickThrough()
        {
            var next = _config.Clone();
            next.ClickThrough = !next.ClickThrough;

            if (!next.Save())
            {
                _notifier.ShowInfo("ctxtray", Strings.Format("set.saveFailed", AppConfig.FilePath));
                return;
            }

            // 設定ファイルの監視を待たずに、次の更新で読み直す。
            _configDirty = true;

            // 設定画面が開いていれば、そのチェックも合わせる（OK で元に戻さないように）。
            if (_settings != null && !_settings.IsDisposed) _settings.SyncClickThrough(next.ClickThrough);
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
            _clickThroughItem.Checked = _config.ClickThrough;
            // 言語を変えても付け直されるよう、メニューを開くたびに入れる。
            _clickThroughItem.ToolTipText = Strings.Get("menu.clickThroughTip");
            _autoStartItem.Checked = AutoStart.IsEnabled;
        }

        /// <summary>
        /// 設定ダイアログ。OK で AppConfig.Save() され、既存の設定ファイル監視が
        /// それを拾って反映する。反映経路を二重に持たない。
        /// </summary>
        private void OpenSettings()
        {
            OpenSettings(false);
        }

        /// <param name="modelsTab">「モデル」タブを前に出す（上限が分からないモデルの通知から）。</param>
        private void OpenSettings(bool modelsTab)
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                if (modelsTab) _settings.ShowModelsTab();
                _settings.Activate();
                return;
            }

            // キーの空き確認は HUD のウィンドウで行う（ホットキーの登録先がそこなので、
            // 「いま自分が使っているキー」を正しく扱える）。
            if (_hud == null || _hud.IsDisposed) _hud = CreateHud();
            // 設定までたどり着いたなら、初回の案内はもう読まなくてよい。
            _hud.DismissWelcome();
            // 設定画面には「いまの設定を返す関数」を渡す。画面は OK の時点の最新の設定を複製して保存する。
            _settings = new SettingsForm(() => _config, PreviewGauge, _hud.IsHotkeyAvailable,
                                         ModelEntries, CheckModelsNow);
            if (modelsTab) _settings.ShowModelsTab();
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

        /// <summary>
        /// 版・稼働時間・更新間隔・設定ファイルの場所・最後に起きた問題。
        ///
        /// 以前の題は「稼働状況」だったが、版や設定ファイルの場所を探す人がここを開く発想にならない。
        /// 中身は同じで、題だけ「ctxtray について」にした（2026-09-20）。
        /// </summary>
        private void ShowAbout()
        {
            var up = DateTime.UtcNow - _startedUtc;
            // 時間は切り捨てる（TotalHours をそのまま "0" で書式化すると 1.6 時間が「2 時間 36 分」になる）。
            var text = Strings.Format("about.body", (int)up.TotalHours, up.Minutes, _config.PollSeconds,
                                      AppConfig.FilePath, AppVersion.Display);
            if (_lastError != null)
                text += Strings.Format("about.lastError", _lastError,
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
