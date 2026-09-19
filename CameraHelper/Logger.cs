using System;
using System.Linq;
using System.Text;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Filter;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace CameraHelper;

public class Logger
{
	private readonly string mLogName;

	public ILog Log { get; set; }

	public Logger(string LoggerName, string BaseDir)
	{
		if (LogManager.Exists(LoggerName) == null)
		{
			ConfigLog(LoggerName, BaseDir);
		}
		mLogName = LoggerName;
		Log = LogManager.GetLogger(LoggerName);
	}

	private void ConfigLog(string LoggerName, string BaseDir)
	{
		RollingFileAppender FileAppender = ConfigRollingFileAppender(LoggerName, BaseDir);
		AnsiColorTerminalAppender ViewerAppender = ConfigAnsiViewerAppender(LoggerName);
		Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();
		log4net.Repository.Hierarchy.Logger logger = hierarchy.GetLogger(LoggerName, hierarchy.LoggerFactory);
		logger.Hierarchy = hierarchy;
		logger.AddAppender(ViewerAppender);
		logger.AddAppender(FileAppender);
		logger.Level = Level.All;
		BasicConfigurator.Configure();
	}

	private RollingFileAppender ConfigRollingFileAppender(string LoggerName, string BaseLogDir)
	{
		RollingFileAppender obj = new RollingFileAppender
		{
			Name = "RollingFileAppender" + LoggerName,
			Encoding = Encoding.UTF8,
			File = BaseLogDir,
			AppendToFile = true,
			RollingStyle = RollingFileAppender.RollingMode.Composite,
			MaxSizeRollBackups = -1,
			MaximumFileSize = "50MB",
			LockingModel = new FileAppender.MinimalLock(),
			StaticLogFileName = false,
			DatePattern = $"'{LoggerName}'yyyy-MM-dd-HH'.log'"
		};
		PatternLayout patternLayout = new PatternLayout();
		patternLayout.ConversionPattern = "%date  %-5level  %message  %newline";
		patternLayout.ActivateOptions();
		obj.Layout = patternLayout;
		LoggerMatchFilter filter = new LoggerMatchFilter();
		filter.LoggerToMatch = LoggerName;
		filter.ActivateOptions();
		obj.AddFilter(filter);
		obj.ActivateOptions();
		return obj;
	}

	private AnsiColorTerminalAppender ConfigAnsiViewerAppender(string LoggerName)
	{
		AnsiColorTerminalAppender obj = new AnsiColorTerminalAppender
		{
			Name = "ColorConsoleAppender" + LoggerName,
			Threshold = Level.All,
			Target = "Console.Error"
		};
		AnsiColorTerminalAppender.LevelColors ErrColor = new AnsiColorTerminalAppender.LevelColors
		{
			ForeColor = AnsiColorTerminalAppender.AnsiColor.Red,
			Level = Level.Error
		};
		ErrColor.ActivateOptions();
		obj.AddMapping(ErrColor);
		AnsiColorTerminalAppender.LevelColors InfoColor = new AnsiColorTerminalAppender.LevelColors
		{
			ForeColor = AnsiColorTerminalAppender.AnsiColor.Green,
			Level = Level.Info
		};
		InfoColor.ActivateOptions();
		obj.AddMapping(InfoColor);
		PatternLayout patternLayout = new PatternLayout
		{
			ConversionPattern = "%date  %-5level  %message  %newline"
		};
		patternLayout.ActivateOptions();
		obj.Layout = patternLayout;
		LevelRangeFilter ViewerFilter = new LevelRangeFilter
		{
			LevelMin = Level.Info,
			LevelMax = Level.Fatal
		};
		ViewerFilter.ActivateOptions();
		LoggerMatchFilter LoggerFilter = new LoggerMatchFilter
		{
			LoggerToMatch = LoggerName
		};
		LoggerFilter.ActivateOptions();
		obj.AddFilter(ViewerFilter);
		obj.AddFilter(LoggerFilter);
		obj.ActivateOptions();
		return obj;
	}

	public void UpdateLogFolder(string logFolder)
	{
		IAppender[] appenders = LogManager.GetRepository().GetAppenders();
		if (appenders.Length != 0 && appenders.First((IAppender p) => p.Name == "RollingFileAppender" + mLogName) is RollingFileAppender targetApp)
		{
			targetApp.File = logFolder;
			targetApp.ActivateOptions();
		}
	}

	public void Info(string mesg)
	{
		if (Log != null)
		{
			Log.Info(mesg);
		}
	}

	public void Error(string mesg, Exception ex = null)
	{
		if (Log != null)
		{
			if (ex != null)
			{
				Log.Error(mesg + "=> " + ex.Message);
			}
			else
			{
				Log.Error(mesg);
			}
		}
	}
}
