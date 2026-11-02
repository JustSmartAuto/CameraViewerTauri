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

        /// <summary>拍照标识单元格地址（如 A21），其取值（11/21/31/41…）标识第几次拍照。</summary>
        private readonly string _identifierCell;

        /// <summary>一次拍照完成：标识单元格取值 + 本次拍照原图。回调在后台线程，位图由接收方接管释放。</summary>
        public event Action<string, Bitmap> SubGridResult;

        public string IpPort { get; }
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public CogSocketCamera(string hostPort, string identifierCell = "", string username = "admin", string password = "")
        {
            var hp = (hostPort ?? "").Trim();
            if (!hp.Contains(':')) hp += ":8087";
            IpPort = hp;
            var parts = hp.Split(':');
            _host = parts[0];
            _port = int.TryParse(parts[1], out var p) ? p : 8087;
            _identifierCell = (identifierCell ?? "").Trim();
            _username = username;
            _password = password;
            _wsUri = new Uri($"ws://{_host}:{_port}/ws");
            // 原图可能十几 MB，下载超时放宽
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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

        public void Dispose()
        {
            CloseAsync().Wait(TimeSpan.FromSeconds(3));
            _ws?.Dispose();
            _http?.Dispose();
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
                Bitmap bmp = null;
                try
                {
                    // 1. 从 cells 中取拍照标识单元格的取值（如 A21 = 11/21/31/41）
                    string shotValue = null;
                    string cellImageUrl = null;
                    if (hmiResult.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cell in cells.EnumerateArray())
                        {
                            var loc = cell.TryGetProperty("location", out var l) ? l.GetString()
                                    : cell.TryGetProperty("name", out var n) ? n.GetString() : null;
                            if (string.IsNullOrEmpty(loc) || !SameCell(loc, _identifierCell)) continue;
                            shotValue = ExtractCellValue(cell);
                            cellImageUrl = ExtractCellImageUrl(cell);
                        }
                    }

                    // 2. 取原图（不缩放）：标识单元格自带图像地址时优先，否则用主采集视图
                    if (!string.IsNullOrEmpty(cellImageUrl))
                        bmp = await FetchImageAsync(cellImageUrl);
                    if (bmp == null)
                        bmp = await DecodeMainImageAsync(hmiResult);
                    if (bmp == null || string.IsNullOrEmpty(shotValue))
                    {
                        bmp?.Dispose();
                        return;
                    }

                    // 3. 交给界面按其绑定槽位显示
                    SubGridResult?.Invoke(shotValue, bmp);
                    bmp = null;
                }
                catch { bmp?.Dispose(); }
                finally
                {
                    // MUST send ready so camera pushes next frame (Python worker.py L138 comment)
                    try { await RequestAsync("post", $"{_sessionId}/ready", "", CancellationToken.None); } catch { }
                }
            });
        }

        /// <summary>单元格地址比较：忽略大小写、$ 包裹与空白（A21 / $A$21）。</summary>
        private static bool SameCell(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            static string Norm(string s) => s.Replace("$", "").Replace(" ", "").Trim().ToUpperInvariant();
            return Norm(a) == Norm(b);
        }

        /// <summary>HmiResult.cells[] 取值：优先 value，其次 data。</summary>
        private static string ExtractCellValue(JsonElement cell)
        {
            foreach (var key in new[] { "value", "data" })
            {
                if (!cell.TryGetProperty(key, out var v)) continue;
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out var ov))
                {
                    if (ov.ValueKind == JsonValueKind.Number) return ov.GetRawText();
                    if (ov.ValueKind == JsonValueKind.String) return ov.GetString();
                }
            }
            return null;
        }

        /// <summary>标识单元格若本身是图像单元格，其 data 为图像 URL。</summary>
        private static string ExtractCellImageUrl(JsonElement cell)
        {
            if (!cell.TryGetProperty("data", out var d)) return null;
            string url = null;
            if (d.ValueKind == JsonValueKind.String) url = d.GetString();
            else if (d.ValueKind == JsonValueKind.Object)
            {
                if (d.TryGetProperty("url", out var u)) url = u.GetString();
                else if (d.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String) url = v.GetString();
            }
            return url != null && (url.StartsWith("/") || url.StartsWith("http") || url.StartsWith("data:")) ? url : null;
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
                    return await FetchImageAsync(url);   // 原图，不做 ?sz= 缩放
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
