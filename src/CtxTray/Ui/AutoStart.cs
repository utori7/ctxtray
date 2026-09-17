using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CtxTray.Ui
{
    /// <summary>
    /// ログオン時の自動起動。
    ///
    /// スタートアップフォルダにショートカットを 1 個置くだけ。
    /// レジストリもタスクスケジューラも触らないので、嫌になったら
    /// 利用者がエクスプローラでファイルを消せば元に戻る。
    ///
    /// .lnk の作成は WScript.Shell を遅延バインドで呼ぶ。
    /// COM 参照を足さずに済み、追加の DLL も要らない。
    /// </summary>
    internal static class AutoStart
    {
        private const string LinkName = "ctxtray.lnk";

        private static string LinkPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName);
            }
        }

        private static string ExePath
        {
            get { return Assembly.GetEntryAssembly().Location; }
        }

        /// <summary>
        /// 自動起動が「いま動いているこの exe」で有効か。
        ///
        /// ★ ショートカットがあるだけでは有効とみなさない。
        ///   exe を別の場所へ移すと、ショートカットは古い場所を指したまま残り、
        ///   サインインしても何も起動しない。以前はそれでもメニューにチェックが付き、
        ///   壊れていることに気づけなかった（フォルダ名を変えたときに実際に起きた）。
        ///   いまの exe を指していなければチェックを外して見せ、クリックで作り直せるようにする。
        ///   別の場所を指すショートカットを勝手に書き換えることはしない（利用者の操作を待つ）。
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    if (!File.Exists(LinkPath)) return false;
                    return PointsTo(ReadTarget(LinkPath), ExePath);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ショートカットの指す先が exe と同じファイルか。大文字小文字と「..」などの表記の違いは無視する。
        /// </summary>
        internal static bool PointsTo(string target, string exe)
        {
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(exe)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(target), Path.GetFullPath(exe),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 有効にするときは、既にあるショートカットも上書きする（古い場所を指していれば直る）。
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    if (File.Exists(LinkPath)) File.Delete(LinkPath);
                    return;
                }

                WithShortcut(LinkPath, (link, linkType) =>
                {
                    Set(linkType, link, "TargetPath", ExePath);
                    Set(linkType, link, "WorkingDirectory", Path.GetDirectoryName(ExePath));
                    Set(linkType, link, "Description", Config.Strings.Get("autostart.description"));
                    linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                });
            }
            catch
            {
                // 自動起動の登録に失敗しても本体の動作は続ける。
            }
        }

        private static string ReadTarget(string linkPath)
        {
            string target = null;
            WithShortcut(linkPath, (link, linkType) =>
            {
                target = linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string;
            });
            return target;
        }

        /// <summary>
        /// WScript.Shell でショートカットを開く（無ければ新規）。使い終わった COM オブジェクトは必ず解放する。
        /// </summary>
        private static void WithShortcut(string linkPath, Action<object, Type> use)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("WScript.Shell is not available");

            var shell = Activator.CreateInstance(shellType);
            object link = null;
            try
            {
                link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                                              null, shell, new object[] { linkPath });
                use(link, link.GetType());
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void Set(Type type, object instance, string property, object value)
        {
            type.InvokeMember(property, BindingFlags.SetProperty, null, instance, new[] { value });
        }
    }
}
