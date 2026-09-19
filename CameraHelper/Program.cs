using System;
using System.Threading;
using System.Windows.Forms;

namespace CameraHelper;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
		{
			MessageBox.Show("UI主线程崩溃:" + e.Exception.Message);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			MessageBox.Show("子线程崩溃:" + ((Exception)e.ExceptionObject).Message);
		};
		ProjectMgr.Inst.IsOnLine = false;
		ProjectMgr.Inst.MainForm = new FrmMain();
		Application.Run(ProjectMgr.Inst.MainForm);
	}
}
