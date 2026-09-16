# CogSocket & Web API 参考文档

> 基于 Cognex In-Sight Web SDK 26.1.0 的示例代码与官方 PDF 文档整理，供 CameraViewerTauri 集成 CogSocket/HMI Web API 使用。
> 关联 TODO.md：#2（取 $A$0 单元格图片）、#6（Cognex HMI 中文化 & 语言跟随）、#7（单元格值设置）、#21（CogSocket 通信）。
>
> 信息来源：
> - `In-Sight HMI Developers Guide.pdf`（26.1.0）
> - `In-Sight HMI API.pdf`（API v3.0，26.1.0）
> - `In-Sight Platform API.pdf`（API v3.0，26.1.0）
> - SDK 示例代码 `SampleCode/javascript/`、`SampleCode/dotnet/`

---

## 一、概述

Cognex In-Sight 相机对外提供三套 Web 通信能力：

| 能力 | 传输层 | 用途 |
|------|--------|------|
| **CogSocket** | WebSocket（`ws://host:port/ws`） | JSON-RPC 风格双向消息，GET/PUT/POST/DELETE + 事件订阅，是 HMI 会话与单元格读写的底层通道 |
| **HMI Web API** | CogSocket + HTTP | 基于 CogSocket 的 HMI 资源树（`cam0/hmi/...`），提供会话、结果、单元格、作业、设置等 |
| **Platform API** | HTTP（REST） | 设备级管理：审计日志、证书、固件升级、备份恢复 |

在 Tauri 项目中，**前端直接使用 `cogsocket.js` 建立 WebSocket**（无需后端 Rust 转发），图片通过 HTTP URL 下载。

---

## 二、相机配置与连接

### 2.1 固件版本差异

| 固件 | HMI Web Server 配置 | 默认端口 | 最大连接数 |
|------|---------------------|----------|-----------|
| **6.x+** | ISE → Sensor Menu → HMI Settings，HMI Mode = "HTTP" | 8087（可配） | Maximum View Connections 可配 |
| **22.2+** | ISVS → Utilities → HMI Settings | **固定 80** | 固定 5（不可调） |

### 2.2 Web HMI 访问 URL

```
http://{address}:{port}?{query option 1}&{query option 2}…
```

浏览器要求：Chrome/Chromium 80+，支持 HTML5、CSS、WebSocket、JavaScript、SVG。

### 2.3 Web HMI URL 查询参数

**视图选择**：

| 参数 | 说明 |
|------|------|
| `view=ImageOnly` | 仅显示图像 |
| `view=ImageWithGraphics` | 图像 + 图形叠加 |
| `view=PointCloudWithGraphics` | 点云 + 图形（仅 3D 设备） |
| `view=EasyViewWithImage` | EasyView + 图像 + 图形（默认） |
| `view=EasyViewWithoutImage` | 仅 EasyView |
| `view=CustomView` | 自定义视图 |
| `view=default` | 默认页面（非上次视图，22.2.1+） |
| `customViewName=[name]` | 指定自定义视图名称 |

**基础页面**（精简模式，无 HMI 控件）：

| 参数 | 说明 |
|------|------|
| `page=Image` | 仅图像 |
| `page=ImageAndGraphics` | 图像 + 图形 |
| `page=PointCloud` | 点云 + 图形 |

**图像控制**：

| 参数 | 说明 |
|------|------|
| `fit=true` | 适配显示（保持宽高比，可能留灰边） |
| `fill=true` | 填充整个显示区 |
| `zoom=[level]` | 缩放：0.1~32（0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8,0.9,1,1.25,1.5,1.75,2,2.25,2.5,2.75,3,3.5,4,4.5,5,5.5,6,8,16,32） |
| `rotate=[0\|90\|180\|270]` | 顺时针旋转 |
| `pan=[x,y]` | 图像偏移 |
| `viewControls=true` | 显示交互式图像工具栏 |
| `fillScreen=true` | 填充浏览器窗口（22.2.1+） |

**认证与其他**：

| 参数 | 说明 |
|------|------|
| `user=[name]` | 指定用户名（避免登录弹窗，可 base64 编码） |
| `password=[pwd]` | 指定密码（不安全，仅适用于可信环境） |
| `hideCookieWarning=true` | 隐藏 cookie 警告 |

