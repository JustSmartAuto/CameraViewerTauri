using System.Windows;

namespace NomacsNet;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application();
        var window = new MainWindow();
        if (args.Length > 0)
            window.OpenPath(args[0]);
        return app.Run(window);
    }
}
