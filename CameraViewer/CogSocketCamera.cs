using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CameraViewer
{
    /// <summary>
    /// Cognex In-Sight CogSocket WebSocket client.
    /// Manages session, subscribes to cell result events, and holds the latest rendering Bitmap per cell.
    /// </summary>
    public class CogSocketCamera : IDisposable
    {
        private readonly string _hostPort;
        private readonly Uri _wsUri;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _receiveLoop;
        private int _sid; // session id from auth response
        private int _rid; // request id counter

        // Latest frame per cell name
        private readonly ConcurrentDictionary<string, Bitmap> _cellFrames = new ConcurrentDictionary<string, Bitmap>();
        private volatile Bitmap _latestMainFrame; // main acquisition image

        public string IpPort => _hostPort;
        public IReadOnlyDictionary<string, Bitmap> CellFrames => _cellFrames;
        public Bitmap LatestMainFrame => _latestMainFrame;

        public event Action<string> CellResultUpdated; // cell name updated
        public event Action MainFrameUpdated;

        public CogSocketCamera(string hostPort)
        {
            _hostPort = hostPort ?? "";
            // Normalize
            var hp = _hostPort;
            if (!hp.Contains(':')) hp += ":8087";
            _wsUri = new Uri($"ws://{hp}/ws");
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(_wsUri, _cts.Token);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
            // Auth: simple anonymous login
            await SendAsync(new { method = "login", @params = new { user = "", password = "" }, id = ++_rid }, _cts.Token);
        }

        public async Task SubscribeCells(IEnumerable<string> cellNames, CancellationToken ct = default)
        {
            if (_ws?.State != WebSocketState.Open) return;
            foreach (var name in cellNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                // Listen: {"method":"listen","params":{"path":"{sid}/cell/{name}/result"},"id":N}
                var path = $"{_sid}/cell/{name.Trim()}/result";
                await SendAsync(new { method = "listen", @params = new { path }, id = ++_rid }, ct);
            }
        }

        public async Task UnsubscribeAll(CancellationToken ct = default)
        {
            if (_ws?.State != WebSocketState.Open) return;
            foreach (var kv in _cellFrames)
            {
                var path = $"{_sid}/cell/{kv.Key}/result";
                await SendAsync(new { method = "unlisten", @params = new { path }, id = ++_rid }, ct);
            }
        }

        public Bitmap TakeLatestCell(string cellName)
        {
            _cellFrames.TryGetValue(cellName, out var bmp);
            return bmp;
        }

        private async Task SendAsync(object payload, CancellationToken ct)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            catch { }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[65536];
            var ms = new MemoryStream();
            try
            {
                while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    ms.Position = 0;
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    HandleMessage(json);
                }
            }
            catch { }
        }

        private void HandleMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Response: has "id"
                if (root.TryGetProperty("id", out _) && root.TryGetProperty("result", out var result))
                {
                    // login result: { "sid": 12345, ... }
                    if (result.TryGetProperty("sid", out var sidEl) && sidEl.ValueKind == JsonValueKind.Number)
                    {
                        _sid = sidEl.GetInt32();
                    }
                    return;
                }

                // Event: has "method" and "params"
                if (root.TryGetProperty("method", out var methodEl))
                {
                    var method = methodEl.GetString();
                    if (root.TryGetProperty("params", out var p))
                    {
                        HandleEvent(method, p);
                    }
                }
            }
            catch { }
        }

        private void HandleEvent(string method, JsonElement p)
        {
            // cell result event: { "path": "{sid}/cell/{name}/result", "data": { "format": "png", "data": "base64..." } }
            if (p.TryGetProperty("path", out var pathEl) && p.TryGetProperty("data", out var dataEl))
            {
                var path = pathEl.GetString() ?? "";
                // Extract cell name: {sid}/cell/{name}/result → {name}
                var parts = path.Split('/');
                if (parts.Length >= 3 && parts[1] == "cell")
                {
                    var cellName = parts[2];
                    var bmp = DecodeImage(dataEl);
                    if (bmp != null)
                    {
                        // Dispose old
                        if (_cellFrames.TryRemove(cellName, out var old)) old?.Dispose();
                        _cellFrames.TryAdd(cellName, bmp);
                        CellResultUpdated?.Invoke(cellName);
                    }
                }
            }
            // Main acquisition result
            if (method == "resultChanged" || method == "acquisitionComplete")
            {
                if (p.TryGetProperty("data", out var mainDataEl))
                {
                    var bmp = DecodeImage(mainDataEl);
                    if (bmp != null)
                    {
                        _latestMainFrame?.Dispose();
                        _latestMainFrame = bmp;
                        MainFrameUpdated?.Invoke();
                    }
                }
            }
        }

        private static Bitmap DecodeImage(JsonElement dataEl)
        {
            try
            {
                string base64 = null;
                if (dataEl.TryGetProperty("data", out var d1))
                    base64 = d1.GetString();
                else if (dataEl.ValueKind == JsonValueKind.String)
                    base64 = dataEl.GetString();

                if (string.IsNullOrEmpty(base64)) return null;
                var bytes = Convert.FromBase64String(base64);
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch { return null; }
        }

        public async Task CloseAsync()
        {
            try { await UnsubscribeAll(); } catch { }
            try { _cts?.Cancel(); } catch { }
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Dispose(); } catch { }
            foreach (var kv in _cellFrames) kv.Value?.Dispose();
            _cellFrames.Clear();
            _latestMainFrame?.Dispose();
        }
    }
}
