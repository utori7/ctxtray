using System;
using System.Collections.Generic;
using System.Globalization;

namespace CtxTray.Config
{
    /// <summary>
    /// 表示文言。日本語と英語を持つ。
    ///
    /// .resx を使わずここに集約する。項目数が少なく、
    /// 公開リポジトリで「どこを直せば訳せるか」が 1 ファイルで分かる方が親切なため。
    ///
    /// 既定は OS の表示言語に従う（設定 language で ja / en に固定できる）。
    /// </summary>
    internal static class Strings
    {
        private static bool _japanese = DetectJapanese();

        private static bool DetectJapanese()
        {
            try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja"; }
            catch { return false; }
        }

        public static bool IsJapanese { get { return _japanese; } }

        /// <summary>設定の language を反映する。auto なら OS の表示言語。</summary>
        public static void Apply(string language)
        {
            if (string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase)) _japanese = true;
            else if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) _japanese = false;
            else _japanese = DetectJapanese();
        }

        public static string Get(string key)
        {
            string[] pair;
            if (!Map.TryGetValue(key, out pair)) return key;
            return _japanese ? pair[0] : pair[1];
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }

        // [0] = 日本語 / [1] = English
        private static readonly Dictionary<string, string[]> Map =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // トレイメニュー
            { "menu.showHud",     new[] { "HUD を表示",        "Show HUD" } },
            { "menu.hideHud",     new[] { "HUD を隠す",        "Hide HUD" } },
            { "menu.autoStart",   new[] { "ログオン時に起動",   "Start at sign-in" } },
            { "menu.settings",    new[] { "設定…",              "Settings…" } },
            { "menu.refresh",     new[] { "今すぐ更新",         "Refresh now" } },
            { "menu.openConfig",  new[] { "設定ファイルを開く", "Open config file" } },
            { "menu.uptime",      new[] { "稼働状況…",          "Uptime…" } },
            { "menu.exit",        new[] { "終了",               "Exit" } },

            // ツールチップ（Windows の制限で 63 文字まで。ラベルを付けて何の値か分かるようにする）
            { "tip.context",      new[] { "コンテキスト {0}% {1}", "Context {0}% {1}" } },
            { "tip.fiveHour",     new[] { "5時間 {0}",          "5h {0}" } },
            { "tip.weekly",       new[] { "週間 {0}",           "Week {0}" } },
            { "tip.noSessions",   new[] { "セッションなし",      "No sessions" } },
            { "tip.noRunning",    new[] { "動いているセッションなし", "No running sessions" } },
            { "tip.rateUnknown",  new[] { "レート不明",         "Rate unknown" } },
            { "tip.error",        new[] { "エラー: {0}",        "error: {0}" } },

            // HUD
            { "hud.capLimits",    new[] { "レート枠",       "LIMITS" } },
            { "hud.capContext",   new[] { "コンテキスト",   "CONTEXT" } },
            { "hud.fiveHour",     new[] { "5時間",          "5h" } },
            { "hud.weekly",       new[] { "週間",           "Week" } },
            { "hud.hidden",       new[] { "ほか {0} 件は非表示", "{0} more hidden" } },

            { "hud.loading",      new[] { "読み取り中…",              "Reading…" } },
            { "hud.rateUnavail",  new[] { "レートリミット: 取得できず", "Rate limits: unavailable" } },
            { "hud.noSessions",   new[] { "セッションなし",            "No sessions" } },
            { "hud.unknownLimit", new[] { "分母不明",                  "window?" } },
            { "hud.untitled",     new[] { "(無題)",                    "(untitled)" } },
            // --verify-weekly でだけ使う（HUD には出さない）。
            { "hud.estimating",   new[] { "{0}ごろ（推定中）",         "around {0} (estimating)" } },

            // 通知
            { "notify.compactSoon",   new[] { "まもなく圧縮されます",             "Compaction is close" } },
            { "notify.contextRising", new[] { "コンテキストが増えています",       "Context is growing" } },
            { "notify.contextBody",   new[] { "{0}\nコンテキスト {1:0.0}%（圧縮まで残り {2:0}%）",
                                              "{0}\nContext {1:0.0}% ({2:0}% left before compaction)" } },
            { "notify.fiveHour",      new[] { "5時間枠が残り少なくなっています",  "5-hour limit is running low" } },
            { "notify.weekly",        new[] { "週間枠が残り少なくなっています",    "Weekly limit is running low" } },
            { "notify.fiveHourBody",  new[] { "5時間枠 {0}%",                     "5-hour {0}%" } },
            { "notify.weeklyBody",    new[] { "週間枠 {0}%",                      "Weekly {0}%" } },

            // 設定・診断
            { "config.unreadable", new[] { "設定を読めませんでした: {0}",
                                           "Could not read the config: {0}" } },
            { "config.broken",     new[] { "設定ファイルが壊れているため既定値で起動しました。元の内容は .bak に退避しました。",
                                           "The config file was invalid, so defaults were used. The original was kept as .bak." } },
            { "config.brokenKept", new[] { "設定ファイルを読めなかったため、直前の設定のまま動いています。直すと自動で反映されます。",
                                           "The config file could not be read, so the previous settings stay in effect. Fix the file and it will be picked up." } },
            { "config.openFailed", new[] { "設定ファイルを開けませんでした: {0}",
                                           "Could not open the config file: {0}" } },

            { "uptime.body",   new[] { "起動してから: {0:0} 時間 {1:00} 分\n更新間隔: {2} 秒\n設定: {3}",
                                       "Running for: {0:0} h {1:00} m\nRefresh interval: {2} s\nConfig: {3}" } },
            { "uptime.lastError", new[] { "\n最後に起きた問題: {0}（{1}）",
                                          "\nLast problem: {0} ({1})" } },
            { "app.crashed",   new[] { "ctxtray が停止しました。\n\n{0}",
                                       "ctxtray stopped unexpectedly.\n\n{0}" } },
            { "app.error",     new[] { "表示の更新中に問題が起きました。動作は続けます。\n{0}",
                                       "Something went wrong while updating. ctxtray keeps running.\n{0}" } },
            { "autostart.description", new[] { "Claude のコンテキスト残量とレート枠を常時表示する",
                                               "Always-visible Claude context and rate-limit readout" } },

            // 設定ダイアログ
            { "set.title",         new[] { "ctxtray の設定",      "ctxtray settings" } },
            { "set.tabHud",        new[] { "HUD",                  "HUD" } },
            { "set.tabTray",       new[] { "トレイアイコン",        "Tray icon" } },
            { "set.tabThresholds", new[] { "しきい値と通知",        "Thresholds and alerts" } },
            { "set.tabGeneral",    new[] { "全般",                 "General" } },

            // 設定ダイアログ: HUD
            { "set.secHudContent", new[] { "表示する内容",          "What to show" } },
            { "set.hudShowRate",   new[] { "レート枠（5時間枠・週間枠）", "Rate limits (5-hour and weekly)" } },
            { "set.hudShowSessions", new[] { "セッションごとのコンテキスト", "Context for each session" } },

            { "set.secHudSessions",new[] { "セッション",            "Sessions" } },
            // 語順が日英で違うので、数値の前後を別の文言にする。
            { "set.hideIdlePre",   new[] { "",                     "Hide sessions not used for" } },
            { "set.hideIdlePost",  new[] { "時間以上使っていないセッションは隠す", "hours or more" } },
            { "set.external",      new[] { "ターミナルや VS Code のセッションも出す",
                                           "Also show terminal and VS Code sessions" } },
            { "set.externalMaxPre",  new[] { "最大",               "Up to" } },
            { "set.externalMaxPost", new[] { "件",                 "sessions" } },

            { "set.secHudColumns", new[] { "列",                   "Columns" } },
            { "set.showBar",       new[] { "バー",                 "Bar" } },
            { "set.showTokens",    new[] { "トークン数（例: 284k / 1M）", "Token count (e.g. 284k / 1M)" } },
            { "set.showResets",    new[] { "5時間枠のリセット時刻", "5-hour reset time" } },
            { "set.always",        new[] { "常に表示",             "Always" } },
            { "set.autoNear",      new[] { "リセットの {0} 分前から", "From {0} min before the reset" } },
            { "set.never",         new[] { "表示しない",           "Never" } },

            { "set.secHudLook",    new[] { "大きさと見た目",        "Size and look" } },
            { "set.textSize",      new[] { "文字の大きさ",          "Text size" } },
            { "set.sizeSmall",     new[] { "小",                   "Small" } },
            { "set.sizeNormal",    new[] { "標準",                 "Normal" } },
            { "set.sizeLarge",     new[] { "大",                   "Large" } },
            { "set.sizeXLarge",    new[] { "特大",                 "Extra large" } },
            { "set.hudWidth",      new[] { "幅",                   "Width" } },
            { "set.hudWidthUnit",  new[] { "（標準は {0}）",        "(default {0})" } },
            { "set.opacity",       new[] { "不透明度",             "Opacity" } },
            { "set.opacityHint",   new[] { "100% にすると後ろが透けなくなります。",
                                           "At 100%, nothing shows through." } },
            { "set.clickThrough",  new[] { "クリックを後ろのウィンドウに通す",
                                           "Let clicks pass through to the window behind" } },
            { "set.clickThroughHint", new[] { "オンの間は HUD をドラッグで動かせません。",
                                              "While this is on, the HUD can't be dragged." } },

            { "set.secHudPlace",   new[] { "表示と位置",           "Showing and position" } },
            { "set.hotkey",        new[] { "表示／非表示のキー",    "Show/hide shortcut" } },
            { "set.hotkeyHint",    new[] { "欄をクリックしてから、Ctrl・Alt・Shift のどれかと一緒にキーを押します。",
                                           "Click the box, then press a key together with Ctrl, Alt, or Shift." } },
            { "set.showAtStartup", new[] { "起動したときに HUD を表示する", "Show the HUD when ctxtray starts" } },
            { "set.position",      new[] { "位置",                 "Position" } },
            { "set.resetPosition", new[] { "右下の隅に戻す",        "Move to bottom-right corner" } },

            // 設定ダイアログ: トレイアイコン
            { "set.secTrayCount",  new[] { "アイコンの数",          "Number of icons" } },
            { "set.modeMulti",     new[] { "値ごとに分ける（目印とバー、最大 3 個）", "One icon per value (label and bar, up to 3)" } },
            { "set.modeSingle",    new[] { "1 個にまとめる（横のバー）", "One combined icon (horizontal bars)" } },

            { "set.secTrayValues", new[] { "表示する値",            "Values to show" } },
            { "set.valContext",    new[] { "コンテキスト（動いている中でいちばん圧縮に近いセッション）",
                                           "Context (the running session closest to compaction)" } },
            { "set.valFiveHour",   new[] { "5時間枠",              "5-hour limit" } },
            { "set.valWeekly",     new[] { "週間枠",               "Weekly limit" } },

            // ラジオボタンの文は折り返せないので、目印と値の対応は補足の行に書く。
            { "set.secTrayLabel",  new[] { "値ごとに分けるときの目印", "Label on each icon" } },
            { "set.labelLetters",  new[] { "文字（C・5h・W）",      "Letters (C, 5h, W)" } },
            { "set.labelGlyphs",   new[] { "絵記号（吹き出し・時計・カレンダー）", "Symbols (bubble, clock, calendar)" } },
            { "set.labelHint",     new[] { "C と吹き出しはコンテキスト、5h と時計は5時間枠、W とカレンダーは週間枠です。",
                                           "C and the bubble mean context, 5h and the clock the 5-hour limit, W and the calendar the weekly limit." } },

            { "set.trayOverflowHint", new[] { "アイコンが通知領域の「^」の中に入ったときは、タスクバーへドラッグして出してください（最初の 1 回だけ）。正確な値はアイコンにマウスを乗せると出ます。",
                                              "If an icon lands under the ^ overflow, drag it onto the taskbar (only needed once). Hover over an icon for the exact values." } },

            { "set.secTrayPreview",new[] { "見本",                 "Preview" } },
            { "set.previewHint",   new[] { "今の値で描いています（左が実寸、右が 2 倍）。",
                                           "Drawn with the current values (actual size, then 2x)." } },

            // 設定ダイアログ: しきい値と通知
            { "set.secThreshold",  new[] { "色が変わる境目",        "When colors change" } },
            { "set.warn",          new[] { "注意",                 "Warn" } },
            { "set.danger",        new[] { "危険",                 "Danger" } },
            { "set.ctxThreshold",  new[] { "コンテキスト",          "Context" } },
            { "set.ctxHint",       new[] { "圧縮が起きる点を 100% とした割合です。1M のモデルなら 注意 {0:N0} ／ 危険 {1:N0} トークン。",
                                           "Measured against the point where compaction happens (100%). On a 1M model: warn at {0:N0}, danger at {1:N0} tokens." } },
            { "set.fhThreshold",   new[] { "5時間枠",              "5-hour limit" } },
            { "set.wkThreshold",   new[] { "週間枠",               "Weekly limit" } },

            { "set.secNotify",     new[] { "通知を出す",           "Send notifications for" } },
            { "set.notifyContext", new[] { "コンテキスト",          "Context" } },
            { "set.notifyFh",      new[] { "5時間枠",              "5-hour limit" } },
            { "set.notifyWk",      new[] { "週間枠",               "Weekly limit" } },
            // 英語はラベルが長く 2〜3 行に折り返したので、数値の前に文を分けて短くした。
            { "set.hysteresis",    new[] { "もう一度知らせるのは",  "Notify again" } },
            { "set.hysteresisPre", new[] { "",                     "after a drop of" } },
            { "set.hysteresisUnit",new[] { "ポイント下がってから",  "points" } },
            { "set.minRepeat",     new[] { "通知の最短間隔",        "Minimum interval" } },
            { "set.minutes",       new[] { "分",                   "min" } },
            { "set.seconds",       new[] { "秒",                   "s" } },

            // 設定ダイアログ: 全般
            { "set.secGeneral",    new[] { "全般",                 "General" } },
            { "set.theme",         new[] { "配色",                 "Theme" } },
            { "set.auto",          new[] { "自動（Windows に合わせる）", "Automatic (follow Windows)" } },
            { "set.light",         new[] { "ライト",               "Light" } },
            { "set.dark",          new[] { "ダーク",               "Dark" } },
            { "set.language",      new[] { "言語",                 "Language" } },
            { "set.langAuto",      new[] { "自動（Windows に合わせる）", "Automatic (follow Windows)" } },
            { "set.poll",          new[] { "更新間隔",             "Refresh interval" } },
            { "set.compactPoint",  new[] { "圧縮点",               "Compaction point" } },
            { "set.compactValue",  new[] { "{0:P0}（Claude Code の既定値）", "{0:P0} (Claude Code's default)" } },
            { "set.compactCustom", new[] { "{0:P0}（設定ファイルの値）", "{0:P0} (from the config file)" } },
            { "set.compactHint",   new[] { "1M のモデルは約 967K トークンで自動圧縮されます（公式ドキュメント）。/autocompact で変えた場合は、設定ファイルの compactThreshold を合わせてください。",
                                           "1M-context models auto-compact at about 967K tokens (per the official docs). If you changed this with /autocompact, set compactThreshold in the config file to match." } },
            { "set.configFile",    new[] { "設定ファイル",          "Config file" } },
            { "set.open",          new[] { "開く",                 "Open" } },

            { "set.ok",            new[] { "OK",                   "OK" } },
            { "set.cancel",        new[] { "キャンセル",           "Cancel" } },
            { "set.reset",         new[] { "既定に戻す",           "Reset to defaults" } },
            { "set.saveFailed",    new[] { "設定を保存できませんでした。\n{0}",
                                           "Could not save the settings.\n{0}" } },

            // コンソール出力
            { "cli.header",     new[] { " Claude の状態   {0}", " Claude status   {0}" } },
            { "cli.rateTitle",  new[] { " レートリミット（{0}）", " Rate limits ({0})" } },
            { "cli.current",    new[] { "現在値", "current" } },
            { "cli.lowerBound", new[] { "下限値 — サンプル以降に稼働あり",
                                        "lower bound - activity since the sample" } },
            { "cli.reference",  new[] { "参考値 — Desktop 未起動か長時間更新なし",
                                        "for reference - Desktop not running, or no recent sample" } },
            { "cli.fiveHour",   new[] { "   5時間枠 ", "   5-hour  " } },
            { "cli.weekly",     new[] { "   週間枠  ", "   weekly  " } },
            { "cli.sampledAt",  new[] { "   採取    : {0}（{1}）", "   sampled : {0} ({1})" } },
            { "cli.nextReset",  new[] { "   5h リセット見込み : {0}", "   next 5h reset : {0}" } },
            { "cli.rateUnavail",new[] { "  レートリミット: 取得できず", "  Rate limits: unavailable" } },
            { "cli.sessions",   new[] { " セッション（* = 直近にアクティブ / ext = Desktop 以外 / ● = 稼働中）",
                                        " Sessions (* = most recent, ext = non-Desktop, ● = running)" } },
            { "cli.none",       new[] { "   該当なし", "   none" } },
            { "cli.unknownModel", new[] { "分母不明のモデル: {0}  ({1:N0} トークン)",
                                          "unknown context window: {0}  ({1:N0} tokens)" } },
            { "cli.noUsage",    new[] { "usage 未記録", "no usage recorded" } },
            { "cli.diag",       new[] { "  ! 読み取りに問題があります: {0}",
                                        "  ! problems while reading: {0}" } },
            { "cli.unknownArg", new[] { "不明な引数: {0}", "Unknown argument: {0}" } },
            { "cli.secondsAgo", new[] { "{0} 秒前", "{0}s ago" } },
            { "cli.minutesAgo", new[] { "{0:N0} 分前", "{0:N0} min ago" } },

            // --verify-weekly
            { "vw.title",       new[] { "週間枠リセットの推定", "Weekly reset estimate" } },
            { "vw.samples",     new[] { "サンプル数: {0}", "samples: {0}" } },
            { "vw.observed",    new[] { "観測できたリセット: {0} 回", "resets observed: {0}" } },
            { "vw.interval",    new[] { "  sd {0,3} -> {1,-3}  {2}  〜  {3}   幅 {4:0.0} 時間",
                                        "  sd {0,3} -> {1,-3}  {2}  ..  {3}   width {4:0.0} h" } },
            { "vw.noObs",       new[] { "  → 有効な観測がまだ無い。時刻は表示しない。",
                                        "  -> no usable observation yet; no time is shown." } },
            { "vw.width",       new[] { "  7 日周期で畳み込んだ交差の幅: {0:0.0} 時間",
                                        "  intersection width after folding over 7 days: {0:0.0} h" } },
            { "vw.weekday",     new[] { "  曜日: {0}", "  weekday: {0}" } },
            { "vw.next",        new[] { "  次回リセット: {0}", "  next reset: {0}" } },
            { "vw.notYet",      new[] { "まだ絞れていないので表示しない",
                                        "not narrow enough yet, so not shown" } },
            { "vw.hud",         new[] { "  推定の表記: {0}（HUD には出さない）", "  estimate label: {0} (not shown in the HUD)" } },
            { "vw.nothing",     new[] { "(何も出さない)", "(nothing)" } },

            // 診断（読み取りに失敗した理由）。
            // バグ報告に貼られる文字列なので、必ず利用者の言語で出す。
            { "diag.noDataRoot",     new[] { "Claude Desktop のデータフォルダが見つからない",
                                             "Claude Desktop data folder not found" } },
            { "diag.noUsageFileYet", new[] { "plan-usage-history.json が見つからない（フォルダのみ検出）",
                                             "plan-usage-history.json not found (folder exists)" } },
            { "diag.noConfigDir",    new[] { "Claude Code の設定フォルダ (~/.claude) が見つからない",
                                             "Claude Code config folder (~/.claude) not found" } },
            { "diag.noUsageFile",    new[] { "plan-usage-history.json が無い",
                                             "plan-usage-history.json is missing" } },
            { "diag.usageUnreadable",new[] { "plan-usage-history.json を読めない",
                                             "cannot read plan-usage-history.json" } },
            { "diag.usageUnparsable",new[] { "plan-usage-history.json を解析できない",
                                             "cannot parse plan-usage-history.json" } },
            { "diag.noSamples",      new[] { "レートリミットのサンプルが空",
                                             "rate-limit history is empty" } },
            { "diag.noTabsDir",      new[] { "claude-code-sessions が無い",
                                             "claude-code-sessions is missing" } },
            { "diag.tabsUnlistable", new[] { "claude-code-sessions を列挙できない",
                                             "cannot list claude-code-sessions" } },
            { "diag.noTabs",         new[] { "開いているタブの登録が 0 件",
                                             "no open tabs registered" } },
            { "diag.tabsFailed",     new[] { "タブ登録ファイル {0} 件が読取失敗",
                                             "{0} tab file(s) could not be read" } },
            { "diag.noProcDir",      new[] { "~/.claude/sessions が無い",
                                             "~/.claude/sessions is missing" } },
            { "diag.procUnlistable", new[] { "~/.claude/sessions を列挙できない",
                                             "cannot list ~/.claude/sessions" } },

            // 曜日（週間枠のリセット推定）
            { "day.0", new[] { "日", "Sun" } },
            { "day.1", new[] { "月", "Mon" } },
            { "day.2", new[] { "火", "Tue" } },
            { "day.3", new[] { "水", "Wed" } },
            { "day.4", new[] { "木", "Thu" } },
            { "day.5", new[] { "金", "Fri" } },
            { "day.6", new[] { "土", "Sat" } },
        };

        public static string Weekday(DayOfWeek d)
        {
            return Get("day." + (int)d);
        }
    }
}
