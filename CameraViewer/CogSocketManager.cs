using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CameraViewer
{
    /// <summary>
    /// 按相机 IP:Port 共享 CogSocket 连接：同一台相机被多个 CameraCell 使用（或刷新重连）时只保留一条 WebSocket。
    /// 相机每次拍照把 (标识单元格, 取值, 原图) 交给管理器，由管理器按订阅者的 identifierCell 分发，
    /// 各 Cell 再按自己的 取值 → 槽位 绑定落图（如 cellValue=11 → slot 1）。
    /// </summary>
    internal static class CogSocketManager
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, SharedConnection> Pool = new Dictionary<string, SharedConnection>(StringComparer.OrdinalIgnoreCase);

        /// <summary>订阅一台相机；同一 hostPort 的连接会被复用。</summary>
        internal static CogSocketSubscription Subscribe(string hostPort, string identifierCell, Action<string, Bitmap> onResult)
        {
            var key = CogSocketCamera.NormalizeHostPort(hostPort);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                SharedConnection shared;
                lock (Gate)
                {
                    if (!Pool.TryGetValue(key, out shared))
                    {
                        shared = new SharedConnection(key);
                        Pool[key] = shared;
                    }
                }
                var sub = shared.Add(identifierCell, onResult);
                if (sub != null) return sub;
                // 该连接正在销毁，重取一次
            }
            return new CogSocketSubscription(null);
        }

        /// <summary>强制重建该相机的共享连接（保留现有订阅者）。</summary>
        internal static void Reconnect(string hostPort)
        {
            var key = CogSocketCamera.NormalizeHostPort(hostPort);
            SharedConnection shared;
            lock (Gate) shared = Pool.TryGetValue(key, out shared) ? shared : null;
            shared?.Restart();
        }

        private static void Release(string key, SharedConnection conn)
        {
            lock (Gate)
            {
                if (Pool.TryGetValue(key, out var current) && current == conn) Pool.Remove(key);
            }
        }

        private sealed class SharedConnection
        {
            private readonly string _key;
            private readonly List<Subscriber> _subscribers = new List<Subscriber>();
            private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1);
            private readonly HashSet<string> _sessionCells = new HashSet<string>();  // 当前连接 openSession 的标识单元格
            private CogSocketCamera _cam;
            private bool _closed;

            internal SharedConnection(string key) => _key = key;

            /// <summary>加入订阅；连接已销毁时返回 null。</summary>
            internal CogSocketSubscription Add(string identifierCell, Action<string, Bitmap> onResult)
            {
                var sub = new Subscriber(identifierCell, onResult);
                lock (_subscribers)
                {
                    if (_closed) return null;
                    _subscribers.Add(sub);
                }
                _ = Task.Run(() => EnsureConnectionAsync(false));
                return new CogSocketSubscription(() => Remove(sub));
            }

            internal void Restart() => _ = Task.Run(() => EnsureConnectionAsync(true));

            private void Remove(Subscriber sub)
            {
                CogSocketCamera cam;
                lock (_subscribers)
                {
                    _subscribers.Remove(sub);
                    if (_subscribers.Count > 0) return;
                    _closed = true;
                    cam = _cam;
                    _cam = null;
                }
                Release(_key, this);
                if (cam != null) _ = Task.Run(() => DisposeCamera(cam));
            }

            private async Task EnsureConnectionAsync(bool force)
            {
                await _sync.WaitAsync();
                try
                {
                    if (_closed) return;
                    List<string> required;
                    lock (_subscribers) required = _subscribers.Select(s => s.IdentifierCell).Distinct().ToList();
                    if (required.Count == 0) return;

                    // 现有连接已覆盖所有标识单元格时直接复用
                    if (!force && _cam != null && _cam.IsConnected && _sessionCells.IsSupersetOf(required)) return;

                    var old = _cam;
                    _cam = null;
                    DisposeCamera(old);
                    _sessionCells.Clear();
                    _sessionCells.UnionWith(required);

                    var cam = new CogSocketCamera(_key, required);
                    cam.CellResult += OnCellResult;
                    _cam = cam;
                    try { await cam.ConnectAsync(required); } catch { /* 失败保留实例，下次订阅或刷新时重试 */ }
                }
                finally { _sync.Release(); }
            }

            private void DisposeCamera(CogSocketCamera cam)
            {
                if (cam == null) return;
                cam.CellResult -= OnCellResult;
                try { cam.CloseAsync().Wait(TimeSpan.FromSeconds(3)); } catch { }
                cam.Dispose();
            }

            /// <summary>把一次拍照结果分发给关注该标识单元格的订阅者；位图所有权随回调转移。</summary>
            private void OnCellResult(string identifierCell, string value, Bitmap bmp)
            {
                Subscriber[] targets;
                lock (_subscribers) targets = _subscribers.Where(s => s.IdentifierCell == identifierCell).ToArray();
                if (targets.Length == 0) { bmp.Dispose(); return; }

                if (targets.Length == 1)
                {
                    try { targets[0].OnResult(value, bmp); } catch { bmp.Dispose(); }
                    return;
                }
                foreach (var t in targets)
                {
                    Bitmap copy = null;
                    try
                    {
                        copy = (Bitmap)bmp.Clone();
                        t.OnResult(value, copy);
                    }
                    catch { copy?.Dispose(); }
                }
                bmp.Dispose();
            }

            private sealed class Subscriber
            {
                internal readonly string IdentifierCell;   // 归一化后的单元格名
                internal readonly Action<string, Bitmap> OnResult;

                internal Subscriber(string identifierCell, Action<string, Bitmap> onResult)
                {
                    IdentifierCell = CogSocketCamera.NormalizeCell(identifierCell);
                    OnResult = onResult;
                }
            }
        }
    }

    /// <summary>单个 Cell 对共享连接的订阅句柄；Dispose 后不再接收图片。</summary>
    internal sealed class CogSocketSubscription : IDisposable
    {
        private Action _remove;

        internal CogSocketSubscription(Action remove) => _remove = remove;

        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}