> **TODO #6 语言跟随**：Cognex HMI 页面语言由相机端设置决定。可在 iframe URL 中附加 `user`/`password` 避免登录弹窗，但语言切换需通过 HMI 设置或相机端配置实现，需真机测试确认具体字段。

### 2.4 CogSocket 连接地址

```
ws://<相机IP>:<端口>/ws
```

根路径（root path）用于定位 HMI 资源，API v3.0+ 为 `cam0/hmi`（旧版为 `system`）。

---

## 三、CogSocket 协议

### 3.1 请求类型

| 类型 | 含义 |
|------|------|
| `get` | 读取属性值 |
| `put` | 写入属性值 |
| `post` | 调用方法 |
| `delete` | 删除资源 |
| `listen` | 订阅事件 |
| `unlisten` | 取消订阅（`*` 表示全部） |
| `event` | 事件通知（服务端→客户端） |

### 3.2 消息结构

| 字段 | 说明 |
|------|------|
| `$type` | 消息类型（小写请求名 / `resp`） |
| `id` | 32 位请求 ID，自增。**0 表示不需要响应**（事件常用） |
| `path` | 资源路径（仅请求有），如 `cam0/hmi/info`，不以 `/` 开头 |
| `error` | 仅错误响应有，负整数错误码，`-1` = 通用错误 |
| `headers` | 可选，应用扩展头（协议未定义具体类型） |
| `body` | 请求体或响应体，任意 JSON 值 |

**请求示例**：
```json
{ "$type": "get", "id": 3, "path": "cam0/hmi/job/name" }
```

**成功响应**：
```json
{ "$type": "resp", "id": 3, "body": "myjob.jobx" }
```

**错误响应**：
```json
{ "$type": "resp", "id": 15, "error": -1610612728, "body": "Access denied: No more HMI connections are available." }
```

**事件消息**（id 可省略，无响应）：
```json
{ "$type": "event", "path": "cam0/hmi/jobLoadingChanged", "body": true }
```

### 3.3 编码

- **JSON 编码**：WebSocket `text` opcode。若接收方不支持，回复 `{"$type": "err_encoding"}`。
- **二进制编码**：WebSocket `binary` opcode，性能更好但实现复杂。
- 单条消息可为任一编码，响应通常与请求编码一致。

### 3.4 连接级消息（`@/` 前缀）

路径以 `@/` 开头表示连接级操作。握手 `@/hello`：

```json
{ "$type": "post", "id": 1, "path": "@/hello", "body": { "name": "Browser", "model": "Browser" } }
```

### 3.5 错误码

| 错误码 | 含义 |
|--------|------|
| `-1` | 通用失败 |
| `-1610612728` | 访问拒绝（如无可用 HMI 连接、登录失败） |

---

## 四、CogSocket JS SDK（`cogsocket.js`）

项目中已有副本：`src/assets/cogsocket/cogsocket.js`，同时支持 RequireJS 与 NodeJS 风格加载。

### 4.1 构造

```javascript
var cogsock = new CogSocket(websocket, root, space);
```

| 参数 | 说明 |
|------|------|
| `websocket` | 已连接到相机 `/ws` 的 `WebSocket` 实例 |
| `root` | 根对象（客户端传 `null`） |
| `space` | JSON 缩进（数字或字符串），调试用 |

**调试日志**：
```javascript
cogsock.log = function(msg) { console.log(msg); };
```

### 4.2 方法

#### `get(path, oncomplete)`
读取属性。`oncomplete(value | Error)`。

#### `put(path, data, oncomplete)`
写入属性。`oncomplete` 无参数（成功）或 `Error`。`oncomplete=null` 不请求响应。

#### `post(path, data, oncomplete)`
调用方法。`data` 为数组=多参数，否则=单参数。`oncomplete(returnValue | Error)`。

#### `addListener(path, listener, oncomplete)`
订阅事件。`listener` 接收事件参数。

#### `removeListener(path, listener, oncomplete)`
取消订阅。`listener=null` 移除全部。

#### `close()`
关闭 WebSocket。

### 4.3 生命周期回调

```javascript
cogsock.onopen  = function() { ... };
cogsock.onclose = function() { ... };
cogsock.onerror = function() { ... };
```

---

## 五、HMI 资源树（`cam0/hmi`）

API v3.0+ 根路径为 `cam0/hmi`（旧版为 `system`）。

