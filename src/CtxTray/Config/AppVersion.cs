using System;
using System.Reflection;

namespace CtxTray.Config
{
    /// <summary>
    /// ctxtray 自身の版。
    ///
    /// 不具合の報告を受けたときに「どの版か」が分からないと調べようがないので、
    /// 「ctxtray について」の画面・設定画面の題名・`--version`・`--json` から見られるようにする（2026-09-18）。
    /// 以前はファイルのプロパティを開くしか確かめる方法が無かった。
    ///
    /// 値はビルド時に埋まる AssemblyInformationalVersion（例 `0.1.4+abb6372…`）。
    /// `+` の後ろはコミットの番号で、SDK が付ける。
    /// </summary>
    internal static class AppVersion
    {
        private static string _full;
        private static string _display;

        /// <summary>そのままの文字列（コミット番号も全部）。`--version` と `--json` で出す。</summary>
        public static string Full
        {
            get
            {
                if (_full != null) return _full;

                var value = "?";
                try
                {
                    var asm = typeof(AppVersion).Assembly;
                    var attrs = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                    if (attrs.Length > 0)
                        value = ((AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
                    else if (asm.GetName().Version != null)
                        value = asm.GetName().Version.ToString();
                }
                catch
                {
                    // 版が読めなくても常駐は続ける。
                }

                _full = value;
                return _full;
            }
        }

        /// <summary>画面に出す形（例 `0.1.4 (abb6372)`）。コミット番号は 7 桁に縮める。</summary>
        public static string Display
        {
            get
            {
                if (_display != null) return _display;

                var full = Full;
                var plus = full.IndexOf('+');
                if (plus < 0)
                {
                    _display = full;
                    return _display;
                }

                var number = full.Substring(0, plus);
                var commit = full.Substring(plus + 1);
                if (commit.Length > 7) commit = commit.Substring(0, 7);

                _display = commit.Length == 0 ? number : number + " (" + commit + ")";
                return _display;
            }
        }
    }
}
