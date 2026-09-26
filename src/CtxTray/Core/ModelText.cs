using CtxTray.Collect;

namespace CtxTray.Core
{
    /// <summary>
    /// モデルとエフォートを 1 つの文字列にする（「Opus 5.5 · high」）。HUD の行と札で共有する。
    ///
    /// モデルは確かめた表示名があればそれ、無ければ ID のまま（名前を組み立てない）。
    /// エフォートは Claude Code が記録した文字列のまま（訳さない。2026-09-26 利用者の決定）。
    /// </summary>
    internal static class ModelText
    {
        /// <summary>どちらか片方だけでも出す。どちらも無ければ null。</summary>
        public static string Format(SessionRow s, bool model = true, bool effort = true)
        {
            if (s == null) return null;

            var m = model ? (!string.IsNullOrEmpty(s.ModelName) ? s.ModelName : s.Model) : null;
            var e = effort ? s.Effort : null;
            var hasModel = !string.IsNullOrEmpty(m);
            var hasEffort = !string.IsNullOrEmpty(e);

            if (hasModel && hasEffort) return m + " · " + e;
            if (hasModel) return m;
            return hasEffort ? e : null;
        }
    }
}