### 5.1 根属性（GET）

| 路径 | 说明 |
|------|------|
| `cam0/hmi/availableSessions` | 可用 HMI 会话数 |
| `cam0/hmi/discreteOnline` | 离散 I/O 在线标志 |
| `cam0/hmi/editorAttached` | 是否有编辑器（ISE/ISVS/VisionView）连接，true 时 HMI 应为只读 |
| `cam0/hmi/ffpOnline` | FFP 在线标志（v3.0+） |
| `cam0/hmi/info` | CameraInfo 对象 |
| `cam0/hmi/isSessionAvailable` | 是否可开新会话 |
| `cam0/hmi/job` | 作业资源（见 5.4） |
| `cam0/hmi/jobLoading` | 作业是否加载中 |
| `cam0/hmi/keepAliveTimeout` | 保活超时（秒），默认 30，最小 3，最大 30000 |
| `cam0/hmi/liveMode` | 实时模式标志 |
| `cam0/hmi/nativeOnline` | 原生模式在线标志 |
| `cam0/hmi/online` | 在线/离线状态 |
| `cam0/hmi/settings` | 设置资源（见 5.5） |
| `cam0/hmi/softOnline` | 软在线标志（HMI 控制在线/离线） |
| `cam0/hmi/state` | State 对象（含全部在线标志） |

### 5.2 根方法（POST）

| 路径 | 说明 |
|------|------|
| `cam0/hmi/openSession` | 打开 HMI 会话，返回 session ID（`hs/~xxxxxx`） |

### 5.3 根事件（listen）

| 事件 | 负载 | 说明 |
|------|------|------|
| `stateChanged` | `[online, softOnline, nativeOnline, discreteOnline, ffpOnline]` | 状态变化 |
| `editorAttachedChanged` | `[bool]` | 编辑器连接变化 |
| `jobLoadingChanged` | `[bool]` | 作业加载中变化 |
| `jobChanged` | 无 | 作业值变化 |
| `jobLoadFailed` | HmiError | 作业加载失败 |
| `jobValidationDone` | — | 作业验证完成（25.1.0+） |
| `liveModeChanged` | `[bool]` | 实时模式变化 |
| `sessionDisposed` | sessionID | 会话因超时/断开被释放 |
| `settingsChanged` | 无 | HMI 设置变化（24.4.0+） |

### 5.4 作业资源（`cam0/hmi/job`）

**属性**：

| 路径 | 说明 |
|------|------|
| `cam0/hmi/job/name` | 当前作业名 |
| `cam0/hmi/job/easyView` | EasyViewSettings（items + names） |
| `cam0/hmi/job/pages` | HmiPages（可用页面列表） |
| `cam0/hmi/job/customViewSettingsList` | HmiCustomViewSettings 数组 |
| `cam0/hmi/job/jobImageOrientation` | HmiImageOrientation（旋转/翻转） |

**方法**：

| 路径 | 说明 |
|------|------|
| `cam0/hmi/job/getSheetFormat` | 获取电子表格格式（列宽/行高/单元格格式） |
| `cam0/hmi/job/getCustomViewFormat` | 获取自定义视图格式 |

### 5.5 设置资源（`cam0/hmi/settings`）

**属性**：

| 路径 | 说明 |
|------|------|
| `cam0/hmi/settings/skipLogin` | 是否可用默认凭据（admin/空密码）登录 |
| `cam0/hmi/settings/hmi` | HmiSettings（可读写） |
| `cam0/hmi/settings/userAccessList` | 用户权限列表 |

**事件**：`settingsChanged`（24.4.0+）

---

## 六、HMI 会话流程

```
connect → openSession → login → addListener(resultChanged) → ready
                                                    ↓
                                            (resultChanged 事件)
                                                    ↓
                                              ready（确认接收）
                                                    ↓
                                          keepAlive（每 20s 保活）
                                                    ↓
                                              dispose（关闭）
```

### 6.1 openSession

```javascript
var sessionInfo = {
  "$type": "HmiSessionInfo",
  "cellNames": ["A0:Z599"]
};
cogsock.post("cam0/hmi/openSession", sessionInfo, function(sessionId) {
  // sessionId = "hs/~1234567890"
});
```

