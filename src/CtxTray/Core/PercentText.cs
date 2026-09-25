using System.Globalization;

namespace CtxTray.Core
{
    /// <summary>
    /// 画面に出す % の数字（整数、% 記号なし）。HUD・トレイのツールチップ・トレイアイコンの数字で共有する。
    ///
    /// 丸めは "0" 書式（x.5 は 0 から遠い方へ）。Math.Round の既定（偶数丸め）を使うと、
    /// 42.5 が HUD では 43、ツールチップでは 42 になり、並べたときに食い違う
    /// （コンテキストの % は小数 1 桁に丸めてあるので x.5 はよく出る）。
    /// </summary>
    internal static class PercentText
    {
        public static string Format(double pct)
        {
            return pct.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
