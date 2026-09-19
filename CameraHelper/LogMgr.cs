namespace CameraHelper;

public class LogMgr
{
	private static LogMgr mgr = null;

	private static object logLock = new object();

	public static LogMgr Inst
	{
		get
		{
			if (mgr == null)
			{
				mgr = new LogMgr();
			}
			return mgr;
		}
	}

	public Logger Loggers { get; protected set; }

	public string LogPath { get; set; } = "Logs\\";

	public LogMgr()
	{
		Loggers = new Logger("System", LogPath);
	}

	public static void Log(string mesg)
	{
		try
		{
			lock (logLock)
			{
				Inst.Loggers.Info(mesg);
			}
		}
		catch
		{
		}
	}
}