**HmiSessionInfo**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `cellNames` | string[] | 要包含的单元格名/范围，如 `["A0:Z599"]`（v2.4+ 支持范围） |
| `sheetName` | string | 工作表名（v3.0+，多表设备） |
| `enableQueuedResults` | bool | 队列冻结时是否返回排队结果 |
| `includeCustomView` | bool | 是否包含自定义视图单元格 |
| `autoReady` | bool | 无需发 ready 自动接收下一帧（慎用，建议仅归档用）（23.1.0+） |
| `requestResult` | bool | 仅请求结果，resultChanged 负载为空（v2.3+） |
| `ignoreEditorAttached` | bool | 编辑器连接时仍允许 API 调用（v3.0+） |

### 6.2 login

```javascript
cogsock.post(sessionId + "/login", ["admin", "password", false]);
```

参数：`[用户名, 密码, 是否编码?]`。用户名/密码应为 64-bit ASCII 编码，第三参数 `false` 表示不编码。
可选第四参数 `true` 返回 UserAccessInfo 而非访问级别字符串。

返回值：访问级别字符串（`full`/`protected`/`locked`）或 UserAccessInfo。

### 6.3 resultChanged

```javascript
cogsock.addListener(sessionId + "/resultChanged", function(hmiResult) {
  // 处理结果
  sendReady(); // 必须调用以接收下一帧
});
```

### 6.4 ready

```javascript
cogsock.post(sessionId + "/ready", "");
```

### 6.5 keepAlive

```javascript
cogsock.post(sessionId + "/keepAlive", "");
```

默认超时 30 秒，建议每 10~20 秒调用一次。

### 6.6 dispose

```javascript
cogsock.post(sessionId + "/dispose", null);
```

---

## 七、HmiResult 结构

`resultChanged` 事件负载或 `GET {SID}/result` 返回。

| 字段 | 说明 |
|------|------|
| `$type` | `"HmiResult"` |
| `id` | 结果唯一 ID（递增） |
| `acqImageView` | 主采集 ViewRecord（图像+图形） |
| `acqPointCloudView` | 点云 ViewRecord（3D 设备） |
| `cells` | 单元格结果数组 |
| `jobStatus` | 作业状态：0=None, 1=Pass, 2=Fail, 3=Warn |
| `cellTagVer` | 单元格结果版本 |
| `jobTagVer` | 作业标签版本 |
| `logicVer` | 单元格逻辑版本 |
| `queuedResult` | 是否来自队列 |
| `rq` | 结果队列状态（HmiRqState），未启用时 null |
| `views` | 所有视图字典（key=视图名，value=View） |

### 7.1 ViewRecord

| 字段 | 说明 |
|------|------|
| `$type` | `"ViewRecord"` |
| `id` | 视图 ID |
| `url` | 视图 URL |
| `layers` | 图层数组（ImageLayer + GraphicsLayer，按序渲染） |
| `source` | 视图来源（如 "Inspection"） |
| `viewport` | ViewPort（height, width） |
| `bounds` | 编辑边界（仅 beginEdit 时有） |

### 7.2 ImageLayer

| 字段 | 说明 |
|------|------|
| `$type` | `"ImageLayer"` |
| `url` | 图像 HTTP URL（相对路径，需拼接相机 IP） |
| `width` / `height` | 图像缓冲区尺寸 |
| `image` | Image 对象 |
| `mask` | 有效像素区域（主图通常 null） |
| `transform` | LinearTransform（主图通常 null，处理后图像有） |

### 7.3 Image

| 字段 | 说明 |
|------|------|
| `url` | 图像 URL |
| `width` / `height` | 像素尺寸 |
| `bitsPerPixel` | 位深 |
| `isColor` | 是否彩色 |
| `frozen` | 是否从文件/存储读取 |
| `acquisitionInfo` | 采集信息（duration, timestamp） |
| `orientation` | 方向 |
| `imageFormat` | 图像格式 |

### 7.4 GraphicsLayer / SVGLayer

- `GraphicsLayer.url`：图形 JSON 数组的 HTTP URL
- `SVGLayer.url`：SVG 内容 URL（`contentType` 指定类型）

---

## 八、图像获取

### 8.1 主图像

```javascript
var cameraUrl = "http://127.0.0.1:80";
var imageUrl = hmiResult.acqImageView.layers[0].url; // 相对路径
image.src = cameraUrl + imageUrl;
```

