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
        //
        // 書き方の約束（2026-09-27、利用者の決定）:
        // - 最前面の小窓は 日本語「パネル」、英語 "panel"（"HUD" は使わない。キーやコマンド名の hud はそのまま）
        // - 日本語は Windows 標準の言い回し（サインイン、実行中、非表示、既定値、フォルダー など）
        // - 英語の項目名（メニュー・チェックボックス・選択肢・見出し）は冠詞を付けない（"Show panel"）。
        //   補足や通知などの文は普通の英文として冠詞を付ける（"The panel takes clicks again."）
        // - 英語の綴りは英国式（colour、grey）で README・docs とそろえる
        // - 呼び名: トレイアイコン / tray icon、トレイメニュー / tray menu、自動圧縮 / auto-compaction、
        //   通知 / notifications（alerts は使わない）、日本語はショートカットキー・英語は shortcut、
        //   常に "Claude Desktop"（"Desktop" だけにしない）、英語の文では "context window"（"window" だけにしない）
        // - 画面を指すときは 日本語「設定」、英語 "Settings"（in Settings）。たどり方は「設定 > モデル」/ "Settings > Models"
        // - 日本語: 動詞は「選択」、「他」（「その他」は別）、文中の列挙は「、」（括弧内の短い並びだけ「・」）、
        //   括弧は全角、数字と日本語の間は半角空白（「5時間枠」「5時間」は例外）
        // - 英語: 行頭は大文字、画面の文言は短縮形（can't）、単位は "30 s" "5 min" "2 h"。
        //   文の途中に入る断片（diag.*、cli.noUsage など）は小文字で始める
        // - パネルとツールチップは幅が限られるので短い形（5時間・週間 / 5h・Week）を使う
        private static readonly Dictionary<string, string[]> Map =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // トレイメニュー
            { "menu.showHud",     new[] { "パネルを表示",        "Show panel" } },
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
            { "tip.rateUnknown",  new[] { "レート枠不明",         "Rate limits unknown" } },
            { "tip.error",        new[] { "エラー: {0}",        "Error: {0}" } },

            // HUD
            { "hud.capLimits",    new[] { "レート枠",       "LIMITS" } },
            { "hud.capContext",   new[] { "コンテキスト",   "CONTEXT" } },
            { "hud.fiveHour",     new[] { "5時間",          "5h" } },
            { "hud.weekly",       new[] { "週間",           "Week" } },
            // 押すと隠した行をその場で出す（設定は変えない）。記号は使わず、
            // 押せることが分かる言い方にする（▲ や + は意味が伝わらなかった、2026-09-15）。
            { "hud.hidden",       new[] { "他 {0} 件を表示",   "show {0} more" } },
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
                                            "No percentage because this model's context window is unknown" } },
            { "hud.tipNoLimitOff",  new[] { "このモデルは ctxtray に登録されていません。設定の「モデル」タブで公式ドキュメントからの取得をオンにするか、上限を選択すると % を表示できます。",
                                            "ctxtray doesn't know this model yet. To see a percentage, turn on fetching from the official docs or pick its context window in Settings > Models." } },
            { "hud.tipNoLimitPending", new[] { "公式ドキュメントでこのモデルの上限を確認中…",
                                               "Checking the official docs for this model's context window…" } },
            { "hud.tipNoLimitNotFound", new[] { "公式ドキュメントにこのモデルの情報が見つかりませんでした（明日再確認します）。設定の「モデル」タブで上限を選択できます。",
                                                "The official docs don't list this model yet (ctxtray will check again tomorrow). You can pick its context window in Settings > Models." } },
            // 題名で分かることは本文に書かない（本文が長いと末尾が切れる）。
            { "notify.unknownModel",     new[] { "モデルの上限が不明です", "Unknown context window" } },
            { "notify.unknownModelBody", new[] { "{0} の上限が不明なため、% を表示できません。クリックすると設定を開きます。",
                                                 "ctxtray can't show a percentage for {0}. Click to open Settings." } },
            { "notify.update",           new[] { "ctxtray {0} が公開されました", "ctxtray {0} is available" } },
            { "notify.updateBody",       new[] { "クリックするとダウンロードページを開きます。",
                                                 "Click to open the download page." } },
            { "hud.tipLimitDocs",   new[] { "上限: 公式ドキュメントから取得", "Context window from the official docs" } },
            { "hud.tipLimitConfig", new[] { "上限: 設定で指定",     "Context window set in Settings" } },
            { "hud.tipSampled",   new[] { "Claude Desktop が {0}に記録", "Claude Desktop recorded this {0}" } },
            { "hud.tipReset",     new[] { "次回リセット {0} ごろ",         "Next reset about {0}" } },
            { "hud.tipReference", new[] { "Claude Desktop が起動していないため、最後に記録された値を表示しています",
                                          "Claude Desktop isn't running, so this is the last value it recorded" } },

            { "hud.loading",      new[] { "読み込み中…",              "Loading…" } },
            { "hud.rateUnavail",  new[] { "レート枠: 取得できません", "Rate limits: unavailable" } },
            { "hud.noSessions",   new[] { "セッションなし",            "No sessions" } },
            { "hud.unknownLimit", new[] { "上限不明",                  "window?" } },
            { "hud.untitled",     new[] { "（無題）",                    "(untitled)" } },
            // --verify-weekly でだけ使う（HUD には出さない）。
            { "hud.estimating",   new[] { "{0} ごろ（推定中）",         "around {0} (estimating)" } },

            // 通知
            { "notify.compactSoon",   new[] { "まもなく自動圧縮されます",             "Auto-compaction is close" } },
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
            { "config.unreadable", new[] { "設定ファイルを読み込めませんでした: {0}",
                                           "Could not read the config file: {0}" } },
            { "config.broken",     new[] { "設定ファイルが壊れているため、既定値で起動しました。元のファイルは .bak として保存してあります。",
                                           "The config file was invalid, so defaults were used. The original was kept as .bak." } },
            { "config.brokenKept", new[] { "設定ファイルを読み込めなかったため、前回の設定で動作しています。ファイルを修正すると自動的に反映されます。",
                                           "The config file could not be read, so the previous settings stay in effect. Fix the file and it will be picked up." } },
            { "config.openFailed", new[] { "設定ファイルを開けませんでした: {0}",
                                           "Could not open the config file: {0}" } },

            { "about.body",   new[] { "バージョン: {4}\n稼働時間: {0:0} 時間 {1:00} 分\n更新間隔: {2} 秒\n設定ファイル: {3}",
                                      "Version: {4}\nRunning for: {0:0} h {1:00} min\nRefresh interval: {2} s\nConfig file: {3}" } },
            { "about.lastError", new[] { "\n直近のエラー: {0}（{1}）",
                                         "\nLast error: {0} ({1})" } },
            { "app.crashed",   new[] { "ctxtray は予期せず停止しました。\n\n{0}",
                                       "ctxtray stopped unexpectedly.\n\n{0}" } },
            { "app.error",     new[] { "表示の更新中にエラーが発生しました。ctxtray は引き続き動作します。\n{0}",
                                       "Something went wrong while updating. ctxtray keeps running.\n{0}" } },
            { "autostart.description", new[] { "Claude のコンテキスト残量とレート枠を常時表示する",
                                               "Always-visible Claude context and rate-limit readout" } },

            // 初めての起動（設定ファイルが無かったとき）に 1 回だけ出す案内。
            // 通知は長いと末尾が切れる（実機で確認）。題名が「ctxtray」なので、本文は操作だけにする。
            { "app.welcome",   new[] { "{0} かトレイアイコンでパネルを表示できます。\n右クリックで設定を開けます。",
                                       "Press {0} or click the tray icon to show the panel. Right-click it for Settings." } },

            // クリックを後ろに通す設定を切り替えたとき、パネルの上に数秒だけ出す（2 行に収める）。
            // オンにするとパネルの右クリックが効かなくなるので、戻し方を必ず添える。
            { "hud.clickThroughOnKey",  new[] { "クリックを透過します。\n{0} またはトレイメニューで解除できます。",
                                                "Clicks now pass through. Press {0} or use the tray menu to undo." } },
            { "hud.clickThroughOnMenu", new[] { "クリックを透過します。\nトレイメニューで解除できます。",
                                                "Clicks now pass through. Use the tray menu to undo." } },
            { "hud.clickThroughOff",    new[] { "クリックの透過を解除しました。",
                                                "The panel takes clicks again." } },

            // ホットキーが登録できなかったとき（押しても何も起きない理由を伝える）。
            { "hotkey.taken",  new[] { "ショートカットキー {0} は他のアプリが使用中のため使用できません。設定で別のキーを選択してください。",
                                       "The {0} shortcut is in use by another app, so it does nothing. Choose a different shortcut in Settings." } },
            { "hotkey.clickThroughTaken", new[] { "「クリックを透過」のショートカットキー {0} は他のアプリが使用中のため使用できません。設定で別のキーを選択してください。",
                                                  "The {0} click-through shortcut is in use by another app, so it does nothing. Choose a different shortcut in Settings." } },
            { "hotkey.clickThroughSame",  new[] { "「クリックを透過」のショートカットキー {0} は「パネルを表示」のキーと同じため使用できません。設定で別のキーを選択してください。",
                                                  "The {0} click-through shortcut is the same as the \"Show panel\" shortcut, so it does nothing. Choose a different shortcut in Settings." } },
            { "hotkey.unparsable", new[] { "設定ファイルのショートカットキー「{0}」を認識できません。設定で選択し直してください。",
                                           "The shortcut \"{0}\" in the config file could not be read. Choose one in Settings." } },

            // 設定ダイアログ
            { "set.title",         new[] { "ctxtray の設定",      "ctxtray settings" } },
            { "set.tabHud",        new[] { "パネル",                  "Panel" } },
            { "set.tabTray",       new[] { "トレイアイコン",        "Tray icon" } },
            { "set.tabThresholds", new[] { "しきい値と通知",        "Thresholds and notifications" } },
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
            { "set.srcBuiltIn",    new[] { "組み込み",             "Built-in" } },
            { "set.srcDocs",       new[] { "公式ドキュメント（{0}）", "Official docs ({0})" } },
            { "set.srcConfig",     new[] { "手動設定",                 "Set by you" } },
            { "set.srcUnknown",    new[] { "不明",                 "Unknown" } },
            { "set.builtInShow",   new[] { "▸ 組み込みのモデル（{0} 件）を表示", "▸ Show built-in models ({0})" } },
            { "set.builtInHide",   new[] { "▾ 組み込みのモデル（{0} 件）を非表示", "▾ Hide built-in models ({0})" } },
            { "set.limitUnset",    new[] { "未設定",               "Not set" } },
            { "set.modelListHint", new[] { "「不明」のモデルは上限を選択すると % が表示されます。その他の値は設定ファイルの modelLimits で変更できます。",
                                           "Choose a context window for an \"Unknown\" model to see a percentage. Other values go in modelLimits in the config file." } },

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
            { "set.showTokens",    new[] { "トークン数（例: 284k/1M）", "Token count (e.g. 284k/1M)" } },
            // モデルとエフォートは 1 つのチェックボックスで両方を切り替える（2026-09-27、利用者の提案）。
            // 片方だけにしたいときは設定ファイルの showModel／showEffort で分けられる。
            { "set.showModel",     new[] { "モデルとエフォート（例: Opus 5.5 · high）",
                                           "Model and effort (e.g. Opus 5.5 · high)" } },
            // モデルとエフォート・トークン数の両方に効く（2026-09-29、利用者の決定）。以前はモデルとエフォートだけの
            // 「表示位置: セッション名の右／下」だった。2 段目のどこに何が出るかは見れば分かるので選択肢に書かない。
            { "set.rowLayout",     new[] { "並べ方",                 "Layout" } },
            { "set.layoutOneLine", new[] { "1 段（パネルの幅が広がる）", "One line (wider panel)" } },
            { "set.layoutTwoLine", new[] { "2 段（行が高くなる）",     "Two lines (taller rows)" } },
            // 対象が 5時間枠だけであることを名前で伝える。項目名の列が最長の項目名に合わせて広がるようになったので、
            // 「リセット時刻」＋補足から戻した（日本語の入力欄は 54px 右へ寄る。2026-09-28、利用者の決定）。
            { "set.showResets",    new[] { "5時間枠のリセット時刻", "5-hour reset time" } },
            { "set.always",        new[] { "常に表示",             "Always" } },
            { "set.autoNear",      new[] { "リセットの {0} 分前から", "From {0} min before reset" } },
            { "set.never",         new[] { "表示しない",           "Never" } },

            { "set.secHudLook",    new[] { "外観",               "Look" } },
            { "set.textSize",      new[] { "文字サイズ",          "Text size" } },
            { "set.sizeXSmall",    new[] { "極小",                 "Extra small" } },
            { "set.sizeSmall",     new[] { "小",                   "Small" } },
            { "set.sizeNormal",    new[] { "標準",                 "Normal" } },
            { "set.sizeLarge",     new[] { "大",                   "Large" } },
            { "set.sizeXLarge",    new[] { "特大",                 "Extra large" } },
            // 「名前」だけでは何の名前か分からない（2026-09-27、利用者の指摘）。単位は倍率 100%・標準の文字サイズでのピクセル。
            { "set.nameWidth",     new[] { "セッション名の幅",     "Session name width" } },
            { "set.nameWidthUnit", new[] { "px（既定値: {0}）",      "px (default {0})" } },
            { "set.opacity",       new[] { "不透明度",             "Opacity" } },
            { "set.clickThrough",  new[] { "クリックを後ろのウィンドウへ透過する",
                                           "Pass clicks through to windows behind" } },
            // 透過中は窓がマウスを一切受け取らないので、ドラッグだけでなく行の詳細も右クリックも死ぬ。
            // 「ドラッグできない」としか書いていなかった頃は、残りの 2 つが壊れたように見えた（2026-09-20）。
            // 戻し方はオンにしたときパネルに出る（hud.clickThroughOnKey など）ので、ここには書かない。
            { "set.clickThroughHint", new[] { "オンの間は、パネルの移動、詳細の表示、右クリックができません。",
                                              "While on, the panel can't be dragged, hovered, or right-clicked." } },

            // 起動の節（全般タブの先頭）。自動起動は以前からトレイのメニューにだけあり、設定画面で見つからなかった（2026-09-26）。
            { "set.secStartup",    new[] { "起動",                 "Startup" } },
            { "set.autoStart",     new[] { "Windows へのサインイン時に ctxtray を起動する",
                                           "Start ctxtray when you sign in to Windows" } },
            { "set.autoStartHint", new[] { "スタートアップフォルダーにショートカットを作成します。",
                                           "Puts a shortcut in your Startup folder." } },

            { "set.secHudControl", new[] { "パネルの操作",           "Panel controls" } },
            // いまの表示状態（保存しない）。クリック透過と同じ並びにするために置いた（2026-09-26、利用者の決定）。
            // トレイメニューと同じ言葉にし、チェックでいまの状態を示す。すぐ下の「起動時にパネルを表示する」とは
            // 「起動時に」の有無で見分ける（「いまパネルを表示する」「Show the HUD now」は不自然だった、2026-09-27、利用者の決定）。
            { "set.showHud",       new[] { "パネルを表示",           "Show panel" } },
            // チェックボックスの直下に字下げして置くので、何の切り替えかは書かない（2 つの欄で同じ言葉）。
            { "set.toggleKey",     new[] { "切り替えキー",       "Shortcut" } },
            { "set.keyNone",       new[] { "なし",                 "None" } },
            // キーの欄をクリックしたときに欄の中へ淡く出す押し方の案内（説明文の代わり。2026-09-27）。
            // 「押しても欄が変わらないキーは、ほかのアプリが先に使っている」（2026-09-18 実機で確認）は README に書く。
            // 中黒（・）だと「全部」なのか「どれか」なのか分かりにくいので、英語と同じく半角スラッシュで書く（2026-09-27、利用者の指摘）。
            { "set.keyPrompt",     new[] { "Ctrl/Alt/Shift + キー", "Ctrl/Alt/Shift + key" } },
            { "set.clickThroughKeySame", new[] { "「パネルを表示」のキーと同じです。",
                                                 "Same as the \"Show panel\" shortcut." } },
            { "set.hotkeyTaken",   new[] { "他のアプリが使用中のため使用できません。",
                                           "Another app is using this shortcut, so it won't work." } },
            { "set.showAtStartup", new[] { "起動時にパネルを表示する", "Show panel when ctxtray starts" } },
            { "set.hideFullscreen", new[] { "全画面表示のアプリを使用中は非表示にする",
                                            "Hide while a full-screen app is in use" } },
            { "set.hideFullscreenHint", new[] { "Claude Desktop が前面にあるときは非表示にしません。",
                                                "Not while Claude Desktop is in front." } },
            { "set.hideFromCapture", new[] { "画面共有とスクリーンショットでは非表示にする",
                                             "Hide from screen sharing and screenshots" } },
            { "set.hideFromCaptureHint", new[] { "自分の画面には表示されます。",
                                                 "Still shown on your own screen." } },
            { "set.position",      new[] { "位置",                 "Position" } },
            { "set.resetPosition", new[] { "右下に戻す",        "Move back to bottom-right" } },

            // 設定ダイアログ: トレイアイコン
            { "set.secTrayCount",  new[] { "アイコンの数",          "Number of icons" } },
            { "set.modeMulti",     new[] { "値ごとに分ける（ラベルとバー、最大 3 個）", "One icon per value (label and bar, up to 3)" } },
            { "set.modeSingle",    new[] { "1 個にまとめる（横のバー）", "One combined icon (horizontal bars)" } },

            { "set.secTrayValues", new[] { "表示する値",            "Values to show" } },
            { "set.valContext",    new[] { "コンテキスト（実行中で自動圧縮に最も近いセッション）",
                                           "Context (running session closest to auto-compaction)" } },
            { "set.valFiveHour",   new[] { "5時間枠",              "5-hour limit" } },
            { "set.valWeekly",     new[] { "週間枠",               "Weekly limit" } },

            // ラジオボタンの文は折り返せないので、目印と値の対応は補足の行に書く。
            { "set.secTrayLabel",  new[] { "値ごとに分けるときのラベル", "Label on each icon" } },
            { "set.labelLetters",  new[] { "文字（C・5h・W）",      "Letters (C, 5h, W)" } },
            { "set.labelGlyphs",   new[] { "記号（吹き出し・時計・カレンダー）", "Symbols (speech bubble, clock, calendar)" } },
            { "set.labelPercent",  new[] { "数値（現在の %）",      "Numbers (current %)" } },
            { "set.labelHint",     new[] { "左から順にコンテキスト、5時間枠、週間枠です。",
                                           "From the left: context, 5-hour, weekly." } },

            { "set.trayOverflowHint", new[] { "「^」の中に隠れたアイコンは、タスクバーへドラッグすると常に表示できます。",
                                              "Drag an icon out of the ^ overflow onto the taskbar to keep it visible." } },

            { "set.secTrayPreview",new[] { "プレビュー",                 "Preview" } },
            { "set.previewHint",   new[] { "左: 実寸、右: 2 倍",  "Actual size, then 2x." } },

            // 設定ダイアログ: しきい値と通知
            { "set.secThreshold",  new[] { "色が変わる使用率",        "When colours change" } },
            { "set.warn",          new[] { "注意",                 "Warn" } },
            { "set.danger",        new[] { "危険",                 "Danger" } },
            { "set.ctxThreshold",  new[] { "コンテキスト",          "Context" } },
            // 自動圧縮の位置以上の値は、その色になる前に圧縮されるので起きない。値は勝手に直さず知らせる。
            // 位置はモデルで違う（200K のモデルは上限）ので、いちばん早く圧縮する 1M のモデルの % で書く。
            { "set.ctxOverCompact",new[] { "自動圧縮（1M のモデルで {0}%）以上の値では、色の変化も通知も起きません。",
                                           "At or above auto-compaction ({0}% on 1M models), this never shows." } },
            { "set.fhThreshold",   new[] { "5時間枠",              "5-hour limit" } },
            { "set.wkThreshold",   new[] { "週間枠",               "Weekly limit" } },
            // 判定は危険から先に見るので、注意を危険より大きくすると注意が一度も起きない。
            // 値は勝手に直さず、その場で知らせる（2026-09-20）。
            { "set.thresholdOrder",new[] { "「注意」は「危険」より小さい値にしてください。大きいと「注意」になりません。",
                                           "Keep warn below danger, or warn never shows." } },

            // 色だけで示すと、赤と緑の区別が付きにくい人には通常と危険が見分けられない。
            // 通知領域のアイコンには入れない（小さすぎて、見えるようにするとうるさくなる）。
            { "set.levelMarks",    new[] { "注意と危険をバーの縞模様でも示す",
                                           "Also mark warn and danger with stripes" } },
            { "set.levelMarksHint",new[] { "注意は粗い縞、危険は細かい縞。色が見分けにくいときに。",
                                           "Wide stripes for warn, tight for danger. Helps when colours are hard to tell apart." } },

            // 値ごとに注意と危険を分けて選ぶ（2026-10-01、利用者の決定）。項目は「□ ■ 注意」の形で、文字は set.warn / set.danger。
            // しきい値は上の 1 組だけ（通知用に別の数字を持たない）ことを補足で言う。
            { "set.secNotify",     new[] { "通知",                 "Notifications" } },
            { "set.notifySameHint",new[] { "上の使用率を超えたときに通知します。", "Sent when a value crosses the thresholds above." } },
            { "set.notifyContext", new[] { "コンテキスト",          "Context" } },
            { "set.notifyFh",      new[] { "5時間枠",              "5-hour limit" } },
            { "set.notifyWk",      new[] { "週間枠",               "Weekly limit" } },
            // 英語はラベルが長く 2〜3 行に折り返したので、数値の前に文を分けて短くした。
            // 「ポイント下がったら」では、ポイントの意味も何が下がるのかも伝わらなかった（2026-09-27、利用者の指摘）。
            // 単位はしきい値の欄と同じ % にし、何が起きるかは欄の値を入れた例で補足に書く
            // （hysteresisHint の {0} = コンテキストの既定の注意 75 − 欄の値。SettingsForm）。
            // 例だけでは何のための設定か分からなかったので、1 行目に目的、続けて値が下がる具体的な場面を書く
            // （2026-09-28、利用者の指摘）。場面は自動圧縮だけ。5時間枠・週間枠はリセットで必ず 0% に戻り、
            // この値に関係なく再び通知されるので書かない（同日、利用者の指摘）。しきい値の近くで小刻みに上下する場面は
            // 確認できていないので書かない。境目は Levels.ForPct と同じ「未満」「以上」。
            { "set.hysteresis",    new[] { "再通知",  "Notify again" } },
            { "set.hysteresisPre", new[] { "しきい値より",         "after falling" } },
            { "set.hysteresisUnit",new[] { "% 下がってから",       "% below the threshold" } },
            // 補足の幅（Hint の MaximumSize）で語の途中から折り返さないよう、場面ごとに改行して 1 行ずつに収める。
            { "set.hysteresisHint",new[] { "値が下がって再び上がったとき、もう一度通知するかを決めます。\n例（コンテキストのしきい値 75%）:\n・自動圧縮で {0}% 未満に下がった → 75% 以上で再び通知\n・{0}% 以上までしか下がらない → 通知しない",
                                           "Whether to notify again after a value drops and rises again.\nExample (context threshold 75%):\n• Below {0}% after auto-compaction → notified at 75%\n• Stays at {0}% or above → not notified" } },
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
            // 右に「97%（Claude Code の既定値）」が並ぶので、名前は「自動圧縮」だけで足りる。
            // 項目名の列に 1 行で収まらなかった「自動圧縮が起きる使用率」から短くした（2026-09-27、利用者の決定）。
            { "set.compactPoint",  new[] { "自動圧縮",             "Auto-compaction" } },
            // /autocompact と同じ書き方で入力する（Core/CompactWindow）。2026-10-01、利用者と決めた形。
            // ctxtray で変えても Claude Code は変わらないので、誤解されないよう補足の 1 文目で言う。
            // 「自動」は ctxtray が /autocompact を読み取って合わせるように見えるので使わない（読み取らない）。
            { "set.compactAuto",   new[] { "Claude Code の既定値", "Claude Code default" } },
            { "set.compactSet",    new[] { "指定する",             "Custom" } },
            { "set.compactPrompt", new[] { "例: 500k",             "e.g. 500k" } },
            { "set.compact1M",     new[] { "1M のモデルで {0}（{1}%）", "{0} ({1}%) on 1M models" } },
            { "set.compactInvalid",new[] { "「{0}」は使えません。100k〜1M の値を入力してください。",
                                           "\"{0}\" can't be used. Enter a value from 100k to 1M." } },
            { "set.compactHint",   new[] { "ここを変えても Claude Code の動作は変わりません。\n/autocompact で変えたときだけ、同じ値を指定してください。",
                                           "Changing this doesn't change Claude Code.\nSet the same value only if you changed it with /autocompact." } },
            { "set.configFile",    new[] { "設定ファイル",          "Config file" } },
            { "set.open",          new[] { "開く",                 "Open" } },

            // 設定ダイアログ: 全般 > 更新（2026-09-29 追加。既定オフ）
            { "set.secUpdates",    new[] { "更新",                 "Updates" } },
            { "set.checkUpdates",  new[] { "新しいバージョンを確認する", "Check for new versions" } },
            // 通信の中身は削らない（set.fetchDocsHint と同じ考え方）。
            // 1 行に収まらず「アカウ／ント」と語の途中で折り返したので、文の区切りで改行する。
            { "set.checkUpdatesHint", new[] { "1 日 1 回、GitHub で最新のバージョン番号を確認します。\n会話やアカウントの情報は送信しません。",
                                              "Once a day, checks GitHub for the latest version number. Nothing about your conversations or account is sent." } },
            { "set.updChecking",   new[] { "確認中…",              "Checking…" } },
            { "set.updUpToDate",   new[] { "最新のバージョンです（{0}）", "Up to date ({0})" } },
            { "set.updAvailable",  new[] { "バージョン {0} があります", "Version {0} is available" } },
            { "set.updFailed",     new[] { "確認できませんでした",   "Couldn't check" } },
            { "set.updOpen",       new[] { "ダウンロードページを開く", "Open download page" } },

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
                                        "lower bound: activity since the sample" } },
            { "cli.reference",  new[] { "参考値: Claude Desktop が起動していないか、長時間更新なし",
                                        "for reference: Claude Desktop not running, or no recent sample" } },
            { "cli.fiveHour",   new[] { "   5時間枠 ", "   5-hour  " } },
            { "cli.weekly",     new[] { "   週間枠  ", "   Weekly  " } },
            { "cli.sampledAt",  new[] { "   記録時刻: {0}（{1}）", "   Sampled : {0} ({1})" } },
            { "cli.nextReset",  new[] { "   次回リセット（5時間枠）: {0}", "   Next 5-hour reset: {0}" } },
            { "cli.rateUnavail",new[] { "  レート枠: 取得できません", "  Rate limits: unavailable" } },
            { "cli.sessions",   new[] { " セッション（* = 最近使用 / ext = Claude Desktop 以外 / ● = 実行中）",
                                        " Sessions (* = most recent, ext = not Claude Desktop, ● = running)" } },
            { "cli.none",       new[] { "   該当なし", "   none" } },
            { "cli.unknownModel", new[] { "上限が不明なモデル: {0}（{1:N0} トークン。設定の「モデル」タブで選択できます）",
                                          "unknown context window: {0}  ({1:N0} tokens; set it in Settings > Models)" } },
            { "cli.noUsage",    new[] { "使用量の記録なし", "no usage recorded" } },
            { "cli.diag",       new[] { "  ! 読み込み時の問題: {0}",
                                        "  ! Problems while reading: {0}" } },
            { "cli.unknownArg", new[] { "不明な引数: {0}", "Unknown argument: {0}" } },
            { "cli.secondsAgo", new[] { "{0} 秒前", "{0} s ago" } },
            { "cli.minutesAgo", new[] { "{0:N0} 分前", "{0:N0} min ago" } },
            { "cli.hoursAgo",   new[] { "{0:N0} 時間前", "{0:N0} h ago" } },

            // --verify-weekly
            { "vw.title",       new[] { "週間枠リセットの推定", "Weekly reset estimate" } },
            { "vw.samples",     new[] { "記録数: {0}", "Samples: {0}" } },
            { "vw.observed",    new[] { "観測できたリセット: {0} 回", "Resets observed: {0}" } },
            { "vw.interval",    new[] { "  sd {0,3} -> {1,-3}  {2}  〜  {3}   幅 {4:0.0} 時間",
                                        "  sd {0,3} -> {1,-3}  {2}  ..  {3}   width {4:0.0} h" } },
            { "vw.noObs",       new[] { "  → 有効な観測がまだありません。時刻は表示しません。",
                                        "  -> No usable observation yet; no time is shown." } },
            { "vw.width",       new[] { "  7 日周期で畳み込んだ交差の幅: {0:0.0} 時間",
                                        "  Intersection width after folding over 7 days: {0:0.0} h" } },
            { "vw.weekday",     new[] { "  曜日: {0}", "  Weekday: {0}" } },
            { "vw.next",        new[] { "  次回リセット: {0}", "  Next reset: {0}" } },
            { "vw.notYet",      new[] { "まだ絞り込めていないため表示しません",
                                        "not narrow enough yet, so not shown" } },
            { "vw.hud",         new[] { "  推定の表記: {0}（パネルには表示しない）", "  Estimate label: {0} (not shown in the panel)" } },
            { "vw.nothing",     new[] { "（表示なし）", "(nothing)" } },

            // 診断（読み取りに失敗した理由）。
            // バグ報告に貼られる文字列なので、必ず利用者の言語で出す。
            { "diag.noDataRoot",     new[] { "Claude Desktop のデータフォルダーが見つかりません",
                                             "Claude Desktop data folder not found" } },
            { "diag.noUsageFileYet", new[] { "plan-usage-history.json が見つかりません（フォルダーはあります）",
                                             "plan-usage-history.json not found (folder exists)" } },
            { "diag.noConfigDir",    new[] { "Claude Code の設定フォルダー（~/.claude）が見つかりません",
                                             "Claude Code config folder (~/.claude) not found" } },
            { "diag.noUsageFile",    new[] { "plan-usage-history.json がありません",
                                             "plan-usage-history.json is missing" } },
            { "diag.usageUnreadable",new[] { "plan-usage-history.json を読み込めません",
                                             "can't read plan-usage-history.json" } },
            { "diag.usageUnparsable",new[] { "plan-usage-history.json を解析できません",
                                             "can't parse plan-usage-history.json" } },
            { "diag.noSamples",      new[] { "レート枠の記録が空です",
                                             "rate-limit history is empty" } },
            { "diag.noTabsDir",      new[] { "claude-code-sessions がありません",
                                             "claude-code-sessions is missing" } },
            { "diag.tabsUnlistable", new[] { "claude-code-sessions の一覧を取得できません",
                                             "can't list claude-code-sessions" } },
            { "diag.noTabs",         new[] { "開いているタブが登録されていません",
                                             "no open tabs registered" } },
            { "diag.tabsFailed",     new[] { "タブの登録ファイル {0} 件を読み込めません",
                                             "{0} tab file(s) could not be read" } },
            { "diag.noProcDir",      new[] { "~/.claude/sessions がありません",
                                             "~/.claude/sessions is missing" } },
            { "diag.procUnlistable", new[] { "~/.claude/sessions の一覧を取得できません",
                                             "can't list ~/.claude/sessions" } },

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
