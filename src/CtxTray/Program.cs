using System;
using System.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CtxTray.Collect;
using CtxTray.Config;
using CtxTray.Core;
using CtxTray.Native;
using CtxTray.Ui;

namespace CtxTray
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // 引数が無ければ常駐 GUI。あればコンソール用途。
            if (args.Length == 0) return RunTray();
            return RunConsole(args);
        }

        // --- 常駐 ---------------------------------------------------------------

        private static int RunTray()
        {
            // 二重起動を防ぐ。トレイアイコンが 2 つ並ぶのを避けるため。
            bool isNew;
            using (var mutex = new Mutex(true, @"Local\ctxtray-single-instance", out isNew))
            {
                if (!isNew) return 0;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // UI スレッドの例外は Application.ThreadException へ回す（TrayApp が受けて記録する）。
                // 窓を作る前に指定する必要がある。
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                try
                {
                    Application.Run(new TrayApp());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Strings.Format("app.crashed", ex),
                                    "ctxtray", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }

                GC.KeepAlive(mutex);
                return 0;
            }
        }

        // --- コンソール ---------------------------------------------------------

        private static int RunConsole(string[] args)
        {
            AttachToParentConsole();

            // 引数の解析より先に設定を読む。--help も利用者の言語で出したいため。
            string problem;
            var config = AppConfig.Load(out problem);
            Strings.Apply(config.Language);

            var json = false;
            var includeArchived = false;
            var includeExternal = true;
            var verifyWeekly = false;
            var iconPreview = false;
            var hudPreview = false;
            var appIconPreview = false;
            var writeAppIcon = false;
            string outDir = null;

            foreach (var a in args)
            {
                // 引数でないもの（出力先）は先に拾う。
                if (!a.StartsWith("-", StringComparison.Ordinal)) { outDir = a; continue; }
                switch (a.ToLowerInvariant())
                {
                    case "--json": json = true; break;
                    case "--status": break;   // 表形式。引数を渡すこと自体が指定になる
                    case "--include-archived": includeArchived = true; break;
                    case "--no-external": includeExternal = false; break;
                    case "--verify-weekly": verifyWeekly = true; break;
                    case "--icon-preview": iconPreview = true; break;
                    case "--hud-preview": hudPreview = true; break;
                    case "--app-icon-preview": appIconPreview = true; break;
                    case "--write-app-icon": writeAppIcon = true; break;
                    case "--version":
                    case "-v":
                        Console.WriteLine("ctxtray " + AppVersion.Full);
                        return 0;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return 0;
                    default:
                        Console.Error.WriteLine(Strings.Format("cli.unknownArg", a));
                        PrintHelp();
                        return 2;
                }
            }

            // 出力先の指定は見本の書き出しのときだけ意味を持つ。それ以外で渡されたら打ち間違いとして知らせる。
            if (!iconPreview && !hudPreview && !appIconPreview && !writeAppIcon && outDir != null)
            {
                Console.Error.WriteLine(Strings.Format("cli.unknownArg", outDir));
                PrintHelp();
                return 2;
            }

            if (iconPreview) return WriteIconPreview(config, outDir);
            if (hudPreview) return WriteHudPreview(outDir);
            if (appIconPreview) return WriteAppIconPreview(outDir);
            if (writeAppIcon) return WriteAppIcon(outDir);

            Snapshot snap;
            try
            {
                snap = SnapshotBuilder.Build(includeExternal, includeArchived, config.ModelLimits);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            if (verifyWeekly) PrintWeeklyVerification(snap);
            // パイプやファイルに出すときは ASCII だけで書く。受け取る側の文字コードによらず壊れない。
            else if (json) Console.WriteLine(ToJson(snap, !ConsoleOutput.StdoutIsConsole));
            else PrintTable(snap);

            return 0;
        }

        /// <summary>
        /// WinExe は自前のコンソールを持たないので、ターミナルから呼ばれたときは
        /// 親のコンソールに結び付ける。書き方（文字コード）は出力先に合わせる（Native/ConsoleOutput.cs）。
        ///
        /// 注意: GUI サブシステムの exe は、シェルが終了を待たない。
        /// PowerShell などから直接叩くとプロンプトが先に戻る。
        /// パイプやリダイレクトで受ければ待つので、機械的な利用はそちらを使う。
        /// </summary>
        private static void AttachToParentConsole()
        {
            ConsoleOutput.Attach();
        }

        private static void PrintHelp()
        {
            // ヘルプだけは行ごとに訳すと崩れるので、まとめて切り替える。
            if (Strings.IsJapanese)
            {
                Console.WriteLine("ctxtray - Claude Desktop / Claude Code の状態を読む（読み取りのみ）");
                Console.WriteLine();
                Console.WriteLine("  ctxtray                     引数なしで常駐（トレイ + HUD）");
                Console.WriteLine("  ctxtray --status            状態を表形式で 1 回表示");
                Console.WriteLine("  ctxtray --json              状態を JSON で出力");
                Console.WriteLine("  ctxtray --no-external       Desktop のタブだけを対象にする");
                Console.WriteLine("  ctxtray --include-archived  終了済みタブも含める");
                Console.WriteLine("  ctxtray --verify-weekly     週間枠リセットの推定過程を表示");
                Console.WriteLine("  ctxtray --icon-preview [dir] トレイアイコンの見本を PNG に出力");
                Console.WriteLine("  ctxtray --hud-preview [dir]  HUD の見本（架空のデータ）を PNG に出力");
                Console.WriteLine("  ctxtray --app-icon-preview [dir]  アプリのアイコンの見本を PNG に出力");
                Console.WriteLine("  ctxtray --write-app-icon [path]   アプリのアイコンを .ico に書き出す（開発用）");
                Console.WriteLine("  ctxtray --version           版を表示");
            }
            else
            {
                Console.WriteLine("ctxtray - read Claude Desktop / Claude Code status (read-only)");
                Console.WriteLine();
                Console.WriteLine("  ctxtray                     run resident (tray + HUD)");
                Console.WriteLine("  ctxtray --status            print the current state as a table");
                Console.WriteLine("  ctxtray --json              print the current state as JSON");
                Console.WriteLine("  ctxtray --no-external       only Claude Desktop tabs");
                Console.WriteLine("  ctxtray --include-archived  include closed tabs");
                Console.WriteLine("  ctxtray --verify-weekly     show how the weekly reset was derived");
                Console.WriteLine("  ctxtray --icon-preview [dir] write a PNG sheet of the tray icons");
                Console.WriteLine("  ctxtray --hud-preview [dir]  write PNGs of the panel with made-up data");
                Console.WriteLine("  ctxtray --app-icon-preview [dir]  write a PNG sheet of the app icon");
                Console.WriteLine("  ctxtray --write-app-icon [path]   write the app icon as .ico (for maintainers)");
                Console.WriteLine("  ctxtray --version           print the version");
            }
        }

        /// <summary>
        /// トレイアイコンの見本を PNG に書き出す。
        ///
        /// 全スタイル・全レベルを 16 / 20 / 24 / 32px（表示倍率 100 / 125 / 150 / 200%）で並べる。
        /// アイコンの見え方を確かめたいときや、表示がおかしいと報告を受けたときに使う。
        ///
        /// 描画の確認は、アプリの内部を外から呼び出すのではなく、必ずこの正規の入口を使う。
        /// </summary>
        private static int WriteIconPreview(AppConfig config, string outDir)
        {
            var dir = string.IsNullOrEmpty(outDir) ? Environment.CurrentDirectory : outDir;
            var path = System.IO.Path.Combine(dir, "ctxtray-icons.png");

            var themes = new[] { Theme.Dark(), Theme.Light() };
            var levels = new[] { Level.Normal, Level.Warn, Level.Danger };
            var sizes = new[] { 16, 20, 24, 32 };
            var styles = new[] { TrayIconRenderer.LettersStyle, TrayIconRenderer.GlyphsStyle,
                                 TrayIconRenderer.PercentStyle, TrayIconRenderer.BarsStyle };
            const int columns = 3;
            // 数字の境目の段（1 桁・3 桁・値なし）。見本の 78/46/19 だけでは幅の違いが見られない。
            const int extraRows = 1;

            // 1 セル = 大きさごとに「実寸 + 余白 + 2 倍拡大 + 余白」。1 行にセルを 3 つ並べる。
            var cell = 0;
            foreach (var s in sizes) cell += s + 6 + s * 2 + 10;

            const int labelW = 120;
            // 32px の 2 倍（64）と、その下の説明文。詰めると説明文が次の行の見出しに見える。
            const int rowH = 96;
            var width = labelW + cell * columns + 12;
            var rowsPerTheme = styles.Length * levels.Length + extraRows;
            var height = rowH * rowsPerTheme * themes.Length + 16;

            using (var bmp = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(Theme.FontFamily, 8f))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                var y = 8;
                foreach (var theme in themes)
                {
                    using (var back = new SolidBrush(theme.IsDark ? Color.FromArgb(32, 32, 32)
                                                                  : Color.FromArgb(243, 243, 243)))
                        g.FillRectangle(back, 0, y - 8, width, rowH * rowsPerTheme + 8);

                    using (var ink = new SolidBrush(theme.TextPrimary))
                    {
                        foreach (var style in styles)
                        {
                            var bars = style == TrayIconRenderer.BarsStyle;
                            foreach (var level in levels)
                            {
                                g.DrawString(style + " " + level, font, ink, 6, y + 24);

                                for (var col = 0; col < columns; col++)
                                {
                                    var cellX = labelW + cell * col;
                                    List<TrayGauge> gauges;
                                    string caption;

                                    if (bars)
                                    {
                                        // 1 個にまとめるモード: 3 本・2 本・1 本。
                                        // レベルは先頭のバーだけに付けて、普段の色と混ざったときの見え方を確かめる。
                                        var count = columns - col;
                                        gauges = new List<TrayGauge>();
                                        for (var i = 0; i < count; i++)
                                        {
                                            var value = AppConfig.AllTrayValues[i];
                                            gauges.Add(PreviewSample(value, i == 0 ? level : Level.Normal));
                                        }
                                        caption = count + (count == 1 ? " bar" : " bars");
                                    }
                                    else
                                    {
                                        var value = AppConfig.AllTrayValues[col];
                                        gauges = new List<TrayGauge> { PreviewSample(value, level) };
                                        caption = value;
                                    }

                                    var x = cellX;
                                    foreach (var size in sizes)
                                    {
                                        using (var icon = TrayIconRenderer.Render(gauges, style, theme, size))
                                        using (var shot = icon.ToBitmap())
                                        {
                                            // 実寸は拡大図（高さ 2 倍）の縦の中央に置く。
                                            g.DrawImage(shot, x, y + size / 2, size, size);
                                            g.DrawImage(shot, x + size + 6, y, size * 2, size * 2);
                                        }
                                        x += size + 6 + size * 2 + 10;
                                    }
                                    g.DrawString(caption, font, ink, cellX, y + 66);
                                }
                                y += rowH;
                            }
                        }

                        g.DrawString(TrayIconRenderer.PercentStyle + " edges", font, ink, 6, y + 24);
                        var edges = new[]
                        {
                            EdgeSample("context", 5, Level.Normal),
                            EdgeSample("fiveHour", 100, Level.Danger),
                            new TrayGauge { Value = "weekly" },
                        };
                        for (var col = 0; col < columns; col++)
                        {
                            var gauge = edges[col];
                            var cellX = labelW + cell * col;
                            var x = cellX;
                            foreach (var size in sizes)
                            {
                                using (var icon = TrayIconRenderer.Render(new List<TrayGauge> { gauge },
                                                                          TrayIconRenderer.PercentStyle, theme, size))
                                using (var shot = icon.ToBitmap())
                                {
                                    g.DrawImage(shot, x, y + size / 2, size, size);
                                    g.DrawImage(shot, x + size + 6, y, size * 2, size * 2);
                                }
                                x += size + 6 + size * 2 + 10;
                            }
                            var caption = gauge.Value + " " + (gauge.HasValue ? gauge.Percent.Value + "%" : "no value");
                            g.DrawString(caption, font, ink, cellX, y + 66);
                        }
                        y += rowH;
                    }
                }

                try
                {
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            Console.WriteLine(path);
            return 0;
        }

        /// <summary>
        /// アプリのアイコン（タスクバー・Explorer に出るもの）の見本を PNG に書き出す。
        ///
        /// 実際に使う大きさを 1:1 で並べ、明るい背景と暗いタスクバーの両方に置いて見せる。
        /// 小さい方で潰れていないかを実寸で確かめるための入口。
        /// </summary>
        private static int WriteAppIconPreview(string outDir)
        {
            var dir = string.IsNullOrEmpty(outDir) ? Environment.CurrentDirectory : outDir;
            var path = System.IO.Path.Combine(dir, "ctxtray-app-icon.png");

            var sizes = new[] { 16, 20, 24, 32, 48, 64 };
            const int detail = 128;   // 形を確かめるための拡大図
            const int gap = 16;
            const int labelH = 22;

            var rowH = detail + labelH + gap * 2;
            var width = gap;
            foreach (var s in sizes) width += s + gap;
            width += detail + gap;

            var strips = new[]
            {
                // タスクバーの地に近い色と、Explorer の一覧に近い色。
                new { Back = Color.FromArgb(32, 32, 32), Ink = Color.FromArgb(230, 234, 242) },
                new { Back = Color.FromArgb(243, 243, 243), Ink = Color.FromArgb(27, 31, 39) },
            };

            using (var bmp = new Bitmap(width, rowH * strips.Length))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(Theme.FontFamily, 8f))
            {
                var y = 0;
                foreach (var strip in strips)
                {
                    using (var back = new SolidBrush(strip.Back))
                        g.FillRectangle(back, 0, y, width, rowH);

                    using (var ink = new SolidBrush(strip.Ink))
                    {
                        var x = gap;
                        // 下端を揃える。タスクバーに並んだときの見え方に近い。
                        var baseline = y + gap + detail;

                        foreach (var size in sizes)
                        {
                            using (var icon = AppIconRenderer.Render(size))
                                g.DrawImage(icon, x, baseline - size, size, size);
                            g.DrawString(size + "px", font, ink, x, baseline + 4);
                            x += size + gap;
                        }

                        using (var icon = AppIconRenderer.Render(detail))
                            g.DrawImage(icon, x, baseline - detail, detail, detail);
                        g.DrawString(detail + "px", font, ink, x, baseline + 4);
                    }

                    y += rowH;
                }

                try
                {
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            Console.WriteLine(path);
            return 0;
        }

        /// <summary>
        /// アプリのアイコンを .ico に書き出す（リポジトリの src/CtxTray/ctxtray.ico を作り直すため）。
        /// 配布する exe には埋め込み済みなので、利用者が使うことはない。
        /// </summary>
        private static int WriteAppIcon(string outPath)
        {
            var path = string.IsNullOrEmpty(outPath)
                ? System.IO.Path.Combine(Environment.CurrentDirectory, "ctxtray.ico")
                : outPath;

            try
            {
                AppIconRenderer.WriteIco(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            Console.WriteLine(path);
            return 0;
        }

        /// <summary>
        /// 見本用のゲージ。値ごとに長さを変え、長い・中くらい・短いバーを同時に見られるようにする。
        /// </summary>
        private static TrayGauge EdgeSample(string value, int pct, Level level)
        {
            return new TrayGauge { Value = value, Fraction = pct / 100.0, Percent = pct, Level = level };
        }

        private static TrayGauge PreviewSample(string value, Level level)
        {
            var pct = value == "context" ? 78 : (value == "fiveHour" ? 46 : 19);
            return new TrayGauge
            {
                Value = value,
                Fraction = pct / 100.0,
                Percent = pct,
                Level = level,
            };
        }

        /// <summary>
        /// HUD の見本を PNG に書き出す。README の画像と、見た目の確認に使う。
        ///
        /// 中身は架空のデータ。利用者の実際のセッション名やフォルダを画像に残さないため。
        /// 英語・日本語 × ダーク・ライトの 4 枚で、大きさはこの画面の表示倍率に従う（実寸）。
        /// 設定は既定値を使う（初めて使う人が見る姿）。
        /// </summary>
        /// <summary>
        /// 見本に出す状態。normal は README の画像に使うもので、
        /// 残りは「普段は見えないが、見え方を確かめたい」状態（2026-09-20）。
        /// </summary>
        private static readonly string[] HudPreviewStates =
            { "danger", "reference", "stopped", "welcome" };

        private static int WriteHudPreview(string outDir)
        {
            var dir = string.IsNullOrEmpty(outDir) ? Environment.CurrentDirectory : outDir;
            var written = new List<string>();

            try
            {
                foreach (var lang in new[] { "en", "ja" })
                {
                    Strings.Apply(lang);
                    foreach (var theme in new[] { "dark", "light" })
                    {
                        var path = System.IO.Path.Combine(dir, "ctxtray-hud-" + lang + "-" + theme + ".png");
                        using (var bmp = RenderHud(theme, lang == "ja", "normal"))
                            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        written.Add(path);

                        // ほかの状態は 1 枚に縦に並べる（1 状態 1 ファイルだと見比べにくい）。
                        var sheet = System.IO.Path.Combine(
                            dir, "ctxtray-hud-states-" + lang + "-" + theme + ".png");
                        using (var bmp = RenderHudStates(theme, lang == "ja"))
                            bmp.Save(sheet, System.Drawing.Imaging.ImageFormat.Png);
                        written.Add(sheet);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            foreach (var p in written) Console.WriteLine(p);
            return 0;
        }

        /// <summary>見本のパネルを 1 枚描く。表示はしない（寸法と描画にハンドルだけ要る）。</summary>
        private static Bitmap RenderHud(string theme, bool japanese, string state)
        {
            using (var hud = new HudForm(new AppConfig { Theme = theme }))
            {
                GC.KeepAlive(hud.Handle);
                hud.SetSnapshot(SampleSnapshot(japanese, state));
                if (state == "welcome")
                    hud.ShowWelcome(Strings.Format("app.welcome", "Ctrl+Alt+C"), 600);

                var bmp = new Bitmap(hud.Width, hud.Height);
                hud.DrawToBitmap(bmp, new Rectangle(0, 0, hud.Width, hud.Height));
                return bmp;
            }
        }

        /// <summary>普段は見えない状態を縦に並べた 1 枚。</summary>
        private static Bitmap RenderHudStates(string theme, bool japanese)
        {
            var panels = new List<Bitmap>();
            try
            {
                foreach (var state in HudPreviewStates) panels.Add(RenderHud(theme, japanese, state));

                const int gap = 12;
                var width = 0;
                var height = gap;
                foreach (var p in panels)
                {
                    width = Math.Max(width, p.Width);
                    height += p.Height + gap;
                }

                var sheet = new Bitmap(width + gap * 2, height);
                using (var g = Graphics.FromImage(sheet))
                {
                    // タスクバーの地色に近い色を敷く（--icon-preview と同じ値）。
                    g.Clear(theme == "light" ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32));
                    var y = gap;
                    foreach (var p in panels)
                    {
                        g.DrawImageUnscaled(p, gap, y);
                        y += p.Height + gap;
                    }
                }
                return sheet;
            }
            finally
            {
                foreach (var p in panels) p.Dispose();
            }
        }

        /// <summary>
        /// 見本用の状態。1 行目は注意の色（圧縮点の 77%）、3 行目は Desktop 以外のセッション。
        /// 5時間枠のリセット時刻は既定の設定（残り 30 分から表示）でも出るよう、18 分後にする。
        /// </summary>
        /// <param name="state">
        /// normal … README の画像に使う普段の状態
        /// danger … コンテキストも枠も危険。色と（あれば）形の出方を見る
        /// reference … Claude Desktop が起動していない。レート枠が淡く、見出しに「参考値」
        /// stopped … 止まっているセッションが混ざっている
        /// welcome … 初めての起動の案内（呼び出し側が ShowWelcome する）
        /// </param>
        private static Snapshot SampleSnapshot(bool japanese, string state = "normal")
        {
            var now = DateTime.UtcNow;
            var danger = state == "danger";
            var reference = state == "reference";

            var snap = new Snapshot
            {
                GeneratedAtUtc = now,
                DesktopRunning = !reference,
                Freshness = reference ? RateFreshness.Reference : RateFreshness.Current,
                HiddenSessionCount = 4,
                RateLimits = new RateLimitStatus
                {
                    FiveHourPct = danger ? 96 : 47,
                    WeeklyPct = danger ? 83 : 28,
                    SampledAtUtc = now,
                    FiveHourWindowStartUtc = now.AddMinutes(18).AddHours(-5),
                    NextFiveHourResetUtc = now.AddMinutes(18),
                },
            };

            // 744K は圧縮点（967K）の 77%。既定の注意（75%）を超え、値の色が黄に替わる例になる。
            // 危険の見本は 940K（圧縮点の 97%）。
            snap.Sessions.Add(SampleRow(japanese ? "認証まわりの整理" : "Refactor auth module",
                                        danger ? 940000 : 744000, true, false, true));
            snap.Sessions.Add(SampleRow(japanese ? "不安定なテストの修正" : "Fix flaky tests",
                                        338000, false, false, state != "stopped"));
            snap.Sessions.Add(SampleRow("my-project", 221000, false, true, true));
            return snap;
        }

        private static SessionRow SampleRow(string title, int tokens, bool active, bool external,
                                            bool alive = true)
        {
            const int limit = 1000000;
            return new SessionRow
            {
                Title = title,
                Model = "claude-opus-5",
                ModelKnown = true,
                ContextTokens = tokens,
                ContextLimit = limit,
                ContextPct = Math.Round(100.0 * tokens / limit, 1),
                IsActive = active,
                IsExternal = external,
                ProcessAlive = alive,
                LastActivityUtc = DateTime.UtcNow,
            };
        }

        /// <summary>
        /// 週間枠リセット推定の検算。
        /// 観測が何回取れていて、交差がどこまで狭まったかをそのまま見せる。
        /// </summary>
        private static void PrintWeeklyVerification(Snapshot snap)
        {
            Console.WriteLine();
            Console.WriteLine(Strings.Get("vw.title"));
            Console.WriteLine(new string('-', 64));
            Console.WriteLine(Strings.Format("vw.samples", snap.Samples.Count));

            var intervals = RateLimits.FindWeeklyResetIntervals(snap.Samples);
            Console.WriteLine(Strings.Format("vw.observed", intervals.Count));
            Console.WriteLine();

            foreach (var iv in intervals)
            {
                Console.WriteLine(Strings.Format("vw.interval",
                    iv.Before, iv.After,
                    Stamp(iv.FromUtc), Stamp(iv.ToUtc),
                    iv.Width.TotalHours));
            }

            Console.WriteLine();
            var est = WeeklyReset.Estimate7Day(snap.Samples);
            if (!est.HasObservations)
            {
                Console.WriteLine(Strings.Get("vw.noObs"));
                return;
            }

            Console.WriteLine(Strings.Format("vw.width", est.WidthMinutes / 60.0));
            Console.WriteLine(Strings.Format("vw.weekday",
                est.Weekday.HasValue ? Strings.Weekday(est.Weekday.Value) : "?"));
            Console.WriteLine(Strings.Format("vw.next",
                est.NextResetUtc.HasValue
                    ? est.NextResetUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : Strings.Get("vw.notYet")));
            Console.WriteLine();
            Console.WriteLine(Strings.Format("vw.hud", WeeklyReset.Hint(snap) ?? Strings.Get("vw.nothing")));
        }

        // --- JSON 出力 ----------------------------------------------------------

        private static string ToJson(Snapshot snap, bool asciiOnly)
        {
            var root = new JObj()
                // 不具合の報告にはこの出力を貼ってもらうので、どの版で採ったかを先頭に入れる。
                .Add("version", AppVersion.Full)
                .Add("generated_at", Local(snap.GeneratedAtUtc))
                .Add("desktop_running", snap.DesktopRunning)
                .Add("freshness", snap.Freshness.ToString().ToLowerInvariant())
                .Add("paths", new JObj()
                    .Add("data_root", snap.DataRoot)
                    .Add("config_dir", snap.ConfigDir));

            if (snap.RateLimits == null)
            {
                root.Add("rate_limits", null);
            }
            else
            {
                var r = snap.RateLimits;
                root.Add("rate_limits", new JObj()
                    .Add("five_hour_pct", r.FiveHourPct)
                    .Add("seven_day_pct", r.WeeklyPct)
                    .Add("sampled_at", Local(r.SampledAtUtc))
                    .Add("staleness_sec", r.StalenessSec)
                    .Add("fh_window_start", LocalOrNull(r.FiveHourWindowStartUtc))
                    .Add("next_fh_reset", LocalOrNull(r.NextFiveHourResetUtc)));
            }

            var rows = new List<object>();
            foreach (var s in snap.Sessions)
            {
                rows.Add(new JObj()
                    .Add("title", s.Title)
                    .Add("cli_session_id", s.CliSessionId)
                    .Add("cwd", s.Cwd)
                    .Add("model", s.Model)
                    .Add("model_known", s.ModelKnown)
                    .Add("effort", s.Effort)
                    .Add("context_tokens", s.ContextTokens)
                    .Add("context_limit", s.ContextLimit)
                    .Add("context_pct", s.ContextPct)
                    .Add("last_measured", LocalOrNull(s.LastMeasuredUtc))
                    .Add("last_activity", LocalOrNull(s.LastActivityUtc))
                    .Add("is_active", s.IsActive)
                    .Add("is_external", s.IsExternal)
                    .Add("process_alive", s.ProcessAlive)
                    .Add("pid", s.Pid == 0 ? null : (object)s.Pid)
                    .Add("entrypoint", s.Entrypoint)
                    .Add("transcript", s.TranscriptPath));
            }
            root.Add("sessions", rows);
            root.Add("diag", snap.Diag.Summary);

            return JObj.Write(root, asciiOnly);
        }

        private static string Local(DateTime utc)
        {
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 曜日つきの短い日時。曜日は OS のロケールではなく表示言語に合わせる
        /// （英語表示なのに「(火)」と出るのを避ける）。
        /// </summary>
        private static string Stamp(DateTime utc)
        {
            var t = utc.ToLocalTime();
            return t.ToString("MM-dd", CultureInfo.InvariantCulture)
                 + "(" + Strings.Weekday(t.DayOfWeek) + ") "
                 + t.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string LocalOrNull(DateTime? utc)
        {
            return utc.HasValue ? Local(utc.Value) : null;
        }

        // --- 表形式 -------------------------------------------------------------

        private static void PrintTable(Snapshot snap)
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 74));
            Console.WriteLine(Strings.Format("cli.header", Local(snap.GeneratedAtUtc)));
            Console.WriteLine(new string('=', 74));
            Console.WriteLine();

            var r = snap.RateLimits;
            if (r == null)
            {
                Console.WriteLine(Strings.Get("cli.rateUnavail"));
            }
            else
            {
                string note;
                switch (snap.Freshness)
                {
                    case RateFreshness.Current: note = Strings.Get("cli.current"); break;
                    case RateFreshness.Behind:  note = Strings.Get("cli.lowerBound"); break;
                    default:                    note = Strings.Get("cli.reference"); break;
                }

                // 値の横の印（▲ / +）は付けない。鮮度は見出しの説明文だけで伝える。
                Console.WriteLine(Strings.Format("cli.rateTitle", note));
                Console.WriteLine("{0}: {1,3}%    {2}",
                                  Strings.Get("cli.fiveHour"), r.FiveHourPct, Bar(r.FiveHourPct));
                Console.WriteLine("{0}: {1,3}%    {2}",
                                  Strings.Get("cli.weekly"), r.WeeklyPct, Bar(r.WeeklyPct));
                Console.WriteLine(Strings.Format("cli.sampledAt", Local(r.SampledAtUtc), Age(r.StalenessSec)));
                if (r.NextFiveHourResetUtc.HasValue)
                    Console.WriteLine(Strings.Format("cli.nextReset", Local(r.NextFiveHourResetUtc.Value)));
            }

            Console.WriteLine();
            Console.WriteLine(Strings.Get("cli.sessions"));

            if (snap.Sessions.Count == 0)
                Console.WriteLine(Strings.Get("cli.none"));

            foreach (var s in snap.Sessions)
            {
                var active = s.IsActive ? "*" : " ";
                var live = s.ProcessAlive ? "●" : "○";
                var kind = s.IsExternal ? "ext" : "   ";
                var title = string.IsNullOrEmpty(s.Title) ? Strings.Get("hud.untitled") : s.Title;
                var prefix = string.Format("  {0}{1} {2} {3}", active, live, kind, Pad(title, 24));

                if (!s.ModelKnown)
                {
                    Console.WriteLine("{0}  {1}", prefix,
                        Strings.Format("cli.unknownModel", s.Model, s.ContextTokens ?? 0));
                }
                else if (!s.ContextPct.HasValue)
                {
                    Console.WriteLine("{0}  {1}", prefix, Strings.Get("cli.noUsage"));
                }
                else
                {
                    Console.WriteLine("{0} {1,5:0.0}%  {2,9:N0} / {3:N0}  {4}", prefix,
                        s.ContextPct.Value, s.ContextTokens ?? 0, s.ContextLimit ?? 0,
                        Bar(s.ContextPct.Value));
                }
            }

            if (!snap.Diag.IsEmpty)
            {
                Console.WriteLine();
                Console.WriteLine(Strings.Format("cli.diag", snap.Diag.Summary));
            }

            Console.WriteLine();
        }

        private static string Age(int seconds)
        {
            if (seconds < 120) return Strings.Format("cli.secondsAgo", seconds);
            return Strings.Format("cli.minutesAgo", seconds / 60.0);
        }

        private static string Bar(double pct)
        {
            const int width = 20;
            var n = (int)Math.Round(pct / 100.0 * width);
            if (n < 0) n = 0;
            if (n > width) n = width;
            return new string('#', n) + new string('.', width - n);
        }

        /// <summary>
        /// 全角文字はコンソール上で 2 セル幅になるので、文字数ではなく表示幅で詰める。
        /// </summary>
        private static string Pad(string s, int width)
        {
            if (s == null) s = string.Empty;
            var sb = new StringBuilder();
            var w = 0;
            foreach (var ch in s)
            {
                var cw = IsWide(ch) ? 2 : 1;
                if (w + cw > width - 1) { sb.Append('…'); w += 1; break; }
                sb.Append(ch);
                w += cw;
            }
            return sb.ToString() + new string(' ', Math.Max(0, width - w));
        }

        private static bool IsWide(char ch)
        {
            int c = ch;
            return (c >= 0x1100 && c <= 0x115F)
                || (c >= 0x2E80 && c <= 0xA4CF)
                || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFE30 && c <= 0xFE6F)
                || (c >= 0xFF00 && c <= 0xFF60)
                || (c >= 0xFFE0 && c <= 0xFFE6);
        }
    }
}
