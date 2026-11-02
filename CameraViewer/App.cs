using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;

namespace CameraViewerDotnet
{
    public static class App
    {
        public static FileTransformer Transit { get; private set; }
        public static ImageCleaner Cleaner { get; private set; }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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

            // Init file transit + cleaner
            Transit = new FileTransformer();
            if (!string.IsNullOrEmpty(ConfigService.Transform.WatchPath) && ConfigService.Transform.IsEnable)
            {
                Transit.Init(ConfigService.Transform);
                Transit.Start();
            }
            Cleaner = new ImageCleaner();
            if (ConfigService.Clean.IsEnable)
            {
                Cleaner.Config = ConfigService.Clean;
                Cleaner.StartClean();
            }

            Application.ApplicationExit += (s, e) =>
            {
                Transit?.Dispose();
                Cleaner?.Dispose();
            };

            Application.Run(new MainWindow());
        }

        public static void CrashExit(Exception ex)
        {
            try
            {
                Transit?.Dispose();
                Cleaner?.Dispose();
            }
            catch { }
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
