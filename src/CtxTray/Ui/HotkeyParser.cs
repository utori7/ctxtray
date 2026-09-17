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
            if (!Enum.TryParse(keyPart, true, out key))
            {
                // "1" のような数字は Keys.D1 になる。
                if (keyPart.Length == 1 && char.IsDigit(keyPart[0]))
                {
                    if (!Enum.TryParse("D" + keyPart, true, out key)) return false;
                }
                else
                {
                    return false;
                }
            }

            virtualKey = (uint)key;
            // 修飾キー無しのホットキーは他アプリと衝突しやすいので受け付けない。
            return modifiers != 0;
        }
    }
}
