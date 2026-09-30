using System;
using System.IO;
using System.Net;
using System.Text;

namespace CtxTray.Collect
{
    /// <summary>
    /// ctxtray の通信はすべてここを通す（公式ドキュメントの取得と更新の確認。どちらも既定オフ）。
    ///
    /// 作法を 1 か所にまとめておくため: HTTPS だけ、決めたホスト以外への転送には従わない、
    /// 10 秒で諦める、読むのは 256KB まで、資格情報を送らない。
    /// </summary>
    internal static class Https
    {
        public const int MaxBytes = 256 * 1024;
        private const int MaxRedirects = 3;

        /// <summary>
        /// 取ってくる。取れなければ null（例外は投げない）。
        /// accept が null なら Accept ヘッダーを付けない。
        /// </summary>
        public static string Get(string url, string allowedHost, string userAgent, string accept)
        {
            try
            {
                for (var hop = 0; hop <= MaxRedirects; hop++)
                {
                    var uri = new Uri(url);
                    if (uri.Scheme != Uri.UriSchemeHttps ||
                        !string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
                        return null;

                    var req = (HttpWebRequest)WebRequest.Create(uri);
                    req.Method = "GET";
                    req.AllowAutoRedirect = false;
                    req.Timeout = 10000;
                    req.ReadWriteTimeout = 10000;
                    req.UserAgent = userAgent;
                    if (accept != null) req.Accept = accept;
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
                // 通信の失敗は「取れなかった」と同じ扱い。表示を止めない。
            }
            return null;
        }
    }
}
