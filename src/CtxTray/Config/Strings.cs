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
            { "menu.showHud",     new[] { "パネルを表示",        "Show HUD" } },
            // 透過中は HUD をドラッグできず右クリックも届かないので、メニューからも切り替えられるようにする。
            { "menu.clickThrough",new[] { "クリックを透過", "Pass clicks through" } },
            { "menu.autoStart",   new[] { "サインイン時に起動",   "Start at sign-in" } },
            { "menu.settings",    new[] { "設定…",              "Settings…" } },
            { "menu.refresh",     new[] { "今すぐ更新",         "Refresh now" } },
            { "menu.openConfig",  new[] { "設定ファイルを開く", "Open config file" } },
            // 「稼働状況」では版や設定ファイルの場所を探しにいく先として思い付けない（2026-09-20）。
            { "menu.about",       new[] { "ctxtray について…",  "About ctxtray…" } },
            { "menu.exit",        new[] { "終了",               "Exit" } },

            // ツールチップ（Windows の制限で 63 文字まで。ラベルを付けて何の値か分かるようにする）
            { "tip.context",      new[] { "コンテキスト {0}% {1}", "Context {0}% {1}" } },
            { "tip.fiveHour",     new[] { "5時間 {0}",          "5h {0}" } },
            { "tip.weekly",       new[] { "週間 {0}",           "Week {0}" } },
            { "tip.noSessions",   new[] { "セッションなし",      "No sessions" } },
            { "tip.noRunning",    new[] { "実行中のセッションなし", "No running sessions" } },
            { "tip.rateUnknown",  new[] { "レート枠不明",         "Rate unknown" } },
            { "tip.error",        new[] { "エラー: {0}",        "error: {0}" } },

            // HUD
            { "hud.capLimits",    new[] { "レート枠",       "LIMITS" } },
            { "hud.capContext",   new[] { "コンテキスト",   "CONTEXT" } },
            { "hud.fiveHour",     new[] { "5時間",          "5h" } },
            { "hud.weekly",       new[] { "週間",           "Week" } },
            // 押すと隠した行をその場で出す（設定は変えない）。記号は使わず、
            // 押せることが分かる言い方にする（▲ や + は意味が伝わらなかった、2026-09-15）。
            { "hud.hidden",       new[] { "ほか {0} 件を表示",   "show {0} more" } },
            { "hud.collapse",     new[] { "折りたたむ",                "hide them" } },
            // レート枠が淡いときの理由。灰色なだけでは「壊れている」と読まれうるので、
            // 見出しの右に言葉で出す（記号は付けない。▲ も + も意味が伝わらなかった）。
            { "hud.capReference", new[] { "参考値",           "for reference" } },

            // HUD の行にマウスを乗せたときに出す詳細（行は簡潔なまま、確かな値だけをここに出す）。
            { "hud.tipTerminal",  new[] { "ターミナルで実行中",        "Running in a terminal" } },
            { "hud.tipVsCode",    new[] { "VS Code で実行中",          "Running in VS Code" } },
            { "hud.tipTokens",    new[] { "{0} / {1} トークン（{2}%）", "{0} / {1} tokens ({2}%)" } },
            { "hud.tipTokensOnly",new[] { "{0} トークン",              "{0} tokens" } },
            { "hud.tipToCompact", new[] { "自動圧縮まであと {0} トークン",  "{0} tokens until auto-compaction" } },
            { "hud.tipLastReply", new[] { "最終応答: {0}",           "Last reply: {0}" } },
            { "hud.tipStopped",   new[] { "Claude Code のプロセスは実行されていません",
                                          "The Claude Code process is not running" } },
            // % が出ない理由は、状況ごとに「何が起きていて、利用者に何ができるか」まで書く。
            { "hud.tipNoLimitHead", new[] { "このモデルの上限が不明なため、% を表示できません",
                                            "No % because this model's context window is unknown" } },
            { "hud.tipNoLimitOff",  new[] { "このモデルは ctxtray に登録されていません。設定の「モデル」タブで公式ドキュメントからの取得をオンにするか、上限を選択すると % を表示できます。",
                                            "ctxtray doesn't know this model yet. In Settings > Models, turn on fetching from the official docs or pick its window to see a %." } },
            { "hud.tipNoLimitPending", new[] { "公式ドキュメントでこのモデルの上限を確認中…",
                                               "Checking the official docs for this model's window…" } },
            { "hud.tipNoLimitNotFound", new[] { "公式ドキュメントにこのモデルの情報が見つかりませんでした（明日再確認します）。設定の「モデル」タブで上限を選択できます。",
                                                "The official docs don't list this model yet (ctxtray will check again tomorrow). You can pick its window in Settings > Models." } },
            // 題名で分かることは本文に書かない（本文が長いと末尾が切れる）。
            { "notify.unknownModel",     new[] { "モデルの上限が不明です", "Unknown context window" } },
            { "notify.unknownModelBody", new[] { "{0} の上限が不明なため、% を表示できません。クリックすると設定を開きます。",
                                                 "ctxtray can't show a % for {0}. Click to open the settings." } },
            { "hud.tipLimitDocs",   new[] { "上限: 公式ドキュメントから取得", "Window taken from the official docs" } },
            { "hud.tipLimitConfig", new[] { "上限: 設定で指定",     "Window set in Settings" } },
            { "hud.tipSampled",   new[] { "Claude Desktop が {0}に記録", "Claude Desktop recorded this {0}" } },
            { "hud.tipReset",     new[] { "次回リセット {0} ごろ",         "Next reset about {0}" } },
            { "hud.tipReference", new[] { "Claude Desktop が起動していないため、最後に記録された値を表示しています",
                                          "Claude Desktop isn't running, so this is the last value it recorded" } },

            { "hud.loading",      new[] { "読み込み中…",              "Reading…" } },
            { "hud.rateUnavail",  new[] { "レート枠: 取得できません", "Rate limits: unavailable" } },
            { "hud.noSessions",   new[] { "セッションなし",            "No sessions" } },
            { "hud.unknownLimit", new[] { "上限不明",                  "window?" } },
            { "hud.untitled",     new[] { "(無題)",                    "(untitled)" } },
            // --verify-weekly でだけ使う（HUD には出さない）。
            { "hud.estimating",   new[] { "{0}ごろ（推定中）",         "around {0} (estimating)" } },

            // 通知
            { "notify.compactSoon",   new[] { "まもなく自動圧縮されます",             "Compaction is close" } },
            { "notify.contextRising", new[] { "コンテキストの使用量が増えています",       "Context is growing" } },
            // ★ 残りを % で書かない。表示する % は「ウィンドウに対する消費率」、
            //   圧縮までの残りは「圧縮点までの到達率」で分母が違うため、
            //   「74.0%（圧縮まで残り 23%）」のように足して 100 にならない数が並び、
            //   丸め誤差か不具合に見えた（2026-09-20）。HUD の行の詳細と同じくトークン数で書く。
            { "notify.contextBody",   new[] { "{0}\nコンテキスト {1:0}%（自動圧縮まであと {2} トークン）",
                                              "{0}\nContext {1:0}% ({2} tokens until auto-compaction)" } },
            { "notify.fiveHour",      new[] { "5時間枠が残り少なくなっています",  "5-hour limit is running low" } },
            { "notify.weekly",        new[] { "週間枠が残り少なくなっています",    "Weekly limit is running low" } },
            { "notify.fiveHourBody",  new[] { "5時間枠 {0}%",                     "5-hour {0}%" } },
            { "notify.weeklyBody",    new[] { "週間枠 {0}%",                      "Weekly {0}%" } },

            // 設定・診断
            { "config.unreadable", new[] { "設定を読み込めませんでした: {0}",
                                           "Could not read the config: {0}" } },
            { "config.broken",     new[] { "設定ファイルが壊れているため、既定値で起動しました。元のファイルは .bak として保存してあります。",
                                           "The config file was invalid, so defaults were used. The original was kept as .bak." } },
            { "config.brokenKept", new[] { "設定ファイルを読み込めなかったため、前回の設定で動作しています。ファイルを修正すると自動的に反映されます。",
                                           "The config file could not be read, so the previous settings stay in effect. Fix the file and it will be picked up." } },
            { "config.openFailed", new[] { "設定ファイルを開けませんでした: {0}",
                                           "Could not open the config file: {0}" } },

            { "about.body",   new[] { "バージョン: {4}\n稼働時間: {0:0} 時間 {1:00} 分\n更新間隔: {2} 秒\n設定ファイル: {3}",
                                      "Version: {4}\nRunning for: {0:0} h {1:00} m\nRefresh interval: {2} s\nConfig: {3}" } },
            { "about.lastError", new[] { "\n直近のエラー: {0}（{1}）",
                                         "\nLast problem: {0} ({1})" } },
            { "app.crashed",   new[] { "ctxtray は予期せず停止しました。\n\n{0}",
                                       "ctxtray stopped unexpectedly.\n\n{0}" } },
            { "app.error",     new[] { "表示の更新中にエラーが発生しました。ctxtray は引き続き動作します。\n{0}",
                                       "Something went wrong while updating. ctxtray keeps running.\n{0}" } },
            { "autostart.description", new[] { "Claude のコンテキスト残量とレート枠を常時表示する",
                                               "Always-visible Claude context and rate-limit readout" } },

            // 初めての起動（設定ファイルが無かったとき）に 1 回だけ出す案内。
            // 通知は長いと末尾が切れる（実機で確認）。題名が「ctxtray」なので、本文は操作だけにする。
            { "app.welcome",   new[] { "{0} か通知領域のアイコンでパネルを表示できます。\n右クリックで設定を開けます。",
                                       "Press {0} or click the tray icon to show the panel. Right-click it for settings." } },

            // クリックを後ろに通す設定を切り替えたとき、パネルの上に数秒だけ出す（2 行に収める）。
            // オンにするとパネルの右クリックが効かなくなるので、戻し方を必ず添える。
            { "hud.clickThroughOnKey",  new[] { "クリックを透過します。\n{0} またはトレイのメニューで解除できます。",
                                                "Clicks now pass through. Press {0} or use the tray menu to undo." } },
            { "hud.clickThroughOnMenu", new[] { "クリックを透過します。\nトレイのメニューで解除できます。",
                                                "Clicks now pass through. Use the tray menu to undo." } },
            { "hud.clickThroughOff",    new[] { "クリックの透過を解除しました。",
                                                "The panel takes clicks again." } },

            // ホットキーが登録できなかったとき（押しても何も起きない理由を伝える）。
            { "hotkey.taken",  new[] { "ショートカット {0} は他のアプリが使用中のため使用できません。設定で別のキーを選んでください。",
                                       "The {0} shortcut is in use by another app, so it does nothing. Choose a different key in the settings." } },
            { "hotkey.clickThroughTaken", new[] { "「クリックを透過」のショートカット {0} は他のアプリが使用中のため使用できません。設定で別のキーを選んでください。",
                                                  "The {0} click-through shortcut is in use by another app, so it does nothing. Choose a different key in the settings." } },
            { "hotkey.clickThroughSame",  new[] { "「クリックを透過」のショートカット {0} は「いまパネルを表示する」のキーと同じため使用できません。設定で別のキーを選んでください。",
                                                  "The {0} click-through shortcut is the same as the \"Show the HUD now\" shortcut, so it does nothing. Choose a different key in the settings." } },
            { "hotkey.unparsable", new[] { "設定ファイルのショートカット「{0}」を認識できません。設定で選び直してください。",
                                           "The shortcut \"{0}\" in the config file could not be read. Choose one in the settings." } },

            // 設定ダイアログ
            { "set.title",         new[] { "ctxtray の設定",      "ctxtray settings" } },
            { "set.tabHud",        new[] { "パネル",                  "HUD" } },
            { "set.tabTray",       new[] { "トレイアイコン",        "Tray icon" } },
            { "set.tabThresholds", new[] { "しきい値と通知",        "Thresholds and alerts" } },
            { "set.tabGeneral",    new[] { "全般",                 "General" } },
            { "set.tabModels",     new[] { "モデル",               "Models" } },

            // 設定ダイアログ: モデル
            { "set.secModelFetch", new[] { "未登録のモデル",          "New models" } },
            { "set.fetchDocs",     new[] { "上限が不明なモデルを公式ドキュメントで調べる",
                                           "Look up unknown models in the official docs" } },
            // 「通信しない」を既定にしているので、オンにしたら何が起きるかを具体的に書く。
            // 説明文はどれも短くする（2026-09-26、利用者の指摘「冗長で読みにくい」）。ただし通信の中身は削らない。
            { "set.fetchDocsHint", new[] { "未登録のモデルのときだけ、platform.claude.com の公開ページを 1 回読み込みます。会話やアカウントの情報は送信しません。",
                                           "Only for a model it doesn't know: reads its public page on platform.claude.com once. Nothing about your conversations or account is sent." } },
            { "set.checkNow",      new[] { "今すぐ確認",           "Check now" } },
            { "set.checkNowStarted", new[] { "確認を開始しました。結果はパネルに表示されます。",
                                             "Checking. Results will show in the panel." } },
            { "set.secModelList",  new[] { "モデルごとの上限", "Known context windows" } },
            { "set.colModel",      new[] { "モデル",               "Model" } },
            { "set.colLimit",      new[] { "上限",                 "Window" } },
            { "set.colSource",     new[] { "取得元",             "Source" } },
            { "set.srcBuiltIn",    new[] { "組み込み",             "Built in" } },
            { "set.srcDocs",       new[] { "公式ドキュメント（{0}）", "Official docs ({0})" } },
            { "set.srcConfig",     new[] { "手動設定",                 "Set by you" } },
            { "set.srcUnknown",    new[] { "不明",                 "Unknown" } },
            { "set.builtInShow",   new[] { "▸ 組み込みのモデル（{0} 件）を表示", "▸ Show built-in models ({0})" } },
            { "set.builtInHide",   new[] { "▾ 組み込みのモデル（{0} 件）を非表示", "▾ Hide built-in models ({0})" } },
            { "set.limitUnset",    new[] { "未設定",               "Not set" } },
            { "set.modelListHint", new[] { "「不明」のモデルは上限を選択すると % が表示されます。その他の値は設定ファイルの modelLimits で変更できます。",
                                           "Choose a window for an \"Unknown\" model to see a %. Other values go in modelLimits in the config file." } },

            // 設定ダイアログ: HUD
            { "set.secHudContent", new[] { "表示する内容",          "What to show" } },
            { "set.hudShowRate",   new[] { "レート枠（5時間枠・週間枠）", "Rate limits (5-hour and weekly)" } },
            { "set.hudShowSessions", new[] { "セッションごとのコンテキスト", "Context for each session" } },

            { "set.moreShow",      new[] { "▸ 詳細設定を表示",     "▸ Show more settings" } },
            { "set.moreHide",      new[] { "▾ 詳細設定を非表示",     "▾ Hide more settings" } },
            // 語順が日英で違うので、数値の前後を別の文言にする。
            { "set.hideIdlePre",   new[] { "",                     "Hide sessions not used for" } },
            { "set.hideIdlePost",  new[] { "時間以上使用していないセッションを非表示にする", "hours or more" } },
            { "set.hideStopped",   new[] { "実行中でないセッション（灰色の行）を非表示にする",
                                           "Hide sessions that aren't running (grey rows)" } },
            { "set.external",      new[] { "ターミナルや VS Code のセッションも表示する",
                                           "Also show terminal and VS Code sessions" } },
            { "set.externalMaxPre",  new[] { "最大",               "Up to" } },
            { "set.externalMaxPost", new[] { "件",                 "sessions" } },

            { "set.secHudColumns", new[] { "各行の表示項目",          "On each row" } },
            { "set.showBar",       new[] { "バー",                 "Bar" } },
            { "set.showTokens",    new[] { "トークン数（例: 284k / 1M）", "Token count (e.g. 284k / 1M)" } },
            // モデルとエフォートは 1 つのチェックボックスで両方を切り替える（2026-09-27、利用者の提案）。
            // 片方だけにしたいときは設定ファイルの showModel／showEffort で分けられる。
            { "set.showModel",     new[] { "モデルとエフォート（例: Opus 5.5 · high）",
                                           "Model and effort (e.g. Opus 5.5 · high)" } },
            { "set.modelLayout",   new[] { "表示位置",               "Layout" } },
            // 「名前」だけだと何の名前か分かりにくい（2026-09-27、利用者の指摘）。
            { "set.layoutColumn",  new[] { "セッション名の右（パネルの幅が広がる）", "After the session name (wider panel)" } },
            { "set.layoutTwoLine", new[] { "セッション名の下（行が高くなる）", "Under the session name (taller rows)" } },
            { "set.showResets",    new[] { "5時間枠のリセット時刻", "5-hour reset time" } },
            { "set.always",        new[] { "常に表示",             "Always" } },
            { "set.autoNear",      new[] { "リセットの {0} 分前から", "From {0} min before the reset" } },
            { "set.never",         new[] { "表示しない",           "Never" } },

            { "set.secHudLook",    new[] { "外観",               "Look" } },
            { "set.textSize",      new[] { "文字サイズ",          "Text size" } },
            { "set.sizeXSmall",    new[] { "極小",                 "Extra small" } },
            { "set.sizeSmall",     new[] { "小",                   "Small" } },
            { "set.sizeNormal",    new[] { "標準",                 "Normal" } },
            { "set.sizeLarge",     new[] { "大",                   "Large" } },
            { "set.sizeXLarge",    new[] { "特大",                 "Extra large" } },
            { "set.nameWidth",     new[] { "名前の幅",             "Name width" } },
            { "set.nameWidthUnit", new[] { "（既定値: {0}）",        "(default {0})" } },
            { "set.opacity",       new[] { "不透明度",             "Opacity" } },
            { "set.clickThrough",  new[] { "クリックを後ろのウィンドウへ透過する",
                                           "Let clicks pass through to the window behind" } },
            // 透過中は窓がマウスを一切受け取らないので、ドラッグだけでなく行の詳細も右クリックも死ぬ。
            // 「ドラッグできない」としか書いていなかった頃は、残りの 2 つが壊れたように見えた（2026-09-20）。
            // 戻し方はオンにしたときパネルに出る（hud.clickThroughOnKey など）ので、ここには書かない。
            { "set.clickThroughHint", new[] { "オンの間は、パネルの移動・詳細の表示・右クリックができません。",
                                              "While on, the HUD can't be dragged, hovered, or right-clicked." } },

            // 起動の節（全般タブの先頭）。自動起動は以前からトレイのメニューにだけあり、設定画面で見つからなかった（2026-09-26）。
            { "set.secStartup",    new[] { "起動",                 "Startup" } },
            { "set.autoStart",     new[] { "Windows へのサインイン時に ctxtray を起動する",
                                           "Start ctxtray when you sign in to Windows" } },
            { "set.autoStartHint", new[] { "スタートアップフォルダーにショートカットを作成します。",
                                           "Puts a shortcut in your Startup folder." } },

            { "set.secHudControl", new[] { "パネルの操作",           "HUD controls" } },
            // いまの表示状態（保存しない）。クリック透過と同じ並びにするために置いた（2026-09-26、利用者の決定）。
            // すぐ下の「起動したときに HUD を表示する」と対の名前にして、違いを補足文なしで伝える（2026-09-27、利用者の指摘）。
            { "set.showHud",       new[] { "いまパネルを表示する",   "Show the HUD now" } },
            // チェックボックスの直下に字下げして置くので、何の切り替えかは書かない（2 つの欄で同じ言葉）。
            { "set.toggleKey",     new[] { "切り替えキー",       "Shortcut" } },
            { "set.keyNone",       new[] { "なし",                 "None" } },
            // キーの欄をクリックしたときに欄の中へ淡く出す押し方の案内（説明文の代わり。2026-09-27）。
            // 「押しても欄が変わらないキーは、ほかのアプリが先に使っている」（2026-09-18 実機で確認）は README に書く。
            // 中黒（・）だと「全部」なのか「どれか」なのか分かりにくいので、英語と同じく半角スラッシュで書く（2026-09-27、利用者の指摘）。
            { "set.keyPrompt",     new[] { "Ctrl/Alt/Shift + キー", "Ctrl/Alt/Shift + key" } },
            { "set.clickThroughKeySame", new[] { "「いまパネルを表示する」のキーと同じです。",
                                                 "Same as the \"Show the HUD now\" shortcut." } },
            { "set.hotkeyTaken",   new[] { "他のアプリが使用中のため使用できません。",
                                           "Another app uses this key, so it won't work." } },
            { "set.showAtStartup", new[] { "起動時にパネルを表示する", "Show the HUD when ctxtray starts" } },
            { "set.hideFullscreen", new[] { "全画面表示のアプリを使用中は非表示にする",
                                            "Hide while a full-screen app is in use" } },
            { "set.hideFullscreenHint", new[] { "Claude Desktop が前面にあるときは非表示にしません。",
                                                "Not while Claude Desktop is in front." } },
            { "set.position",      new[] { "位置",                 "Position" } },
            { "set.resetPosition", new[] { "右下に戻す",        "Move to bottom-right corner" } },

            // 設定ダイアログ: トレイアイコン
            { "set.secTrayCount",  new[] { "アイコンの数",          "Number of icons" } },
            { "set.modeMulti",     new[] { "値ごとに分ける（ラベルとバー、最大 3 個）", "One icon per value (label and bar, up to 3)" } },
            { "set.modeSingle",    new[] { "1 個にまとめる（横のバー）", "One combined icon (horizontal bars)" } },

            { "set.secTrayValues", new[] { "表示する値",            "Values to show" } },
            { "set.valContext",    new[] { "コンテキスト（実行中で自動圧縮に最も近いセッション）",
                                           "Context (the running session closest to compaction)" } },
            { "set.valFiveHour",   new[] { "5時間枠",              "5-hour limit" } },
            { "set.valWeekly",     new[] { "週間枠",               "Weekly limit" } },

            // ラジオボタンの文は折り返せないので、目印と値の対応は補足の行に書く。
            { "set.secTrayLabel",  new[] { "値ごとに分けるときのラベル", "Label on each icon" } },
            { "set.labelLetters",  new[] { "文字（C・5h・W）",      "Letters (C, 5h, W)" } },
            { "set.labelGlyphs",   new[] { "記号（吹き出し・時計・カレンダー）", "Symbols (bubble, clock, calendar)" } },
            { "set.labelPercent",  new[] { "数値（現在の %）",      "Numbers (current %)" } },
            { "set.labelHint",     new[] { "左から順にコンテキスト、5時間枠、週間枠です。",
                                           "From the left: context, 5-hour, weekly." } },

            { "set.trayOverflowHint", new[] { "「^」の中に隠れたアイコンは、タスクバーへドラッグすると常に表示できます。",
                                              "Drag an icon out of the ^ overflow onto the taskbar to keep it visible." } },

            { "set.secTrayPreview",new[] { "プレビュー",                 "Preview" } },
            { "set.previewHint",   new[] { "左: 実寸、右: 2 倍",  "Actual size, then 2x." } },

            // 設定ダイアログ: しきい値と通知
            { "set.secThreshold",  new[] { "色が変わる使用率",        "When colors change" } },
            { "set.warn",          new[] { "注意",                 "Warn" } },
            { "set.danger",        new[] { "危険",                 "Danger" } },
            { "set.ctxThreshold",  new[] { "コンテキスト",          "Context" } },
            // 圧縮点以上の値は、その色になる前に圧縮されるので起きない。値は勝手に直さず知らせる。
            { "set.ctxOverCompact",new[] { "自動圧縮（{0:P0}）以上の値では、色の変化も通知も起きません。",
                                           "At or above auto-compaction ({0:P0}), this never shows." } },
            { "set.fhThreshold",   new[] { "5時間枠",              "5-hour limit" } },
            { "set.wkThreshold",   new[] { "週間枠",               "Weekly limit" } },
            // 判定は危険から先に見るので、注意を危険より大きくすると注意が一度も起きない。
            // 値は勝手に直さず、その場で知らせる（2026-09-20）。
            { "set.thresholdOrder",new[] { "「注意」は「危険」より小さい値にしてください。大きいと「注意」になりません。",
                                           "Keep warn below danger, or warn never shows." } },

            // 色だけで示すと、赤と緑の区別が付きにくい人には通常と危険が見分けられない。
            // 通知領域のアイコンには入れない（小さすぎて、見えるようにするとうるさくなる）。
            { "set.levelMarks",    new[] { "注意・危険をバーの縞模様でも示す",
                                           "Also mark warn and danger on the panel's bars" } },
            { "set.levelMarksHint",new[] { "注意は粗い縞、危険は細かい縞。色が見分けにくいときに。",
                                           "Wide stripes for warn, tight for danger. Helps when colours are hard to tell apart." } },

            { "set.secNotify",     new[] { "通知する項目",           "Send notifications for" } },
            { "set.notifyContext", new[] { "コンテキスト",          "Context" } },
            { "set.notifyFh",      new[] { "5時間枠",              "5-hour limit" } },
            { "set.notifyWk",      new[] { "週間枠",               "Weekly limit" } },
            // 英語はラベルが長く 2〜3 行に折り返したので、数値の前に文を分けて短くした。
            { "set.hysteresis",    new[] { "再通知",  "Notify again" } },
            { "set.hysteresisPre", new[] { "",                     "after a drop of" } },
            { "set.hysteresisUnit",new[] { "ポイント下がったら",  "points" } },
            { "set.minRepeat",     new[] { "通知の最小間隔",        "Minimum interval" } },
            { "set.minutes",       new[] { "分",                   "min" } },
            { "set.seconds",       new[] { "秒",                   "s" } },

            // 設定ダイアログ: 全般
            { "set.secGeneral",    new[] { "全般",                 "General" } },
            { "set.theme",         new[] { "配色",                 "Theme" } },
            { "set.auto",          new[] { "自動（Windows の設定に合わせる）", "Automatic (follow Windows)" } },
            { "set.light",         new[] { "ライト",               "Light" } },
            { "set.dark",          new[] { "ダーク",               "Dark" } },
            { "set.language",      new[] { "言語",                 "Language" } },
            { "set.langAuto",      new[] { "自動（Windows の設定に合わせる）", "Automatic (follow Windows)" } },
            { "set.poll",          new[] { "更新間隔",             "Refresh interval" } },
            { "set.compactPoint",  new[] { "自動圧縮のしきい値",               "Compaction point" } },
            { "set.compactValue",  new[] { "{0:P0}（Claude Code の既定値）", "{0:P0} (Claude Code's default)" } },
            { "set.compactCustom", new[] { "{0:P0}（設定ファイルの値）", "{0:P0} (from the config file)" } },
            { "set.compactHint",   new[] { "/autocompact で変更した場合は、設定ファイルの compactThreshold も同じ値にしてください。",
                                           "If you changed it with /autocompact, set compactThreshold in the config file to match." } },
            { "set.configFile",    new[] { "設定ファイル",          "Config file" } },
            { "set.open",          new[] { "開く",                 "Open" } },

            { "set.ok",            new[] { "OK",                   "OK" } },
            { "set.cancel",        new[] { "キャンセル",           "Cancel" } },
            // 閉じずに保存する。見ながら合わせる項目（幅・文字の大きさ・不透明度）のため。
            { "set.apply",         new[] { "適用",                 "Apply" } },
            { "set.reset",         new[] { "既定値に戻す",           "Reset to defaults" } },
            { "set.saveFailed",    new[] { "設定を保存できませんでした。\n{0}",
                                           "Could not save the settings.\n{0}" } },

            // コンソール出力
            { "cli.header",     new[] { " Claude の状態   {0}", " Claude status   {0}" } },
            { "cli.rateTitle",  new[] { " レート枠（{0}）", " Rate limits ({0})" } },
            { "cli.current",    new[] { "現在値", "current" } },
            { "cli.lowerBound", new[] { "下限値: 記録後に使用あり",
                                        "lower bound - activity since the sample" } },
            { "cli.reference",  new[] { "参考値: Desktop が起動していないか、長時間更新なし",
                                        "for reference - Desktop not running, or no recent sample" } },
            { "cli.fiveHour",   new[] { "   5時間枠 ", "   5-hour  " } },
            { "cli.weekly",     new[] { "   週間枠  ", "   weekly  " } },
            { "cli.sampledAt",  new[] { "   記録時刻: {0}（{1}）", "   sampled : {0} ({1})" } },
            { "cli.nextReset",  new[] { "   次回リセット（5時間枠）: {0}", "   next 5h reset : {0}" } },
            { "cli.rateUnavail",new[] { "  レート枠: 取得できません", "  Rate limits: unavailable" } },
            { "cli.sessions",   new[] { " セッション（* = 最近使用 / ext = Desktop 以外 / ● = 実行中）",
                                        " Sessions (* = most recent, ext = non-Desktop, ● = running)" } },
            { "cli.none",       new[] { "   該当なし", "   none" } },
            { "cli.unknownModel", new[] { "上限が不明なモデル: {0}（{1:N0} トークン。設定の「モデル」タブで選択できます）",
                                          "unknown context window: {0}  ({1:N0} tokens; set it in Settings > Models)" } },
            { "cli.noUsage",    new[] { "使用量の記録なし", "no usage recorded" } },
            { "cli.diag",       new[] { "  ! 読み込み時の問題: {0}",
                                        "  ! problems while reading: {0}" } },
            { "cli.unknownArg", new[] { "不明な引数: {0}", "Unknown argument: {0}" } },
            { "cli.secondsAgo", new[] { "{0} 秒前", "{0}s ago" } },
            { "cli.minutesAgo", new[] { "{0:N0} 分前", "{0:N0} min ago" } },
            { "cli.hoursAgo",   new[] { "{0:N0} 時間前", "{0:N0} h ago" } },

            // --verify-weekly
            { "vw.title",       new[] { "週間枠リセットの推定", "Weekly reset estimate" } },
            { "vw.samples",     new[] { "サンプル数: {0}", "samples: {0}" } },
            { "vw.observed",    new[] { "観測できたリセット: {0} 回", "resets observed: {0}" } },
            { "vw.interval",    new[] { "  sd {0,3} -> {1,-3}  {2}  〜  {3}   幅 {4:0.0} 時間",
                                        "  sd {0,3} -> {1,-3}  {2}  ..  {3}   width {4:0.0} h" } },
            { "vw.noObs",       new[] { "  → 有効な観測がまだありません。時刻は表示しません。",
                                        "  -> no usable observation yet; no time is shown." } },
            { "vw.width",       new[] { "  7 日周期で畳み込んだ交差の幅: {0:0.0} 時間",
                                        "  intersection width after folding over 7 days: {0:0.0} h" } },
            { "vw.weekday",     new[] { "  曜日: {0}", "  weekday: {0}" } },
            { "vw.next",        new[] { "  次回リセット: {0}", "  next reset: {0}" } },
            { "vw.notYet",      new[] { "まだ絞り込めていないため表示しません",
                                        "not narrow enough yet, so not shown" } },
            { "vw.hud",         new[] { "  推定の表記: {0}（パネルには表示しない）", "  estimate label: {0} (not shown in the HUD)" } },
            { "vw.nothing",     new[] { "(表示なし)", "(nothing)" } },

            // 診断（読み取りに失敗した理由）。
            // バグ報告に貼られる文字列なので、必ず利用者の言語で出す。
            { "diag.noDataRoot",     new[] { "Claude Desktop のデータフォルダーが見つかりません",
                                             "Claude Desktop data folder not found" } },
            { "diag.noUsageFileYet", new[] { "plan-usage-history.json が見つかりません（フォルダーはあります）",
                                             "plan-usage-history.json not found (folder exists)" } },
            { "diag.noConfigDir",    new[] { "Claude Code の設定フォルダー (~/.claude) が見つかりません",
                                             "Claude Code config folder (~/.claude) not found" } },
            { "diag.noUsageFile",    new[] { "plan-usage-history.json がありません",
                                             "plan-usage-history.json is missing" } },
            { "diag.usageUnreadable",new[] { "plan-usage-history.json を読み込めません",
                                             "cannot read plan-usage-history.json" } },
            { "diag.usageUnparsable",new[] { "plan-usage-history.json を解析できません",
                                             "cannot parse plan-usage-history.json" } },
            { "diag.noSamples",      new[] { "レート枠の記録が空です",
                                             "rate-limit history is empty" } },
            { "diag.noTabsDir",      new[] { "claude-code-sessions がありません",
                                             "claude-code-sessions is missing" } },
            { "diag.tabsUnlistable", new[] { "claude-code-sessions の一覧を取得できません",
                                             "cannot list claude-code-sessions" } },
            { "diag.noTabs",         new[] { "開いているタブが登録されていません",
                                             "no open tabs registered" } },
            { "diag.tabsFailed",     new[] { "タブの登録ファイル {0} 件を読み込めません",
                                             "{0} tab file(s) could not be read" } },
            { "diag.noProcDir",      new[] { "~/.claude/sessions がありません",
                                             "~/.claude/sessions is missing" } },
            { "diag.procUnlistable", new[] { "~/.claude/sessions の一覧を取得できません",
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
