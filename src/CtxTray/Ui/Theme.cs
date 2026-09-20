using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using CtxTray.Core;

namespace CtxTray.Ui
{
    /// <summary>
    /// 配色と書体。HUD・トレイアイコン・設定画面が同じものを使う。
    ///
    /// 既定では Windows のライト/ダーク設定に追従する。
    /// 明るいデスクトップに黒い箱が浮くのを避けるため。
    /// 追従はレジストリを読むだけで、ポーリングはしない
    /// （変更は WM_SETTINGCHANGE で通知される。HudForm 側で拾う）。
    /// </summary>
    internal sealed class Theme
    {
        public bool IsDark;

        /// <summary>
        /// Windows のコントラストテーマから作った配色か。
        /// 独自の色を足さない（角丸の飾りや淡い色を重ねない）ための目印。
        /// </summary>
        public bool IsHighContrast;

        public Color Background;
        public Color Border;
        public Color Separator;
        public Color TextPrimary;
        public Color TextSecondary;
        public Color BarTrack;

        /// <summary>
        /// トレイアイコンのバーの下地。HUD のバーより一段はっきりさせる。
        /// タスクバーの地色と近すぎると、バーがあること自体が見えなくなるため。
        /// </summary>
        public Color TrayTrack;

        /// <summary>
        /// 自前のスクロールバーのつまみ（Ui/ThinScrollBar.cs）。
        /// 背景に対して控えめだが、つまめる物だと分かる程度の差は付ける。
        /// Hot はマウスを乗せたときとドラッグ中。
        /// </summary>
        public Color ScrollThumb;
        public Color ScrollThumbHot;

        /// <summary>
        /// 入力欄（数値・ドロップダウン・キーの欄）の地と枠。
        /// 枠は Border より一段はっきりさせる。地が背景に近いので、
        /// Border と同じ濃さだと「押せる欄」に見えない。
        /// マウスを乗せたときは TextSecondary、入力中は Normal（青）にする。
        /// </summary>
        public Color Field;
        public Color FieldBorder;

        public Color Normal;
        public Color Warn;
        public Color Danger;

        // --- 値の識別色 -------------------------------------------------------
        //
        // 深刻度の色とは別枠。普段は値ごとにこの色で描き、何の値かを色でも伝える。
        // 注意・危険になった値だけ、深刻度の色（黄・赤）に置き換える（ColorFor）。
        public Color IdContext;
        public Color IdFiveHour;
        public Color IdWeekly;

        /// <summary>値の名前から識別色を引く。知らない名前ならコンテキスト扱い。</summary>
        public Color IdentityFor(string value)
        {
            if (string.Equals(value, "fiveHour", StringComparison.OrdinalIgnoreCase)) return IdFiveHour;
            if (string.Equals(value, "weekly", StringComparison.OrdinalIgnoreCase)) return IdWeekly;
            return IdContext;
        }

        /// <summary>レベルに対応する色。表示の色を閾値と別の根拠から決めないための入口。</summary>
        public Color For(Level level)
        {
            switch (level)
            {
                case Level.Danger: return Danger;
                case Level.Warn: return Warn;
                default: return Normal;
            }
        }

        /// <summary>
        /// 値を描く色。普段は値ごとの識別色、注意・危険になった値だけ黄・赤。
        /// HUD とトレイアイコンが同じ規則で塗るよう、ここ 1 か所で決める。
        /// 以前は HUD だけ深刻度の色（普段は全部青）で、トレイと見分け方が違っていた（2026-09-17）。
        /// </summary>
        public Color ColorFor(string value, Level level)
        {
            if (level != Level.Normal) return For(level);
            return IdentityFor(value);
        }

        /// <summary>
        /// Windows のコントラストテーマ（ハイコントラスト）用。
        ///
        /// 色は自分で決めず、すべて Windows が用意した組み合わせ（SystemColors）から取る。
        /// 独自の色を混ぜると、利用者がコントラストテーマを選んだ意味が無くなるため。
        ///
        /// 値ごとの識別色は使えない（使える色が限られていて、どれも背景と対にされた色なので）。
        /// そのぶん、何の値かは行の名前と目印で、注意・危険は形（Ui/LevelMark.cs）で分かる。
        /// </summary>
        public static Theme HighContrast()
        {
            var back = SystemColors.Window;
            var text = SystemColors.WindowText;
            // 地が暗いコントラストテーマ（黒 #1・黒 #2）と明るいもの（白）の両方がある。
            var dark = back.GetBrightness() < 0.5;

            return new Theme
            {
                IsDark = dark,
                IsHighContrast = true,
                Background = back,
                Border = SystemColors.ActiveBorder,
                Separator = SystemColors.ActiveBorder,
                TextPrimary = text,
                TextSecondary = SystemColors.GrayText,
                BarTrack = SystemColors.Control,
                TrayTrack = SystemColors.Control,
                ScrollThumb = SystemColors.ControlDark,
                ScrollThumbHot = SystemColors.Highlight,
                Field = back,
                FieldBorder = text,
                // 通常・注意・危険は、Windows が「対になる」と保証している色だけを使う。
                Normal = text,
                Warn = SystemColors.Highlight,
                Danger = SystemColors.HotTrack,
                IdContext = text,
                IdFiveHour = text,
                IdWeekly = text,
            };
        }

