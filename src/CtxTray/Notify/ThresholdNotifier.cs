using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CtxTray.Collect;
using CtxTray.Config;
using CtxTray.Core;

namespace CtxTray.Notify
{
    /// <summary>
    /// 閾値を超えたときに Windows の通知を出す。
    ///
    /// 閾値は AppConfig の 1 組だけを使う（HUD の色と同じ定義）。
    /// 通知用に別の数字を持たない。
    ///
    /// NotifyIcon.ShowBalloonTip を使う。追加の依存が要らず、
    /// Windows 10/11 ではトーストとして表示される。
    /// 制限: フォーカスアシスト中は出ない。アクションセンターには残らない。
    /// 本来のトースト（AppUserModelID 登録が必要）は将来の課題。
    /// </summary>
    internal sealed class ThresholdNotifier
    {
        private sealed class State
        {
            /// <summary>いまのレベル。上がったときだけ通知する。</summary>
            public Level Level;

            /// <summary>最後に通知したレベルと時刻。最短間隔の判定に使う。</summary>
            public Level NotifiedLevel;
            public DateTime LastNotifiedUtc;

            /// <summary>最後に値を見た時刻。消えたセッションの状態を捨てるのに使う。</summary>
            public DateTime LastSeenUtc;
        }

        private sealed class Pending
        {
            public string Title;
            public string Text;
            public ToolTipIcon Icon;
            /// <summary>クリックされたときにすること。無ければ呼び出し側の既定（HUD を出す）。</summary>
            public Action OnClick;
        }

        /// <summary>いま出ている（最後に出した）通知のクリック時の動作。</summary>
        private Action _shownClick;

        /// <summary>
        /// バルーンは 1 つずつしか出せない。連続で ShowBalloonTip を呼ぶと
        /// 前のものが置き換わり、最後の 1 件しか見えない。
        /// 複数の枠が同時に閾値を超えることは普通にあるので、間隔を空けて順に出す。
        /// </summary>
        private const int SpacingSeconds = 7;
        private const int MaxQueued = 5;

        /// <summary>この時間見かけなかったセッションの状態は捨てる（溜まり続けないように）。</summary>
        private static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(24);

        private readonly Func<NotifyIcon> _trayFor;
        private readonly Action<string, string, ToolTipIcon> _sink;
        private readonly Dictionary<string, State> _states =
            new Dictionary<string, State>(StringComparer.Ordinal);
        private readonly Queue<Pending> _queue = new Queue<Pending>();
        private DateTime _lastShownUtc = DateTime.MinValue;

        /// <summary>現在時刻。試験で時間を進めるために差し替えられるようにしてある。</summary>
        internal Func<DateTime> Clock = () => DateTime.UtcNow;

        /// <param name="trayFor">
        /// 通知を出すアイコンを返す。固定のアイコンを握ると、そのアイコンが
        /// 非表示のとき（値ごとに分けるモードで値を外したとき）に通知が出せない。
        /// </param>
        public ThresholdNotifier(Func<NotifyIcon> trayFor)
        {
            _trayFor = trayFor;
        }

        /// <summary>試験用。通知を画面に出す代わりに sink へ渡す（tests/ の試験で使う）。</summary>
        internal ThresholdNotifier(Action<string, string, ToolTipIcon> sink)
        {
            _sink = sink;
        }

        public void ShowInfo(string title, string text)
        {
            Show(title, text, ToolTipIcon.Info);
        }

        /// <summary>クリックで onClick を呼ぶ通知（設定画面を開く、など）。</summary>
        public void ShowInfo(string title, string text, Action onClick)
        {
            Show(title, text, ToolTipIcon.Info, onClick);
        }

        /// <summary>
        /// クリックされた通知の動作を受け取る（一度だけ）。null なら既定の動作にする。
        /// バルーンはアイコンに 1 つしか出ないので、最後に出したものがクリックされたものになる。
        /// </summary>
        public Action TakeClickAction()
        {
            var action = _shownClick;
            _shownClick = null;
            return action;
        }

        /// <summary>
        /// 待たせている通知を 1 件だけ出す。呼び出し側から定期的に叩く。
        /// </summary>
        public void Pump()
        {
            if (_queue.Count == 0) return;
            var now = Clock();
            if ((now - _lastShownUtc).TotalSeconds < SpacingSeconds) return;

            if (_sink != null)
            {
                var queued = _queue.Dequeue();
                _lastShownUtc = now;
                _shownClick = queued.OnClick;
                _sink(queued.Title, queued.Text, queued.Icon);
                return;
            }

            // 出せるアイコンが無い間は待たせておく（捨てない）。
            var tray = _trayFor == null ? null : _trayFor();
            if (tray == null || !tray.Visible) return;

            var next = _queue.Dequeue();
            _lastShownUtc = now;
            _shownClick = next.OnClick;
            ShowNow(tray, next.Title, next.Text, next.Icon);
        }

