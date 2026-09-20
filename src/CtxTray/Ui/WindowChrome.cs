using System;
using System.Drawing;
using CtxTray.Native;

namespace CtxTray.Ui
{
    /// <summary>
    /// 窓枠（タイトルバー・枠線）を Theme の配色に合わせる。
    ///
    /// 設定画面は中身を Paint_ で手塗りしているのに、枠だけ Windows 標準のままだったので、
    /// 暗い本体の上に明るい帯が乗って浮いていた（利用者の指摘、2026-09-20）。
    ///
    /// Windows 11 は DWM に色を指定できるので、窓を枠なしにして
    /// タイトルバーを自前で描き直す必要はない。そうすると Windows 側の
    /// スナップ・最小化の動き・アクセシビリティを自分で作り直すことになる。
    /// 対応していない Windows では DwmSetWindowAttribute が失敗を返すだけで、
    /// 従来どおり標準の枠になる。
    /// </summary>
    internal static class WindowChrome
    {
        /// <summary>タイトルバーの地・文字・枠を塗る。ハンドルができた後に呼ぶ。</summary>
        public static void Apply(IntPtr hwnd, Theme theme)
        {
            if (hwnd == IntPtr.Zero || theme == null) return;

            try
            {
                // 暗い配色の指定。キャプションの色を自分で指定しても、
                // × ボタンに重ねる強調色（ホバー時の地）はこちらで決まる。
                var dark = theme.IsDark ? 1 : 0;
                if (NativeMethods.DwmSetWindowAttribute(
                        hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                {
                    // Windows 10 1809〜1903 は番号が違う。
                    NativeMethods.DwmSetWindowAttribute(
                        hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_1809, ref dark, sizeof(int));
                }

                var caption = ColorRef(theme.Background);
                NativeMethods.DwmSetWindowAttribute(
                    hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

                var text = ColorRef(theme.TextPrimary);
                NativeMethods.DwmSetWindowAttribute(
                    hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref text, sizeof(int));

                var border = ColorRef(theme.Border);
                NativeMethods.DwmSetWindowAttribute(
                    hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// COLORREF は 0x00BBGGRR。Color.ToArgb（0xAARRGGBB）をそのまま渡すと
        /// 赤と青が入れ替わるので、ここで組み直す。
        /// </summary>
        private static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }
    }
}
