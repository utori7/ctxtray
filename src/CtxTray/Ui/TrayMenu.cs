using System;
using System.Drawing;
using System.Windows.Forms;
using CtxTray.Native;

namespace CtxTray.Ui
{
    /// <summary>
    /// トレイアイコンと HUD の右クリックで出すメニュー。
    ///
    /// ★ Windows 標準のメニュー（ContextMenu。中身は Win32 のポップアップメニュー）を使う。
    ///   0.4.0 までの ContextMenuStrip は WinForms が Office 2003 風に自前で描く疑似メニューで、
    ///   チェックの欄や余白が 100% 前提の寸法のまま、Windows 11 の角丸・影にもならず古く見えた。
    ///   文字もサインイン時の倍率で覚えたフォントを使うので、倍率やテキストのサイズを
    ///   サインインし直さずに変えると追従しなかった（2026-09-26、利用者の指摘と実測）。
    ///   Windows 標準のメニューなら、見た目・倍率・テキストのサイズは Windows が面倒を見る。
    ///
    /// 標準のメニューは項目ごとの説明（ツールチップ）を出せない。
    /// </summary>
    internal sealed class TrayMenu : IDisposable
    {
        /// <summary>MenuID（Windows に渡る項目の番号）は protected なので、取り出すために派生させる。</summary>
        private sealed class Item : MenuItem
        {
            public Item(string text, EventHandler onClick) : base(text, onClick) { }
            public int Id { get { return MenuID; } }
        }

        private readonly ContextMenu _menu = new ContextMenu();

        /// <summary>開く直前。チェックや文言を付け直すのに使う（トレイからも HUD からも来る）。</summary>
        public event EventHandler Opening;

        public TrayMenu()
        {
            // トレイから開くときは NotifyIcon が Popup を起こす。
            _menu.Popup += (s, e) => RaiseOpening();
        }

        /// <summary>NotifyIcon.ContextMenu に渡す実体。</summary>
        public ContextMenu Menu { get { return _menu; } }

        public MenuItem Add(string text, Action onClick)
        {
            var item = new Item(text, (s, e) => onClick());
            _menu.MenuItems.Add(item);
            return item;
        }

        public void AddSeparator()
        {
            _menu.MenuItems.Add(new MenuItem("-"));
        }

        /// <summary>
        /// HUD の右クリックから出す。
        ///
        /// ContextMenu.Show は持ち主の窓を前面にしないので、前面に来ない HUD（WS_EX_NOACTIVATE）から出すと
        /// メニューの外を押しても閉じない。NotifyIcon と同じく、前面にしてから出し、閉じた後に WM_NULL を送る。
        /// 選ばれた項目は戻り値で受け取って押す（WM_COMMAND を HUD の窓に流さない）。
        /// </summary>
        public void ShowAt(Control owner, Point screen)
        {
            if (owner == null || owner.IsDisposed) return;

            RaiseOpening();
            var hwnd = owner.Handle;
            NativeMethods.SetForegroundWindow(hwnd);
            var id = NativeMethods.TrackPopupMenuEx(_menu.Handle,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD, screen.X, screen.Y, hwnd, IntPtr.Zero);
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (id == 0) return;

            foreach (MenuItem m in _menu.MenuItems)
            {
                var item = m as Item;
                if (item != null && item.Id == id)
                {
                    item.PerformClick();
                    return;
                }
            }
        }

        private void RaiseOpening()
        {
            var handler = Opening;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static int _appliedMode = -1;

        /// <summary>
        /// メニューの明暗を ctxtray の配色に合わせる（2026-09-26、利用者の決定）。
        ///
        /// 何もしないと Windows 標準のメニューはダークモードでも白い。暗くするには公開されていない関数が要る
        /// （NativeMethods の説明を参照）。効かなくなっても白に戻るだけなので、失敗は無視する。
        /// コントラストテーマでは Windows の配色に任せる。プロセス全体の設定なので、変わったときだけ呼ぶ。
        /// </summary>
        public static void ApplyTheme(Theme theme)
        {
            if (theme == null) return;
            // 番号 135 の意味が今のものになったのは Windows 10 1903（ビルド 18362）から。
            if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 18362) return;

            var mode = theme.IsHighContrast ? NativeMethods.AppModeDefault
                     : theme.IsDark ? NativeMethods.AppModeForceDark
                     : NativeMethods.AppModeForceLight;
            if (mode == _appliedMode) return;

            try
            {
                NativeMethods.SetPreferredAppMode(mode);
                NativeMethods.FlushMenuThemes();
                _appliedMode = mode;
            }
            catch
            {
                // 関数が無い版。白いメニューのまま使う。
            }
        }

        public void Dispose()
        {
            _menu.Dispose();
        }
    }
}
