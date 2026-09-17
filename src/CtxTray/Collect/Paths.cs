using System;
using System.Collections.Generic;
using System.IO;

namespace CtxTray.Collect
{
    /// <summary>
    /// 読み取りに失敗した理由を貯める。
    ///
    /// 「読めなかった」と「使用量がゼロ」を同じ空データで表すと、後から原因が分からない。
    /// 開発中に実際にこれに詰まり、15 分ぶんの空ログを貯めてから気づいた
    /// （原因は下の MSIX のパス）。黙って空を返さないことをこのクラスで強制する。
    /// </summary>
    internal sealed class Diagnostics
    {
        private readonly List<string> _messages = new List<string>();

        public void Add(string message)
        {
            if (!string.IsNullOrEmpty(message) && !_messages.Contains(message))
                _messages.Add(message);
        }

        public bool IsEmpty { get { return _messages.Count == 0; } }

        public List<string> Messages { get { return _messages; } }

        public string Summary { get { return string.Join("; ", _messages.ToArray()); } }
    }

    /// <summary>
    /// 読みに行く場所の解決。
    /// </summary>
    internal static class Paths
    {
        /// <summary>
        /// Claude Desktop のデータフォルダを探す。
        ///
        /// ★ ここが最大の罠（docs/how-it-works.md の 1 節）。
        ///   Claude Desktop は MSIX パッケージなので %APPDATA%\Claude への書き込みが
        ///   パッケージ専用の場所へリダイレクトされる。
        ///
        ///     実体   : %LOCALAPPDATA%\Packages\Claude_xxxxx\LocalCache\Roaming\Claude\
        ///     仮想   : %APPDATA%\Claude\   ← パッケージコンテナ内のプロセスからしか見えない
        ///
        ///   ctxtray はコンテナの外で動くので、%APPDATA% 決め打ちだと必ず空になる。
        ///   開発中は Claude Code (コンテナ内) から実行すると動いてしまうため気づけない。
        ///   実体パスを優先し、見つからないときだけ %APPDATA% に落とす。
        ///
        ///   パッケージ名は決め打ちせずワイルドカードで拾う。
        /// </summary>
        public static string FindClaudeDataRoot(Diagnostics diag)
        {
            var candidates = new List<string>();

            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                var packages = Path.Combine(localAppData, "Packages");
                if (Directory.Exists(packages))
                {
                    string[] dirs;
                    try { dirs = Directory.GetDirectories(packages, "Claude_*"); }
                    catch { dirs = new string[0]; }

                    foreach (var d in dirs)
                        candidates.Add(Path.Combine(d, @"LocalCache\Roaming\Claude"));
                }
            }

            // MSIX 以外の配布形態に戻った場合の保険。
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
                candidates.Add(Path.Combine(appData, "Claude"));

            // まず「レートリミットのファイルがある」候補を優先する。
            // 複数アカウントやパッケージ残骸で候補が複数出ることがあるため。
            foreach (var c in candidates)
            {
                if (File.Exists(Path.Combine(c, "plan-usage-history.json")))
                    return c;
            }

            // ファイルがまだ無いだけ、という場合もあるのでフォルダの存在で再判定。
            foreach (var c in candidates)
            {
                if (Directory.Exists(c))
                {
                    diag.Add(Config.Strings.Get("diag.noUsageFileYet"));
                    return c;
                }
            }

            diag.Add(Config.Strings.Get("diag.noDataRoot"));
            return null;
        }

        /// <summary>
        /// Claude Code の設定フォルダ（transcript とセッション登録の親）。
        ///
        /// CLAUDE_CONFIG_DIR で移せるので、決め打ちにしない。
        /// なおこのフォルダは Claude Code が書いており、MSIX のリダイレクト対象ではない。
        /// </summary>
        public static string FindClaudeCodeConfigDir(Diagnostics diag)
        {
            var overridden = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrEmpty(overridden) && Directory.Exists(overridden))
                return overridden;

            var home = Environment.GetEnvironmentVariable("USERPROFILE");
            if (string.IsNullOrEmpty(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(home))
            {
                var dir = Path.Combine(home, ".claude");
                if (Directory.Exists(dir)) return dir;
            }

            diag.Add(Config.Strings.Get("diag.noConfigDir"));
            return null;
        }

        /// <summary>
        /// 書き込み中のファイルでも読めるように共有指定して全文を読む。
        /// Claude Code / Desktop が同じファイルを開いたまま追記していることがある。
        /// </summary>
        public static string ReadAllTextShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, new System.Text.UTF8Encoding(false), true))
            {
                return sr.ReadToEnd();
            }
        }
    }
}
