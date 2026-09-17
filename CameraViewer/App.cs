using System;
using System.Threading.Tasks;
using System.Windows;
using CameraViewer;

namespace CameraViewerDotnet
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 全局未处理异常处理：崩溃时优雅退出（释放文件锁），避免产生僵尸进程
            this.DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true;
                CrashExit(args.Exception);
            };
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

            var win = new MainWindow();
            MainWindow = win;
            win.Show();
            base.OnStartup(e);
        }

        private static void CrashExit(Exception ex)
        {
            try
            {
                var msg = ex?.ToString() ?? "未知错误 / Unknown error";
                if (Current?.MainWindow != null)
                    MessageBox.Show(msg, "CameraViewer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            try { Current?.Shutdown(); } catch { }
            Environment.Exit(1);
        }
    }
}
