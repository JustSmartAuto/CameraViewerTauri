using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CameraViewer
{
    /// <summary>开机自动启动：在 shell:startup 目录创建/删除带 --auto-start 参数的快捷方式。</summary>
    public static class StartupManager
    {
        public const string AutoStartArgument = "--auto-start";
        private const string ShortcutFileName = "CameraViewerDotnet.lnk";

        public static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutFileName);

        public static bool IsEnabled => File.Exists(ShortcutPath);

        public static void SetEnabled(bool enabled)
        {
            if (enabled) CreateShortcut();
            else DeleteShortcut();
        }

        private static void CreateShortcut()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));

            object shell = null;
            object shortcut = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("WScript.Shell unavailable");

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { ShortcutPath });

                var shortcutType = shortcut.GetType();
                SetProperty(shortcutType, shortcut, "TargetPath", Application.ExecutablePath);
                SetProperty(shortcutType, shortcut, "WorkingDirectory",
                    AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                SetProperty(shortcutType, shortcut, "Arguments", AutoStartArgument);
                SetProperty(shortcutType, shortcut, "IconLocation", Application.ExecutablePath + ",0");
                SetProperty(shortcutType, shortcut, "Description", "CameraViewerDotnet");
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }

        private static void SetProperty(Type type, object target, string name, object value) =>
            type.InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value });

        private static void DeleteShortcut()
        {
            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
        }
    }
}
