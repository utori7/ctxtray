using System;
using System.Windows.Forms;
using CtxTray.Native;

namespace CtxTray.Ui
{
    /// <summary>
    /// "Ctrl+Alt+C" のような文字列を RegisterHotKey の引数に変換する。
    /// 設定ファイルを手で書き換える前提なので、表記の揺れは寛容に受ける。
    /// </summary>
    internal static class HotkeyParser
    {
        public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            if (string.IsNullOrEmpty(text)) return false;

            var parts = text.Split(new[] { '+', '-' }, StringSplitOptions.RemoveEmptyEntries);
            string keyPart = null;

            foreach (var raw in parts)
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;

                switch (p.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                    case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                    case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                    case "win":
                    case "windows": modifiers |= NativeMethods.MOD_WIN; break;
                    default: keyPart = p; break;
                }
            }

            if (keyPart == null) return false;

            Keys key;
            // "1" のような数字は Keys.D1 にする。
            // ★ 先に見る。Enum.TryParse は数字の文字列を「その番号の値」として受け付けるので、
            //   "1" が Keys.LButton（マウスの左ボタン、番号 1）になっていた（2026-09-26、試験で発見）。
            if (keyPart.Length == 1 && char.IsDigit(keyPart[0])) keyPart = "D" + keyPart;
            // 名前ではなく番号で書かれたもの（"65" など）も同じ理由で受けない。
            if (char.IsDigit(keyPart[0]) || !Enum.TryParse(keyPart, true, out key)) return false;

            virtualKey = (uint)key;
            // 修飾キー無しのホットキーは他アプリと衝突しやすいので受け付けない。
            return modifiers != 0;
        }

        /// <summary>
        /// 表記の違い（"ctrl+alt+c" と "Ctrl+Alt+C"、修飾キーの順番）を無視して同じキーか。
        /// どちらかが読めない（空を含む）なら false。
        /// </summary>
        public static bool Same(string a, string b)
        {
            uint ma, va, mb, vb;
            if (!TryParse(a, out ma, out va)) return false;
            if (!TryParse(b, out mb, out vb)) return false;
            return ma == mb && va == vb;
        }
    }
}
