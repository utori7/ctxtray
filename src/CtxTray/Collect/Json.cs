using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace CtxTray.Collect
{
    /// <summary>
    /// JSON の読み取り。
    ///
    /// .NET Framework 同梱の JavaScriptSerializer を使う。
    /// Newtonsoft.Json を入れると DLL を並べて配ることになり、
    /// 「exe を 1 個置くだけ」という配布方針が崩れるため採らない。
    /// </summary>
    internal static class Json
    {
        private static JavaScriptSerializer NewSerializer()
        {
            var s = new JavaScriptSerializer();
            // 既定の上限は 2MB。transcript の 1 行が巨大なツール出力を含むことがあるので外す。
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        /// <summary>解析できなければ null を返す。例外は投げない（表示層まで伝播させない）。</summary>
        public static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                return NewSerializer().DeserializeObject(text) as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 行コメント（// …）とブロックコメント（/* … */）を取り除く。文字列の中は触らない。
        ///
        /// 設定ファイル用。README の設定例はコメント付きなので、そのまま貼っても読めるようにする。
        /// JavaScriptSerializer はコメントを受け付けず、以前は「壊れた設定」として既定値に戻していた（実測）。
        /// 改行は残すので、解析エラーの位置がずれない。
        /// </summary>
        public static string StripComments(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('/') < 0) return text;

            const char Quote = (char)0x22;      // "
            const char Backslash = (char)0x5C;  // \

            var sb = new StringBuilder(text.Length);
            var inString = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];

                if (inString)
                {
                    sb.Append(ch);
                    if (ch == Backslash && i + 1 < text.Length) { sb.Append(text[++i]); continue; }
                    if (ch == Quote) inString = false;
                    continue;
                }

                if (ch == Quote) { inString = true; sb.Append(ch); continue; }

                if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    if (i < text.Length) sb.Append('\n');
                    continue;
                }

                if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    i += 2;
                    while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                    {
                        if (text[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;   // 閉じの '/' を読み飛ばす（ループの i++ と合わせて 2 文字）
                    continue;
                }

                sb.Append(ch);
            }
            return sb.ToString();
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v as Dictionary<string, object>;
        }

        public static object[] Arr(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v as object[];
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            bool parsed;
            return bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
        }

        /// <summary>
        /// 数値の取り出し。JavaScriptSerializer は大きさによって int / long / decimal を
        /// 返し分けるので、型で分岐せず一度文字列を経由して丸める。
        /// </summary>
        public static long Long(Dictionary<string, object> d, string key, long fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            return ToLong(v, fallback);
        }

        public static long ToLong(object v, long fallback = 0)
        {
            if (v == null) return fallback;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            try
            {
                return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            }
            catch
            {
                long parsed;
                var s = Convert.ToString(v, CultureInfo.InvariantCulture);
                return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
            }
        }
    }

    /// <summary>
    /// 出力用の最小 JSON ライター。
    ///
    /// キーの順序を保ちたい（--json を目で読むときと、設定ファイルを手で編集するときに
    /// 項目の並びが毎回変わると読みにくい）。
    /// Dictionary は順序を保証せず、JavaScriptSerializer は DateTime を
    /// "\/Date(...)\/" 形式で書くので、どちらも使わず自前で組む。
    /// </summary>
    internal sealed class JObj
    {
        private readonly List<KeyValuePair<string, object>> _items = new List<KeyValuePair<string, object>>();

        public JObj Add(string key, object value)
        {
            _items.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        /// <param name="asciiOnly">
        /// true なら ASCII 以外の文字を \uXXXX で書く。パイプやファイルに出すとき、
        /// 受け取る側がどの文字コードで読んでも壊れないようにするため（Native/ConsoleOutput.cs）。
        /// </param>
        public static string Write(object value, bool asciiOnly = false)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0, asciiOnly);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v, int indent, bool ascii)
        {
            if (v == null) { sb.Append("null"); return; }

            var o = v as JObj;
            if (o != null) { o.WriteTo(sb, indent, ascii); return; }

            var list = v as System.Collections.IList;
            if (list != null && !(v is string))
            {
                if (list.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < list.Count; i++)
                {
                    Indent(sb, indent + 1);
                    WriteValue(sb, list[i], indent + 1, ascii);
                    if (i < list.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, indent);
                sb.Append(']');
                return;
            }

            if (v is bool) { sb.Append(((bool)v) ? "true" : "false"); return; }

            if (v is int || v is long || v is short || v is byte)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }

            if (v is double || v is float || v is decimal)
            {
                sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }

            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture), ascii);
        }

        private void WriteTo(StringBuilder sb, int indent, bool ascii)
        {
            if (_items.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            for (int i = 0; i < _items.Count; i++)
            {
                Indent(sb, indent + 1);
                WriteString(sb, _items[i].Key, ascii);
                sb.Append(": ");
                WriteValue(sb, _items[i].Value, indent + 1, ascii);
                if (i < _items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, indent);
            sb.Append('}');
        }

        private static void Indent(StringBuilder sb, int level)
        {
            sb.Append(' ', level * 2);
        }

        private static void WriteString(StringBuilder sb, string s, bool ascii)
        {
            // エスケープ記号は文字コードで持つ。
            // C# のエスケープ表記だと、生成ツールやシェルを経由したときに
            // バックスラッシュが失われて静かに壊れることがある（実際に一度踏んだ）。
            const char Quote = (char)0x22;      // "
            const char Backslash = (char)0x5C;  // \

            sb.Append(Quote);
            foreach (var ch in s)
            {
                if (ch == Quote) { sb.Append(Backslash).Append(Quote); }
                else if (ch == Backslash) { sb.Append(Backslash).Append(Backslash); }
                else if (ch == (char)0x0A) { sb.Append(Backslash).Append('n'); }
                else if (ch == (char)0x0D) { sb.Append(Backslash).Append('r'); }
                else if (ch == (char)0x09) { sb.Append(Backslash).Append('t'); }
                else if (ch < 0x20 || (ascii && ch > 0x7E))
                {
                    // 制御文字は常に、ASCII 以外は asciiOnly のときだけ \uXXXX にする。
                    // サロゲートペアも 1 単位ずつ書けば正しい JSON になる。
                    sb.Append(Backslash).Append('u')
                      .Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(ch);
                }
            }
            sb.Append(Quote);
        }
    }
}