### 8.2 缩放参数 `sz`

```
?sz=dw,dh,sx,sy,sw,sh
```

| 参数 | 说明 |
|------|------|
| `dw` | 目标位图宽度（像素） |
| `dh` | 目标位图高度（像素） |
| `sx` | 源图像采样 X 起点 |
| `sy` | 源图像采样 Y 起点 |
| `sw` | 源图像采样列数 |
| `sh` | 源图像采样行数 |

示例（640×480 半分辨率）：
```
http://127.0.0.1:80/cam0/img/000000000000001?sz=320,240,0,0,640,480
```

### 8.3 图形（Graphics）

通过 GraphicsLayer URL 获取 JSON 数组：
```
http://127.0.0.1/cam0/app/views/001000000000136/layers/2/graphics
```

图形类型包括：Annulus、Arc、BeadPath、BlobChain、Circle、ColorMatch、CompositeRegion、Cross、FilledBox、Fixture、Line、LineList、MaskedRegion、MultiGraphics、Point、Polygon、Polyline、PolylinePath、Rectangle、Region、SubRegion、Text、4Side。

> **TODO #2 取 $A$0 图片**：`$A$0` 通常是 AcquireImage 单元格，其图片在 `acqImageView.layers[0]` 的 `url`。若需特定单元格的图片，检查 `views` 字典中对应视图的 ViewRecord。

---

## 九、单元格结果类型

`HmiResult.cells` 数组中每个单元格的 `$type`：

| `$type` | 类型 | 额外字段 |
|---------|------|---------|
| `HmiFloatResult` | 浮点数 | — |
| `HmiEditFloatResult` | 可编辑浮点 | `min`, `max` |
| `HmiEditIntResult` | 可编辑整数 | `min`, `max` |
| `HmiStringResult` | 字符串 | — |
| `HmiEditStringResult` | 可编辑字符串 | `maxLength`, `maskInput` |
| `HmiButtonResult` | 按钮 | `caption` |
| `HmiCheckBoxResult` | 复选框 | `caption` |
| `HmiListBoxResult` | 列表框 | `options[]` |
| `HmiStatusResult` | 状态 | `caption`, `color` |
| `HmiStatusLightResult` | 状态灯 | `caption`, `color` |
| `HmiMultiStatusResult` | 多状态 | `color0`, `color1`, `numBits`, `startBit`, `reverse` |
| `HmiColorLabelResult` | 颜色标签 | `foreColor`, `backColor` |
| `HmiProfileViewResult` | 轮廓视图 | — |
| `HmiErrorCellResult` | 错误单元格 | — |
| `HmiUnsupportedCellResult` | 不支持 | — |

**通用字段**：`location`（如 "A0"）、`name`、`data`（值）、`disabled`、`error`、`editable`。

**地址解析**：首字母为列（A=0, B=1, ...），后续数字为行号。

### 9.1 queryCellResults

按需查询单元格结果（v2.4+）：
```javascript
cogsock.post(sessionId + "/queryCellResults", [["A2:A7", "BlobCell"]], function(results) {
  // results: HmiCellResult[]
});
```

### 9.2 getAllCellNames

获取所有单元格名映射：
```javascript
cogsock.post(sessionId + "/getAllCellNames", null, function(names) {
  // names: { "A3": "Acquisition.Trigger", "B21": "Blobs_1.Pass", ... }
});
```

---

## 十、设置单元格值（TODO #7）

### 10.1 setCellValue

```javascript
cogsock.post(sessionId + "/setCellValue", ["MyEditInt", 10]);
```

第一个参数为单元格名或地址，第二个为值（可为复杂对象，如 EditRegion 的 Region）。

### 10.2 setCellValues（批量）

```javascript
cogsock.post(sessionId + "/setCellValues", [{
  "MyEditInt": 1,
  "MyEditString": "test"
}]);
```

### 10.3 getCellExpression / setCellExpression

```javascript
// 获取表达式
cogsock.post(sessionId + "/getCellExpression", "MyEditInt", function(expr) {
  // expr = "EditInt(0,255)"
});

// 设置表达式
cogsock.post(sessionId + "/setCellExpression", ["MyEditInt", "EditInt(0,255)"]);
```

权限：需 `IS.EDITJOB`。

### 10.4 EasyView 名称

