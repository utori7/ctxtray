using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CtxTray.Collect;

namespace CtxTray.Config
{
    /// <summary>
    /// 設定。%LOCALAPPDATA%\ctxtray\config.json
    ///
    /// 閾値の定義はここ 1 か所だけに置く。
    /// HUD の行の色・トレイアイコンの色・通知の発火は、すべて同じ値から導く。
    /// 色と通知で別々の数字を持つと、片方だけ直して食い違う。
    ///
    /// 保存すると再起動なしで反映する（ConfigWatcher）。
    /// </summary>
    internal sealed class AppConfig
    {
        // --- 閾値 -----------------------------------------------------------
        //
        // コンテキストだけ絶対 % ではなく「圧縮点までの到達率」で持つ。
        // 圧縮点 (CompactThreshold) は /autocompact などで変わりうる値なので、
        // 「85% で赤」と絶対値で固定すると、圧縮点を 80% に下げたときに
        // 「圧縮された後に赤くなる」ことになる。到達率なら圧縮点が動いても意味が保たれる。
        public double ContextWarn = 0.75;
        public double ContextDanger = 0.90;

        // レート枠は 100% が確定した上限なので絶対 % でよい。
        public int FiveHourWarn = 80;
        public int FiveHourDanger = 95;
        public int WeeklyWarn = 80;
        public int WeeklyDanger = 95;

        // --- 通知 -----------------------------------------------------------
        public bool NotifyContext = true;
        public bool NotifyFiveHour = true;
        public bool NotifyWeekly = true;
        public int NotifyHysteresisPts = 10;
        public int NotifyMinRepeatMinutes = 30;

        // --- 圧縮点 ---------------------------------------------------------
        //
        // auto-compact の発火点（ウィンドウに対する割合）。Claude Code の公式ドキュメントの既定値で、
        // 1M のモデルは約 967K トークンで圧縮する（Desktop の表示も 97%）。
        // https://code.claude.com/docs/en/model-config#default-auto-compact-thresholds
        // 上限（100%）で圧縮するモデル（200K など）では、少し早めに 100% に達する（安全側）。
        // /autocompact で変えた場合は、利用者が設定ファイルで合わせる。
        public const double DefaultCompactThreshold = 0.967;

        // v0.1.1 までの仮の値。調査中に圧縮を観測できなかったので置いていた。
        private const double OldPlaceholderCompactThreshold = 0.92;

        public double CompactThreshold = DefaultCompactThreshold;

        // --- 動作 -----------------------------------------------------------
        public int PollSeconds = 5;
        public string Hotkey = "Ctrl+Alt+C";
        public string Language = "auto";       // auto / ja / en

        // --- 表示 -----------------------------------------------------------
        //
        // auto は Windows のライト/ダーク設定に追従する。
        public string Theme = "auto";           // auto / light / dark

        // --- トレイアイコン ---------------------------------------------------
        //
        // single = アイコン 1 個に、選んだ値を横のバーで並べる（数字なし）
        // multi  = 値ごとにアイコンを分け、目印（英字か絵記号）とバーで描く
        //
        // Windows 11 は新しいアイコンを既定で「隠れているインジケーター」に入れる。
        // タスクバーへ出す手間が 1 回で済むので、既定は single（利用者の判断、2026-09-16）。
        public string TrayMode = "single";

        // multi のときの目印。letters = C / 5h / W、glyphs = 吹き出し / 時計 / カレンダー。
        // 既定は 16px でも読みやすい letters。
        public string TrayLabel = "letters";

        // 表示する値（両モード共通）。部分集合でよい。並びは描画時に正式な順へ揃える。
        // 値: context（動いている中で最も圧縮に近いセッション）/ fiveHour / weekly
        public List<string> TrayValues = new List<string> { "context", "fiveHour", "weekly" };

