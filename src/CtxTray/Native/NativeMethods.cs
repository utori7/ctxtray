using System;
using System.Runtime.InteropServices;

namespace CtxTray.Native
{
    internal static class NativeMethods
    {
        // --- ホットキー -------------------------------------------------------
        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // --- クリック透過 -----------------------------------------------------
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        // WS_EX_LAYERED の窓の透明度。一度も設定しないと、その窓は画面に描かれない。
        public const uint LWA_ALPHA = 0x00000002;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                                    : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
        }

        // --- ドラッグ移動（枠なしウィンドウをつかんで動かす）-------------------
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // --- 最前面の維持 -----------------------------------------------------
        //
        // TopMost プロパティだけでは、他アプリが最前面を取ったあとに
        // 後ろへ回されることがある。定期的に押し戻す。
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                               int x, int y, int cx, int cy, uint flags);

        // --- 角丸ウィンドウ / テーマ変更通知 ------------------------------------
        //
        // Windows 11 (build 22000+) は DWM に角丸を任せられる。自前で Region を
        // 作るとアンチエイリアスが効かずギザギザになるので、まずこちらを試す。
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute,
                                                       ref int value, int size);

        // --- タイトルバーと枠の配色 -------------------------------------------
        //
        // Windows 11 (build 22000+) は、タイトルバーの地・文字・枠の色を DWM に指定できる。
        // 窓を枠なしにして自前で描き直さなくても、中身と同じ配色に揃えられる。
        // 対応していない Windows では DwmSetWindowAttribute が失敗を返すだけなので、
        // 戻り値を見ずに呼びっぱなしでよい。
        //
        // 暗い配色の指定だけは番号が変わった歴史がある。
        // Windows 10 1809〜1903 が 19、それ以降（と Windows 11）が 20。
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_1809 = 19;

        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_TEXT_COLOR = 36;

        /// <summary>ライト/ダークの切り替えは WM_SETTINGCHANGE で飛んでくる。</summary>
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_DPICHANGED = 0x02E0;

        /// <summary>
        /// コントラストテーマ（ハイコントラスト）の入り切り。
        /// こちらは WM_SETTINGCHANGE の文字列では飛んでこないので、別に拾う。
        /// </summary>
        public const int WM_THEMECHANGED = 0x031A;

        // --- 座標からモニタの倍率を取る ---------------------------------------
        //
        // 設定画面は窓を作る前に倍率が要る（部品の大きさを自分で掛けて組むため）。
        // GetDpiForWindow はハンドルが要るので、開く場所（マウスの位置）のモニタから直接引く。
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        /// <summary>GetDpiForMonitor の種類。実際の表示倍率は MDT_EFFECTIVE_DPI。</summary>
        public const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        // Windows 8.1 以降。対象は Windows 10 1903+ なので必ずあるが、念のため呼び出し側で try する。
        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        // --- 全画面のアプリの検出 ---------------------------------------------
        //
        // 最前面の HUD は、動画・発表・ゲームの全画面表示の上にも残る。
        // Windows は「いま通知を出してよいか」を SHQueryUserNotificationState で教えてくれるので、
        // 通知を控えるべき状態（全画面・発表モード）を HUD を隠す合図に使う。
        // 最大化しただけの窓はここに含まれない（全画面とは別）。
        public const int QUNS_BUSY = 2;                      // 全画面のアプリ、または発表設定が有効
        public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;   // 全画面のゲームなど
        public const int QUNS_PRESENTATION_MODE = 4;         // 発表モード

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        // --- アイコン後始末 ---------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr handle);

        // --- コンソール -------------------------------------------------------
        //
        // 常駐 GUI なので WinExe でビルドする（コンソール窓を出さないため）。
        // ただし `ctxtray --json` をターミナルから実行したときは
        // 親のコンソールに書けないと使えないので、そのときだけ結び付ける。
        public const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeConsole();

        public const int STD_OUTPUT_HANDLE = -11;
        public const int STD_ERROR_HANDLE = -12;
        public const uint FILE_TYPE_CHAR = 0x0002;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int stdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetFileType(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetConsoleMode(IntPtr handle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetConsoleOutputCP();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteConsoleW(IntPtr handle, string buffer, int length,
                                                out int written, IntPtr reserved);

        // --- プロセスの実行ファイル -------------------------------------------
        //
        // Process.MainModule は、MSIX のアプリや 64/32 ビットが違う相手で例外になる。
        // QueryFullProcessImageName は最小限の照会権限で実行ファイルのパスを返す。
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageName(IntPtr process, int flags,
                                                            System.Text.StringBuilder name, ref int size);

        /// <summary>プロセスの実行ファイルのパス。照会できなければ null。</summary>
        public static string ProcessImagePath(int processId)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var size = 1024;
                var sb = new System.Text.StringBuilder(size);
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
