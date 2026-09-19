using System.IO;

namespace CameraHelper;

public class FileTransformer
{
	public TransformConfig Config = new TransformConfig();

	private FileSystemWatcher watcher = new FileSystemWatcher();

	public void Init(TransformConfig config)
	{
		Config = config;
		if (!string.IsNullOrEmpty(Config.WatchPath) && Directory.Exists(Config.WatchPath))
		{
			watcher.Path = Config.WatchPath;
			watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
			watcher.Filter = "*.*";
			watcher.IncludeSubdirectories = false;
			watcher.EnableRaisingEvents = true;
		}
	}

	public void Close()
	{
	}

	public void Start()
	{
		watcher.Created -= Watcher_Created;
		watcher.Created += Watcher_Created;
	}

	public void Stop()
	{
		watcher.Created -= Watcher_Created;
	}

	private void Watcher_Created(object sender, FileSystemEventArgs e)
	{
		ProjectMgr.Inst.ActionQueueWorker.Enqueue(new TransformAction
		{
			Config = Config
		});
	}
}
