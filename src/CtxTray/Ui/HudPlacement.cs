using System;
using System.Drawing;

namespace CtxTray.Ui
{
    /// <summary>
    /// HUD をどこに置くかを決める。
    ///
    /// HUD の高さはセッションの数で変わる。上端を固定していた頃は、行が増えると
    /// 下へ伸びてタスクバーの裏や画面の外へ出ていた（2026-09-18 に修正）。
    /// 画面の下半分に置かれている HUD は下端を固定して上へ伸ばす。既定の位置（右下の隅）と同じ動きになる。
    ///
    /// ここは画面の情報を引数で受け取るだけの計算にしてある（Screen や Form に触らない）。
    /// tests/CtxTray.Tests から呼んで確かめられるようにするため。
    /// </summary>
    internal static class HudPlacement
    {
        /// <summary>
        /// この位置と大きさの HUD を、下端基準で扱うか。
        /// HUD の中心が作業領域の下半分にあれば下端基準。
        /// </summary>
        public static bool AnchorBottom(Rectangle bounds, Rectangle workArea)
        {
            var center = bounds.Top + bounds.Height / 2;
            return center >= workArea.Top + workArea.Height / 2;
        }

        /// <summary>
        /// 保存された座標から、実際に置く左上の座標を求める。
        ///
        /// anchorBottom のときは y を下端として扱う。
        /// どちらの場合も、作業領域からはみ出した分は内側へ押し戻す
        /// （外部モニタを外した・文字を大きくした・行が増えた、のどれでも画面内に残るように）。
        /// 作業領域より大きい HUD は、左上を作業領域の左上に合わせる（頭を切らない）。
        /// </summary>
        public static Point Place(int x, int y, bool anchorBottom, Size size, Rectangle workArea)
        {
            var top = anchorBottom ? y - size.Height : y;

            var maxX = workArea.Right - size.Width;
            var maxY = workArea.Bottom - size.Height;

            return new Point(Clamp(x, workArea.Left, maxX), Clamp(top, workArea.Top, maxY));
        }

        /// <summary>
        /// 位置を保存するときの座標。下端基準なら下端の y を返す。
        /// </summary>
        public static Point Anchor(Rectangle bounds, bool anchorBottom)
        {
            return new Point(bounds.Left, anchorBottom ? bounds.Bottom : bounds.Top);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            // 作業領域より大きいときは hi < lo になる。そのときは lo（左上に合わせる）。
            if (hi < lo) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
