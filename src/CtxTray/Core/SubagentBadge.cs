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
    /// 札の文字は 2 段目と同じ小さめの字。名前の欄は、Widest の札が欄の半分に入る幅より狭くしない
    /// （承認待ちの強調と同じ下限の決め方。収まらないときに件数などを省く案から、利用者が下限の方を選んだ。2026-10-19）。
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

        /// <summary>札の文字。</summary>
        /// <param name="label">「サブ」「Sub」。</param>
        public static string Text(SubagentSummary sum, string label)
        {
            if (sum == null) return null;

            var text = label + " " + sum.Running;
            if (sum.Level != Level.Normal && sum.MaxPct.HasValue)
                text += " · " + PercentText.Format(sum.MaxPct.Value) + "%";
            if (sum.Compacted) text += " ↻";
            return text;
        }

        /// <summary>
        /// 名前の欄の下限を決めるときに測る札。件数は 2 桁（同時に動かせる数の既定の上限は 20）、% も 2 桁
        /// （自動圧縮は 100% より手前で起きる）で、↻ 付き。これより長い札（上限を上げて 100 件以上など）は「…」で切れる。
        /// </summary>
        public static string Widest(string label)
        {
            return label + " 20 · 99% ↻";
        }
    }
}
