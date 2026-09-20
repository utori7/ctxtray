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
}
