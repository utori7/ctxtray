using System;
using System.Collections.Generic;

namespace CtxTray.Collect
{
    /// <summary>
    /// モデルごとのコンテキストウィンドウ(分母)。
    ///
    /// transcript にも Desktop のローカルファイルにも分母は書かれていないため、
    /// モデル名から引くしかない。表に無いモデルは % を出さず「分母不明」に落とす。
    /// 誤った % を出すより安全。
    ///
    /// claude-opus-5 = 1,000,000 は公式インジケーターとの突き合わせで確定済み
    /// (公式表示 169k / 1M に対し本方式の算出値 169,046)。
    /// 他のモデルは Claude API のモデル仕様に一致させている。
    ///
    /// 新しいモデルが出たときに利用者が自分で直せるよう、設定の modelLimits を
    /// 組み込みの表より優先する。
    /// </summary>
    internal static class ModelLimits
    {
        private static readonly Dictionary<string, int> Table =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "claude-opus-5",     1000000 },   // 実測で確定
                { "claude-sonnet-5",   1000000 },
                { "claude-fable-5",    1000000 },
                { "claude-mythos-5",   1000000 },
                { "claude-opus-4-8",   1000000 },
                { "claude-opus-4-7",   1000000 },
                { "claude-opus-4-6",   1000000 },
                { "claude-sonnet-4-6", 1000000 },
                { "claude-haiku-4-5",   200000 },
            };

        /// <summary>
        /// 分母を引く。分からなければ null。
        /// 日付サフィックス付き (claude-haiku-4-5-20251001) も同じモデルとして扱う。
        /// overrides（設定の modelLimits）に載っていればそちらを使う。
        /// </summary>
        public static int? Lookup(string model, IDictionary<string, int> overrides = null)
        {
            if (string.IsNullOrEmpty(model)) return null;

            return Find(overrides, model) ?? Find(Table, model);
        }

        /// <summary>
        /// 完全一致、なければ「キー + "-"」で始まる中で最も長いキー。
        /// claude-fable-5 と claude-fable-5-1 の両方が載っていても、
        /// 辞書の並び順によらず claude-fable-5-1-xxx は後者に当たるようにする。
        /// </summary>
        private static int? Find(IDictionary<string, int> table, string model)
        {
            if (table == null || table.Count == 0) return null;

            foreach (var kv in table)
            {
                if (string.Equals(kv.Key, model, StringComparison.OrdinalIgnoreCase) && kv.Value > 0)
                    return kv.Value;
            }

            string bestKey = null;
            var bestValue = 0;
            foreach (var kv in table)
            {
                if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
                if (!model.StartsWith(kv.Key + "-", StringComparison.OrdinalIgnoreCase)) continue;
                if (bestKey != null && kv.Key.Length <= bestKey.Length) continue;
                bestKey = kv.Key;
                bestValue = kv.Value;
            }

            return bestKey == null ? (int?)null : bestValue;
        }
    }
}
