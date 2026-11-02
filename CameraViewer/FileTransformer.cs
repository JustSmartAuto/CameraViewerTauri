using System;
using System.IO;
using System.Threading;

namespace CameraViewer
{
    public class FileTransformer : IDisposable
    {
        private TransformConfig _config = new TransformConfig();
        private FileSystemWatcher _watcher;
        private readonly ActionQueueWorker _worker = new ActionQueueWorker();
        private bool _running;

        public void Init(TransformConfig config)
        {
            _config = config ?? new TransformConfig();
            _worker.Start(ThreadPriority.Normal);

            _watcher = new FileSystemWatcher
            {
                Path = _config.WatchPath ?? "",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                Filter = "*.*",
                IncludeSubdirectories = false,
                EnableRaisingEvents = false,
            };
        }

        public void Start()
        {
            if (_watcher == null || string.IsNullOrEmpty(_config.WatchPath) || !Directory.Exists(_config.WatchPath))
                return;
            if (_running) return;
            _watcher.Created -= Watcher_Created;
            _watcher.Created += Watcher_Created;
            _watcher.EnableRaisingEvents = true;
            _running = true;
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= Watcher_Created;
            }
            _running = false;
        }

        public void UpdateConfig(TransformConfig config)
        {
            Stop();
            _config = config ?? new TransformConfig();
            if (_watcher != null)
            {
                _watcher.Path = _config.WatchPath ?? "";
            }
            if (_config.IsEnable && !string.IsNullOrEmpty(_config.WatchPath) && Directory.Exists(_config.WatchPath))
                Start();
        }

        public void ProcessOnce()
        {
            try { _worker.Enqueue(new TransformAction { Config = _config }); } catch { }
        }

        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            try { _worker.Enqueue(new TransformAction { Config = _config }); } catch { }
        }

        public void Dispose()
        {
            Stop();
            _worker.Dispose();
            _watcher?.Dispose();
        }
    }
}
