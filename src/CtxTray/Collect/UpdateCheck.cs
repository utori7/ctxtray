using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CtxTray.Config;

namespace CtxTray.Collect
{
    /// <summary>
    /// 新しいバージョンが出ているかの確認。
    ///
    /// ★ ctxtray は既定では通信しない。これは利用者が設定でオンにしたときだけ使う
    ///   （checkUpdates、2026-09-29 利用者の決定。それまでは「更新の確認はしない」と約束していた）。
    ///   読むのは GitHub の公開 API の 1 か所（認証なし）で、取り出すのは最新リリースのタグ名だけ。
    ///   知らせるだけで、ダウンロードや exe の入れ替えはしない（利用者が手で行う）。
    ///   自動で入れ替える案は、署名の無い exe を書き換える動きがウイルス対策に止められるおそれがあり採らなかった。
    /// </summary>
    internal static class UpdateCheck
    {
        public const string Host = "api.github.com";
        public const string LatestUrl = "https://api.github.com/repos/utori7/ctxtray/releases/latest";
        public const string Accept = "application/vnd.github+json";

        /// <summary>ダウンロードページ。応答の URL は使わず、確かめた版番号から組み立てる。</summary>
        public const string ReleasePagePrefix = "https://github.com/utori7/ctxtray/releases/tag/v";

        /// <summary>リリースのタグは v0.8.0 の形だけ。プレリリースや別の形のタグは相手にしない。</summary>
        private static readonly Regex TagPattern =
            new Regex(@"^v([0-9]{1,4})\.([0-9]{1,4})\.([0-9]{1,4})$", RegexOptions.CultureInvariant);

        /// <summary>タグ名（v0.8.0）を版に。形が違えば null。</summary>
        public static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var m = TagPattern.Match(tag);
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                               int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                               int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        /// <summary>API の応答（JSON）から最新リリースの版を読む。読めなければ null。</summary>
        public static Version ParseLatest(string json)
        {
            var o = Json.ParseObject(json);
            return o == null ? null : ParseTag(Json.Str(o, "tag_name"));
        }

