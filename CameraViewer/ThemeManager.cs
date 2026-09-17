using System.Linq;
using System.Windows;
using System.Windows.Media;
using HandyControl.Themes;

namespace CameraViewer
{
    public static class ThemeManager
    {
        public static string Theme = "light";

        public static void Apply(string theme)
        {
            if (theme != "dark") theme = "light";
            Theme = theme;

            var app = Application.Current;
            if (!app.Resources.MergedDictionaries.Any(d => d is ThemeResources))
                app.Resources.MergedDictionaries.Add(new ThemeResources());

            HandyControl.Themes.ThemeManager.Current.ApplicationTheme =
                theme == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark;

            var r = app.Resources;
            if (theme == "dark")
            {
                r["BgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1E1E1E"));
                r["BgBrush2"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF252526"));
                r["BgBrush3"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D2D30"));
                r["InputBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3C3C3C"));
                r["FgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCCCCCC"));
                r["FgDimBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF888888"));
                r["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0E639C"));
                r["HoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF37373D"));
                r["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3F3F46"));
            }
            else
            {
                r["BgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF0F0F0"));
                r["BgBrush2"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFFFF"));
                r["BgBrush3"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7F7F7"));
                r["InputBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFFFF"));
                r["FgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF333333"));
                r["FgDimBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF777777"));
                r["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0078D4"));
                r["HoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE5E5E5"));
                r["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0"));
            }
        }
    }
}
