using System;

namespace CtxTray.Core
{
    /// <summary>
    /// HUD の横の寸法（標準の文字サイズ・96 DPI での値。HudForm が倍率を掛けて使う）。
    ///
    /// ★ 設定するのは名前の欄の幅だけ。パネルの幅は、名前の幅とオンにした列の合計で決まる。
    ///   以前はパネルの幅を設定していたが、トークン数をオンにすると名前が縮み、モデルの列をオンにすると
    ///   パネルが広がる、と列によって動きが違い、「幅」が何を決めているのか分からなかった
    ///   （2026-09-26、利用者の指摘で名前の幅に置き換えた）。どの列もオンにした分だけ広がる。
    /// </summary>
    internal static class HudLayout
    {
        public const int PadX = 14;
        public const int Gap = 8;
        public const int Pct = 44;
        public const int Bar = 84;
        public const int Tokens = 60;

        public const int DefaultNameWidth = 180;
        public const int MinNameWidth = 70;
        public const int MaxNameWidth = 1000;

        /// <summary>
        /// モデルとエフォートの専用の列の幅。中身で幅を変えると行ごと・更新ごとに列がずれるので固定。
        /// 実測（Segoe UI 12.5px）で「Sonnet 4.5 · medium」127px、「Mythos 5.1」72px、「medium」55px。
        /// 収まらないものは末尾を「…」にする（全文は札に出る）。
        /// </summary>
        public static int ModelColumn(bool model, bool effort)
        {
            if (model && effort) return 124;
            if (model) return 72;
            return effort ? 52 : 0;
        }

        /// <summary>
        /// パネルの幅。並びは 名前・[モデルの列]・[バー]・%・[トークン数]。
        /// </summary>
        /// <param name="modelColumn">モデルの列の幅（出さない・名前の下に出すときは 0）。</param>
        public static int PanelWidth(int nameWidth, bool bar, bool tokens, int modelColumn)
        {
            var w = PadX * 2 + ClampName(nameWidth) + Gap + Pct;
            if (modelColumn > 0) w += modelColumn + Gap;
            if (bar) w += Bar + Gap;
            if (tokens) w += Gap + Tokens;
            return w;
        }

        /// <summary>
        /// 0.2.0 までの設定（パネルの幅 hudWidth）から、同じ見た目になる名前の幅を求める。
        /// 当時の計算と同じく、パネルの幅からバー・トークン数などを引いた残りが名前（最小 70）。
        /// モデルの列は当時もパネルの幅の外に足していたので引かない。
        /// </summary>
        public static int NameWidthFromPanel(int hudWidth, bool bar, bool tokens)
        {
            var used = PadX * 2 + Gap + Pct;
            if (bar) used += Bar + Gap;
            if (tokens) used += Gap + Tokens;
            return ClampName(hudWidth - used);
        }

        public static int ClampName(int nameWidth)
        {
            return Math.Max(MinNameWidth, Math.Min(MaxNameWidth, nameWidth));
        }
    }
}
