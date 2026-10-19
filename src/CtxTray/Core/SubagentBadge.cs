using System.Collections.Generic;
using CtxTray.Collect;
using CtxTray.Config;

namespace CtxTray.Core
{
    /// <summary>セッション 1 つの、動いているサブエージェントのまとめ。</summary>
    internal sealed class SubagentSummary
    {
        public int Running;

        /// <summary>分母が分かるものの中で最大の %（パネルと同じ基準）。どれも分からなければ null。</summary>
        public double? MaxPct;

        /// <summary>最大の % のレベル。閾値はセッションのコンテキストと同じもの。</summary>
        public Level Level;

        /// <summary>どれかに自動圧縮が起きたか。</summary>
        public bool Compacted;

        /// <summary>動いているもの。% の大きい順（分母が分からないものは後ろ）。</summary>
        public List<SubagentInfo> Items = new List<SubagentInfo>();
    }

    /// <summary>
    /// パネルの名前の欄の右端に出す、サブエージェントの札（2026-10-19、実寸の見本で利用者が決定）。
    ///
    ///   普段         「サブ 2」（灰色）
    ///   注意・危険   「サブ 2 · 84%」（その色）。色だけに頼らないよう、% が出ること自体を合図にする。
    ///                普段は % を出さない（行の % と紛れるため）
    ///   圧縮あり     末尾に「↻」（結果の質が落ちうる、という合図）
    /// 札の文字は 2 段目と同じ小さめの字。名前の欄の半分に収まらなければ、件数 → ↻ の順に落とす。
    ///
    /// トレイアイコンと通知には使わない。サブエージェントの使用量は利用者が手を打てる対象ではないため。
    /// </summary>
    internal static class SubagentBadge
    {
        /// <summary>動いているサブエージェントが無ければ null。</summary>
        public static SubagentSummary Summarize(SessionRow s, AppConfig cfg)
        {
            if (s == null || !s.ProcessAlive || s.Subagents == null) return null;

            var sum = new SubagentSummary();
            foreach (var a in s.Subagents)
            {
                if (!a.Running) continue;
                sum.Items.Add(a);
                if (a.Compactions > 0) sum.Compacted = true;
                if (a.Pct.HasValue && (!sum.MaxPct.HasValue || a.Pct.Value > sum.MaxPct.Value))
                    sum.MaxPct = a.Pct.Value;
            }
            if (sum.Items.Count == 0) return null;

            sum.Running = sum.Items.Count;
            if (sum.MaxPct.HasValue)
                sum.Level = Levels.ForPct(sum.MaxPct.Value / 100.0, cfg.ContextWarn, cfg.ContextDanger);

            sum.Items.Sort((a, b) =>
            {
                if (a.Pct.HasValue != b.Pct.HasValue) return a.Pct.HasValue ? -1 : 1;
                if (a.Pct.HasValue && a.Pct.Value != b.Pct.Value) return b.Pct.Value.CompareTo(a.Pct.Value);
                return (b.Tokens ?? 0).CompareTo(a.Tokens ?? 0);
            });
            return sum;
        }

        /// <summary>札の文字。長い順に並べる。描く側は名前の欄の半分に収まる最初のものを使う。</summary>
        /// <param name="label">「サブ」「Sub」。</param>
        public static List<string> Forms(SubagentSummary sum, string label)
        {
            var forms = new List<string>();
            if (sum == null) return forms;

            var count = " " + sum.Running;
            var mark = sum.Compacted ? " ↻" : "";
            if (sum.Level != Level.Normal && sum.MaxPct.HasValue)
            {
                var pct = PercentText.Format(sum.MaxPct.Value) + "%";
                forms.Add(label + count + " · " + pct + mark);
                forms.Add(label + " " + pct + mark);
                forms.Add(label + " " + pct);
            }
            else
            {
                forms.Add(label + count + mark);
                forms.Add(label + mark);
                forms.Add(label);
            }
            return forms;
        }
    }
}
