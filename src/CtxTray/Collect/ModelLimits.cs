using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CtxTray.Collect
{
    /// <summary>設定画面の「モデル」タブの 1 行。</summary>
    internal sealed class ModelEntry
    {
        public string Id;
        /// <summary>分からなければ null。</summary>
        public int? Limit;
        public LimitSource Source;
        /// <summary>公式ドキュメントから取得した時刻（Source が Docs のときだけ）。</summary>
        public DateTime? FetchedUtc;
    }

    /// <summary>分母を引くときに見るもの（収集に渡す）。どれも無くてよい。</summary>
    internal sealed class LimitSources
    {
        /// <summary>設定の modelLimits。</summary>
        public IDictionary<string, int> Config { get; set; }

        /// <summary>公式ドキュメントから取得して保存した値。</summary>
        public IDictionary<string, int> Docs { get; set; }

        /// <summary>公式ドキュメントから取得して保存したモデル名（ページの title）。</summary>
        public IDictionary<string, string> DocsNames { get; set; }

        /// <summary>分母が分からないモデルの確認の状態。無ければ「オフ」。</summary>
        public Func<string, DocsLookupState> DocsState { get; set; }
    }

    /// <summary>
    /// モデルごとのコンテキストウィンドウ(分母)。
    ///
    /// transcript にも Desktop のローカルファイルにも分母は書かれていないため、
    /// モデル名から引くしかない。どこにも無いモデルは % を出さず「分母不明」に落とす。
    /// 誤った % を出すより安全。
    ///
    /// 引く順は 設定の modelLimits → 組み込みの表 → 公式ドキュメントから取得した値。
    /// 利用者が自分で直せるよう、設定をいちばん優先する。
    ///
    /// ★ 名前の照合は「同じ名前」か「日付だけ違う名前」だけ。
    ///   以前は前方一致で claude-opus-5-5 を claude-opus-5 の値にしていたが、
    ///   それは確かめていない推測で、分母の違うモデルが出たら黙って誤った % を出す
    ///   （2026-09-25、利用者の判断で廃止）。
    /// </summary>
    internal static class ModelLimits
    {
        /// <summary>
        /// 組み込みの表。根拠は行ごとに書く。
        /// 公式ドキュメント = platform.claude.com/docs/en/models/&lt;名前&gt;/overview.md の
        /// 「Model ID」「Context window」の行（llms.txt に載っている全モデル）。
        /// </summary>
        private static readonly Dictionary<string, int> Table =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "claude-fable-5-1",  1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-mythos-5-1", 1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-fable-5",    1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-mythos-5",   1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-opus-5-5",   1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-opus-5",     1000000 },   // 実測（公式インジケーター 169k / 1M に対し算出値 169,046）＋公式ドキュメント
                { "claude-opus-4-8",   1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-opus-4-7",   1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-opus-4-6",   1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-opus-4-5",    200000 },   // 公式ドキュメント（2026-09-25 確認、ページの ID は -20251101 付き）
                { "claude-sonnet-5",   1000000 },   // 実測（ターミナルの /context 51.3k/1m と一致）＋公式ドキュメント
                { "claude-sonnet-4-6", 1000000 },   // 公式ドキュメント（2026-09-25 確認）
                { "claude-sonnet-4-5",  200000 },   // 公式ドキュメント（2026-09-25 確認、ページの ID は -20250929 付き）
                { "claude-haiku-4-5",   200000 },   // 公式ドキュメント（2026-09-25 確認、ページの ID は -20251001 付き）
            };

        /// <summary>
        /// 組み込みの表示名。公式ドキュメントのモデルのページの先頭にある title をそのまま載せる
        /// （14 件とも 2026-09-26 確認。Model ID の行が上の表のモデルと一致することも確かめた）。
        /// 画面では先頭の「Claude 」を外して出す（ShortName）。
        ///
        /// ★ ID から名前を組み立てない（claude-opus-5-5 → Opus 5.5 のような整形）。
        ///   命名の規則が変われば黙って誤った名前を出すので、分母と同じく確かめたものだけにする
        ///   （2026-09-26、利用者の決定）。どこにも無ければ ID のまま出す。
        /// </summary>
        private static readonly Dictionary<string, string> NameTable =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "claude-fable-5-1",  "Claude Fable 5.1" },
                { "claude-mythos-5-1", "Claude Mythos 5.1" },
                { "claude-fable-5",    "Claude Fable 5" },
                { "claude-mythos-5",   "Claude Mythos 5" },
                { "claude-opus-5-5",   "Claude Opus 5.5" },
                { "claude-opus-5",     "Claude Opus 5" },
                { "claude-opus-4-8",   "Claude Opus 4.8" },
                { "claude-opus-4-7",   "Claude Opus 4.7" },
                { "claude-opus-4-6",   "Claude Opus 4.6" },
                { "claude-opus-4-5",   "Claude Opus 4.5" },
                { "claude-sonnet-5",   "Claude Sonnet 5" },
                { "claude-sonnet-4-6", "Claude Sonnet 4.6" },
                { "claude-sonnet-4-5", "Claude Sonnet 4.5" },
                { "claude-haiku-4-5",  "Claude Haiku 4.5" },
            };

        /// <summary>
        /// 画面に出すモデル名。組み込みの表 → 公式ドキュメントから取得した名前 → 無ければ null
        /// （呼び出し側は ID をそのまま出す）。照合は分母と同じ「同じ名前」か「日付だけ違う名前」。
        /// </summary>
        public static string DisplayName(string model, IDictionary<string, string> fromDocs = null)
        {
            if (string.IsNullOrEmpty(model)) return null;
            var name = FindName(NameTable, model) ?? FindName(fromDocs, model);
            return ShortName(name);
        }

        /// <summary>
        /// 「Claude Opus 5.5」→「Opus 5.5」。HUD の幅は狭く、どのモデルも Claude なので外す。
        /// 「Claude 」で始まらない名前はそのまま。
        /// </summary>
        public static string ShortName(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            var t = title.Trim();
            const string Prefix = "Claude ";
            if (t.Length > Prefix.Length && t.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                t = t.Substring(Prefix.Length).Trim();
            return t.Length == 0 ? null : t;
        }

        private static string FindName(IDictionary<string, string> table, string model)
        {
            if (table == null || table.Count == 0) return null;

            string exact;
            if (table.TryGetValue(model, out exact) && !string.IsNullOrEmpty(exact)) return exact;

            var name = BaseName(model);
            foreach (var kv in table)
            {
                if (string.IsNullOrEmpty(kv.Value) || string.IsNullOrEmpty(kv.Key)) continue;
                if (string.Equals(BaseName(kv.Key), name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>末尾の日付（-20251001 など）。同じモデルの版の違いを表す。</summary>
        private static readonly Regex DateSuffix = new Regex(@"-\d{8}$", RegexOptions.CultureInvariant);

        /// <summary>日付を外した名前。claude-haiku-4-5-20251001 → claude-haiku-4-5。</summary>
        public static string BaseName(string model)
        {
            if (string.IsNullOrEmpty(model)) return model;
            return DateSuffix.Replace(model.Trim(), string.Empty);
        }

        /// <summary>分母を引く。分からなければ null。</summary>
        public static int? Lookup(string model, IDictionary<string, int> overrides = null,
                                  IDictionary<string, int> fromDocs = null)
        {
            LimitSource source;
            return Lookup(model, overrides, fromDocs, out source);
        }

        /// <summary>分母と、その出どころを返す。</summary>
        /// <param name="overrides">設定の modelLimits。</param>
        /// <param name="fromDocs">公式ドキュメントから取得して保存した値。</param>
        public static int? Lookup(string model, IDictionary<string, int> overrides,
                                  IDictionary<string, int> fromDocs, out LimitSource source)
        {
            source = LimitSource.Unknown;
            if (string.IsNullOrEmpty(model)) return null;

            var value = Find(overrides, model);
            if (value.HasValue) { source = LimitSource.Config; return value; }

            value = Find(Table, model);
            if (value.HasValue) { source = LimitSource.BuiltIn; return value; }

            value = Find(fromDocs, model);
            if (value.HasValue) { source = LimitSource.Docs; return value; }

            return null;
        }

        /// <summary>組み込みの表にあるか（公式ドキュメントを見に行くかの判定に使う）。</summary>
        public static bool IsBuiltIn(string model)
        {
            return Find(Table, model).HasValue;
        }

        /// <summary>組み込みの表の中身（設定画面の一覧に出す）。</summary>
        public static IEnumerable<KeyValuePair<string, int>> BuiltIn
        {
            get { return Table; }
        }

        /// <summary>
        /// 同じ名前、または日付だけ違う名前のキーを探す。
        /// 完全一致を先に見るので、日付付きのキーを設定で書いた場合はそれが勝つ。
        /// </summary>
        private static int? Find(IDictionary<string, int> table, string model)
        {
            if (table == null || table.Count == 0) return null;

            int exact;
            if (table.TryGetValue(model, out exact) && exact > 0) return exact;

            var name = BaseName(model);
            foreach (var kv in table)
            {
                if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
                if (string.Equals(BaseName(kv.Key), name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }
    }
}
