using System;
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
            ConfigService.Load();
            I18n.Language = ConfigService.App.language == "en" ? "en" : "zh";
            ThemeManager.Apply(ConfigService.App.theme);

            var win = new MainWindow();
            MainWindow = win;
            win.Show();
            base.OnStartup(e);
        }
    }
}
