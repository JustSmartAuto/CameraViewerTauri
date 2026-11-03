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
        private int _port;
        private readonly bool _portExplicit;
        private readonly string _username;
        private readonly string _password;
        private readonly HttpClient _http;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _readLoop;
        private Task _keepAliveLoop;

        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly ConcurrentDictionary<string, Action<JsonElement>> _listeners = new();

        private string _root = "cam0/hmi";
        private string _sessionId;

        /// <summary>需要关注的拍照标识单元格集合（如 A21），其取值（11/21/31/41…）标识第几次拍照。</summary>
        private readonly HashSet<string> _identifierCells;

        /// <summary>每个标识单元格最近一次已显示的图像地址，用于丢弃“同一张图 + 旧取值”的滞后事件。</summary>
        private readonly ConcurrentDictionary<string, string> _lastImageByCell = new();

        /// <summary>一次拍照完成：标识单元格名 + 取值 + 图像 + 是否原图。先发 640 缩略图，原图取到后再补发一次。</summary>
        public event Action<string, string, Bitmap, bool, string, int, int> CellResult;

        public string IpPort { get; }
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public CogSocketCamera(string hostPort, IEnumerable<string> identifierCells, string username = "admin", string password = "")
        {
            var raw = (hostPort ?? "").Trim();
            _portExplicit = raw.Contains(':');
            var hp = NormalizeHostPort(raw);
            IpPort = hp;
            var parts = hp.Split(':');
            _host = parts[0];
            _port = int.TryParse(parts[1], out var p) ? p : 80;
            _identifierCells = new HashSet<string>(
                (identifierCells ?? Enumerable.Empty<string>())
                    .Select(NormalizeCell)
                    .Where(c => c.Length > 0));
            _username = username;
            _password = password;
            // 原图可能十几 MB，下载超时放宽
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.Add("User-Agent", "CameraViewerDotnet");
        }

        /// <summary>hostPort 归一化：未写端口时默认 80（固件 22.2+ 固定 80；更早固件用 8087，连接时自动回退）。</summary>
        public static string NormalizeHostPort(string hostPort)
        {
            var hp = (hostPort ?? "").Trim();
            if (!hp.Contains(':')) hp += ":80";
            return hp;
        }

        /// <summary>优先用配置端口；连接失败时在 80（固件 22.2+）与 8087（早期固件）之间回退。</summary>
        private IEnumerable<int> PortsToTry()
        {
            yield return _port;
            if (_port == 80) yield return 8087;
            else if (_port == 8087) yield return 80;
        }

        /// <summary>openSession 的 cellNames 需为范围形式：A21 → A21:A21（否则固件返回空 cells）。</summary>
        private static string ToCellRange(string cell)
        {
            var v = (cell ?? "").Trim();
            if (v.Length == 0 || v.Contains(':')) return v;
            var bare = v.Replace("$", "");
            return System.Text.RegularExpressions.Regex.IsMatch(bare, "^[A-Za-z]+[0-9]+$")
                ? bare + ":" + bare
                : v;   // 单元格名保持原样
        }

        /// <summary>单元格地址归一化：忽略大小写、$ 包裹与空白（A21 / $A$21）。</summary>
        public static string NormalizeCell(string cell) =>
            (cell ?? "").Replace("$", "").Replace(" ", "").Trim().ToUpperInvariant();

        private readonly string _host;
        private int _nextId;

        // ==================== Public ====================

        public async Task ConnectAsync(IEnumerable<string> cellNames, CancellationToken ct = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Exception lastError = null;
            foreach (var port in PortsToTry())
            {
                _port = port;
                _ws = new ClientWebSocket();
                try
                {
                    await _ws.ConnectAsync(new Uri($"ws://{_host}:{port}/ws"), _cts.Token);
                    lastError = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try { _ws.Dispose(); } catch { }
                    _ws = null;
                }
            }
            if (_ws == null) throw lastError ?? new CogSocketException(-1, $"无法连接 {_host}");
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

            // 1. hello (optional, old firmware may reject — non-fatal)
            try { await RequestAsync("post", "@/hello", new { name = "CameraViewerDotnet", model = "CameraViewerDotnet" }, _cts.Token); } catch { }

            // 2. detect root
            _root = await DetectRootAsync(_cts.Token);

            // 3. openSession（固件只识别范围形式，单地址需转成 A21:A21）
            var body = new Dictionary<string, object>
            {
                ["$type"] = "HmiSessionInfo",
                ["cellNames"] = (cellNames ?? Enumerable.Empty<string>())
                    .Select(ToCellRange).Where(s => s.Length > 0).ToList(),
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
            // 1. 找出本次结果中所有已订阅的拍照标识单元格及其取值（一台相机可被多个 Cell 共享）
            var matches = new List<(string Cell, string Value, string ImageUrl)>();
            if (hmiResult.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array)
            {
                foreach (var cell in cells.EnumerateArray())
                {
                    var loc = cell.TryGetProperty("location", out var l) ? l.GetString()
                            : cell.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var norm = NormalizeCell(loc);
                    if (norm.Length == 0 || !_identifierCells.Contains(norm)) continue;
                    var value = ExtractCellValue(cell);
                    if (string.IsNullOrEmpty(value)) continue;
                    matches.Add((norm, value.Trim(), ExtractCellImageUrl(cell)));
                }
            }
            if (matches.Count == 0)
            {
                _ = SendReadyAsync();
                return;
            }

            var main = GetMainImage(hmiResult);
            // 叠加图使用主视图的 SVGLayer（坐标即图像像素坐标），文本很小，随本次拍照一起取回
            var svgUrl = GetMainSvg(hmiResult);

            // 2. Fire-and-forget：先取 640 缩略图尽快落图，原图在后台补取（失败则保留缩略图）
            _ = Task.Run(async () =>
            {
                var svg = await FetchSvgAsync(svgUrl);
                var originals = new List<(string Cell, string Value, string Url)>();
                try
                {
                    foreach (var m in matches)
                    {
                        var url = !string.IsNullOrEmpty(m.ImageUrl) ? m.ImageUrl : main.Url;
                        if (string.IsNullOrEmpty(url)) continue;

                        // 相机一次拍照会连发两个事件：先“新图+旧取值”，后“旧图+新取值”。
                        // 同一标识单元格重复收到同一张图时直接丢弃，避免把上一次拍照的图填进新槽位。
                        var last = _lastImageByCell.TryGetValue(m.Cell, out var prev) ? prev : null;
                        if (url != last) _lastImageByCell[m.Cell] = url;
                        else if (!url.StartsWith("data:")) continue;

                        Bitmap bmp = null;
                        if (!url.StartsWith("data:"))
                            bmp = await FetchImageAsync(ThumbnailUrl(url, main.Width, main.Height));
                        if (bmp == null) bmp = await FetchImageAsync(url);   // 缩略图失败时退回原图

                        if (bmp != null) Dispatch(m.Cell, m.Value, bmp, false, svg, main.Width, main.Height);
                        if (!url.StartsWith("data:")) originals.Add((m.Cell, m.Value, url));
                    }
                }
                catch { }
                finally
                {
                    // 发完缩略图立即 ready，让相机尽快推送下一帧；原图下载不阻塞取图
                    await SendReadyAsync();
                }

                foreach (var o in originals)
                {
                    Bitmap bmp = null;
                    try { bmp = await FetchImageAsync(o.Url); } catch { }
                    if (bmp != null) Dispatch(o.Cell, o.Value, bmp, true, svg, main.Width, main.Height);
                }
            });
        }

        private async Task SendReadyAsync()
        {
            // MUST send ready so camera pushes next frame (Python worker.py L138 comment)
            try { await RequestAsync("post", $"{_sessionId}/ready", "", CancellationToken.None); } catch { }
        }

        /// <summary>交付图像给订阅者；位图所有权随回调转移，isOriginal 表示这是后台补取的原图。</summary>
        private void Dispatch(string cell, string value, Bitmap bmp, bool isOriginal, string svg, int srcWidth, int srcHeight)
        {
            var handler = CellResult;
            if (handler == null) { bmp.Dispose(); return; }
            try { handler(cell, value, bmp, isOriginal, svg, srcWidth, srcHeight); }
            catch { bmp.Dispose(); }
        }

        /// <summary>640 宽缩略图地址（服务器按 BMP 返回；不指定高度时由服务器保持宽高比）。</summary>
        private static string ThumbnailUrl(string url, int width, int height)
        {
            if (url.Contains("sz=")) return url;
            var sep = url.Contains('?') ? "&" : "?";
            if (width > 0 && height > 0)
            {
                var th = Math.Max(1, (int)Math.Round(640.0 * height / width));
                return $"{url}{sep}sz=640,{th}";
            }
            return $"{url}{sep}sz=640,h";
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

        /// <summary>取主采集视图的 ImageLayer 地址与原始尺寸（用于生成等比缩略图参数）。</summary>
        private static (string Url, int Width, int Height) GetMainImage(JsonElement hmiResult)
        {
            if (!hmiResult.TryGetProperty("acqImageView", out var acq)) return (null, 0, 0);
            if (!acq.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array) return (null, 0, 0);
            foreach (var layer in layers.EnumerateArray())
            {
                var type = layer.TryGetProperty("$type", out var t) ? t.GetString() : null;
                if (type != "ImageLayer") continue;
                var url = layer.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(url)) continue;
                var w = layer.TryGetProperty("width", out var wEl) && wEl.ValueKind == JsonValueKind.Number ? wEl.GetInt32() : 0;
                var h = layer.TryGetProperty("height", out var hEl) && hEl.ValueKind == JsonValueKind.Number ? hEl.GetInt32() : 0;
                return (url, w, h);
            }
            return (null, 0, 0);
        }

        /// <summary>取主采集视图的 SVGLayer 地址；其坐标系与采集图像像素坐标一致。</summary>
        private static string GetMainSvg(JsonElement hmiResult)
        {
            if (!hmiResult.TryGetProperty("acqImageView", out var acq)) return null;
            if (!acq.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array) return null;
            foreach (var layer in layers.EnumerateArray())
            {
                var type = layer.TryGetProperty("$type", out var t) ? t.GetString() : null;
                if (type != "SVGLayer") continue;
                var url = layer.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return null;
        }

        /// <summary>获取叠加图形 SVG 文本；失败返回空串（叠加图保留上一次或只显示产品图）。</summary>
        private async Task<string> FetchSvgAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try
            {
                if (!url.StartsWith("http")) url = $"http://{_host}:{_port}{url}";
                var resp = await _http.GetAsync(url);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
            catch { return ""; }
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
