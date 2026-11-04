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
        private string _lastTrigger = "";

        public void Init(TransformConfig config)
        {
            _config = config ?? new TransformConfig();
            _worker.Start(ThreadPriority.Normal);
            LogHelper.Info($"[转存] 初始化配置 name={_config.Name} enable={_config.IsEnable} watchPath={_config.WatchPath}");

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
            {
                LogHelper.Warn($"[转存] Start 跳过：watcher={(_watcher != null ? "OK" : "NULL")} pathOk={Directory.Exists(_config.WatchPath ?? "")}");
                return;
            }
            if (_running) return;
            _watcher.Created -= Watcher_Created;
            _watcher.Created += Watcher_Created;
            _watcher.EnableRaisingEvents = true;
            _running = true;
            LogHelper.Info($"[转存] 启动监听 name={_config.Name} path={_config.WatchPath}");
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= Watcher_Created;
            }
            if (_running) LogHelper.Info($"[转存] 停止监听 name={_config.Name}");
            _running = false;
        }

        public void UpdateConfig(TransformConfig config)
        {
            Stop();
            _config = config ?? new TransformConfig();
            LogHelper.Info($"[转存] 配置更新 name={_config.Name} enable={_config.IsEnable} watchPath={_config.WatchPath}");
            if (_watcher != null)
            {
                _watcher.Path = _config.WatchPath ?? "";
            }
            if (_config.IsEnable && !string.IsNullOrEmpty(_config.WatchPath) && Directory.Exists(_config.WatchPath))
                Start();
        }

        public void ProcessOnce()
        {
            try
            {
                _worker.Enqueue(new TransformAction { Config = _config, ConfigName = _config.Name, Trigger = "手动触发" });
                LogHelper.Info($"[转存] 手动触发一次 name={_config.Name}");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"[转存] 手动触发入队失败 name={_config.Name}", ex);
            }
        }

        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            // 防抖：短时间内大量 Created 事件只入队一次（以文件名去重）
            var sig = e.FullPath + "|" + DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 200; // 200ms 窗口
            if (_lastTrigger == sig) return;
            _lastTrigger = sig;
            try
            {
                _worker.Enqueue(new TransformAction { Config = _config, ConfigName = _config.Name, Trigger = $"监听:{e.Name}" });
                LogHelper.Info($"[转存] 文件事件 name={_config.Name} file={e.FullPath}");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"[转存] 文件事件入队失败 name={_config.Name} file={e.FullPath}", ex);
            }
        }

        public void Dispose()
        {
            Stop();
            _worker.Dispose();
            _watcher?.Dispose();
            LogHelper.Info($"[转存] 释放 name={_config.Name}");
        }
    }
}
