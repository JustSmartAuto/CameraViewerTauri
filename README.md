# 相机显示软件

基于 Java + Swing/FlatLaf + MQTT + QuickJS 的相机/测量数据可视化桌面应用。

## 功能

- 主界面 2×2 四宫格，每格独立显示一路画面。
- 每路画面通过 MQTT 订阅图像、测量数据、日志三类消息。
- 支持为每路画面编写 JavaScript 脚本，解析 XML 数据并调用内置脚本函数。
- 设置界面配置每格的 MQTT 参数与显示选项。
- 脚本编辑器支持语法高亮、测试数据、控制台输出与预览。
- 配置以 JSON 形式保存在 jar 同目录。

## 运行方式

```bash
java -jar camera-viewer-1.0.0-fat_*.jar
```

或双击 `camera-viewer-1.0.0-fat_*.jar`。

## 构建方式

项目完全离线构建，依赖均已放在 `libs/` 目录。

### 一键打包（推荐）

运行项目根目录的打包脚本，会自动构建并在项目根目录生成带时间戳的 fat jar：

```bash
./build-with-timestamp.sh
```

构建成功后例如生成 `camera-viewer-1.0.0-fat_2608272356.jar`。

### 手动构建

```bash
gradle-dist/gradle-9.7.1/bin/gradle fatJar --offline
```

产物位于 `build/libs/camera-viewer-1.0.0-fat.jar`，可手动复制到项目根目录。

## 配置文件

首次运行后会在 jar 同目录生成 `camera-viewer-config.json`，包含 4 个格子的：

- 名称、启用状态
- MQTT broker 地址与端口
- Client ID
- 图像/数据/日志订阅话题
- JavaScript 脚本
- 显示选项
- 全局：启动时最大化
- 全局：无图像数据时显示 `imgs/calibboard.jpg` 占位图（按 5120×5120 坐标系）

## 脚本入口

每个格子的脚本必须定义 `process(message)` 函数。`message` 为 JavaScript 对象，字段随消息类型不同：

### 图像消息

```javascript
{
  timestamp: "202608271857890",
  imageHeight: 5120,
  imageWidth: 5120,
  productNumber: 1,
  surfaceNumber: 1,
  imageData: "...base64..."
}
```

> **面号与格子对应关系**：图像、测量数据、日志三类消息均会根据 `surfaceNumber` 自动分发到对应格子，即格子 1 只处理面 1 的消息，格子 2 只处理面 2 的消息，以此类推。非本格面号的消息不会刷新该格画面，也不会清空已有绘制内容。

```javascript
{
  timestamp: "202608271857890",
  imageHeight: 5120,
  imageWidth: 5120,
  productNumber: 1,
  surfaceNumber: 1,
  baseLineLeftPointX: 10.0,
  baseLineLeftPointY: 500.0,
  baseLineRightPointX: 502.0,
  baseLineRightPointY: 500.0,
  leftPointX: 50.0,
  leftPointY: 250.0,
  rightPointX: 462.0,
  rightPointY: 250.0,
  productWidth: 412.0,
  topPointX: 256.0,
  topPointY: 50.0,
  productHeight: 450.0,
  symmetry: 0.12
}
```

### 日志消息

```javascript
{
  timestamp: "202608271857890",
  imageHeight: 5120,
  imageWidth: 5120,
  productNumber: 1,
  surfaceNumber: 1,
  message: "检测完成"
}
```

## XML 消息格式

MQTT 实际收发的消息为 XML 文本。建议每类消息都使用显式根元素；旧版无根元素的 XML 也会被自动兼容（内部会先包裹一层 `<root>` 再解析）。

### 图像消息

```xml
<?xml version="1.0" encoding="UTF-8"?>
<ImageMessage>
  <TimeStamp>202608271857890</TimeStamp>
  <ImageHeight>9344</ImageHeight>
  <ImageWidth>7000</ImageWidth>
  <ProductNumber>1</ProductNumber>
  <SurfaceNumber>1</SurfaceNumber>
  <ImageData>...</ImageData>
</ImageMessage>
```

- `ImageData` 为图像的 base64 编码字符串，留空或省略时将按设置显示占位图。

### 测量数据消息

