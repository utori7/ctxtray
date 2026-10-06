using System;
using CtxTray.Collect;

namespace CtxTray.Core
{
    /// <summary>
    /// Claude Desktop がレート枠の記録を止めたことを見分ける。
    ///
    /// 2026-10-02、Desktop を起動した直後に 1 件記録したきり、使用中も 40 分記録しない状態が起きた
    /// （Desktop のインジケーターは 26% を出していたが、ファイルは 0% のまま。Desktop の再起動で直った）。
    /// 2026-10-06 には PC のスリープからの復帰後に同じことが起き、27 分で利用者が再起動した（30 分では出なかった）。
    /// 普段の使用中は 15 分ごとに記録されるので、Behind（記録の後に使用あり）が
    /// 同じ記録のまま 20 分続いたら Stalled にする（利用者の決定）。
    /// 15 分ちょうどにはならず、使用中でも 20〜27 分あいて自然に記録されたことが 3 週間に 5 回ほどあった。
    /// その分は Stalled と出るが、止まったことに早く気づける方を選んだ。
    ///
    /// 経過時間だけでは決めない。しばらく使わずに戻ったときは記録が古いのが普通で、
    /// Desktop はその後すぐ記録する。だから「使用ありと分かってから」の時間を数える。
    /// 始まりは ctxtray が Behind を最初に見た時刻（常駐の途中で起動したときは、そこから数え直す）。
    /// 状態を持つので常駐の 1 か所でだけ使う（コンソールの 1 回きりの実行では Stalled にならない）。
    /// </summary>
    internal sealed class RateStall
    {
        public static readonly TimeSpan After = TimeSpan.FromMinutes(20);

        private DateTime _sampledAtUtc = DateTime.MinValue;
        private DateTime? _behindSinceUtc;

        public RateFreshness Apply(RateFreshness freshness, DateTime sampledAtUtc, DateTime nowUtc)
        {
            // 新しい記録が来たら数え直す。
            if (sampledAtUtc != _sampledAtUtc)
            {
                _sampledAtUtc = sampledAtUtc;
                _behindSinceUtc = null;
            }

            if (freshness != RateFreshness.Behind)
            {
                _behindSinceUtc = null;
                return freshness;
            }

            if (!_behindSinceUtc.HasValue) _behindSinceUtc = nowUtc;
            return nowUtc - _behindSinceUtc.Value >= After ? RateFreshness.Stalled : RateFreshness.Behind;
        }
    }
}
