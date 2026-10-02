using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CtxTray.Core
{
    /// <summary>
    /// 自動圧縮のウィンドウ（Claude Code の autoCompactWindow と同じ意味のトークン数）。0 は「自動」。
    ///
    /// Claude Code の公式ドキュメント（2026-10-01 に原文で確認）:
    /// - /autocompact と --autocompact は 100K〜1M のトークン数を受け付ける。書き方は
    ///   200000（そのまま）、500k / 1M（k・M 付き）、100〜1000 の数字だけ（千の単位、200 = 200,000）、auto（既定に戻す）
    /// - 値はモデルのコンテキストウィンドウで切り詰められる
    /// - 未設定なら、1M のモデルは約 967K、それ以外はモデルの上限で圧縮する
    /// https://code.claude.com/docs/en/model-config#set-the-auto-compact-window
    ///
    /// ★ 割合（0.967）で持つと、/autocompact 500k を 200K のモデルで 100K と誤って計算し、
    ///   既定でも 200K のモデルを 193K と少なく見積もっていた。トークン数を上限で切り詰めればどちらも合う。
    /// </summary>
    internal static class CompactWindow
    {
        public const long Auto = 0;
        public const long Min = 100000;
        public const long Max = 1000000;

        /// <summary>未設定のときの値。1M のモデルは約 967K で圧縮する（公式ドキュメント）。上限で切り詰めると他のモデルにも合う。</summary>
        public const long Default = 967000;

        // 公式に書かれた書き方だけを受ける。小数・カンマは書かれていないので受けない。大文字小文字は区別しない。
        private static readonly Regex Pattern = new Regex(@"^\s*(\d{1,7})\s*([kKmM]?)\s*$", RegexOptions.CultureInvariant);

        /// <summary>/autocompact と同じ書き方を読む。auto は Auto（0）。読めない・範囲外なら false。</summary>
        public static bool TryParse(string text, out long tokens)
        {
            tokens = Auto;
            if (text == null) return false;
            if (string.Equals(text.Trim(), "auto", StringComparison.OrdinalIgnoreCase)) return true;

            var m = Pattern.Match(text);
            if (!m.Success) return false;

            long n;
            if (!long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return false;

            var suffix = m.Groups[2].Value.ToLowerInvariant();
            long value;
            if (suffix == "k") value = n * 1000;
            else if (suffix == "m") value = n * 1000000;
            else if (n >= 100 && n <= 1000) value = n * 1000;   // 数字だけの 100〜1000 は千の単位
            else value = n;

            if (value < Min || value > Max) return false;
            tokens = value;
            return true;
        }

        /// <summary>画面と設定ファイルに出す形（500k、1M）。k で割り切れなければ数字のまま。</summary>
        public static string Format(long tokens)
        {
            if (tokens % 1000000 == 0) return (tokens / 1000000).ToString(CultureInfo.InvariantCulture) + "M";
            if (tokens % 1000 == 0) return (tokens / 1000).ToString(CultureInfo.InvariantCulture) + "k";
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 0.9.0 までの compactThreshold（全モデル共通の割合）を引き継ぐ。
        /// 0.967（既定値）と 0.92（v0.1.1 までの仮の値）は利用者が選んだ値ではないので既定値に戻す。
        /// それ以外は 1M のモデルでの位置とみなしてトークン数にする（0.5 → 500k。/autocompact 500k を割合で書いた形）。
        /// 範囲外は既定値。
        /// </summary>
        public static long FromOldThreshold(double ratio)
        {
            if (Math.Abs(ratio - 0.967) < 1e-9 || Math.Abs(ratio - 0.92) < 1e-9) return Auto;
            var tokens = (long)Math.Round(ratio * 1000000);
            return tokens >= Min && tokens <= Max ? tokens : Auto;
        }

        /// <summary>そのモデルで自動圧縮が起きるトークン数（ウィンドウを上限で切り詰める）。</summary>
        public static long PointFor(long window, long contextLimit)
        {
            var w = window == Auto ? Default : window;
            return Math.Min(w, contextLimit);
        }
    }
}
