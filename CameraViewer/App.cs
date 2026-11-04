using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using CameraViewer;

namespace CameraViewerDotnet
{
    public static class App
    {
        /// <summary>每套文件中转配置对应一个 FileTransformer。</summary>
        public static List<FileTransformer> Transits { get; } = new List<FileTransformer>();
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

            // 文件转存日志初始化（放在 logs/ 目录，跟随配置目录）
            var cfgDir = ConfigService.ConfigDir;
            LogHelper.Init(Path.Combine(cfgDir, "logs"));
            LogHelper.Info($"程序启动，配置目录 {cfgDir}");

            // 开机自启快捷方式带 --auto-start 参数，按配置延时启动（默认 1 秒）
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (!string.Equals(arg, StartupManager.AutoStartArgument, StringComparison.OrdinalIgnoreCase)) continue;
                var delay = Math.Clamp(ConfigService.App.startupDelay, 0, 60);
                if (delay > 0) System.Threading.Thread.Sleep(delay * 1000);
                break;
            }

            I18n.Language = ConfigService.App.language == "en" ? "en" : "zh";
            ThemeManager.Apply(ConfigService.App.theme);

            // Init file transit + cleaner
            StartTransits();
            Cleaner = new ImageCleaner();
            if (ConfigService.Clean.IsEnable)
            {
                Cleaner.Config = ConfigService.Clean;
                Cleaner.StartClean();
            }

            Application.ApplicationExit += (s, e) =>
            {
                StopTransits();
                Cleaner?.Dispose();
            };

            Application.Run(new MainWindow());
        }

        /// <summary>按当前配置列表重建所有中转监听器（保存/删除配置后调用）。</summary>
        public static void RecreateTransits()
        {
            StopTransits();
            StartTransits();
        }

        private static void StartTransits()
        {
            foreach (var cfg in ConfigService.Transforms)
            {
                if (!cfg.IsEnable || string.IsNullOrEmpty(cfg.WatchPath) || !System.IO.Directory.Exists(cfg.WatchPath))
                    continue;
                var transformer = new FileTransformer();
                transformer.Init(cfg);
                transformer.Start();
                Transits.Add(transformer);
            }
        }

        private static void StopTransits()
        {
            foreach (var t in Transits)
            {
                try { t.Dispose(); } catch { }
            }
            Transits.Clear();
        }

        public static void CrashExit(Exception ex)
        {
            try
            {
                StopTransits();
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