```javascript
cogsock.get("cam0/hmi/job/easyView/names", function(names) {
  // names: ["Job.Pass", "MyEditInt"]
});
```

---

## 十一、相机信息与状态

### 11.1 CameraInfo（`GET cam0/hmi/info`）

| 字段 | 说明 |
|------|------|
| `model` | 型号 |
| `name` | DNS 名 |
| `ipAddress` | IP 地址 |
| `macID` | MAC 地址 |
| `serial` | 序列号 |
| `firmwareVersion` | 固件版本 |
| `hmiProtocolVersion` | HMI API 版本 |
| `acq` | `{ nativeWidth, nativeHeight, isColor }` |
| `capabilities` | 能力数组：`autoReady`, `customView`, `dialogs`, `getCellExpressions`, `https`, `resultsQueue`, `xyCoordinates` |
| `httpsEnabled` | 是否启用 HTTPS |
| `jobExtension` | 作业扩展名（如 "jobx"） |
| `httpRequestRoot` | HTTP 资源根路径（旧相机为 "sys"） |

### 11.2 State（`GET cam0/hmi/state`）

| 字段 | 说明 |
|------|------|
| `online` | 在线状态 |
| `softOnline` | 软在线（HMI 控制） |
| `nativeOnline` | 原生模式在线 |
| `discreteOnline` | 离散 I/O 在线 |
| `ffpOnline` | FFP 在线（v3.0+） |
| `liveMode` | 实时模式 |

> 相机上线需 `softOnline && nativeOnline && discreteOnline && ffpOnline` 均为 true。

### 11.3 切换在线/离线

```javascript
// 软在线
cogsock.put(sessionId + "/softOnline", true);

// 实时模式
cogsock.put(sessionId + "/liveMode", true);
```

### 11.4 手动触发

```javascript
cogsock.post(sessionId + "/manualTrigger");
```

---

## 十二、HmiSettings（`GET cam0/hmi/settings/hmi`）

| 字段 | 类型 | 说明 |
|------|------|------|
| `allowAdjustImage` | bool | 允许调整图像（平移/缩放） |
| `allowFilmstrip` | bool | 允许胶片条 |
| `allowFilmstripSaveImage` | bool | 允许保存图像 |
| `allowFocus` | bool | 显示对焦按钮 |
| `allowJobLoad` | bool | 显示加载作业按钮 |
| `allowJobSave` | bool | 显示保存作业按钮 |
| `allowLocalStorage` | bool | 允许浏览器 localStorage |
| `allowProcessedImages` | bool | 显示处理后图像 |
| `allowSideMenu` | bool | 允许右侧菜单（24.4.0+） |
| `allowSoftOnline` | bool | 显示在线/离线按钮 |
| `allowSwitchView` | bool | 显示视图切换按钮 |
| `allowTrigger` | bool | 显示触发按钮 |
| `defaultColorScheme` | string | 颜色主题 |
| `enableHttpImages` | bool | HTTPS 下允许 HTTP 传图（提升性能） |
| `imageResolution` | int | 1=FULL, 2=HALF, 3=QUARTER, 4=EIGHTH |
| `inactivityTimeout` | int | 无操作超时登出（分钟），0=禁用 |
| `statusStyle` | int | 0=通过/失败, 1=几何, 2=勾叉 |

修改后需调用 `save` 持久化到设备。

---

## 十三、用户与权限（UserAccessInfo）

| 字段 | 说明 |
|------|------|
| `name` | 用户名 |
| `access` | `full` / `protected` / `locked`（已废弃，用 privileges） |
| `accessLevel` | 0=ADMIN, 1=OPERATOR, 2=MONITOR（已废弃） |
| `privileges` | 权限字符串数组 |

**权限常量**：`IS.CFG`, `IS.IMAGE`, `IS.MNT`, `IS.OPS`, `IS.FILE`, `IS.OPENFILE`, `IS.WRITEFILE`, `IS.JOB`, `IS.OPENJOB`, `IS.EDITJOB`, `IS.2AUTH`, `IS.CFGJOB`, `IS.CSTMALL`, `IS.SAVE`, `IS.SAVEJOBAS`, `IS.TESTRUN`, `IS.ONLINEOFFLINE`。

获取当前用户：`GET {SID}/currentUser`。

---

## 十四、作业管理

### 14.1 加载/保存作业