```xml
<?xml version="1.0" encoding="UTF-8"?>
<DataMessage>
  <TimeStamp>202608271857890</TimeStamp>
  <ImageHeight>7000</ImageHeight>
  <ImageWidth>9344</ImageWidth>
  <ProductNumber>1</ProductNumber>
  <SurfaceNumber>1</SurfaceNumber>
  <BaseLineLeftPointX>10.0</BaseLineLeftPointX>
  <BaseLineLeftPointY>500.0</BaseLineLeftPointY>
  <BaseLineRightPointX>502.0</BaseLineRightPointX>
  <BaseLineRightPointY>500.0</BaseLineRightPointY>
  <LeftPointX>50.0</LeftPointX>
  <LeftPointY>250.0</LeftPointY>
  <RightPointX>462.0</RightPointX>
  <RightPointY>250.0</RightPointY>
  <ProductWidth>412.0</ProductWidth>
  <TopPointX>256.0</TopPointX>
  <TopPointY>50.0</TopPointY>
  <ProductHeight>450.0</ProductHeight>
  <Symmetry>0.12</Symmetry>
</DataMessage>
```

### 日志消息

```xml
<?xml version="1.0" encoding="UTF-8"?>
<LogMessage>
  <TimeStamp>202608271857890</TimeStamp>
  <ImageHeight>7000</ImageHeight>
  <ImageWidth>9344</ImageWidth>
  <ProductNumber>1</ProductNumber>
  <SurfaceNumber>1</SurfaceNumber>
  <Message>检测完成</Message>
</LogMessage>
```

## 内置脚本函数

坐标系为左手坐标系，原点位于图像左上角，X 向右，Y 向下。

### 绘图函数

| 函数 | 签名 |
|---|---|
| `PlotString` | `PlotString(imgW, imgH, x, y, text, fontSize, rgb)` |
| `PlotLine` | `PlotLine(imgW, imgH, x1, y1, x2, y2, thickness, rgb)` |
| `PlotCircle` | `PlotCircle(imgW, imgH, cx, cy, diameter, thickness, rgb)` |
| `PlotPolygon` | `PlotPolygon(imgW, imgH, [x0, y0, x1, y1, ...], thickness, rgb)` |
| `PlotPoint` | `PlotPoint(imgW, imgH, x, y, thickness, rgb)` |

`rgb` 可以是整数（如 `0xFF0000`）或十六进制字符串（如 `"#FF0000"`）。

### 图像与工具函数

| 函数 | 签名 | 说明 |
|---|---|---|
| `ShowImage` | `ShowImage(base64)` | 设置当前格子显示的图像。 |
| `SaveImage` | `SaveImage(base64, path)` | 将 base64 图片直接解码保存到指定路径，支持 `data:image/xxx;base64,` 前缀；目录不存在时自动创建。 |
| `SaveRenderedImage` | `SaveRenderedImage(path)` | 保存当前格子中**已渲染的图像**（底图 + 脚本叠加图形），输出为 JPG，质量 95%；目录不存在时自动创建。 |
| `GetTimeStamp` | `GetTimeStamp(format)` | 返回当前时间字符串，`format` 与 Java `DateTimeFormatter` 格式一致，为空时默认 `yyyyMMdd_HHmmss`。 |
| `Clear` | `Clear()` | 清除当前格子的图像与脚本叠加图形。 |

`SaveRenderedImage` 保存的是调用时刻格子中已显示的画面。如果在处理 `IMAGE` 消息的 `process(message)` 中调用，保存的是上一帧（当前帧底图尚未刷新到界面）；在 `DATA`/`LOG` 消息中调用或配合按钮触发时，可保存当前已渲染画面。

## 图像操作

每个格子的图像区域支持：

- **适合**：图像完整适应显示区域。
- **最大化**：按实际像素大小（1:1）显示。
- **放大 / 缩小**：以画布中心为基准缩放。
- **鼠标滚轮**：以鼠标指针位置为中心缩放。
- **鼠标拖拽**：缩放后可拖拽平移图像。

缩放比例在画布左上角实时显示。

## 示例脚本

```javascript
function process(message) {
    if (message.imageData) {
        ShowImage(message.imageData)
    }
    if (message.productWidth !== undefined) {
        PlotString(512, 512, 10, 30,
            "宽度: " + message.productWidth.toFixed(2), 24, 0x00FF00)
        PlotLine(512, 512,
            message.leftPointX, message.leftPointY,
            message.rightPointX, message.rightPointY,
            3, 0xFF0000)
    }
}
```

