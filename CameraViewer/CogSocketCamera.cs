using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CameraViewer
{
    /// <summary>
    /// Cognex In-Sight CogSocket WebSocket client.
    /// Protocol: ws://host:port/ws  →  JSON-RPC {"$type":"post|get|listen|unlisten","id":N,"path":"...","body":...}
    /// Session flow: connect → hello(optional) → detect root(cam0/hmi or system) →
    ///   openSession(root/openSession, {cellNames}) → login → keepAlive(15s) →
    ///   listen {sid}/resultChanged → ready → cycle.
    /// Images: HmiResult.cells[].data gives URL (relative or data:), HTTP GET or decode base64.
    /// </summary>
    public class CogSocketCamera : IDisposable
    {
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly Uri _wsUri;
        private readonly HttpClient _http;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _readLoop;
        private Task _keepAliveLoop;

        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly ConcurrentDictionary<string, Action<JsonElement>> _listeners = new();

        private string _root = "cam0/hmi";
        private string _sessionId;

        // Latest frame per cell name
        private readonly ConcurrentDictionary<string, Bitmap> _cellFrames = new();
        public Bitmap LatestMainFrame { get; private set; }

        public string IpPort { get; }
        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public IReadOnlyDictionary<string, Bitmap> CellFrames => _cellFrames;

        public event Action<string> CellResultUpdated;
        public event Action MainFrameUpdated;

        public CogSocketCamera(string hostPort, string username = "admin", string password = "")
        {
            var hp = (hostPort ?? "").Trim();
            if (!hp.Contains(':')) hp += ":8087";
            IpPort = hp;
            var parts = hp.Split(':');
            _host = parts[0];
            _port = int.TryParse(parts[1], out var p) ? p : 8087;
            _username = username;
            _password = password;
            _wsUri = new Uri($"ws://{_host}:{_port}/ws");
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.Add("User-Agent", "CameraViewerDotnet");
        }

        private readonly string _host;
        private int _nextId;

        // ==================== Public ====================

        public async Task ConnectAsync(IEnumerable<string> cellNames, CancellationToken ct = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(_wsUri, _cts.Token);
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

            // 1. hello (optional, old firmware may reject — non-fatal)
            try { await RequestAsync("post", "@/hello", new { name = "CameraViewerDotnet", model = "CameraViewerDotnet" }, _cts.Token); } catch { }

            // 2. detect root
            _root = await DetectRootAsync(_cts.Token);

            // 3. openSession
            var body = new Dictionary<string, object>
            {
                ["$type"] = "HmiSessionInfo",
                ["cellNames"] = (cellNames?.ToList() ?? new List<string>()),
            };
            var sidResp = await RequestAsync("post", $"{_root}/openSession", body, _cts.Token);
            _sessionId = sidResp.ValueKind == JsonValueKind.String ? sidResp.GetString() : sidResp.ToString();

            // 4. login (non-fatal: some cameras allow anonymous read-only)
            try { await RequestAsync("post", $"{_sessionId}/login", new object[] { _username, _password, false }, _cts.Token); } catch { }

            // 5. keepAlive loop
            _keepAliveLoop = Task.Run(() => KeepAliveLoopAsync(_cts.Token), _cts.Token);

            // 6. listen resultChanged
            await ListenAsync($"{_sessionId}/resultChanged", OnResultChanged, _cts.Token);

            // 7. ready → camera will push first frame
            try { await RequestAsync("post", $"{_sessionId}/ready", "", _cts.Token); } catch { }
        }

        public async Task CloseAsync()
        {
            try { if (_sessionId != null) await RequestAsync("post", $"{_sessionId}/dispose", null, CancellationToken.None); } catch { }
            try { _cts?.Cancel(); } catch { }
            try { if (_ws != null && _ws.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            foreach (var tcs in _pending.Values) { try { tcs.TrySetCanceled(); } catch { } }
            _pending.Clear();
            _listeners.Clear();
            _sessionId = null;
        }

        public Bitmap TakeLatestCell(string cellName)
        {
            _cellFrames.TryGetValue(cellName, out var bmp);
            return bmp;
        }

        public void Dispose()
        {
            CloseAsync().Wait(TimeSpan.FromSeconds(3));
            _ws?.Dispose();
            _http?.Dispose();
            foreach (var kv in _cellFrames) kv.Value?.Dispose();
            _cellFrames.Clear();
            LatestMainFrame?.Dispose();
        }

        // ==================== Request infrastructure ====================

        private async Task<JsonElement> RequestAsync(string type, string path, object body, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonElement>();
            _pending[id] = tcs;

            // Build message
            var msg = new Dictionary<string, object> { ["$type"] = type, ["id"] = id, ["path"] = path };
            if (body != null) msg["body"] = body;
            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

            try
            {
                var resp = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                return resp;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                throw new CogSocketException(-1, $"Request timeout: {path}");
            }
        }

        private async Task ListenAsync(string path, Action<JsonElement> handler, CancellationToken ct)
        {
            await RequestAsync("listen", path, null, ct);
            _listeners[path] = handler;
        }

        private async Task KeepAliveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _sessionId != null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    try { await RequestAsync("post", $"{_sessionId}/keepAlive", "", ct); } catch { }
                }
            }
            catch { }
        }

        private async Task<string> DetectRootAsync(CancellationToken ct)
        {
            try { await RequestAsync("get", "cam0/hmi/info", null, ct); return "cam0/hmi"; } catch { }
            try { await RequestAsync("get", "system/info", null, ct); return "system"; } catch { }
            return "cam0/hmi"; // default
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[131072];
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

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var type = root.TryGetProperty("$type", out var t) ? t.GetString() : null;

                        if (type == "resp")
                        {
                            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : -1;
                            var body = root.TryGetProperty("body", out var b) ? b : default;
                            if (_pending.TryRemove(id, out var tcs))
                            {
                                if (root.TryGetProperty("error", out var errEl))
                                {
                                    var code = errEl.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
                                    var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : errEl.ToString();
                                    tcs.TrySetException(new CogSocketException(code, msg));
                                }
                                else
                                {
                                    tcs.TrySetResult(body.ValueKind == JsonValueKind.Undefined ? default : body.Clone());
                                }
                            }
                        }
                        else if (type == "event")
                        {
                            var path = root.TryGetProperty("path", out var p) ? p.GetString() : "";
                            var body = root.TryGetProperty("body", out var b) ? b : default;
                            if (_listeners.TryGetValue(path, out var cb))
                            {
                                try { cb(body.Clone()); } catch { }
                            }
                        }
                    }
                    catch { /* ignore malformed */ }
                }
            }
            catch { }
            finally
            {
                foreach (var tcs in _pending.Values) { try { tcs.TrySetCanceled(); } catch { } }
                _pending.Clear();
            }
        }

        // ==================== HmiResult → Bitmap ====================

        private void OnResultChanged(JsonElement hmiResult)
        {
            // Fire-and-forget: HTTP download + image decode on thread pool
            _ = Task.Run(async () =>
            {
                try
                {
                    // Parse cells
                    if (hmiResult.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cell in cells.EnumerateArray())
                        {
                            var loc = cell.TryGetProperty("location", out var l) ? l.GetString()
                                    : cell.TryGetProperty("name", out var n) ? n.GetString() : null;
                            if (string.IsNullOrEmpty(loc)) continue;

                            var bmp = await DecodeCellImageAsync(cell);
                            if (bmp != null)
                            {
                                if (_cellFrames.TryRemove(loc, out var old)) old?.Dispose();
                                _cellFrames.TryAdd(loc, bmp);
                                CellResultUpdated?.Invoke(loc);
                            }
                        }
                    }

                    // Main acquisition image
                    var mainBmp = await DecodeMainImageAsync(hmiResult);
                    if (mainBmp != null)
                    {
                        LatestMainFrame?.Dispose();
                        LatestMainFrame = mainBmp;
                        MainFrameUpdated?.Invoke();
                    }
                }
                catch { }
                finally
                {
                    // MUST send ready so camera pushes next frame (Python worker.py L138 comment)
                    try { await RequestAsync("post", $"{_sessionId}/ready", "", CancellationToken.None); } catch { }
                }
            });
        }

        private async Task<Bitmap> DecodeCellImageAsync(JsonElement cell)
        {
            var data = cell.TryGetProperty("data", out var d) ? d : default;
            string url = null;
            if (data.ValueKind == JsonValueKind.String)
                url = data.GetString();
            else if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("url", out var u)) url = u.GetString();
                else if (data.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String) url = v.GetString();
            }
            if (string.IsNullOrEmpty(url)) return null;
            return await FetchImageAsync(url);
        }

        private async Task<Bitmap> DecodeMainImageAsync(JsonElement hmiResult)
        {
            if (!hmiResult.TryGetProperty("acqImageView", out var acq)) return null;
            if (!acq.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array) return null;
            foreach (var layer in layers.EnumerateArray())
            {
                var type = layer.TryGetProperty("$type", out var t) ? t.GetString() : null;
                if (type != "ImageLayer") continue;
                var url = layer.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrEmpty(url))
                {
                    // Get image size for ?sz= scaling
                    int? w = null, h = null;
                    if (layer.TryGetProperty("image", out var imgObj) && imgObj.ValueKind == JsonValueKind.Object)
                    {
                        if (imgObj.TryGetProperty("width", out var wEl)) w = wEl.GetInt32();
                        if (imgObj.TryGetProperty("height", out var hEl)) h = hEl.GetInt32();
                    }
                    if (w.HasValue && w.Value > 640 && h.HasValue)
                        url = $"{url}{(url.Contains('?') ? '&' : '?')}sz=640,{(int)(h.Value * 640.0 / w.Value)}";
                    return await FetchImageAsync(url);
                }
            }
            return null;
        }

        private async Task<Bitmap> FetchImageAsync(string url)
        {
            try
            {
                byte[] imgBytes;
                if (url.StartsWith("data:"))
                {
                    var idx = url.IndexOf(',');
                    if (idx < 0) return null;
                    var b64 = url.Substring(idx + 1);
                    imgBytes = Convert.FromBase64String(b64);
                }
                else
                {
                    string fullUrl = url;
                    if (!url.StartsWith("http"))
                        fullUrl = $"http://{_host}:{_port}{url}";
                    var resp = await _http.GetAsync(fullUrl);
                    resp.EnsureSuccessStatusCode();
                    imgBytes = await resp.Content.ReadAsByteArrayAsync();
                    if (imgBytes.Length == 0) return null;
                }
                using var ms = new MemoryStream(imgBytes);
                return new Bitmap(ms);
            }
            catch { return null; }
        }
    }

    public class CogSocketException : Exception
    {
        public int Code { get; }
        public CogSocketException(int code, string message) : base($"[{code}] {message}") => Code = code;
    }
}