        /// <summary>
        /// 手元の版（AppVersion.Full、例 0.7.1+2d04e67…）を比べられる形に。
        /// コミット番号を外し、3 つの数に揃える（0.7.1.0 と 0.7.1 を同じに扱うため）。読めなければ null。
        /// </summary>
        public static Version ParseCurrent(string full)
        {
            if (string.IsNullOrEmpty(full)) return null;
            var plus = full.IndexOf('+');
            if (plus >= 0) full = full.Substring(0, plus);
            Version v;
            if (!Version.TryParse(full, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        public static Version Current
        {
            get { return ParseCurrent(AppVersion.Full); }
        }

        /// <summary>latest が手元より新しいか。どちらかが分からなければ false（推測で知らせない）。</summary>
        public static bool IsNewer(Version latest, Version current)
        {
            return latest != null && current != null && latest > current;
        }

        public static string ReleaseUrl(Version v)
        {
            return v == null ? null : ReleasePagePrefix + v.ToString(3);
        }
    }

    internal enum UpdateState
    {
        /// <summary>まだ一度も確認していない。</summary>
        None,
        Checking,
        UpToDate,
        Available,
        /// <summary>最後の確認が失敗した（通信できない・応答が読めない）。</summary>
        Failed,
    }

    internal sealed class UpdateStatus
    {
        public UpdateState State;
        /// <summary>最後に確認できた最新の版。</summary>
        public Version Latest;
        public Version Current;
        /// <summary>最後に確認できた時刻。</summary>
        public DateTime? CheckedUtc;
    }

    /// <summary>
    /// 確認の記録（%LOCALAPPDATA%\ctxtray\update-check.json）。
    /// 起動のたびに確認しない・同じ版を何度も知らせないために残す。確認をオンにするまでは作らない。
    /// </summary>
    internal sealed class UpdateStore
    {
        public const string FileName = "update-check.json";

        /// <summary>確認できたら、次は 1 日おく。</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        /// <summary>
        /// 失敗したら 1 時間おく。サインイン直後はまだ回線がつながっていないことがあり、
        /// そこで 1 日待たせないため。
        /// </summary>
        public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

        public DateTime? CheckedUtc;
        public Version Latest;
        public DateTime? FailedUtc;
        public Version Notified;

        /// <summary>最後の確認が失敗だったか。</summary>
        public bool LastFailed
        {
            get { return FailedUtc.HasValue && (!CheckedUtc.HasValue || FailedUtc.Value > CheckedUtc.Value); }
        }

        /// <summary>確認しに行く頃か。</summary>
        public bool IsDue(DateTime nowUtc)
        {
            if (LastFailed) return Elapsed(FailedUtc.Value, nowUtc, RetryAfter);
            if (!CheckedUtc.HasValue) return true;
            return Elapsed(CheckedUtc.Value, nowUtc, Interval);
        }

        /// <summary>時計が戻された（記録が未来にある）ときも、待たずに確認する。</summary>
        private static bool Elapsed(DateTime since, DateTime now, TimeSpan wait)
        {
            var d = now - since;
            return d >= wait || d < TimeSpan.Zero;
        }

        /// <summary>読めなければ空の記録を返す（例外は投げない）。</summary>
        public static UpdateStore Load(string path)
        {
            var store = new UpdateStore();
            try
            {
                if (!File.Exists(path)) return store;
                var o = Json.ParseObject(File.ReadAllText(path, Encoding.UTF8));
                if (o == null) return store;
                store.CheckedUtc = ParseTime(Json.Str(o, "checkedAt"));
                store.FailedUtc = ParseTime(Json.Str(o, "failedAt"));
                // 記録ファイルは手で書き換えられうるので、版も応答と同じ形だけ受け付ける。
                store.Latest = UpdateCheck.ParseTag(Json.Str(o, "latest"));
                store.Notified = UpdateCheck.ParseTag(Json.Str(o, "notified"));
            }
            catch { }
            return store;
        }

        /// <summary>保存する。書けなければ false。一時ファイルから置き換える（AppConfig.Save と同じ）。</summary>
        public bool Save(string path)
        {
            var root = new JObj()
                .Add("version", 1)
                .Add("checkedAt", CheckedUtc.HasValue ? Time(CheckedUtc.Value) : null)
                .Add("latest", Tag(Latest))
                .Add("failedAt", FailedUtc.HasValue ? Time(FailedUtc.Value) : null)
                .Add("notified", Tag(Notified));

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

        private static string Tag(Version v)
        {
            return v == null ? null : "v" + v.ToString(3);
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
    /// 常駐用の確認係。裏のスレッドで確認し、結果を記録に残す（ModelDocsFetcher と同じ形）。
    /// 常駐の更新（TrayApp.TickCore）は待たせない。例外は外へ出さない。
    /// 呼ぶのは確認の設定がオンのときと、利用者が「今すぐ確認」を押したときだけ（オフなら通信しない約束）。
    /// </summary>
    internal sealed class UpdateChecker
    {
        private readonly object _lock = new object();
        private readonly string _path;
        private readonly string _userAgent;
        private readonly UpdateStore _store;
        private bool _running;
        private volatile bool _changed;

        /// <summary>現在時刻。試験で差し替えられるようにしてある。</summary>
        internal Func<DateTime> Clock = () => DateTime.UtcNow;

        /// <summary>取ってくる処理。試験で通信の代わりを差し込む。</summary>
        internal Func<string, string> Download;

        /// <summary>手元の版。試験で差し替えられるようにしてある。</summary>
        internal Version Current = UpdateCheck.Current;

        /// <summary>試験用。true なら裏のスレッドを起こさない（RunNow で処理する）。</summary>
        internal bool Manual { get; set; }

        public UpdateChecker(string dir, string userAgent)
        {
            _path = Path.Combine(dir, UpdateStore.FileName);
            _userAgent = userAgent;
            _store = UpdateStore.Load(_path);
            Download = url => Https.Get(url, UpdateCheck.Host, _userAgent, UpdateCheck.Accept);
        }

        /// <summary>前回の確認から間があいていれば確認する（1 日 1 回、失敗なら 1 時間後）。</summary>
        public void RequestIfDue()
        {
            lock (_lock)
            {
                if (_running || !_store.IsDue(Clock())) return;
                _running = true;
            }
            Start();
        }

        /// <summary>待たずに確認する（「今すぐ確認」）。確認中なら重ねない。</summary>
        public void RequestNow()
        {
            lock (_lock)
            {
                if (_running) return;
                _running = true;
            }
            _changed = true;   // 「確認中」を画面に出すため
            Start();
        }

        private void Start()
        {
            if (!Manual) ThreadPool.QueueUserWorkItem(_ => Work());
        }

        /// <summary>試験用。確認を頼まれていれば、裏のスレッドを使わずその場で処理する。</summary>
        internal void RunNow()
        {
            lock (_lock) if (!_running) return;
            Work();
        }

        /// <summary>確認中か（試験用）。</summary>
        internal bool Running
        {
            get { lock (_lock) return _running; }
        }

        public UpdateStatus Status()
        {
            lock (_lock)
            {
                var s = new UpdateStatus { Latest = _store.Latest, Current = Current, CheckedUtc = _store.CheckedUtc };
                if (_running) s.State = UpdateState.Checking;
                else if (_store.LastFailed) s.State = UpdateState.Failed;
                else if (!_store.CheckedUtc.HasValue) s.State = UpdateState.None;
                else s.State = UpdateCheck.IsNewer(_store.Latest, Current) ? UpdateState.Available : UpdateState.UpToDate;
                return s;
            }
        }

        /// <summary>確認が済んで状態が変わったか。読むと下ろす。</summary>
        public bool TakeChanged()
        {
            var changed = _changed;
            _changed = false;
            return changed;
        }

        /// <summary>まだ知らせていない新しい版。無ければ null。同じ版は 1 回だけ知らせる。</summary>
        public Version ToNotify()
        {
            lock (_lock)
            {
                var latest = _store.Latest;
                if (!UpdateCheck.IsNewer(latest, Current)) return null;
                if (_store.Notified != null && latest <= _store.Notified) return null;
                return latest;
            }
        }

        /// <summary>知らせたことを記録する。</summary>
        public void MarkNotified(Version v)
        {
            if (v == null) return;
            lock (_lock)
            {
                _store.Notified = v;
                _store.Save(_path);
            }
        }

        private void Work()
        {
            Version latest = null;
            try
            {
                latest = UpdateCheck.ParseLatest(Download(UpdateCheck.LatestUrl));
            }
            catch { }

            lock (_lock)
            {
                var now = Clock();
                if (latest != null)
                {
                    _store.Latest = latest;
                    _store.CheckedUtc = now;
                    _store.FailedUtc = null;
                }
                else
                {
                    _store.FailedUtc = now;
                }
                _running = false;
                _store.Save(_path);
            }
            _changed = true;
        }
    }
}