### 保存图像示例

```javascript
function process(message) {
    if (message.imageData) {
        ShowImage(message.imageData)
    }
    if (message.productWidth !== undefined) {
        PlotString(512, 512, 10, 30,
            "宽度: " + message.productWidth.toFixed(2), 24, 0x00FF00)

        // 保存叠加了绘制内容的渲染图
        var ts = GetTimeStamp("yyyyMMdd_HHmmss")
        SaveRenderedImage("D:/snap/render_" + ts + ".jpg")

        // 也可以直接保存原始 base64 图像
        // SaveImage(message.imageData, "D:/snap/raw_" + ts + ".jpg")
    }
}
```

## MQTT 测试推送

项目附带 `test_mqtt_publish.py`，可用于向软件推送测试数据。脚本会根据目标格子自动设置对应的面号（格子 0 对应面 1，格子 1 对应面 2，以此类推），因此 `--all-grids` 会同时测试 4 个面的独立显示：

```bash
# 同时向 4 个格子推送面 1-4 的图像、数据、日志（推荐）
python test_mqtt_publish.py --host 127.0.0.1 --port 8907 --all-grids --all

# 推送全部三类消息到指定格子（默认格子 0 / 面 1，图像使用 imgs/test1.jpg）
python test_mqtt_publish.py --host 127.0.0.1 --port 8907 --grid 0 --all

# 仅推送图像
python test_mqtt_publish.py --host 127.0.0.1 --port 8907 --grid 0 --image

# 使用脚本生成的测试图像
python test_mqtt_publish.py --host 127.0.0.1 --port 8907 --grid 0 --image --generated

# 图像消息不含 ImageData，触发占位图显示
python test_mqtt_publish.py --host 127.0.0.1 --port 8907 --grid 0 --image --no-image-data

# 循环推送 5 次
python test_mqtt_publish.py --host 127.0.0.1 --port 8907 --grid 0 --all --loop 5

# 同时向 4 个格子循环推送 3 轮
python test_mqtt_publish.py --host 127.0.0.1 --port 8907 --all-grids --all --loop 3
```

依赖：`paho-mqtt`、`Pillow`。

## VisionMaster 脚本推送

项目附带 `UserScript_VM42.cs`，用于在 VisionMaster 流程中将图像与测量数据通过 MQTT 推送到本软件显示。

### 脚本输入

在 VisionMaster 脚本模块中按以下顺序绑定输入：

| 输入 | VM 类型 | 说明 |
|---|---|---|
| `in0` | IMAGE | 待显示的图像 |
| `in1` | INT | 配方号 ProductNumber |
| `in2` | INT | 面号 SurfaceNumber |
| `in3` | POINT | 测量点（取第一个点的 PointX/PointY） |
| `in4` | DOUBLE | 对称度 Symmetry |
| `in5` | DOUBLE | 高度 ProductHeight |
| `in6` | DOUBLE | 宽度 ProductWidth |

### 依赖引用

在 VisionMaster 脚本模块的 DLL 引用中添加：

- `OpenCvSharp.dll`
- `M2Mqtt.Net.dll`（可参考 `D:\JustStupid\mqttx图片自定义显示_2608172137\M2Mqtt.Net.dll`）

### 发布内容

脚本每次执行会连接 MQTT broker（默认 `127.0.0.1:8907`），并依次发布三类 XML 消息：

- `/autoweld/measure/image/raw`：JPEG 编码的图像 base64
- `/autoweld/measure/data/raw`：测量数据（以输入点为中心，根据宽高推算左右测量点、顶点与基线端点）
- `/autoweld/measure/logs/raw`：检测完成日志

消息格式与本软件 `XML 消息格式` 章节一致，可直接被默认脚本解析并绘制测量结果。

## 常见问题

1. **FlatLaf native access 警告**：不影响运行。如需消除，可添加 JVM 参数 `--enable-native-access=ALL-UNNAMED`。
2. **MQTT 连接失败**：检查 broker 地址、端口、网络连通性及防火墙设置。
3. **图像不显示**：确认 XML 中 `ImageData` 字段为有效 base64 字符串，且脚本中调用了 `ShowImage` 或默认脚本已启用。
