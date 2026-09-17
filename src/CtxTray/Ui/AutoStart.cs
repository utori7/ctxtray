using System;
using System.IO;
using System.Reflection;

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

        public static bool IsEnabled
        {
            get
            {
                try { return File.Exists(LinkPath); }
                catch { return false; }
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    if (File.Exists(LinkPath)) File.Delete(LinkPath);
                    return;
                }

                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                var shell = Activator.CreateInstance(shellType);
                try
                {
                    var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                                                      null, shell, new object[] { LinkPath });
                    var linkType = link.GetType();

                    Set(linkType, link, "TargetPath", ExePath);
                    Set(linkType, link, "WorkingDirectory", Path.GetDirectoryName(ExePath));
                    Set(linkType, link, "Description", Config.Strings.Get("autostart.description"));

                    linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
            }
            catch
            {
                // 自動起動の登録に失敗しても本体の動作は続ける。
            }
        }

        private static void Set(Type type, object instance, string property, object value)
        {
            type.InvokeMember(property, BindingFlags.SetProperty, null, instance, new[] { value });
        }
    }
}