        public bool TrayMultiMode
        {
            get { return string.Equals(TrayMode, "multi", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// 値の正式な並び順。トレイアイコンのスロット順（uID）、バーの上からの順でもある。
        /// HUD・設定画面・ツールチップもこの順に揃える（Claude Desktop の Claude Code の表示に合わせた、2026-09-17）。
        /// </summary>
        public static readonly string[] AllTrayValues = { "context", "fiveHour", "weekly" };

        /// <summary>
        /// 選ばれた値を正式な並び順で返す。空なら context だけ
        /// （アイコンに何も描かれない状態を作らないため）。
        /// </summary>
        public List<string> OrderedTrayValues()
        {
            var list = new List<string>();
            foreach (var known in AllTrayValues)
            {
                foreach (var picked in TrayValues)
                {
                    if (string.Equals(known, picked, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(known);
                        break;
                    }
                }
            }

            if (list.Count == 0) list.Add(AllTrayValues[0]);
            return list;
        }

        // --- HUD ------------------------------------------------------------
        public double Opacity = 0.90;
        public bool ClickThrough = false;
        public string ShowResets = "auto";          // always / auto / never（5時間枠のリセット時刻）
        public int ResetLeadFiveHourMinutes = 30;

        // 出す節と列。レート枠・セッションの両方を外したときは HUD 側でセッションを出す。
        public bool HudShowRateLimits = true;
        public bool HudShowSessions = true;
        public bool HudShowBar = true;
        public bool HudShowTokens = false;

        // しばらく使っていないセッションを隠す。何週間も触っていないタブが並ぶと
        // 行が増えるだけで役に立たないため（利用者の指摘、2026-09-15）。
        // 隠したものはトレイと通知の対象からも外す（Core/SessionFilter.cs）。
        public bool HideIdleSessions = true;
        public int IdleHours = 24;

        // 止まっているセッション（HUD で淡く出る行）を隠す。既定はオフ＝これまでどおり出す。
        // トレイ・通知は元から動いている行しか見ないので、これは HUD の見た目だけの設定。
        public bool HideStoppedSessions = false;

        // 全画面のアプリ（動画・発表・ゲーム）を使っている間は HUD を隠す。
        // 最前面の窓なので、隠さないと全画面の上に残る。
        // 前面が Claude Desktop のときは隠さない（TrayApp）。
        public bool HideWhenFullscreen = true;

        // 文字の大きさ。寸法もこの倍率で伸びる。
        public string HudTextSize = "normal";       // small / normal / large / xlarge
        public static readonly string[] TextSizes = { "small", "normal", "large", "xlarge" };

        // 標準の文字サイズ・96 DPI での幅。
        public const int DefaultHudWidth = 352;
        public const int MinHudWidth = 240;
        public const int MaxHudWidth = 1200;
        public int HudWidth = DefaultHudWidth;

        public bool HudShowAtStartup = true;

        // HUD の位置。-1 は「未設定（右下に置く）」。
        public int HudX = -1;
        public int HudY = -1;

        /// <summary>
        /// 位置の基準。true なら HudY は HUD の「下端」の座標。
        ///
        /// 画面の下半分に置いた HUD は、セッションが増えたときに上へ伸ばす。
        /// 上端を固定していた頃は、行が増えるとタスクバーの裏や画面の外へ出ていた（2026-09-18）。
        /// このキーが無い旧設定は上端基準として読む。
        /// </summary>
        public bool HudAnchorBottom = false;

        /// <summary>
        /// 設定ファイルが無かった（初めての起動）。ファイルには保存しない。
        /// 初回だけ操作の案内を出すために見る（Ui/TrayApp.cs）。
        /// </summary>
        public bool WasMissing;

        public double HudTextScale
        {
            get
            {
                switch ((HudTextSize ?? "").ToLowerInvariant())
                {
                    case "small": return 0.9;
                    case "large": return 1.15;
                    case "xlarge": return 1.3;
                    default: return 1.0;
                }
            }
        }

        // --- セッション -----------------------------------------------------
        public bool ExternalSessionsEnabled = true;
        public int ExternalSessionsMax = 20;

        /// <summary>組み込みのモデル分母表への追加・上書き。</summary>
        public Dictionary<string, int> ModelLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // --------------------------------------------------------------------

        /// <summary>
        /// 複製を作る（一覧と辞書も別物にする）。
        ///
        /// 設定画面やトレイのメニューは、この複製を書き換えて保存し、実行中の設定は再読込で差し替える。
        /// 実行中の設定オブジェクトを直接書き換えていた頃は、次のことが起きた（2026-09-19）。
        ///   - 「変わったか」を前後の設定で比べると常に同じになり、クリック透過が反映されなかった
        ///   - 保存に失敗しても、書き換えた値の一部だけが画面に反映された
        ///   - 設定画面を開いている間に別の場所で変えた値（メニューの透過、パネルの位置）を、OK で古い値に戻した
        /// パネルの位置だけは実行中の状態そのものなので、HudForm が実行中の設定を直接書き換える。
        /// </summary>
        public AppConfig Clone()
        {
            var copy = (AppConfig)MemberwiseClone();
            copy.TrayValues = new List<string>(TrayValues);
            copy.ModelLimits = new Dictionary<string, int>(ModelLimits, StringComparer.OrdinalIgnoreCase);
            copy.WasMissing = false;
            return copy;
        }

        public static string Dir
        {
            get
            {
                // LOCALAPPDATA を優先する。Paths.cs のデータフォルダ解決と揃えるため。
                // GetFolderPath は環境変数を見ないので、片方だけ使うと
                // 「同じ実行なのに参照する場所が違う」状態になる。
                var root = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrEmpty(root))
                    root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                return Path.Combine(root, "ctxtray");
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

        /// <summary>
        /// 読み込む。壊れていたら既定値を返し、元のファイルは .bak に退避する。
        /// 黙って上書きしない（利用者が原因を見られるように）。
        /// </summary>
        public static AppConfig Load(out string problem)
        {
            bool failed;
            return Load(out problem, out failed);
        }

        /// <param name="failed">
        /// ファイルはあるのに読めなかった・解析できなかったとき true。
        /// 実行中の再読込では、既定値に切り替えず直前の設定を使い続けるために見る。
        /// </param>
        public static AppConfig Load(out string problem, out bool failed)
        {
            problem = null;
            failed = false;
            var config = new AppConfig();

            // エディタが保存している最中（中身を空にしてから書く等）に読むと、
            // 読めない・途中までしか無い、が起きる。少し待って 1 回だけ読み直す。
            Dictionary<string, object> o = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0) System.Threading.Thread.Sleep(250);

                string text;
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        config.WasMissing = true;
                        return config;
                    }
                    using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                                                   FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                        text = sr.ReadToEnd();
                }
                catch (Exception ex)
                {
                    if (attempt == 0) continue;
                    problem = Strings.Format("config.unreadable", ex.Message);
                    failed = true;
                    return config;
                }

                // README の設定例はコメント付きなので、コメントは取り除いてから読む。
                o = Json.ParseObject(Json.StripComments(text));
                if (o != null) break;
            }

            if (o == null)
            {
                problem = Strings.Get("config.broken");
                failed = true;
                TryBackupBroken();
                return config;
            }

            config.ApplyFrom(o);
            return config;
        }

        private static void TryBackupBroken()
        {
            try
            {
                var bak = FilePath + ".bak";
                File.Copy(FilePath, bak, true);
            }
            catch { }
        }

        private void ApplyFrom(Dictionary<string, object> o)
        {
            var thresholds = Json.Obj(o, "thresholds");
            if (thresholds != null)
            {
                var ctx = Json.Obj(thresholds, "context");
                if (ctx != null)
                {
                    ContextWarn = Dbl(ctx, "warn", ContextWarn);
                    ContextDanger = Dbl(ctx, "danger", ContextDanger);
                }
                var fh = Json.Obj(thresholds, "fiveHour");
                if (fh != null)
                {
                    FiveHourWarn = (int)Json.Long(fh, "warn", FiveHourWarn);
                    FiveHourDanger = (int)Json.Long(fh, "danger", FiveHourDanger);
                }
                var wk = Json.Obj(thresholds, "weekly");
                if (wk != null)
                {
                    WeeklyWarn = (int)Json.Long(wk, "warn", WeeklyWarn);
                    WeeklyDanger = (int)Json.Long(wk, "danger", WeeklyDanger);
                }
            }

            var notify = Json.Obj(o, "notify");
            if (notify != null)
            {
                var enabled = Json.Obj(notify, "enabled");
                if (enabled != null)
                {
                    NotifyContext = Json.Bool(enabled, "context", NotifyContext);
                    NotifyFiveHour = Json.Bool(enabled, "fiveHour", NotifyFiveHour);
                    NotifyWeekly = Json.Bool(enabled, "weekly", NotifyWeekly);
                }
                NotifyHysteresisPts = (int)Json.Long(notify, "hysteresisPts", NotifyHysteresisPts);
                NotifyMinRepeatMinutes = (int)Json.Long(notify, "minRepeatMinutes", NotifyMinRepeatMinutes);
            }

            CompactThreshold = Dbl(o, "compactThreshold", CompactThreshold);
            // 旧版は、設定画面で変えられない仮の値 0.92 を保存のたびに書き込んでいた。
            // この値は利用者が選んだものではないので「未設定」とみなし、公式の値に置き換える。
            // 次に保存したときにファイルも新しい値になる。
            if (Math.Abs(CompactThreshold - OldPlaceholderCompactThreshold) < 1e-9)
                CompactThreshold = DefaultCompactThreshold;
            PollSeconds = Math.Max(1, (int)Json.Long(o, "pollSeconds", PollSeconds));
            Hotkey = Json.Str(o, "hotkey") ?? Hotkey;
            Language = Json.Str(o, "language") ?? Language;

            var tray = Json.Obj(o, "tray");
            if (tray != null)
            {
                TrayMode = Json.Str(tray, "mode") ?? TrayMode;

                var label = Json.Str(tray, "label");
                if (string.Equals(label, "glyphs", StringComparison.OrdinalIgnoreCase)) TrayLabel = "glyphs";
                else if (string.Equals(label, "letters", StringComparison.OrdinalIgnoreCase)) TrayLabel = "letters";

                var values = Json.Arr(tray, "values");
                if (values != null)
                {
                    // 部分集合を許す（「コンテキストと週間枠だけ」を選べるように）。
                    // 知らない名前は捨て、結果が空になったときだけ既定に戻す。
                    var list = new List<string>();
                    foreach (var v in values)
                    {
                        var s = v as string;
                        if (string.IsNullOrEmpty(s) || list.Contains(s)) continue;

                        foreach (var known in AllTrayValues)
                        {
                            if (string.Equals(known, s, StringComparison.OrdinalIgnoreCase))
                            {
                                list.Add(known);
                                break;
                            }
                        }
                    }

                    if (list.Count > 0) TrayValues = list;
                }

                // 旧形式（リング）からの引き継ぎ。1 個にまとめるモードでは、リングの本数だけ
                // 並びの先頭から値を使っていた（ring1 は 1 つ、ring2 は 2 つ、ring3 は 3 つ）。
                // バーにしたとき見える値の数が変わらないよう、同じ数に絞る。
                // style は保存しないので、この引き継ぎは一度きり。
                var style = Json.Str(tray, "style");
                if (style != null && !TrayMultiMode)
                {
                    var keep = string.Equals(style, "ring3", StringComparison.OrdinalIgnoreCase) ? 3
                             : string.Equals(style, "ring2", StringComparison.OrdinalIgnoreCase) ? 2
                             : 1;

                    var padded = new List<string>(TrayValues);
                    foreach (var v in AllTrayValues)
                        if (!padded.Contains(v)) padded.Add(v);

                    TrayValues = padded.GetRange(0, Math.Min(keep, padded.Count));
                }
            }

            var display = Json.Obj(o, "display");
            if (display != null)
            {
                Theme = Json.Str(display, "theme") ?? Theme;
                Opacity = Dbl(display, "opacity", Opacity);
                ClickThrough = Json.Bool(display, "clickThrough", ClickThrough);
                ShowResets = Json.Str(display, "showResets") ?? ShowResets;
                HudX = (int)Json.Long(display, "hudX", HudX);
                HudY = (int)Json.Long(display, "hudY", HudY);
                // キーが無い旧設定は上端基準（これまでと同じ意味）で読む。
                HudAnchorBottom = string.Equals(Json.Str(display, "hudAnchor"), "bottom",
                                                StringComparison.OrdinalIgnoreCase);
                HideWhenFullscreen = Json.Bool(display, "hideWhenFullscreen", HideWhenFullscreen);
                HideStoppedSessions = Json.Bool(display, "hideStoppedSessions", HideStoppedSessions);

                HudShowRateLimits = Json.Bool(display, "showRateLimits", HudShowRateLimits);
                HudShowSessions = Json.Bool(display, "showSessions", HudShowSessions);
                HudShowBar = Json.Bool(display, "showBar", HudShowBar);
                HideIdleSessions = Json.Bool(display, "hideIdleSessions", HideIdleSessions);
                IdleHours = Math.Max(1, (int)Json.Long(display, "idleHours", IdleHours));
                HudWidth = Clamp((int)Json.Long(display, "hudWidth", HudWidth), MinHudWidth, MaxHudWidth);

                var size = Json.Str(display, "textSize");
                foreach (var known in TextSizes)
                    if (string.Equals(known, size, StringComparison.OrdinalIgnoreCase)) HudTextSize = known;

                // 旧形式からの引き継ぎ（新しいキーが無いときだけ）。
                //   density: "detailed" … トークン数の列を出す、という意味だった
                //   hudVisible          … 前回終了時に HUD が出ていたか
                HudShowTokens = display.ContainsKey("showTokens")
                    ? Json.Bool(display, "showTokens", HudShowTokens)
                    : string.Equals(Json.Str(display, "density"), "detailed", StringComparison.OrdinalIgnoreCase);
                HudShowAtStartup = display.ContainsKey("showAtStartup")
                    ? Json.Bool(display, "showAtStartup", HudShowAtStartup)
                    : Json.Bool(display, "hudVisible", HudShowAtStartup);

                var lead = Json.Obj(display, "resetLeadMinutes");
                if (lead != null)
                    ResetLeadFiveHourMinutes = (int)Json.Long(lead, "fiveHour", ResetLeadFiveHourMinutes);
            }

            var ext = Json.Obj(o, "externalSessions");
            if (ext != null)
            {
                ExternalSessionsEnabled = Json.Bool(ext, "enabled", ExternalSessionsEnabled);
                ExternalSessionsMax = Math.Max(1, (int)Json.Long(ext, "max", ExternalSessionsMax));
            }

            var models = Json.Obj(o, "modelLimits");
            if (models != null)
            {
                foreach (var kv in models)
                {
                    var limit = (int)Json.ToLong(kv.Value);
                    if (limit > 0) ModelLimits[kv.Key] = limit;
                }
            }
        }

        private static double Dbl(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>
        /// 保存する。書けなかったら false（呼び出し側が利用者に知らせる）。
        /// 手で書いたコメントは残らない（読み込み時に取り除いているため）。
        /// </summary>
        public bool Save()
        {
            var modelLimits = new JObj();
            foreach (var kv in ModelLimits) modelLimits.Add(kv.Key, kv.Value);

            var root = new JObj()
                .Add("thresholds", new JObj()
                    .Add("context", new JObj().Add("warn", ContextWarn).Add("danger", ContextDanger))
                    .Add("fiveHour", new JObj().Add("warn", FiveHourWarn).Add("danger", FiveHourDanger))
                    .Add("weekly", new JObj().Add("warn", WeeklyWarn).Add("danger", WeeklyDanger)))
                .Add("notify", new JObj()
                    .Add("enabled", new JObj()
                        .Add("context", NotifyContext)
                        .Add("fiveHour", NotifyFiveHour)
                        .Add("weekly", NotifyWeekly))
                    .Add("hysteresisPts", NotifyHysteresisPts)
                    .Add("minRepeatMinutes", NotifyMinRepeatMinutes))
                .Add("compactThreshold", CompactThreshold)
                .Add("pollSeconds", PollSeconds)
                .Add("hotkey", Hotkey)
                .Add("language", Language)
                .Add("tray", new JObj()
                    .Add("mode", TrayMode)
                    .Add("label", TrayLabel)
                    .Add("values", OrderedTrayValues().ToArray()))
                .Add("display", new JObj()
                    .Add("theme", Theme)
                    .Add("opacity", Opacity)
                    .Add("clickThrough", ClickThrough)
                    .Add("textSize", HudTextSize)
                    .Add("hudWidth", HudWidth)
                    .Add("showRateLimits", HudShowRateLimits)
                    .Add("showSessions", HudShowSessions)
                    .Add("hideIdleSessions", HideIdleSessions)
                    .Add("idleHours", IdleHours)
                    .Add("hideStoppedSessions", HideStoppedSessions)
                    .Add("hideWhenFullscreen", HideWhenFullscreen)
                    .Add("showBar", HudShowBar)
                    .Add("showTokens", HudShowTokens)
                    .Add("showResets", ShowResets)
                    .Add("resetLeadMinutes", new JObj()
                        .Add("fiveHour", ResetLeadFiveHourMinutes))
                    .Add("showAtStartup", HudShowAtStartup)
                    .Add("hudX", HudX)
                    .Add("hudY", HudY)
                    .Add("hudAnchor", HudAnchorBottom ? "bottom" : "top"))
                .Add("externalSessions", new JObj()
                    .Add("enabled", ExternalSessionsEnabled)
                    .Add("max", ExternalSessionsMax))
                .Add("modelLimits", modelLimits);

            try
            {
                Directory.CreateDirectory(Dir);
                // 一時ファイルへ書いてから置換する。読み手が半端な内容を掴まないように。
                // 「削除してから移動」だと、その間に読まれたとき「ファイルが無い＝既定値」になるので、
                // 1 回で入れ替わる File.Replace を使う。
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JObj.Write(root), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