        public void Check(Snapshot snap, AppConfig config)
        {
            if (snap == null) return;

            var now = Clock();
            var slack = Math.Max(0, config.NotifyHysteresisPts);

            if (config.NotifyContext)
            {
                foreach (var s in snap.Sessions)
                {
                    // プロセスが止まっているセッションは知らせない。止まっている間は値が増えず、
                    // 以前は ctxtray を起動するたびに、そうした行の通知が出ていた。
                    // 高いまま再開すれば、動き出した時点でここを通って知らせる。
                    if (!SessionFilter.IsRunning(s)) continue;

                    if (!Levels.ContextRatio(s).HasValue) continue;

                    var level = Levels.ForContext(s, config);
                    var title = string.IsNullOrEmpty(s.Title) ? Strings.Get("hud.untitled") : s.Title;

                    // ★ 残りは % ではなくトークン数で書く。
                    //   画面に出る % は「ウィンドウに対する消費率」、到達率は「圧縮点に対する割合」で
                    //   分母が違うので、並べると足して 100 にならず、丸め誤差か不具合に見えた（2026-09-20）。
                    //   計算の根拠は HUD の行の詳細（hud.tipToCompact）と同じ。
                    var toCompact = Math.Max(0, (int)Math.Round(
                        (s.ContextLimit ?? 0) * config.CompactThreshold - (s.ContextTokens ?? 0)));

                    var body = Strings.Format("notify.contextBody",
                        title,
                        s.ContextPct.HasValue ? s.ContextPct.Value : 0,
                        toCompact.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));

                    Evaluate("ctx:" + s.CliSessionId, level, Levels.ForContext(s, config, slack), config, now,
                             Strings.Get(level == Level.Danger ? "notify.compactSoon" : "notify.contextRising"),
                             body);
                }
            }

            var r = snap.RateLimits;
            if (r != null)
            {
                // 以前は「表示値は下限」の注記を本文に付けていたが、HUD の "+" と同じく
                // 意味が伝わらなかったので付けない（2026-09-15）。

                if (config.NotifyFiveHour)
                {
                    Evaluate("fh", Levels.ForFiveHour(r, config), Levels.ForFiveHour(r, config, slack), config, now,
                             Strings.Get("notify.fiveHour"),
                             Strings.Format("notify.fiveHourBody", r.FiveHourPct));
                }

                if (config.NotifyWeekly)
                {
                    Evaluate("wk", Levels.ForWeekly(r, config), Levels.ForWeekly(r, config, slack), config, now,
                             Strings.Get("notify.weekly"),
                             Strings.Format("notify.weeklyBody", r.WeeklyPct));
                }
            }

            Forget(now);
        }

        /// <summary>
        /// レベルが上がった瞬間だけ通知する。
        ///
        /// 下がったとみなすのは、閾値を hysteresisPts だけ下げて判定しても下のレベルになったとき
        /// （relaxed）。閾値ちょうどを行き来しても連投しないため。
        ///
        /// 最短間隔（minRepeatMinutes）は「前回と同じか低いレベル」の再通知にだけ効かせる。
        /// 注意を知らせた直後に危険へ上がったときは、待たずに知らせる。
        /// 以前は間隔待ちの間にレベルだけ上げていたので、注意から 30 分以内に危険になると
        /// 危険の通知が出なかった。
        /// </summary>
        private void Evaluate(string key, Level level, Level relaxed, AppConfig config, DateTime now,
                              string title, string body)
        {
            State state;
            if (!_states.TryGetValue(key, out state))
            {
                state = new State
                {
                    Level = Level.Normal,
                    NotifiedLevel = Level.Normal,
                    LastNotifiedUtc = DateTime.MinValue,
                };
                _states[key] = state;
            }
            state.LastSeenUtc = now;

            if (level <= state.Level)
            {
                if (relaxed < state.Level) state.Level = relaxed;
                return;
            }

            state.Level = level;

            var quiet = state.LastNotifiedUtc != DateTime.MinValue
                     && (now - state.LastNotifiedUtc).TotalMinutes < config.NotifyMinRepeatMinutes;
            if (quiet && level <= state.NotifiedLevel) return;

            state.NotifiedLevel = level;
            state.LastNotifiedUtc = now;

            Show(title, body, level == Level.Danger ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        private void Forget(DateTime now)
        {
            List<string> stale = null;
            foreach (var kv in _states)
            {
                if (now - kv.Value.LastSeenUtc <= ForgetAfter) continue;
                if (stale == null) stale = new List<string>();
                stale.Add(kv.Key);
            }
            if (stale == null) return;
            foreach (var key in stale) _states.Remove(key);
        }

        /// <summary>
        /// 表示を予約する。実際に出すのは Pump。
        /// 溜まりすぎたら捨てる（起動直後に大量に条件が揃っても連投しないため）。
        /// </summary>
        private void Show(string title, string text, ToolTipIcon icon, Action onClick = null)
        {
            if (_queue.Count >= MaxQueued) return;
            _queue.Enqueue(new Pending { Title = title, Text = text, Icon = icon, OnClick = onClick });
            Pump();
        }

        private static void ShowNow(NotifyIcon tray, string title, string text, ToolTipIcon icon)
        {
            try
            {
                tray.BalloonTipTitle = title;
                tray.BalloonTipText = text;
                tray.BalloonTipIcon = icon;
                tray.ShowBalloonTip(10000);
            }
            catch { }
        }
    }
}
