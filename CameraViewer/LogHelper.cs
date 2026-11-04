using System;
using System.IO;
using System.Threading;

namespace CameraViewer
{
    /// <summary>轻量文件日志：线程安全，100MB 自动滚动分卷，零第三方依赖。</summary>
    public static class LogHelper
    {
        private static readonly object _lock = new();
        private const int RollSizeBytes = 100 * 1024 * 1024; // 100MB
        private const int MaxFiles = 10;                     // 保留 10 卷
        private static string _logDir;
        private static string BaseFile => Path.Combine(_logDir, "transit.log");

        public static void Init(string logDirectory)
        {
            _logDir = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(_logDir);
        }

        private static void EnsureDir()
        {
            if (_logDir == null) _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(_logDir);
        }

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                Monitor.Enter(_lock);
                EnsureDir();
                RollIfNeeded();

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                if (ex != null)
                    line += Environment.NewLine + ex;

                using var fs = new FileStream(BaseFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
                using var sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
                sw.WriteLine(line);
            }
            catch
            {
                // 日志本身不能抛出
            }
            finally
            {
                if (Monitor.IsEntered(_lock)) Monitor.Exit(_lock);
            }
        }

        private static void RollIfNeeded()
        {
            if (!File.Exists(BaseFile)) return;
            var info = new FileInfo(BaseFile);
            if (info.Length < RollSizeBytes) return;

            // 删除最老卷：transit.log.10
            var oldest = BaseFile + "." + MaxFiles;
            try { if (File.Exists(oldest)) File.Delete(oldest); } catch { }

            // 依次重命名：.9 → .10, .8 → .9, ... , transit.log → .1
            for (var i = MaxFiles - 1; i >= 1; i--)
            {
                var src = BaseFile + "." + i;
                if (!File.Exists(src)) continue;
                var dst = BaseFile + "." + (i + 1);
                try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                try { File.Move(src, dst); } catch { }
            }

            try { File.Move(BaseFile, BaseFile + ".1"); } catch { }
        }

        public static string[] ListFiles()
        {
            try
            {
                EnsureDir();
                return Directory.GetFiles(_logDir, "transit.log*");
            }
            catch { return Array.Empty<string>(); }
        }
    }
}