        public static Theme Dark()
        {
            return new Theme
            {
                IsDark = true,
                Background = Color.FromArgb(22, 24, 29),
                Border = Color.FromArgb(43, 48, 59),
                Separator = Color.FromArgb(38, 43, 52),
                TextPrimary = Color.FromArgb(230, 234, 242),
                TextSecondary = Color.FromArgb(141, 148, 164),
                BarTrack = Color.FromArgb(42, 46, 56),
                TrayTrack = Color.FromArgb(62, 68, 82),
                ScrollThumb = Color.FromArgb(62, 68, 82),
                ScrollThumbHot = Color.FromArgb(96, 104, 122),
                Field = Color.FromArgb(38, 42, 51),
                FieldBorder = Color.FromArgb(62, 68, 82),
                Normal = Color.FromArgb(127, 178, 255),
                Warn = Color.FromArgb(245, 196, 81),
                Danger = Color.FromArgb(255, 107, 107),
                IdContext = Color.FromArgb(127, 178, 255),   // 青
                IdFiveHour = Color.FromArgb(110, 231, 168),  // 緑
                IdWeekly = Color.FromArgb(196, 162, 255),    // 紫
            };
        }

        public static Theme Light()
        {
            return new Theme
            {
                IsDark = false,
                Background = Color.FromArgb(251, 251, 253),
                Border = Color.FromArgb(221, 225, 232),
                Separator = Color.FromArgb(232, 235, 240),
                TextPrimary = Color.FromArgb(27, 31, 39),
                TextSecondary = Color.FromArgb(98, 107, 123),
                BarTrack = Color.FromArgb(228, 232, 238),
                TrayTrack = Color.FromArgb(198, 205, 216),
                ScrollThumb = Color.FromArgb(198, 205, 216),
                ScrollThumbHot = Color.FromArgb(160, 170, 184),
                Field = Color.White,
                FieldBorder = Color.FromArgb(198, 205, 216),
                // ライト背景では明度を落とさないと読めない。
                Normal = Color.FromArgb(47, 111, 224),
                Warn = Color.FromArgb(184, 116, 6),
                Danger = Color.FromArgb(211, 58, 58),
                // ライト背景では識別色も明度を落とさないと読めない。
                IdContext = Color.FromArgb(47, 111, 224),
                IdFiveHour = Color.FromArgb(30, 142, 90),
                IdWeekly = Color.FromArgb(110, 69, 201),
            };
        }

        /// <summary>
        /// 設定値からテーマを決める。auto なら Windows の設定に従う。
        /// </summary>
        public static Theme Resolve(string setting)
        {
            // コントラストテーマは「自動」のときだけ。light / dark を選んだ人の指定は尊重する。
            if (IsAuto(setting) && HighContrastOn()) return HighContrast();

            if (string.Equals(setting, "light", StringComparison.OrdinalIgnoreCase)) return Light();
            if (string.Equals(setting, "dark", StringComparison.OrdinalIgnoreCase)) return Dark();
            return SystemPrefersLight() ? Light() : Dark();
        }

        private static bool IsAuto(string setting)
        {
            return !string.Equals(setting, "light", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(setting, "dark", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Windows のコントラストテーマが入っているか。読めなければ入っていない扱い。</summary>
        public static bool HighContrastOn()
        {
            try { return SystemInformation.HighContrast; }
            catch { return false; }
        }

        /// <summary>
        /// トレイアイコン用のテーマ。
        ///
        /// Windows はアプリの配色（AppsUseLightTheme）とタスクバーの配色
        /// （SystemUsesLightTheme）を別々に設定できる。アイコンが載るのはタスクバーなので、
        /// こちらだけは別の値を見ないと、暗いタスクバーに暗い色で描いてしまう。
        /// </summary>
        public static Theme ResolveForTray(string setting)
        {
            if (IsAuto(setting) && HighContrastOn()) return HighContrast();

            if (string.Equals(setting, "light", StringComparison.OrdinalIgnoreCase)) return Light();
            if (string.Equals(setting, "dark", StringComparison.OrdinalIgnoreCase)) return Dark();
            return ReadPersonalize("SystemUsesLightTheme") ? Light() : Dark();
        }

        /// <summary>
        /// アプリ用のライト/ダーク設定。値が無い環境（古い Windows）ではライト扱いになるが、
        /// その場合でも読めない配色にはならない。
        /// </summary>
        public static bool SystemPrefersLight()
        {
            return ReadPersonalize("AppsUseLightTheme");
        }

        private static bool ReadPersonalize(string valueName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return true;
                    var v = key.GetValue(valueName);
                    if (v == null) return true;
                    return Convert.ToInt32(v) != 0;
                }
            }
            catch
            {
                return true;
            }
        }

        // --- 書体 ---------------------------------------------------------------

        private static string _family;

        /// <summary>
        /// Windows 11 の標準書体を優先する。無ければ Segoe UI に落とす。
        /// フォント名を直接 new Font に渡すと、無い場合に黙って別の書体に置き換わるので、
        /// インストール済みかどうかを一度だけ調べる。
        /// </summary>
        public static string FontFamily
        {
            get
            {
                if (_family != null) return _family;

                _family = "Segoe UI";
                try
                {
                    using (var installed = new InstalledFontCollection())
                    {
                        foreach (var f in installed.Families)
                        {
                            if (f.Name == "Segoe UI Variable Text") { _family = f.Name; break; }
                        }
                    }
                }
                catch { }

                return _family;
            }
        }
    }
}
