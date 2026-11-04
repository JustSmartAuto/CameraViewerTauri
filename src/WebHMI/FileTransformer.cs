using System.IO;
using System.Threading;

namespace WebHMI;

public class FileTransformer
{
	private TransformConfig Config = new TransformConfig();

	private FileSystemWatcher watcher = new FileSystemWatcher();

	private ActionQueueWorker worker = new ActionQueueWorker();

	public void Init(TransformConfig config)
	{
		Config = config;
		worker.Start(ThreadPriority.Normal);
		watcher.Path = Config.WatchPath;
		watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
		watcher.Filter = "*.*";
		watcher.IncludeSubdirectories = false;
		watcher.EnableRaisingEvents = true;
	}

	public void Close()
	{
		worker.Destroy();
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
		worker.Enqueue(new TransformAction
		{
			Config = Config
		});
	}
}
