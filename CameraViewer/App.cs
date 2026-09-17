using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;

namespace CameraViewerDotnet
{
    public static class App
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 全局未处理异常处理：崩溃时优雅退出（释放文件锁），避免产生僵尸进程
            Application.ThreadException += (s, args) => CrashExit(args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                CrashExit(args.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                args.SetObserved();
            };

            ConfigService.Load();
            I18n.Language = ConfigService.App.language == "en" ? "en" : "zh";
            ThemeManager.Apply(ConfigService.App.theme);

            Application.Run(new MainWindow());
        }

        public static void CrashExit(Exception ex)
        {
            try
            {
                var msg = ex?.ToString() ?? "未知错误 / Unknown error";
                MessageBox.Show(msg, "CameraViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            Environment.Exit(1);
        }
    }
}
