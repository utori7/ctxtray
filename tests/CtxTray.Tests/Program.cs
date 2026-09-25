using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CtxTray.Collect;
using CtxTray.Config;
using CtxTray.Core;
using CtxTray.Notify;
using CtxTray.Ui;

namespace CtxTray.Tests
{
    /// <summary>
    /// 本体の判定ロジックの試験。試験用の枠組み（NuGet のテストフレームワーク）は使わず、
    /// 失敗した項目を数えて終了コードで返すだけにしている。依存を増やさないため。
    /// </summary>
    internal static class Program
    {
        private static int _failed;
        private static int _passed;
        private static string _temp;

        private static int Main()
        {
            Strings.Apply("en");
            _temp = Path.Combine(Path.GetTempPath(), "ctxtray-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);

            try
            {
                Run("StripComments", StripComments);
                Run("Config with comments and modelLimits", ConfigLoad);
                Run("Compaction point: documented default, old placeholder migrated", CompactThresholdMigration);
                Run("Broken config", BrokenConfig);
                Run("Config: new display keys and older files without them", NewDisplayKeys);
                Run("Config: a copy is independent of the original", ConfigClone);
                Run("Config: tray label (letters / glyphs / percent)", TrayLabel);
                Run("Percent text: the same rounding everywhere", PercentRounding);
                Run("ModelLimits lookup", ModelLimitsLookup);
                Run("Model docs: page URL and parsing", ModelDocsParsing);
                Run("Model docs: record, 24-hour retry, one notice", ModelDocsFetching);
                Run("Notify: a notice with its own click action", NotifyClickAction);
                Run("Rate samples: latest org only, incomplete samples skipped", RateSamples);
                Run("JSON writer: ASCII-only output", AsciiJson);
                Run("Transcript: latest usage", TranscriptLatest);
                Run("Sessions: a Desktop tab keeps its Desktop process", TabProcessChoice);
                Run("Levels with slack", LevelsSlack);
                Run("Tray: running sessions only", TrayPicksRunning);
                Run("Sessions: hide the ones that are not running", HideStoppedSessions);
                Run("Sessions: the filtered-out rows are kept for the panel", HiddenSessionsKept);
                Run("HUD placement: grows upward in the lower half, stays on screen", Placement);
                Run("Notify: stopped sessions are not reported", NotifySkipsStopped);
                Run("Notify: the context body counts tokens, not a second percentage", NotifyContextBody);
                Run("Notify: warn then danger within the quiet period", NotifyEscalation);
                Run("Notify: hysteresis", NotifyHysteresis);
                Run("Notify: same level within the quiet period", NotifyQuiet);
                Run("Notify: spacing between balloons", NotifySpacing);
                Run("AutoStart: shortcut target comparison", AutoStartTarget);
                Run("Colors: value colours, amber/red override", ValueColors);
                Run("Colors: the Windows contrast theme", ContrastTheme);
            }
            finally
            {
                try { Directory.Delete(_temp, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("passed: {0}, failed: {1}", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            Console.WriteLine("- " + name);
            try { test(); }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("    FAIL (exception) " + ex);
            }
        }

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("    FAIL " + what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual),
                  what + " (expected " + expected + ", got " + actual + ")");
        }

        // --- 設定・JSON --------------------------------------------------------

        private static void StripComments()
        {
            var text = "{\n  // line comment \"with quotes\"\n  \"a\": \"x // not a comment\", /* block\n comment */\n"
                     + "  \"b\": \"escaped \\\" quote // still a string\",\n  \"c\": 1 // trailing\n}";
            var o = Json.ParseObject(Json.StripComments(text));
            Check(o != null, "parses after stripping");
            if (o == null) return;
            Equal("x // not a comment", Json.Str(o, "a"), "// inside a string is kept");
            Equal("escaped \" quote // still a string", Json.Str(o, "b"), "escaped quote does not end the string");
            Equal(1L, Json.Long(o, "c"), "value before a trailing comment");
            Check(Json.ParseObject(text) == null, "the serializer itself rejects comments (reason for StripComments)");
        }

        private static string UseConfigDir(string name)
        {
            var root = Path.Combine(_temp, name);
            Directory.CreateDirectory(Path.Combine(root, "ctxtray"));
            Environment.SetEnvironmentVariable("LOCALAPPDATA", root);
            return Path.Combine(root, "ctxtray", "config.json");
        }

        private static void ConfigLoad()
        {
            var path = UseConfigDir("config-ok");
            File.WriteAllText(path,
                "{\n  // comment\n  \"language\": \"ja\",\n  \"notify\": { \"hysteresisPts\": 7 },\n"
                + "  \"modelLimits\": { \"claude-example-6\": 123456 } /* end */\n}",
                new UTF8Encoding(false));

            string problem;
            bool failed;
            var c = AppConfig.Load(out problem, out failed);
            Check(!failed && problem == null, "loads without a problem: " + problem);
            Equal("ja", c.Language, "language");
            Equal(7, c.NotifyHysteresisPts, "hysteresisPts");
            Check(c.ModelLimits.ContainsKey("claude-example-6"), "modelLimits entry is read");
            Check(!File.Exists(path + ".bak"), "no backup for a valid file");
            // 通信しないのが既定。書いていない設定ファイルでもオフのまま。
            Check(!c.FetchModelLimits, "fetching from the docs is off by default");

            c.FetchModelLimits = true;
            Check(c.Save(), "save succeeds");
            string problem2;
            bool failed2;
            var again = AppConfig.Load(out problem2, out failed2);
            Check(!failed2, "the saved file loads again");
            Equal(123456, again.ModelLimits["claude-example-6"], "modelLimits survives a save");
            Check(again.FetchModelLimits, "fetchModelLimits survives a save");
            Check(again.Clone().FetchModelLimits, "fetchModelLimits is copied");
            Check(!File.Exists(path + ".tmp"), "temporary file is gone after save");
        }

        /// <summary>
        /// 複製は元と切り離されている（設定画面とメニューは複製を書き換えて保存する）。
        /// 一覧や辞書を共有していると、複製への変更が実行中の設定に漏れる。
        /// </summary>
        private static void ConfigClone()
        {
            var original = new AppConfig();
            original.ClickThrough = false;
            original.HudX = 100;
            original.ModelLimits["claude-example-6"] = 1000000;

            var copy = original.Clone();
            Check(!ReferenceEquals(original, copy), "a new object");
            Equal(100, copy.HudX, "values are copied");
            Equal(1000000, copy.ModelLimits["claude-example-6"], "modelLimits are copied");

            copy.ClickThrough = true;
            copy.TrayValues.Clear();
            copy.TrayValues.Add("weekly");
            copy.ModelLimits["claude-example-6"] = 200000;
            copy.ModelLimits["claude-example-7"] = 1;

            Check(!original.ClickThrough, "changing the copy does not change the original");
            Equal(3, original.TrayValues.Count, "the tray values list is not shared");
            Equal(1000000, original.ModelLimits["claude-example-6"], "the modelLimits dictionary is not shared");
            Check(!original.ModelLimits.ContainsKey("claude-example-7"), "entries added to the copy stay in the copy");
            Check(copy.ModelLimits.ContainsKey("CLAUDE-EXAMPLE-6"), "the copy keeps case-insensitive model names");

            original.WasMissing = true;
            Check(!original.Clone().WasMissing, "the first-run mark is not copied (a copy is never a first run)");
        }

        /// <summary>
        /// 値ごとに分けるモードの目印。percent は 2026-09-25 に追加。知らない値は無視して前の値のまま。
        /// </summary>
        private static void TrayLabel()
        {
            var path = UseConfigDir("config-tray-label");
            string problem;
            bool failed;

            File.WriteAllText(path, "{ \"tray\": { \"mode\": \"multi\" } }", new UTF8Encoding(false));
            Equal("letters", AppConfig.Load(out problem, out failed).TrayLabel, "no label means letters");

            File.WriteAllText(path, "{ \"tray\": { \"label\": \"Percent\" } }", new UTF8Encoding(false));
            var c = AppConfig.Load(out problem, out failed);
            Check(!failed, "a percent label loads");
            Equal("percent", c.TrayLabel, "percent is read (any case)");

            Check(c.Save(), "save succeeds");
            Equal("percent", AppConfig.Load(out problem, out failed).TrayLabel, "percent survives a save");

            File.WriteAllText(path, "{ \"tray\": { \"label\": \"digits\" } }", new UTF8Encoding(false));
            Equal("letters", AppConfig.Load(out problem, out failed).TrayLabel, "an unknown label falls back to letters");
        }

        /// <summary>
        /// HUD・ツールチップ・アイコンの数字は同じ丸め（x.5 は上へ）。
        /// 以前のツールチップは Math.Round（偶数丸め）で、42.5 が HUD と 1 ずれていた。
        /// </summary>
        private static void PercentRounding()
        {
            Equal("0", PercentText.Format(0), "0");
            Equal("5", PercentText.Format(5), "one digit stays one digit (no leading zero)");
            Equal("42", PercentText.Format(42.4), "42.4");
            Equal("43", PercentText.Format(42.5), "42.5 rounds up like the panel");
            Equal("3", PercentText.Format(2.5), "2.5 rounds up (Math.Round would give 2)");
            Equal("100", PercentText.Format(99.5), "99.5 becomes 100");
            Equal("100", PercentText.Format(100), "100 is not capped at 99");
        }

        /// <summary>
        /// 2026-09-18 に足した設定（位置の基準、止まっている行を隠す、全画面で隠す）と、
        /// それらのキーが無い旧設定の読み方。
        /// </summary>
        private static void NewDisplayKeys()
        {
            var path = UseConfigDir("config-new-keys");

            // 旧設定（新しいキーが無い）: 位置は上端基準、全画面では隠す、止まっている行は出す。
            File.WriteAllText(path, "{ \"display\": { \"hudX\": 10, \"hudY\": 20 } }", new UTF8Encoding(false));
            string problem;
            bool failed;
            var old = AppConfig.Load(out problem, out failed);
            Check(!failed, "an older config still loads");
            Check(!old.HudAnchorBottom, "no hudAnchor means the top edge (as before)");
            Check(!old.HideStoppedSessions, "stopped sessions are shown by default");
            Check(old.HideWhenFullscreen, "hiding over full-screen apps is on by default");
            Check(!old.WasMissing, "the file was there");

            // 書いた値がそのまま読める。
            File.WriteAllText(path,
                "{ \"display\": { \"hudX\": 30, \"hudY\": 40, \"hudAnchor\": \"bottom\","
                + " \"hideStoppedSessions\": true, \"hideWhenFullscreen\": false } }",
                new UTF8Encoding(false));
            var set = AppConfig.Load(out problem, out failed);
            Check(set.HudAnchorBottom, "hudAnchor: bottom is read");
            Check(set.HideStoppedSessions, "hideStoppedSessions is read");
            Check(!set.HideWhenFullscreen, "hideWhenFullscreen is read");

            // 保存して読み直しても同じ（設定画面の OK で消えない）。
            Check(set.Save(), "save succeeds");
            var back = AppConfig.Load(out problem, out failed);
            Check(back.HudAnchorBottom && back.HideStoppedSessions && !back.HideWhenFullscreen,
                  "the three settings survive a save");
            Equal(40, back.HudY, "the saved position survives too");

            // 形の手がかり（thresholds.marks）は既定でオン。古い設定にも無いので既定が効く。
            Check(old.LevelMarks, "the shape marks are on by default");
            File.WriteAllText(path, "{ \"thresholds\": { \"marks\": false } }", new UTF8Encoding(false));
            var noMarks = AppConfig.Load(out problem, out failed);
            Check(!noMarks.LevelMarks, "marks: false is read");
            Check(noMarks.Save(), "save succeeds");
            Check(!AppConfig.Load(out problem, out failed).LevelMarks, "and survives a save");

            // ファイルが無いときは「初めての起動」として分かる（初回の案内に使う）。
            var missing = UseConfigDir("config-first-run");
            Check(!File.Exists(missing), "no config file yet");
            var first = AppConfig.Load(out problem, out failed);
            Check(first.WasMissing, "a missing file is reported as the first run");
            Check(!failed && problem == null, "and is not treated as a problem");
        }

        private static void CompactThresholdMigration()
        {
            Equal(0.967, new AppConfig().CompactThreshold, "default is the documented value");

            // 旧版が保存していた仮の値だけを置き換え、利用者が書いた値は残す。
            var cases = new[]
            {
                new { Name = "compact-old", Json = "{ \"compactThreshold\": 0.92 }", Expected = 0.967 },
                new { Name = "compact-own", Json = "{ \"compactThreshold\": 0.8 }", Expected = 0.8 },
                new { Name = "compact-none", Json = "{ \"language\": \"en\" }", Expected = 0.967 },
            };
            foreach (var c in cases)
            {
                var path = UseConfigDir(c.Name);
                File.WriteAllText(path, c.Json, new UTF8Encoding(false));
                string problem;
                bool failed;
                var loaded = AppConfig.Load(out problem, out failed);
                Check(!failed, c.Name + " loads");
                Equal(c.Expected, loaded.CompactThreshold, c.Name);
            }

            // 置き換えた値は保存で書き戻される。
            var saved = UseConfigDir("compact-old-saved");
            File.WriteAllText(saved, "{ \"compactThreshold\": 0.92 }", new UTF8Encoding(false));
            string p;
            bool f;
            Check(AppConfig.Load(out p, out f).Save(), "save succeeds");
            Check(File.ReadAllText(saved).Contains("\"compactThreshold\": 0.967"), "the new value is written back");
        }

        private static void BrokenConfig()
        {
            var path = UseConfigDir("config-broken");
            File.WriteAllText(path, "{ \"language\": ", new UTF8Encoding(false));

            string problem;
            bool failed;
            var c = AppConfig.Load(out problem, out failed);
            Check(failed, "reported as failed");
            Check(problem != null, "a problem message is returned");
            Check(File.Exists(path + ".bak"), "the broken file is kept as .bak");
            Equal("auto", c.Language, "defaults are returned");

            var missing = UseConfigDir("config-missing");
            string problem2;
            bool failed2;
            AppConfig.Load(out problem2, out failed2);
            Check(!failed2 && problem2 == null && !File.Exists(missing), "a missing file is not an error");
        }

        private static void ModelLimitsLookup()
        {
            Equal((int?)1000000, ModelLimits.Lookup("claude-opus-5"), "exact");
            Equal((int?)200000, ModelLimits.Lookup("claude-haiku-4-5-20251001"), "dated suffix");
            Equal((int?)null, ModelLimits.Lookup("claude-opus-4-1-20250805"), "unknown model");
            Equal((int?)null, ModelLimits.Lookup("claude-opus-4"), "a shorter name does not match a longer key");
            Equal((int?)null, ModelLimits.Lookup(null), "null");

            // ★ 前方一致の推測はしない（2026-09-25）。点付きの新しい版は、確かめるまで不明。
            Equal((int?)null, ModelLimits.Lookup("claude-fable-5-2"), "a newer point release is not guessed");
            Equal((int?)null, ModelLimits.Lookup("claude-opus-5-9"), "not guessed from claude-opus-5 either");
            Equal((int?)null, ModelLimits.Lookup("claude-opus-5-extra"), "only an 8-digit date counts as the same model");

            // 組み込みの 14 モデル（公式ドキュメントで確認した値）。
            var expected = new Dictionary<string, int>
            {
                { "claude-fable-5-1", 1000000 }, { "claude-mythos-5-1", 1000000 },
                { "claude-fable-5", 1000000 },   { "claude-mythos-5", 1000000 },
                { "claude-opus-5-5", 1000000 },  { "claude-opus-5", 1000000 },
                { "claude-opus-4-8", 1000000 },  { "claude-opus-4-7", 1000000 },
                { "claude-opus-4-6", 1000000 },  { "claude-opus-4-5-20251101", 200000 },
                { "claude-sonnet-5", 1000000 },  { "claude-sonnet-4-6", 1000000 },
                { "claude-sonnet-4-5-20250929", 200000 }, { "claude-haiku-4-5-20251001", 200000 },
            };
            foreach (var kv in expected)
                Equal((int?)kv.Value, ModelLimits.Lookup(kv.Key), "built in: " + kv.Key);
            var count = 0;
            foreach (var kv in ModelLimits.BuiltIn) count++;
            Equal(14, count, "the built-in table has 14 models");

            var overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "claude-sonnet-5", 200000 },
                { "claude-new", 300000 },
                { "claude-new-2", 400000 },
            };
            Equal((int?)200000, ModelLimits.Lookup("claude-sonnet-5", overrides), "override wins over the table");
            Equal((int?)1000000, ModelLimits.Lookup("claude-opus-5", overrides), "table still used for others");
            Equal((int?)400000, ModelLimits.Lookup("claude-new-2-20270101", overrides), "a dated name matches its key");
            Equal((int?)null, ModelLimits.Lookup("claude-new-3", overrides), "settings are not matched by prefix either");

            // 出どころと、取得済みの値の順番（設定 → 組み込み → 公式ドキュメント）。
            var docs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "claude-future-7", 2000000 },
                { "claude-opus-5", 5 },
            };
            LimitSource source;
            Equal((int?)2000000, ModelLimits.Lookup("claude-future-7", overrides, docs, out source), "value from the docs");
            Equal(LimitSource.Docs, source, "source: docs");
            Equal((int?)1000000, ModelLimits.Lookup("claude-opus-5", overrides, docs, out source), "the built-in table wins over the docs");
            Equal(LimitSource.BuiltIn, source, "source: built in");
            ModelLimits.Lookup("claude-sonnet-5", overrides, docs, out source);
            Equal(LimitSource.Config, source, "source: config");
            ModelLimits.Lookup("claude-nothing-1", overrides, docs, out source);
            Equal(LimitSource.Unknown, source, "source: unknown");
        }

        /// <summary>公式ドキュメントのページの組み立てと読み取り（通信しない）。</summary>
        private static void ModelDocsParsing()
        {
            Equal("https://platform.claude.com/docs/en/models/opus-5-5/overview.md",
                  ModelDocs.PageUrl("claude-opus-5-5"), "page URL");
            Equal("https://platform.claude.com/docs/en/models/haiku-4-5/overview.md",
                  ModelDocs.PageUrl("claude-haiku-4-5-20251001"), "the date is dropped");
            Equal((string)null, ModelDocs.PageUrl("gpt-5"), "only claude- models");
            Equal((string)null, ModelDocs.PageUrl("claude-../../x"), "no odd characters in the URL");
            Equal((string)null, ModelDocs.PageUrl("claude-opus 5"), "no spaces");
            Equal((string)null, ModelDocs.PageUrl(null), "null");

            // 2026-09-25 に取得した実際のページの冒頭と同じ形。
            var opus55 = "---\ntitle: Claude Opus 5.5\n---\n\n# Claude Opus 5.5\n\nModel ID: `claude-opus-5-5`\n\n"
                       + "Context window: 1M tokens · Max output: 128K tokens · Input pricing: $4 / MTok\n";
            Equal((int?)1000000, ModelDocs.ParsePage(opus55, "claude-opus-5-5"), "1M");
            var haiku = "Model ID: `claude-haiku-4-5-20251001`\n\nContext window: 200K tokens · Max output: 64K tokens\n";
            Equal((int?)200000, ModelDocs.ParsePage(haiku, "claude-haiku-4-5"), "200K, dated ID on the page");
            Equal((int?)200000, ModelDocs.ParsePage(haiku, "claude-haiku-4-5-20251001"), "dated name too");
            Equal((int?)1500000, ModelDocs.ParsePage("Model ID: `claude-x-1`\nContext window: 1.5M tokens", "claude-x-1"), "decimal");

            Equal((int?)null, ModelDocs.ParsePage(opus55, "claude-opus-5"), "the page is for another model");
            Equal((int?)null, ModelDocs.ParsePage("Context window: 1M tokens", "claude-opus-5-5"), "no Model ID line");
            Equal((int?)null, ModelDocs.ParsePage("Model ID: `claude-opus-5-5`", "claude-opus-5-5"), "no window line");
            Equal((int?)null, ModelDocs.ParsePage("Model ID: `claude-x-1`\nContext window: 500M tokens", "claude-x-1"), "out of range");
            Equal((int?)null, ModelDocs.ParsePage("<html>Not found</html>", "claude-x-1"), "an error page");
            Equal((int?)null, ModelDocs.ParsePage(null, "claude-x-1"), "nothing downloaded");
        }

        /// <summary>取得係: 記録・24 時間の再確認・通知は 1 回だけ（通信は差し替える）。</summary>
        private static void ModelDocsFetching()
        {
            var dir = Path.Combine(_temp, "model-docs");
            var now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
            var requested = new List<string>();

            var f = new ModelDocsFetcher(dir, "test");
            f.Manual = true;
            f.Clock = () => now;
            f.Download = url =>
            {
                requested.Add(url);
                return url.Contains("/future-7/") ? "Model ID: `claude-future-7`\nContext window: 2M tokens" : null;
            };

            Equal(DocsLookupState.Off, f.StateFor("claude-future-7", false), "off when the setting is off");

            f.Request(new[] { "claude-future-7", "claude-missing-1", "claude-opus-5", "gpt-5" }, false);
            Equal(DocsLookupState.Pending, f.StateFor("claude-missing-1", true), "pending while queued");
            f.RunQueuedNow();

            Equal(2, requested.Count, "built-in and non-claude models are not fetched");
            Check(f.TakeChanged(), "a change is reported");
            Check(!f.TakeChanged(), "and only once");
            Equal(2000000, f.Limits()["claude-future-7"], "found value is kept");
            Equal(DocsLookupState.NotFound, f.StateFor("claude-missing-1", true), "missing model is not found");

            requested.Clear();
            f.Request(new[] { "claude-future-7", "claude-missing-1" }, false);
            f.RunQueuedNow();
            Equal(0, requested.Count, "nothing again within 24 hours, and a found model is never refetched");

            now = now.AddHours(24);
            f.Request(new[] { "claude-missing-1" }, false);
            f.RunQueuedNow();
            Equal(1, requested.Count, "a missing model is checked again after 24 hours");

            requested.Clear();
            f.Request(new[] { "claude-missing-1" }, true);
            f.RunQueuedNow();
            Equal(1, requested.Count, "Check now does not wait");

            Check(f.MarkNotified("claude-missing-1"), "first notice");
            Check(!f.MarkNotified("claude-missing-1"), "no second notice");

            // 記録ファイルから読み直しても同じ。
            var again = new ModelDocsFetcher(dir, "test");
            Equal(2000000, again.Limits()["claude-future-7"], "found value survives a restart");
            Equal(DocsLookupState.NotFound, again.StateFor("claude-missing-1", true), "missing model survives a restart");
            Check(!again.MarkNotified("claude-missing-1"), "notices survive a restart");
            Check(again.UnknownSoFar().Contains("claude-missing-1"), "listed as unknown for the settings");
            Equal(1, again.Entries().Count, "one entry from the docs");

            File.WriteAllText(Path.Combine(dir, ModelDocsStore.FileName), "{ broken", new UTF8Encoding(false));
            Equal(0, new ModelDocsFetcher(dir, "test").Limits().Count, "a broken file reads as empty");
        }

        /// <summary>通知のクリック: 動作を持つ通知はそれを返し、ほかは既定（null）。</summary>
        private static void NotifyClickAction()
        {
            var n = new ThresholdNotifier((t, b, i) => { });
            var now = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
            n.Clock = () => now;

            var opened = false;
            n.ShowInfo("title", "body", () => opened = true);
            var action = n.TakeClickAction();
            Check(action != null, "the shown notice carries its action");
            if (action != null) action();
            Check(opened, "the action runs");
            Check(n.TakeClickAction() == null, "taken only once");

            now = now.AddSeconds(10);
            n.ShowInfo("title", "plain");
            Check(n.TakeClickAction() == null, "a plain notice keeps the default");
        }

        private static void RateSamples()
        {
            var root = Path.Combine(_temp, "rate");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "plan-usage-history.json"),
                "{\"version\":2,\"samples\":["
                + "{\"t\":1000000000000,\"org\":\"A\",\"u\":{\"fh\":90,\"sd\":50}},"
                + "{\"t\":1000000060000,\"org\":\"B\",\"u\":{\"fh\":10,\"sd\":5}},"
                + "{\"t\":1000000120000,\"org\":\"A\",\"u\":{\"fh\":95,\"sd\":51}},"
                + "{\"t\":1000000180000,\"org\":\"B\",\"u\":{\"fh\":0}},"
                + "{\"t\":1000000240000,\"org\":\"B\",\"u\":{\"fh\":12,\"sd\":6}}"
                + "]}", new UTF8Encoding(false));

            var diag = new Diagnostics();
            var samples = RateLimits.ReadSamples(root, diag);
            Equal(2, samples.Count, "only org B, without the sample that lacks sd");
            if (samples.Count == 2)
            {
                Equal(10, samples[0].FiveHourPct, "first B sample");
                Equal(12, samples[1].FiveHourPct, "latest B sample");
            }
            Check(diag.IsEmpty, "no diagnostics: " + diag.Summary);

            File.WriteAllText(Path.Combine(root, "plan-usage-history.json"),
                "{\"version\":2,\"samples\":[{\"t\":1000000000000,\"u\":{\"x\":1}}]}", new UTF8Encoding(false));
            var diag2 = new Diagnostics();
            Equal(0, RateLimits.ReadSamples(root, diag2).Count, "unknown format gives no samples (not 0%)");
            Check(!diag2.IsEmpty, "and says so in the diagnostics");
        }

        private static void AsciiJson()
        {
            var root = new JObj().Add("title", "認証まわり \"quoted\" 😀").Add("n", 1);
            var ascii = JObj.Write(root, true);
            var plain = JObj.Write(root);

            var onlyAscii = true;
            foreach (var ch in ascii) if (ch > 0x7E || (ch < 0x20 && ch != '\n')) onlyAscii = false;
            Check(onlyAscii, "ASCII-only output contains no other characters");
            Check(plain.Contains("認証"), "normal output keeps the characters");

            var parsed = Json.ParseObject(ascii);
            Equal("認証まわり \"quoted\" 😀", parsed == null ? null : Json.Str(parsed, "title"), "round trip");
        }

        // --- transcript --------------------------------------------------------

        private static void TranscriptLatest()
        {
            var path = Path.Combine(_temp, "session.jsonl");
            var lines = new[]
            {
                "{\"type\":\"user\",\"timestamp\":\"2026-09-16T12:00:00Z\",\"message\":{\"content\":\"hi\"}}",
                Usage("2026-09-16T12:00:05Z", "claude-opus-5", 100, 200, 300, 50),
                Usage("2026-09-16T12:01:00Z", "claude-opus-5", 1000, 2000, 3000, 70),
                // 中断されたターン。全部 0 なので拾わない。
                Usage("2026-09-16T12:02:00Z", "<synthetic>", 0, 0, 0, 0),
            };
            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));

            var latest = Transcript.ReadLatest(path);
            Check(latest != null, "a usage line is found");
            if (latest == null) return;
            Equal(6000, latest.PromptTokens, "input + cache creation + cache read (output excluded)");
            Equal("claude-opus-5", latest.Model, "model of the last real turn");
            Equal(new DateTime(2026, 9, 16, 12, 1, 0, DateTimeKind.Utc), latest.AtUtc, "timestamp");
        }

        private static string Usage(string at, string model, int input, int creation, int read, int output)
        {
            return "{\"type\":\"assistant\",\"timestamp\":\"" + at + "\",\"message\":{\"model\":\"" + model
                 + "\",\"usage\":{\"input_tokens\":" + input + ",\"cache_creation_input_tokens\":" + creation
                 + ",\"cache_read_input_tokens\":" + read + ",\"output_tokens\":" + output + "}}}";
        }

        // --- プロセス ----------------------------------------------------------

        private static LiveProcess Proc(int pid, string session, string entrypoint, long startedAt, bool alive = true)
        {
            return new LiveProcess
            {
                Pid = pid,
                SessionId = session,
                Entrypoint = entrypoint,
                StartedAtMs = startedAt,
                Alive = alive,
            };
        }

        private static void TabProcessChoice()
        {
            // 実際に起きた並び: Desktop のプロセスの後に、同じ会話を再開した VS Code のプロセスを読む。
            var procs = new List<LiveProcess>
            {
                Proc(14832, "shared", "claude-desktop", 100),
                Proc(7108, "shared", "claude-vscode", 200),
                Proc(13280, "vscode-only", "claude-vscode", 300),
                Proc(500, "exited", "claude-desktop", 50, false),
                Proc(600, null, "cli", 60),
            };
            var map = Sessions.AliveBySession(procs);
            Equal(14832, map.ContainsKey("shared") ? map["shared"].Pid : 0,
                  "the Desktop process wins over a later VS Code one");
            Equal(13280, map.ContainsKey("vscode-only") ? map["vscode-only"].Pid : 0,
                  "a session with only a VS Code process keeps it");
            Check(!map.ContainsKey("exited"), "exited processes are left out");
            Equal(2, map.Count, "processes without a session id are left out");

            procs.Reverse();
            var reversed = Sessions.AliveBySession(procs);
            Equal(14832, reversed.ContainsKey("shared") ? reversed["shared"].Pid : 0,
                  "the same choice when the files are read in the other order");

            var twoCli = new List<LiveProcess> { Proc(2, "s", "cli", 200), Proc(1, "s", "cli", 100) };
            Equal(2, Sessions.AliveBySession(twoCli)["s"].Pid, "same kind: the later start wins");
            twoCli.Reverse();
            Equal(2, Sessions.AliveBySession(twoCli)["s"].Pid, "same kind, other order");

            Equal(0, Sessions.AliveBySession(null).Count, "null gives an empty map");
        }

        // --- 判定と通知 --------------------------------------------------------

        private static void LevelsSlack()
        {
            var cfg = new AppConfig { FiveHourWarn = 70, FiveHourDanger = 90 };
            var r = new RateLimitStatus { FiveHourPct = 85 };
            Equal(Level.Warn, Levels.ForFiveHour(r, cfg), "85 is warn");
            Equal(Level.Danger, Levels.ForFiveHour(r, cfg, 10), "85 is still danger with 10 points of slack");

            // 到達率 0.804（境目ちょうどの浮動小数点誤差を避けて、少しだけ上にする）
            var s = new SessionRow { ContextTokens = 740000, ContextLimit = 1000000 };
            var c = new AppConfig { ContextWarn = 0.75, ContextDanger = 0.90, CompactThreshold = 0.92 };
            Equal(Level.Warn, Levels.ForContext(s, c), "context reach 0.804 is warn");
            Equal(Level.Danger, Levels.ForContext(s, c, 10), "slack is applied in percentage points of reach");
        }

        private sealed class Harness
        {
            public readonly List<string> Shown = new List<string>();
            public DateTime Now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
            public readonly ThresholdNotifier Notifier;
            public readonly AppConfig Config;

            public Harness(int minRepeatMinutes)
            {
                Notifier = new ThresholdNotifier((title, text, icon) => Shown.Add(icon + ": " + text));
                Notifier.Clock = () => Now;
                Config = new AppConfig
                {
                    FiveHourWarn = 70,
                    FiveHourDanger = 90,
                    NotifyHysteresisPts = 10,
                    NotifyMinRepeatMinutes = minRepeatMinutes,
                    NotifyContext = false,
                    NotifyWeekly = false,
                };
            }

            /// <summary>5時間枠の値を渡し、間隔待ちに掛からないよう時間を進めてから出す。</summary>
            public void FiveHour(int pct, int advanceSeconds = 10)
            {
                Now = Now.AddSeconds(advanceSeconds);
                var snap = new Snapshot { RateLimits = new RateLimitStatus { FiveHourPct = pct, WeeklyPct = 0 } };
                Notifier.Check(snap, Config);
                Notifier.Pump();
            }
        }

        private static SessionRow Row(string id, int tokens, int? limit, bool running)
        {
            return new SessionRow
            {
                CliSessionId = id,
                Title = id,
                ContextTokens = tokens,
                ContextLimit = limit,
                ContextPct = limit.HasValue ? 100.0 * tokens / limit.Value : (double?)null,
                ModelKnown = limit.HasValue,
                ProcessAlive = running,
            };
        }

        private static void TrayPicksRunning()
        {
            var cfg = new AppConfig();
            var snap = new Snapshot();
            snap.Sessions.Add(Row("stopped-90", 900000, 1000000, false));
            snap.Sessions.Add(Row("running-50", 500000, 1000000, true));
            snap.Sessions.Add(Row("running-unknown", 950000, null, true));
            snap.Sessions.Add(Row("running-30", 300000, 1000000, true));

            var picked = SessionFilter.MostPressed(snap, cfg);
            Equal("running-50", picked == null ? null : picked.CliSessionId,
                  "the highest running session, not the stopped one");

            // 分母が違っても、圧縮点に近い方を選ぶ（200K の 60% は 1M の 50% より近い）。
            snap.Sessions.Add(Row("running-200k-60", 120000, 200000, true));
            picked = SessionFilter.MostPressed(snap, cfg);
            Equal("running-200k-60", picked == null ? null : picked.CliSessionId, "compared by reach");

            var allStopped = new Snapshot();
            allStopped.Sessions.Add(Row("stopped-a", 900000, 1000000, false));
            allStopped.Sessions.Add(Row("stopped-b", 400000, 1000000, false));
            Check(SessionFilter.MostPressed(allStopped, cfg) == null, "nothing when every session is stopped");

            Check(SessionFilter.MostPressed(new Snapshot(), cfg) == null, "nothing when there are no sessions");
            Check(!SessionFilter.IsRunning(null), "null is not running");
        }

        /// <summary>
        /// 「動いていないセッションは隠す」設定（既定はオフ）。
        /// 隠した行は非表示の件数に数え、HUD の見出しに出す。
        /// </summary>
        private static void HideStoppedSessions()
        {
            var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

            Func<Snapshot> build = () =>
            {
                var s = new Snapshot();
                var running = Row("running", 300000, 1000000, true);
                var stopped = Row("stopped", 900000, 1000000, false);
                running.LastActivityUtc = now.AddMinutes(-1);
                stopped.LastActivityUtc = now.AddMinutes(-2);
                s.Sessions.Add(running);
                s.Sessions.Add(stopped);
                return s;
            };

            var off = new AppConfig { HideStoppedSessions = false, HideIdleSessions = false };
            var kept = build();
            SessionFilter.Apply(kept, off, now);
            Equal(2, kept.Sessions.Count, "off: both rows stay");
            Equal(0, kept.HiddenSessionCount, "off: nothing is hidden");

            var on = new AppConfig { HideStoppedSessions = true, HideIdleSessions = false };
            var filtered = build();
            SessionFilter.Apply(filtered, on, now);
            Equal(1, filtered.Sessions.Count, "on: only the running row stays");
            Equal("running", filtered.Sessions.Count == 1 ? filtered.Sessions[0].CliSessionId : null,
                  "on: the kept row is the running one");
            Equal(1, filtered.HiddenSessionCount, "on: the stopped row is counted as hidden");

            // 動いている行が無ければ 0 件になるが、隠した件数で「動いているものが無い」と分かる。
            var allStopped = new Snapshot();
            allStopped.Sessions.Add(Row("a", 900000, 1000000, false));
            allStopped.Sessions.Add(Row("b", 400000, 1000000, false));
            SessionFilter.Apply(allStopped, on, now);
            Equal(0, allStopped.Sessions.Count, "on: no rows when nothing runs");
            Equal(2, allStopped.HiddenSessionCount, "on: both are counted as hidden");
        }

        /// <summary>
        /// 落とした行は捨てずに取っておく（HUD の「ほか N 件を表示」で描くため）。
        /// Sessions の中身は今までどおりで、トレイ・ツールチップ・通知が見るものは変わらない。
        /// </summary>
        private static void HiddenSessionsKept()
        {
            var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

            var snap = new Snapshot();
            var running = Row("running", 300000, 1000000, true);
            var stopped = Row("stopped", 900000, 1000000, false);
            running.LastActivityUtc = now.AddMinutes(-1);
            stopped.LastActivityUtc = now.AddMinutes(-2);
            snap.Sessions.Add(running);
            snap.Sessions.Add(stopped);

            var config = new AppConfig { HideStoppedSessions = true, HideIdleSessions = false };
            SessionFilter.Apply(snap, config, now);

            Equal(1, snap.Sessions.Count, "the visible list is unchanged");
            Equal("running", snap.Sessions[0].CliSessionId, "and holds the running row");
            Equal(1, snap.HiddenSessions.Count, "the dropped row is kept aside");
            Equal("stopped", snap.HiddenSessions[0].CliSessionId, "and it is the stopped one");
            Equal(snap.HiddenSessions.Count, snap.HiddenSessionCount, "the count matches the list");

            // 取っておいても、トレイが見る対象は変わらない。
            var worst = SessionFilter.MostPressed(snap, config);
            Equal("running", worst == null ? null : worst.CliSessionId,
                  "the tray still ignores what was filtered out");

            // 何も落とさなければ空のまま。
            var all = new Snapshot();
            all.Sessions.Add(Row("a", 100000, 1000000, true));
            SessionFilter.Apply(all, new AppConfig { HideIdleSessions = false }, now);
            Equal(0, all.HiddenSessions.Count, "nothing is set aside when nothing is dropped");
        }

        // --- HUD の位置 --------------------------------------------------------

        /// <summary>
        /// 行が増えても画面の外へ出ない。下半分に置いた HUD は下端を固定して上へ伸びる。
        /// </summary>
        private static void Placement()
        {
            // 1920x1080 からタスクバー 40px を除いた作業領域。
            var work = new System.Drawing.Rectangle(0, 0, 1920, 1040);

            var upper = new System.Drawing.Rectangle(100, 100, 352, 200);
            var lower = new System.Drawing.Rectangle(100, 800, 352, 200);
            Check(!HudPlacement.AnchorBottom(upper, work), "a HUD in the upper half keeps its top edge");
            Check(HudPlacement.AnchorBottom(lower, work), "a HUD in the lower half keeps its bottom edge");

            // 上端基準: 高さが増えても上端はそのまま（下に余裕がある間）。
            Equal(new System.Drawing.Point(100, 100),
                  HudPlacement.Place(100, 100, false, new System.Drawing.Size(352, 400), work),
                  "top-anchored: the top edge stays");

            // 下端基準: 下端 1000 のまま高さ 200 → 400 で上へ伸びる。
            var anchor = HudPlacement.Anchor(lower, true);
            Equal(new System.Drawing.Point(100, 1000), anchor, "the bottom edge is what gets saved");
            Equal(new System.Drawing.Point(100, 800),
                  HudPlacement.Place(anchor.X, anchor.Y, true, new System.Drawing.Size(352, 200), work),
                  "bottom-anchored: same height, same place");
            Equal(new System.Drawing.Point(100, 600),
                  HudPlacement.Place(anchor.X, anchor.Y, true, new System.Drawing.Size(352, 400), work),
                  "bottom-anchored: a taller HUD grows upward");

            // はみ出しは押し戻す（上端基準で行が増えた場合＝以前は画面外へ出ていた）。
            Equal(new System.Drawing.Point(100, 640),
                  HudPlacement.Place(100, 900, false, new System.Drawing.Size(352, 400), work),
                  "a HUD that would run past the bottom is pushed back inside");
            Equal(new System.Drawing.Point(1568, 100),
                  HudPlacement.Place(1800, 100, false, new System.Drawing.Size(352, 200), work),
                  "same for the right edge");
            Equal(new System.Drawing.Point(0, 0),
                  HudPlacement.Place(-50, -80, false, new System.Drawing.Size(352, 200), work),
                  "and for the left and top edges");

            // 作業領域より大きい HUD は左上に合わせる（頭を切らない）。
            Equal(new System.Drawing.Point(0, 0),
                  HudPlacement.Place(100, 900, true, new System.Drawing.Size(2000, 1200), work),
                  "a HUD larger than the work area starts at its top-left");

            // 左上が負の座標のモニタ（主モニタの左・上にある画面）でも同じ規則。
            var left = new System.Drawing.Rectangle(-1920, -200, 1920, 1040);
            Equal(new System.Drawing.Point(-1800, 400),
                  HudPlacement.Place(-1800, 600, true, new System.Drawing.Size(352, 200), left),
                  "works on a monitor with negative coordinates");
        }

        private static void NotifySkipsStopped()
        {
            var h = new Harness(30);
            h.Config.NotifyContext = true;
            h.Config.NotifyFiveHour = false;
            h.Config.ContextWarn = 0.75;
            h.Config.ContextDanger = 0.90;
            h.Config.CompactThreshold = 0.967;

            var snap = new Snapshot();
            var row = Row("s", 950000, 1000000, false);
            snap.Sessions.Add(row);

            h.Notifier.Check(snap, h.Config);
            h.Notifier.Pump();
            Equal(0, h.Shown.Count, "a stopped session over danger is not reported");

            row.ProcessAlive = true;
            h.Now = h.Now.AddSeconds(10);
            h.Notifier.Check(snap, h.Config);
            h.Notifier.Pump();
            Equal(1, h.Shown.Count, "reported once it runs again");
            if (h.Shown.Count == 1) Check(h.Shown[0].StartsWith("Warning"), "as danger");
        }

        /// <summary>
        /// コンテキストの通知は、残りをトークン数で書く。
        /// 以前は「74.0%（圧縮まで残り 23%）」のように分母の違う % を並べていて、
        /// 足して 100 にならないので丸め誤差に見えた（2026-09-20）。
        /// </summary>
        private static void NotifyContextBody()
        {
            var h = new Harness(30);
            h.Config.NotifyContext = true;
            h.Config.NotifyFiveHour = false;
            h.Config.ContextWarn = 0.75;
            h.Config.ContextDanger = 0.90;
            h.Config.CompactThreshold = 0.967;

            var snap = new Snapshot();
            snap.Sessions.Add(Row("s", 750000, 1000000, true));

            h.Notifier.Check(snap, h.Config);
            h.Notifier.Pump();
            Equal(1, h.Shown.Count, "warn is reported");
            if (h.Shown.Count != 1) return;

            var body = h.Shown[0];
            // 967,000 - 750,000 = 217,000
            Check(body.Contains("217,000"), "the remainder is given in tokens: " + body);
            Check(body.Contains("75%"), "the percentage is the one shown on screen: " + body);
            Check(!body.Contains("% left"), "no second percentage on another scale: " + body);
        }

        private static void NotifyEscalation()
        {
            var h = new Harness(30);
            h.FiveHour(60);
            Equal(0, h.Shown.Count, "nothing below warn");
            h.FiveHour(75);
            Equal(1, h.Shown.Count, "warn is reported");
            h.FiveHour(92, 60);
            Equal(2, h.Shown.Count, "danger one minute later is reported despite the 30-minute quiet period");
            if (h.Shown.Count == 2) Check(h.Shown[1].StartsWith("Warning"), "danger uses the warning icon");
        }

        private static void NotifyHysteresis()
        {
            var h = new Harness(0);
            h.FiveHour(92);
            Equal(1, h.Shown.Count, "danger reported");
            h.FiveHour(85);
            h.FiveHour(91);
            Equal(1, h.Shown.Count, "a dip to 85 (within 10 points) does not re-arm");
            h.FiveHour(78);
            h.FiveHour(92);
            Equal(2, h.Shown.Count, "a dip to 78 re-arms danger");
            h.FiveHour(3);
            h.FiveHour(75);
            Equal(3, h.Shown.Count, "after a reset, warn is reported again");
        }

        private static void NotifyQuiet()
        {
            var h = new Harness(30);
            h.FiveHour(92);
            h.FiveHour(50);
            h.FiveHour(93, 60);
            Equal(1, h.Shown.Count, "same level again within 30 minutes is not reported");
            h.FiveHour(50);
            h.FiveHour(94, 31 * 60);
            Equal(2, h.Shown.Count, "after 30 minutes it is reported again");
        }

        // --- 自動起動 ----------------------------------------------------------

        private static void AutoStartTarget()
        {
            var exe = Path.Combine(_temp, "app", "ctxtray.exe");
            Check(AutoStart.PointsTo(exe, exe), "same path");
            Check(AutoStart.PointsTo(exe.ToUpperInvariant(), exe), "case is ignored");
            Check(AutoStart.PointsTo(Path.Combine(_temp, "app", "..", "app", "ctxtray.exe"), exe), "'..' is resolved");
            Check(!AutoStart.PointsTo(Path.Combine(_temp, "old", "ctxtray.exe"), exe), "a moved exe does not match");
            Check(!AutoStart.PointsTo(null, exe), "missing target");
            Check(!AutoStart.PointsTo(string.Empty, exe), "empty target");
            Check(!AutoStart.PointsTo("bad" + (char)0 + "path", exe), "an invalid target does not throw");
        }

        // --- 配色 ----------------------------------------------------------------

        /// <summary>
        /// HUD とトレイが共有する色の規則（Theme.ColorFor）。
        /// 普段は値ごとの色、注意・危険はその色で、値の種類によらない。
        /// </summary>
        private static void ValueColors()
        {
            foreach (var theme in new[] { Theme.Dark(), Theme.Light() })
            {
                var name = theme.IsDark ? "dark" : "light";
                var ids = new Dictionary<string, System.Drawing.Color>
                {
                    { "context", theme.IdContext },
                    { "fiveHour", theme.IdFiveHour },
                    { "weekly", theme.IdWeekly },
                };

                foreach (var kv in ids)
                {
                    Equal(kv.Value.ToArgb(), theme.ColorFor(kv.Key, Level.Normal).ToArgb(), name + " " + kv.Key + " normal");
                    Equal(theme.Warn.ToArgb(), theme.ColorFor(kv.Key, Level.Warn).ToArgb(), name + " " + kv.Key + " warn");
                    Equal(theme.Danger.ToArgb(), theme.ColorFor(kv.Key, Level.Danger).ToArgb(), name + " " + kv.Key + " danger");
                }

                Check(theme.IdContext != theme.IdFiveHour && theme.IdFiveHour != theme.IdWeekly
                      && theme.IdContext != theme.IdWeekly, name + " the three values have distinct colours");
                Equal(theme.IdContext.ToArgb(), theme.ColorFor(null, Level.Normal).ToArgb(),
                      name + " an unknown value uses the context colour");
            }
        }

        /// <summary>
        /// コントラストテーマの配色。色は Windows の組み合わせからだけ取る。
        /// 「自動」のときだけ使い、light / dark を選んだ人の指定は上書きしない。
        /// </summary>
        private static void ContrastTheme()
        {
            var hc = Theme.HighContrast();
            Check(hc.IsHighContrast, "the contrast theme is marked as such");
            Equal(System.Drawing.SystemColors.Window.ToArgb(), hc.Background.ToArgb(),
                  "the background comes from Windows");
            Equal(System.Drawing.SystemColors.WindowText.ToArgb(), hc.TextPrimary.ToArgb(),
                  "so does the text");
            // 値ごとの色は使わない（限られた組み合わせしか使えないので、形で区別する）。
            Equal(hc.IdContext.ToArgb(), hc.IdFiveHour.ToArgb(), "values share one colour");
            Equal(hc.IdFiveHour.ToArgb(), hc.IdWeekly.ToArgb(), "all three of them");

            // 固定の指定はコントラストテーマに乗っ取られない。
            Check(!Theme.Resolve("dark").IsHighContrast, "an explicit dark theme stays dark");
            Check(!Theme.Resolve("light").IsHighContrast, "an explicit light theme stays light");
            Check(!Theme.ResolveForTray("dark").IsHighContrast, "same for the tray");

            // 「自動」はいまの Windows の設定に従う（この PC の状態で判定する）。
            Equal(Theme.HighContrastOn(), Theme.Resolve("auto").IsHighContrast,
                  "auto follows the Windows contrast setting");
        }

        private static void NotifySpacing()
        {
            var h = new Harness(30);
            h.Notifier.ShowInfo("a", "first");
            h.Notifier.ShowInfo("b", "second");
            Equal(1, h.Shown.Count, "only one balloon at a time");
            h.Now = h.Now.AddSeconds(3);
            h.Notifier.Pump();
            Equal(1, h.Shown.Count, "still waiting after 3 seconds");
            h.Now = h.Now.AddSeconds(5);
            h.Notifier.Pump();
            Equal(2, h.Shown.Count, "second one after 7 seconds");
        }
    }
}
