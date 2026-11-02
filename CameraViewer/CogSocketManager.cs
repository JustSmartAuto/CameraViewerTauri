using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace CameraViewer
{
    /// <summary>
    /// 管理 CogSocketCamera 实例的共享池：同一个 hostPort 只建一个连接，多个 CameraCell 订阅使用引用计数。
    /// </summary>
    public static class CogSocketManager
    {
        private static readonly ConcurrentDictionary<string, CogSocketCamera> _cameras = new ConcurrentDictionary<string, CogSocketCamera>();
        private static readonly ConcurrentDictionary<string, int> _refCounts = new ConcurrentDictionary<string, int>();

        public static CogSocketCamera GetOrCreate(string hostPort)
        {
            if (string.IsNullOrWhiteSpace(hostPort)) return null;
            var key = Normalize(hostPort);
            var cam = _cameras.GetOrAdd(key, _ => new CogSocketCamera(key));
            _refCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
            return cam;
        }

        public static async Task ReleaseAsync(string hostPort)
        {
            if (string.IsNullOrWhiteSpace(hostPort)) return;
            var key = Normalize(hostPort);
            _refCounts.AddOrUpdate(key, 0, (_, c) => Math.Max(0, c - 1));
            if (_refCounts.TryGetValue(key, out var c) && c == 0)
            {
                _refCounts.TryRemove(key, out _);
                if (_cameras.TryRemove(key, out var cam))
                {
                    try { await cam.CloseAsync(); } catch { }
                    cam.Dispose();
                }
            }
        }

        public static int RefCount(string hostPort)
        {
            var key = Normalize(hostPort);
            return _refCounts.TryGetValue(key, out var c) ? c : 0;
        }

        private static string Normalize(string hostPort)
        {
            var hp = hostPort.Trim();
            if (!hp.Contains(':')) hp += ":8087";
            return hp.ToLowerInvariant();
        }
    }
}
