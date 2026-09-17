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
                Run("ModelLimits lookup", ModelLimitsLookup);
                Run("Rate samples: latest org only, incomplete samples skipped", RateSamples);
                Run("JSON writer: ASCII-only output", AsciiJson);
                Run("Transcript: latest usage", TranscriptLatest);
                Run("Sessions: a Desktop tab keeps its Desktop process", TabProcessChoice);
                Run("Levels with slack", LevelsSlack);
                Run("Tray: running sessions only", TrayPicksRunning);
                Run("Notify: stopped sessions are not reported", NotifySkipsStopped);
                Run("Notify: warn then danger within the quiet period", NotifyEscalation);
                Run("Notify: hysteresis", NotifyHysteresis);
                Run("Notify: same level within the quiet period", NotifyQuiet);
                Run("Notify: spacing between balloons", NotifySpacing);
                Run("AutoStart: shortcut target comparison", AutoStartTarget);
                Run("Colors: value colours, amber/red override", ValueColors);
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

            Check(c.Save(), "save succeeds");
            string problem2;
            bool failed2;
            var again = AppConfig.Load(out problem2, out failed2);
            Check(!failed2, "the saved file loads again");
            Equal(123456, again.ModelLimits["claude-example-6"], "modelLimits survives a save");
            Check(!File.Exists(path + ".tmp"), "temporary file is gone after save");
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
            Equal((int?)1000000, ModelLimits.Lookup("claude-fable-5-1"), "point release");
            Equal((int?)null, ModelLimits.Lookup("claude-opus-4-1-20250805"), "unknown model");
            Equal((int?)null, ModelLimits.Lookup("claude-opus-4"), "a shorter name does not match a longer key");
            Equal((int?)null, ModelLimits.Lookup(null), "null");

            var overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "claude-sonnet-5", 200000 },
                { "claude-new", 300000 },
                { "claude-new-2", 400000 },
            };
            Equal((int?)200000, ModelLimits.Lookup("claude-sonnet-5", overrides), "override wins over the table");
            Equal((int?)1000000, ModelLimits.Lookup("claude-opus-5", overrides), "table still used for others");
            Equal((int?)400000, ModelLimits.Lookup("claude-new-2-20270101", overrides), "longest key wins");
            Equal((int?)300000, ModelLimits.Lookup("claude-new-3", overrides), "shorter key when the longer does not match");
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