```javascript
// 加载相机上的作业
cogsock.post(sessionId + "/loadJob", "MyJob.jobx");

// 保存作业到相机
cogsock.post(sessionId + "/saveJob", "MyJob.jobx");
```

### 14.2 loadJobData / saveJobData（HTTP）

大文件应走 HTTP 而非 CogSocket。

**loadJobData**：
```
POST http://{ip}/cam0/hmi/hs/{sid}/loadJobData
Content-Type: application/json
{ "$type": "HmiNamedContent", "name": "MyJob.jobx", "content": "<base64>" }
```

**saveJobData**：
```javascript
cogsock.post(sessionId + "/saveJobData", "MyJob.jobx", function(resp) {
  // resp: { "$type": "Byte[]", "base64": "..." }
});
```

### 14.3 作业事件

- `jobLoadingChanged`：作业加载中状态变化
- `jobChanged`：作业值变化
- `jobLoadFailed`：加载失败

---

## 十五、HmiSession 完整方法列表

| 方法 | 说明 | 所需权限 |
|------|------|---------|
| `login` | 登录 | — |
| `logoff` | 登出（会话不关闭） | — |
| `dispose` | 释放会话 | — |
| `keepAlive` | 保活 | — |
| `ready` | 确认接收结果 | — |
| `manualTrigger` | 手动触发 | IS.OPS |
| `setCellValue` | 设置单元格值 | IS.CFGJOB |
| `setCellValues` | 批量设置 | IS.CFGJOB |
| `setCellExpression` | 设置单元格表达式 | IS.EDITJOB |
| `getCellExpression` | 获取表达式 | 登录 |
| `getCellExpressions` | 批量获取表达式 | 登录 |
| `setCellName` | 设置单元格名 | IS.EDITJOB |
| `getCellCondition` / `setCellCondition` | 单元格状态条件 | IS.EDITJOB |
| `queryCellResults` | 查询单元格结果 | 登录 |
| `getAllCellNames` | 获取所有单元格名 | 登录 |
| `getLatestResult` | 获取最新结果 | 登录 |
| `loadJob` | 加载作业 | IS.OPENJOB |
| `loadJobData` | 加载作业数据（HTTP） | IS.OPENJOB |
| `saveJob` / `saveJobData` | 保存作业 | IS.SAVE |
| `loadImage` | 加载图像（HTTP） | IS.IMAGE |
| `createNewJob` | 新建空作业 | IS.OPENJOB |
| `setEasyView` | 设置 EasyView | IS.CFG |
| `setHmiPages` | 设置 HMI 页面 | IS.CFG |
| `setCustomViewList` | 设置自定义视图列表 | IS.CFGJOB |
| `setSessionInfo` | 设置会话信息 | — |
| `beginEdit` / `endEdit` | 图形编辑 | — |
| `listFiles` | 列出文件 | FTP 读 |
| `getSessionIDs` | 获取所有会话 ID | IS.CFG |
| `runJobValidation` / `cancelJobValidation` | 作业验证 | admin/engineer |
| `systemValidationFlag` | 系统验证标志 | — |
| `setStartupOnline` / `setStartupJob` | 启动在线/启动作业 | IS.ONLINEOFFLINE |

---

## 十六、Platform API（HTTP REST）

根路径 `api/`，需 Full 权限，默认凭据 `admin`/空密码。

### 16.1 审计日志

```
GET /api/audit-log?before=<ISO8601>&after=<ISO8601>
```

支持 `Accept-Encoding: gzip`。

### 16.2 Syslog 转发

```
GET  /api/audit-log/syslog-forwarding
PUT  /api/audit-log/syslog-forwarding   { address, port?, severityFilter?, tcp_framing? }
DELETE /api/audit-log/syslog-forwarding
```

### 16.3 服务器证书

```
GET  /api/security/certificates/protocols
GET  /api/security/certificates/tls
PUT  /api/security/certificates/tls/{id}        (安装证书，multipart/form-data)
DELETE /api/security/certificates/tls/{id}
PUT  /api/security/certificates/tls/{id}/csr    (生成 CSR)
PUT  /api/security/certificates/tls/{id}/pem    (安装签名证书)
PUT  /api/security/certificates/tls/{id}/activate { protocols: ["https"] }
```

### 16.4 固件升级

