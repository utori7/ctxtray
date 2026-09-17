using System;
using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CtxTray.Native
{
    /// <summary>
    /// コンソール用途（--json / --status など）の出力先を作る。
    ///
    /// ★ 出力先によって、正しく読まれる書き方が違う。
    ///   - 画面（コンソール）… WriteConsoleW で Unicode のまま書く。コードページに左右されない
    ///   - パイプ・ファイル  … 受け取る側（Windows PowerShell 5.1 など）は、
    ///                          コンソールのコードページ（日本語環境は 932）で読む
    ///   以前は常に UTF-8 で書いていたため、日本語環境の Windows PowerShell で
    ///   `ctxtray --json | ConvertFrom-Json` とするとタイトルが化け、JSON としても読めなかった（実測）。
    ///   また、UTF-8 に揃えるためにコンソールのコードページを書き換えると、
    ///   呼び出し元の窓の設定を勝手に変えてしまうので、それもしない。
    /// </summary>
    internal static class ConsoleOutput
    {
        /// <summary>
        /// 標準出力が画面かどうか。画面でなければ --json は ASCII だけで書く
        /// （日本語を \uXXXX にすれば、どのコードページで読まれても壊れない）。
        /// </summary>
        public static bool StdoutIsConsole { get; private set; }

        /// <summary>親のコンソールに結び付け、標準出力と標準エラーを差し替える。</summary>
        public static void Attach()
        {
            try
            {
                var attached = NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

                bool isConsole;
                var stdout = Open(NativeMethods.STD_OUTPUT_HANDLE, attached, out isConsole);
                StdoutIsConsole = isConsole;
                if (stdout != null) Console.SetOut(stdout);

                bool ignored;
                var stderr = Open(NativeMethods.STD_ERROR_HANDLE, attached, out ignored);
                if (stderr != null) Console.SetError(stderr);
            }
            catch
            {
                // 出力先を作れなくても、既定の Console.Out に任せて処理は続ける。
            }
        }

        private static TextWriter Open(int which, bool attached, out bool isConsole)
        {
            isConsole = false;

            var handle = NativeMethods.GetStdHandle(which);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

            uint mode;
            if (NativeMethods.GetFileType(handle) == NativeMethods.FILE_TYPE_CHAR &&
                NativeMethods.GetConsoleMode(handle, out mode))
            {
                isConsole = true;
                return new ConsoleWriter(handle);
            }

            // パイプかファイル。コンソールに結び付いていれば、そのコードページで書く。
            // 結び付いていない（コンソールの無い親から呼ばれた）ときは UTF-8。
            var encoding = attached ? EncodingFor(NativeMethods.GetConsoleOutputCP()) : new UTF8Encoding(false);
            var stream = new FileStream(new SafeFileHandle(handle, false), FileAccess.Write);
            return new StreamWriter(stream, encoding) { AutoFlush = true };
        }

        private static Encoding EncodingFor(uint codePage)
        {
            // 65001 を GetEncoding で取ると BOM 付きになり、先頭に余計な 3 バイトが入る。
            if (codePage == 0 || codePage == 65001) return new UTF8Encoding(false);
            try { return Encoding.GetEncoding((int)codePage); }
            catch { return new UTF8Encoding(false); }
        }

        /// <summary>画面に Unicode のまま書く。</summary>
        private sealed class ConsoleWriter : TextWriter
        {
            private readonly IntPtr _handle;

            public ConsoleWriter(IntPtr handle)
            {
                _handle = handle;
            }

            public override Encoding Encoding { get { return Encoding.Unicode; } }

            public override void Write(char value)
            {
                Write(value.ToString());
            }

            public override void Write(char[] buffer, int index, int count)
            {
                Write(new string(buffer, index, count));
            }

            public override void Write(string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                int written;
                NativeMethods.WriteConsoleW(_handle, value, value.Length, out written, IntPtr.Zero);
            }
        }
    }
}
