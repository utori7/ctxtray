using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CtxTray.Ui
{
    /// <summary>
    /// 表示倍率の取得。
    ///
    /// ★ Control.DeviceDpi は使えない。
    ///   .NET Framework の WinForms は、実行ファイルのマニフェストで
    ///   PerMonitorV2 を宣言していても、app.config の
    ///   System.Windows.Forms.ApplicationConfigurationSection で
    ///   別途 opt-in しない限り内部的には 96dpi のまま動く。
    ///   実測でも、プロセスは PER_MONITOR_AWARE なのに DeviceDpi は 96 を返し、
    ///   150% の画面で HUD が実寸 352px（＝小さすぎる）になっていた。
    ///
    ///   app.config を足せば直るが、そうすると ctxtray.exe.config が
    ///   配布に必須のファイルになり「exe 1 個で動く」という前提が崩れる。
    ///   よって OS から直接 DPI を取る。
    /// </summary>
    internal static class Dpi
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        /// <summary>そのウィンドウが載っているモニタの倍率。1.0 = 100%。</summary>
        public static float ScaleFor(IntPtr hWnd)
        {
            if (hWnd != IntPtr.Zero)
            {
                try
                {
                    // Windows 10 1607 以降。それより古いと 0 が返る。
                    var dpi = GetDpiForWindow(hWnd);
                    if (dpi >= 48) return dpi / 96f;
                }
                catch { }
            }

            return SystemScale;
        }

        /// <summary>
        /// その画面座標が載っているモニタの倍率。
        ///
        /// 窓を作る前に倍率が要る画面（設定画面）で使う。ハンドルがまだ無いので
        /// ScaleFor は使えず、SystemScale ではプライマリモニタの倍率になってしまう
        /// （倍率の違う 2 枚目で開くと大きさが合わなかった。2026-09-20）。
        /// </summary>
        public static float ScaleForPoint(Point screenPoint)
        {
            try
            {
                var pt = new Native.NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
                var monitor = Native.NativeMethods.MonitorFromPoint(
                    pt, Native.NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    uint dpiX, dpiY;
                    if (Native.NativeMethods.GetDpiForMonitor(
                            monitor, Native.NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0
                        && dpiX >= 48)
                        return dpiX / 96f;
                }
            }
            catch { }

            return SystemScale;
        }

        /// <summary>
        /// Windows のダイアログの本文の文字（メッセージ用フォント）の書体と大きさ（px）を、その倍率で。
        ///
        /// ★ 表示倍率とアクセシビリティの「テキストのサイズ」の両方が入った値で、タイトルバーの文字と同じ系統。
        ///   0.4.0 までの設定画面は本文を「9pt × 倍率」で作っていたため、(1) WinForms がポイントを
        ///   サインイン時の倍率でさらに px へ換算して倍率が二重に掛かり、(2) テキストのサイズは読んでいなかった。
        ///   利用者の画面（150%・テキスト 150%）では偶然 27px で揃っていたが、どちらかを変えるとずれた（2026-09-26 実測）。
        ///   呼ぶたびに Windows に尋ねるので、サインインし直さずに変えた値もそのまま取れる。
        /// </summary>
        public static SystemFont MessageFont(float scale)
        {
            try
            {
                var metrics = new Native.NativeMethods.NONCLIENTMETRICS();
                metrics.cbSize = Marshal.SizeOf(typeof(Native.NativeMethods.NONCLIENTMETRICS));
                var dpi = (uint)Math.Round(scale * 96);
                if (Native.NativeMethods.SystemParametersInfoForDpi(
                        Native.NativeMethods.SPI_GETNONCLIENTMETRICS, (uint)metrics.cbSize, ref metrics, 0, dpi)
                    && metrics.lfMessageFont.lfHeight != 0)
                {
                    // 負の値は文字そのものの高さ（GDI+ のピクセル単位のフォントと同じ意味）。
                    return new SystemFont(metrics.lfMessageFont.lfFaceName,
                                          Math.Abs(metrics.lfMessageFont.lfHeight));
                }
            }
            catch { }

            // 取れなければ Windows の既定（96dpi で 12px = 9pt）に倍率だけ掛ける。
            return new SystemFont(null, 12f * scale);
        }

        /// <summary>ウィンドウがまだ無いときの既定。プライマリモニタの倍率。</summary>
        public static float SystemScale
        {
            get
            {
                try
                {
                    using (var g = Graphics.FromHwnd(IntPtr.Zero))
                        return g.DpiX / 96f;
                }
                catch
                {
                    return 1f;
                }
            }
        }
    }

    /// <summary>Windows から取った書体と大きさ。書体が取れなければ Face は null。</summary>
    internal struct SystemFont
    {
        public readonly string Face;
        public readonly float Pixels;

        public SystemFont(string face, float pixels)
        {
            Face = string.IsNullOrEmpty(face) ? null : face;
            Pixels = pixels;
        }
    }
}