```
POST /api/firmware/update?reboot=true
Content-Type: application/octet-stream
Body: .cogfw 文件
```

### 16.5 备份恢复

```
GET  /api/backup-restore/backup                          (下载备份)
POST /api/backup-restore/restore?type=replicate|replace  (multipart/form-data, archive=)
```

### 16.6 错误响应格式

```json
{ "status": 400, "detail": "错误描述" }
```

---

## 十七、HMI API 版本历史

| 版本 | 发布固件 | 主要变更 |
|------|---------|---------|
| 1.0 | 5.6.0 | 初始 |
| 2.0 | 5.7.0 | — |
| 2.2 | 5.8.1 | Results Queue、Custom View、dialog、findDevices |
| 2.3 | 5.9.2 | login 返回 UserAccessInfo、getLatestResult、loadImage、HmiSessionInfo:requestResult |
| 2.4 | 6.3 | queryCellResults、cellNames 支持范围、CameraInfo.capabilities |
| 2.5 | 6.5 | getCellExpressions |
| **3.0** | **22.1.0** | 根路径改为 `cam0/hmi`、xyCoordinates、ffpOnline、移除 findDevices/Dialogs |
| 23.1.0 | | autoReady |
| 23.3.0 | | 多自定义视图、setCustomViewList |
| 24.2.0 | | HmiResult 新增 views |
| 24.4.0 | | EditMultiGraphics、settingsChanged 事件 |
| 25.1.0 | | JobValidation |
| 26.1.0 | | JobValidationResult |

---

## 十八、集成建议（CameraViewerTauri）

### 18.1 前端模块结构

```
src/
  assets/cogsocket/
    cogsocket.js          # SDK 副本（已存在）
    require.js            # 依赖（已存在）
  cogsocket_manager.js    # 封装层（新建）
```

`CogSocketManager` 封装：
- `connect(ip, port, rootPath)` / `disconnect()`
- `get(path)` / `put(path, data)` / `post(path, data)` —— 返回 Promise
- `on(event, callback)` / `off(event)` 事件订阅
- `openSession(cellRange)` / `login(user, pwd)` / `closeSession()`
- 自动 keepAlive 定时器（10~20 秒）
- `ready` 自动管理

### 18.2 与 TODO.md 任务的对应

| TODO 任务 | 实现要点 |
|-----------|---------|
| **#21 CogSocket 通信** | 实现 `CogSocketManager`，完成连接、GET/PUT/POST、事件订阅 |
| **#2 取 $A$0 图片** | `openSession` → `resultChanged` 中取 `acqImageView.layers[0].url`，或 `views` 中对应视图 |
| **#7 单元格值设置** | `setCellValue([cell, value])` 写入，`queryCellResults` 回读验证 |
| **#6 HMI 中文化** | 主界面走 i18n；HMI iframe 语言需通过相机端设置或 URL 参数（需真机确认） |

### 18.3 注意事项

- **根路径**：API v3.0+ 用 `cam0/hmi`，旧版（5.x~6.x）用 `system`，通过 `CameraInfo.hmiProtocolVersion` 判断。
- **必须 send ready**：不调用 `ready` 相机不会推送下一帧结果。
- **keepAlive 间隔**：默认超时 30 秒，建议 10~20 秒。
- **登录编码**：用户名/密码应为 64-bit ASCII，`login` 第三参数传 `false` 表示不编码。
- **大文件走 HTTP**：`loadJobData`、`loadImage` 必须用 HTTP，CogSocket 有大小限制。
- **编辑器连接**：`editorAttached=true` 时 HMI 应只读（除非 `ignoreEditorAttached=true`）。
- **可用会话数**：连接前检查 `isSessionAvailable`，超出会返回错误 `-1610612728`。

---

## 参考文件

- SDK JS 示例：`SampleCode/javascript/cogsocket.js`、`cogsocket_test.js`、`display_results.html`
- SDK .NET 示例：`SampleCode/dotnet/WindowsFormsApp/WebAPISampleApp/MainForm.cs`
- 序列化类：`Cognex.InSight.Web/Serialization/`
- HMI 网页互操作：`SampleCode/dotnet/WebView2WindowsFormsApp/HmiWebPageInterop.cs`
- 官方文档：`In-Sight HMI API.pdf`、`In-Sight HMI Developers Guide.pdf`、`In-Sight Platform API.pdf`
