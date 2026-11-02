using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CameraViewer
{
    public class ImageCleaner : IDisposable
    {
        private Timer _timer;
        private CancellationTokenSource _cts;
        public CleanConfig Config { get; set; } = new CleanConfig();
        public bool IsCleaning { get; private set; }

        public void StartClean()
        {
            StopClean();
            if (Config?.IsEnable != true) return;
            var intervalMs = (int)(Config.ScanInterval * 3600000.0);
            if (intervalMs < 1000) intervalMs = 3600000; // min 1h
            _timer = new Timer(_ => CleanOnce(), null, intervalMs, intervalMs);
        }

        public void StopClean()
        {
            _timer?.Dispose();
            _timer = null;
            try { _cts?.Cancel(); } catch { }
            IsCleaning = false;
        }

        public void UpdateConfig(CleanConfig config)
        {
            Config = config ?? new CleanConfig();
            StartClean();
        }

        public void CleanOnce()
        {
            if (IsCleaning || Config == null || string.IsNullOrEmpty(Config.FolderPath) || !Directory.Exists(Config.FolderPath))
                return;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            IsCleaning = true;
            Task.Run(() =>
            {
                try
                {
                    var root = Path.GetPathRoot(Config.FolderPath);
                    // Collect all files recursively
                    var allFiles = new List<FileInfo>();
                    CollectFiles(new DirectoryInfo(Config.FolderPath), allFiles, ct);

                    // Sort by CreationTime ascending (oldest first)
                    var sorted = allFiles.OrderBy(f => f.CreationTime).ToList();

                    foreach (var file in sorted)
                    {
                        if (ct.IsCancellationRequested) break;
                        try { Thread.Sleep(Config.DeleteInterval); } catch { }
                        if (!file.Exists) continue;

                        if (Config.IsEnableHoldDays &&
                            (DateTime.Now - file.CreationTime).TotalDays > Config.HoldDays)
                        {
                            try { file.Delete(); } catch { }
                        }
                        else if (Config.IsEnableRemainingSpace &&
                                 GetFreeSpaceGB(root, out var freeGB) &&
                                 freeGB < Config.RemainingSpace)
                        {
                            try { file.Delete(); } catch { }
                        }
                    }

                    if (Config.IsCleanEmptyFolders)
                        CleanEmptyDirs(Config.FolderPath);
                }
                catch { }
                finally { IsCleaning = false; }
            }, ct);
        }

        private static void CollectFiles(DirectoryInfo dir, List<FileInfo> list, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                foreach (var f in dir.GetFiles())
                    list.Add(f);
                foreach (var sub in dir.GetDirectories())
                    CollectFiles(sub, list, ct);
            }
            catch { }
        }

        private static void CleanEmptyDirs(string rootPath)
        {
            // Iterate until no more empty dirs found
            while (true)
            {
                var found = new List<string>();
                FindEmptyDirs(new DirectoryInfo(rootPath), found);
                if (found.Count == 0) break;
                foreach (var d in found)
                {
                    try { if (Directory.Exists(d)) Directory.Delete(d); } catch { }
                }
            }
        }

        private static void FindEmptyDirs(DirectoryInfo dir, List<string> empty)
        {
            try
            {
                var subDirs = dir.GetDirectories();
                var files = dir.GetFiles();
                if (subDirs.Length == 0 && files.Length == 0)
                {
                    empty.Add(dir.FullName);
                    return;
                }
                foreach (var sub in subDirs)
                    FindEmptyDirs(sub, empty);
            }
            catch { }
        }

        private static bool GetFreeSpaceGB(string rootPath, out double freeGB)
        {
            freeGB = 0;
            if (string.IsNullOrWhiteSpace(rootPath)) return false;
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.Name.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
                        rootPath.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        freeGB = (double)drive.TotalFreeSpace / 1073741824.0;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            StopClean();
            _cts?.Dispose();
        }
    }
}
