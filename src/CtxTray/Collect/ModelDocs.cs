using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CtxTray.Collect
{
    /// <summary>
    /// 公式ドキュメントのモデルのページから、コンテキストウィンドウ（分母）を読む。
    ///
    /// ★ ctxtray は既定では通信しない。これは利用者が設定でオンにしたときだけ使う
    ///   （fetchModelLimits、2026-09-25 利用者の決定）。読むのは誰でも見られる公開ドキュメントの
    ///   1 ページで、送るのはその URL だけ。認証情報は使わない。
    ///
    /// ページは Markdown 版（URL の末尾が .md）を読む。冒頭に
    ///   Model ID: `claude-opus-5-5`
    ///   Context window: 1M tokens · Max output: …
    /// の行がある（2026-09-25 に 14 モデルのページで確認）。
    /// ID がモデル名と一致しないページ、値が読めないページは採用しない。
    /// </summary>
    internal static class ModelDocs
    {
        public const string Host = "platform.claude.com";

        private static readonly Regex SlugPattern = new Regex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex IdLine = new Regex(@"Model ID:\s*`([^`]+)`", RegexOptions.CultureInvariant);
        private static readonly Regex WindowLine = new Regex(
            @"Context window:\s*([0-9]+(?:\.[0-9]+)?)\s*([KkMm])\s*tokens", RegexOptions.CultureInvariant);

        /// <summary>これより小さい・大きい値は読み違いとみなす。</summary>
        public const int MinLimit = 1000;
        public const int MaxLimit = 10000000;

        /// <summary>
        /// ページの URL。claude- と末尾の日付を外した名前を使う
        /// （claude-opus-5-5 → …/models/opus-5-5/overview.md）。
        /// 見に行けない名前（claude- で始まらない、英小文字・数字・ハイフン以外を含む）は null。
        /// </summary>
        public static string PageUrl(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            var name = ModelLimits.BaseName(model).ToLowerInvariant();
            if (!name.StartsWith("claude-", StringComparison.Ordinal)) return null;

            var slug = name.Substring("claude-".Length);
            if (!SlugPattern.IsMatch(slug)) return null;

            return "https://" + Host + "/docs/en/models/" + slug + "/overview.md";
        }

        /// <summary>
        /// ページから分母を読む。ページの Model ID（日付を除く）がモデル名と一致し、
        /// 値が範囲内のときだけ返す。それ以外は null。
        /// </summary>
        public static int? ParsePage(string text, string model)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(model)) return null;

            var id = IdLine.Match(text);
            if (!id.Success) return null;
            if (!string.Equals(ModelLimits.BaseName(id.Groups[1].Value), ModelLimits.BaseName(model),
                               StringComparison.OrdinalIgnoreCase))
                return null;

            var window = WindowLine.Match(text);
            if (!window.Success) return null;

            double number;
            if (!double.TryParse(window.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return null;

            var unit = char.ToUpperInvariant(window.Groups[2].Value[0]) == 'M' ? 1000000.0 : 1000.0;
            var limit = number * unit;
            if (limit < MinLimit || limit > MaxLimit) return null;
            return (int)Math.Round(limit);
        }

        /// <summary>
        /// ページを取ってくる。取れなければ null（例外は投げない）。
        /// HTTPS だけ。ほかのホストへの転送には従わない。読むのは 256KB まで。
        /// </summary>
        public static string Download(string url, string userAgent)
        {
            const int MaxBytes = 256 * 1024;
            const int MaxRedirects = 3;

            try
            {
                for (var hop = 0; hop <= MaxRedirects; hop++)
                {
                    var uri = new Uri(url);
                    if (uri.Scheme != Uri.UriSchemeHttps ||
                        !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
                        return null;

                    var req = (HttpWebRequest)WebRequest.Create(uri);
                    req.Method = "GET";
                    req.AllowAutoRedirect = false;
                    req.Timeout = 10000;
                    req.ReadWriteTimeout = 10000;
                    req.UserAgent = userAgent;
                    // 利用者の Windows の設定に従う（社内のプロキシなど）。資格情報は送らない。
                    req.UseDefaultCredentials = false;

                    HttpWebResponse res;
                    try { res = (HttpWebResponse)req.GetResponse(); }
                    catch (WebException ex) { res = ex.Response as HttpWebResponse; if (res == null) return null; }

                    using (res)
                    {
                        var code = (int)res.StatusCode;
                        if (code >= 300 && code < 400)
                        {
                            var location = res.Headers[HttpResponseHeader.Location];
                            if (string.IsNullOrEmpty(location)) return null;
                            url = new Uri(uri, location).ToString();
                            continue;
                        }
                        if (code != 200) return null;

                        using (var stream = res.GetResponseStream())
                        using (var buffer = new MemoryStream())
                        {
                            var chunk = new byte[8192];
                            int read;
                            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                            {
                                if (buffer.Length + read > MaxBytes) return null;
                                buffer.Write(chunk, 0, read);
                            }
                            return Encoding.UTF8.GetString(buffer.ToArray());
                        }
                    }
                }
            }
            catch
            {
                // 通信の失敗は「見つからなかった」と同じ扱い。表示を止めない。
            }
            return null;
        }
    }

    /// <summary>
    /// 取得の記録（%LOCALAPPDATA%\ctxtray\model-limits.json）。
    /// 取得できた値、見つからなかったモデルと次に確認する時刻、通知済みのモデルを持つ。
    /// </summary>
    internal sealed class ModelDocsStore
    {
        public const string FileName = "model-limits.json";

        /// <summary>見つからなかったモデルを確認し直すまでの間隔。</summary>
        public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(24);

        public sealed class Found
        {
            public int Limit;
            public DateTime FetchedUtc;
            public string Url;
        }

        public readonly Dictionary<string, Found> Fetched =
            new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);

        /// <summary>見つからなかったモデル → 最後に確認した時刻。</summary>
        public readonly Dictionary<string, DateTime> Missing =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>「上限が分からない」を通知したモデル。モデルごとに 1 回だけ知らせるため。</summary>
        public readonly HashSet<string> Notified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>取得できた値（ModelLimits.Lookup に渡す形）。</summary>
        public Dictionary<string, int> Limits()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Fetched) d[kv.Key] = kv.Value.Limit;
            return d;
        }

        /// <summary>見に行くべきか。取得済みなら行かない。見つからなかったものは 24 時間おく。</summary>
        public bool ShouldCheck(string model, DateTime nowUtc, bool force)
        {
            if (Fetched.ContainsKey(model)) return false;
            if (force) return true;
            DateTime last;
            return !Missing.TryGetValue(model, out last) || nowUtc - last >= RetryAfter;
        }

        /// <summary>読めなければ空の記録を返す（例外は投げない）。</summary>
        public static ModelDocsStore Load(string path)
        {
            var store = new ModelDocsStore();
            try
            {
                if (!File.Exists(path)) return store;
                var o = Json.ParseObject(File.ReadAllText(path, Encoding.UTF8));
                if (o == null) return store;

                var fetched = Json.Obj(o, "fetched");
                if (fetched != null)
                {
                    foreach (var kv in fetched)
                    {
                        var e = kv.Value as Dictionary<string, object>;
                        var limit = (int)Json.Long(e, "limit");
                        if (limit < ModelDocs.MinLimit || limit > ModelDocs.MaxLimit) continue;
                        store.Fetched[kv.Key] = new Found
                        {
                            Limit = limit,
                            FetchedUtc = ParseTime(Json.Str(e, "fetchedAt")) ?? DateTime.MinValue,
                            Url = Json.Str(e, "url"),
                        };
                    }
                }

                var missing = Json.Obj(o, "notFound");
                if (missing != null)
                {
                    foreach (var kv in missing)
                    {
                        var at = ParseTime(kv.Value as string);
                        if (at.HasValue) store.Missing[kv.Key] = at.Value;
                    }
                }

                var notified = Json.Arr(o, "notified");
                if (notified != null)
                    foreach (var v in notified)
                        if (v is string) store.Notified.Add((string)v);
            }
            catch { }
            return store;
        }

        /// <summary>保存する。書けなければ false。一時ファイルから置き換える（AppConfig.Save と同じ）。</summary>
        public bool Save(string path)
        {
            var fetched = new JObj();
            foreach (var kv in Fetched)
                fetched.Add(kv.Key, new JObj()
                    .Add("limit", kv.Value.Limit)
                    .Add("fetchedAt", Time(kv.Value.FetchedUtc))
                    .Add("url", kv.Value.Url));

            var missing = new JObj();
            foreach (var kv in Missing) missing.Add(kv.Key, Time(kv.Value));

            var root = new JObj()
                .Add("version", 1)
                .Add("fetched", fetched)
                .Add("notFound", missing)
                .Add("notified", new List<string>(Notified).ToArray());

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JObj.Write(root), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Time(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseTime(string s)
        {
            DateTime t;
            if (string.IsNullOrEmpty(s)) return null;
            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t))
                return null;
            return DateTime.SpecifyKind(t, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// 常駐用の取得係。裏のスレッドで 1 件ずつ取りに行き、結果を記録に残す。
    ///
    /// 常駐の更新（TrayApp.TickCore）は待たせない。取得が済んだら Changed を立て、
    /// 次の更新で分母が反映される。例外は外へ出さない。
    /// </summary>
    internal sealed class ModelDocsFetcher
    {
        private readonly object _lock = new object();
        private readonly string _path;
        private readonly string _userAgent;
        private readonly ModelDocsStore _store;
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _running;
        private volatile bool _changed;

        /// <summary>現在時刻。試験で差し替えられるようにしてある。</summary>
        internal Func<DateTime> Clock = () => DateTime.UtcNow;

        /// <summary>取ってくる処理。試験で通信の代わりを差し込む。</summary>
        internal Func<string, string> Download;

        /// <summary>試験用。true なら裏のスレッドを起こさない（RunQueuedNow で処理する）。</summary>
        internal bool Manual { get; set; }

        public ModelDocsFetcher(string dir, string userAgent)
        {
            _path = Path.Combine(dir, ModelDocsStore.FileName);
            _userAgent = userAgent;
            _store = ModelDocsStore.Load(_path);
            Download = url => ModelDocs.Download(url, _userAgent);
        }

        /// <summary>取得できた値の複製。</summary>
        public Dictionary<string, int> Limits()
        {
            lock (_lock) return _store.Limits();
        }

        /// <summary>取得できたモデルの一覧（設定画面用）。</summary>
        public List<ModelEntry> Entries()
        {
            var list = new List<ModelEntry>();
            lock (_lock)
            {
                foreach (var kv in _store.Fetched)
                    list.Add(new ModelEntry
                    {
                        Id = kv.Key,
                        Limit = kv.Value.Limit,
                        Source = LimitSource.Docs,
                        FetchedUtc = kv.Value.FetchedUtc,
                    });
            }
            return list;
        }

        /// <summary>これまでに上限が分からなかったモデル（見つからなかった・通知した）。</summary>
        public List<string> UnknownSoFar()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                foreach (var m in _store.Missing.Keys) set.Add(m);
                foreach (var m in _store.Notified) set.Add(m);
            }
            return new List<string>(set);
        }

        /// <summary>分母が分からないモデルの、確認の状態。</summary>
        public DocsLookupState StateFor(string model, bool enabled)
        {
            if (!enabled) return DocsLookupState.Off;
            lock (_lock)
            {
                if (_pending.Contains(model)) return DocsLookupState.Pending;
                if (_store.Missing.ContainsKey(model)) return DocsLookupState.NotFound;
            }
            // まだ順番が回ってきていないものも「確認中」と見せる（次の更新で取りに行く）。
            return DocsLookupState.Pending;
        }

        /// <summary>取得が済んで記録が変わったか。読むと下ろす。</summary>
        public bool TakeChanged()
        {
            var changed = _changed;
            _changed = false;
            return changed;
        }

        /// <summary>
        /// 取りに行く。取得済み・確認中のものは飛ばし、見つからなかったものは 24 時間おく
        /// （force なら待たない。「今すぐ確認」）。
        /// </summary>
        public void Request(IEnumerable<string> models, bool force)
        {
            if (models == null) return;
            var now = Clock();
            lock (_lock)
            {
                foreach (var m in models)
                {
                    if (string.IsNullOrEmpty(m) || _pending.Contains(m)) continue;
                    if (ModelLimits.IsBuiltIn(m)) continue;
                    if (ModelDocs.PageUrl(m) == null) continue;
                    if (!_store.ShouldCheck(m, now, force)) continue;
                    _pending.Add(m);
                    _queue.Enqueue(m);
                }
                if (Manual || _running || _queue.Count == 0) return;
                _running = true;
            }
            ThreadPool.QueueUserWorkItem(_ => Work());
        }

        /// <summary>
        /// 通知したことを記録する。初めてなら true（呼び出し側が通知を出す）。
        /// </summary>
        public bool MarkNotified(string model)
        {
            lock (_lock)
            {
                if (!_store.Notified.Add(model)) return false;
                _store.Save(_path);
                return true;
            }
        }

        /// <summary>試験用。裏のスレッドを使わず、待っている分をその場で処理する。</summary>
        internal void RunQueuedNow()
        {
            lock (_lock) _running = true;
            Work();
        }

        private void Work()
        {
            while (true)
            {
                string model;
                lock (_lock)
                {
                    if (_queue.Count == 0) { _running = false; return; }
                    model = _queue.Dequeue();
                }

                var url = ModelDocs.PageUrl(model);
                int? limit = null;
                try
                {
                    var text = url == null ? null : Download(url);
                    limit = ModelDocs.ParsePage(text, model);
                }
                catch { }

                lock (_lock)
                {
                    var now = Clock();
                    if (limit.HasValue)
                    {
                        _store.Fetched[model] = new ModelDocsStore.Found { Limit = limit.Value, FetchedUtc = now, Url = url };
                        _store.Missing.Remove(model);
                    }
                    else
                    {
                        _store.Missing[model] = now;
                    }
                    _pending.Remove(model);
                    _store.Save(_path);
                }
                _changed = true;
            }
        }
    }
}
