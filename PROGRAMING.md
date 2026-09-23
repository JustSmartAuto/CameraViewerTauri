# 脚本编程手册

本手册介绍如何为电磁系统相机显示软件编写 JavaScript 脚本。

脚本基于 QuickJS 引擎，每个画面格子拥有独立的执行上下文，互不干扰。

---

## 目录

1. [快速开始](#快速开始)
2. [脚本入口](#脚本入口)
3. [消息对象结构](#消息对象结构)
4. [内置脚本函数](#内置脚本函数)
5. [常用示例](#常用示例)
6. [调试技巧](#调试技巧)

---

## 快速开始

1. 在主界面点击菜单 **脚本 → 编辑格子 N 脚本**。
2. 在编辑器中编写 `process(message)` 函数。
3. 点击 **运行测试**，选择消息类型并加载测试模板。
4. 观察右侧预览与底部控制台输出。
5. 点击 **保存到配置** 或 **应用并关闭**。

---

## 脚本入口

脚本必须包含一个 `process(message)` 函数。当对应格子收到 MQTT 消息时，该函数会被调用。

```javascript
function process(message) {
    // 处理消息并调用绘图函数
}
```

---

## 消息对象结构

### 图像消息

对应 MQTT 话题 `/autoweld/measure/image/raw`：

```javascript
{
  timestamp: "202608271857890",
  imageHeight: 5120,
  imageWidth: 5120,
  productNumber: 1,
  surfaceNumber: 1,
  imageData: "...base64编码的图像数据..."
}
```

> **面号分发**：图像消息会根据 `surfaceNumber` 自动显示到对应格子，即格子 1 显示面 1 的图像，格子 2 显示面 2 的图像，以此类推。非本格面号的图像不会刷新该格画面。

### 测量数据消息

对应 MQTT 话题 `/autoweld/measure/data/raw`：

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

对应 MQTT 话题 `/autoweld/measure/logs/raw`：

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

---

## 内置脚本函数

### 坐标系说明

所有绘图函数均使用左手坐标系：原点位于图像左上角，X 向右增长，Y 向下增长。

### 绘图函数

#### PlotString

在指定像素坐标绘制文字。

```javascript
PlotString(imageWidth, imageHeight, x, y, text, fontSize, color)
```

#### PlotLine

绘制线段。

```javascript
PlotLine(imageWidth, imageHeight, x1, y1, x2, y2, thickness, color)
```

#### PlotCircle

绘制圆（描边）。

```javascript
PlotCircle(imageWidth, imageHeight, centerX, centerY, diameter, thickness, color)
```

#### PlotPolygon

绘制多边形（描边）。

```javascript
PlotPolygon(imageWidth, imageHeight, [x0, y0, x1, y1, ...], thickness, color)
```

#### PlotPoint

绘制实心点。

```javascript
PlotPoint(imageWidth, imageHeight, x, y, thickness, color)
```

#### ShowImage

显示 base64 编码的图像。支持纯 base64 字符串，也支持带 `data:image/xxx;base64,` 前缀的 data URI。

```javascript
ShowImage(base64String)
```

### 图像与工具函数

#### SaveImage

将 base64 编码的图片直接解码并保存到指定路径。支持 `data:image/xxx;base64,` 前缀；目标目录不存在时会自动创建。

```javascript
SaveImage(base64String, filePath)
```

示例：

```javascript
SaveImage(message.imageData, "D:/snap/raw_" + GetTimeStamp("yyyyMMdd_HHmmss") + ".jpg")
```

#### SaveRenderedImage

保存当前格子中**已渲染的图像**（底图 + 脚本叠加图形），输出为 JPG 格式，质量 95%。目标目录不存在时会自动创建。

```javascript
SaveRenderedImage(filePath)
```

**注意**：该函数保存的是调用时刻格子中已显示的画面。如果在处理 `IMAGE` 消息的 `process(message)` 中调用，保存的是上一帧（当前帧底图尚未刷新到界面）；在 `DATA`/`LOG` 消息中调用或配合按钮触发时，可保存当前已渲染画面。

示例：

```javascript
SaveRenderedImage("D:/snap/render_" + GetTimeStamp("yyyyMMdd_HHmmss") + ".jpg")
```

#### Clear

清除当前格子的图像与脚本叠加图形。

```javascript
Clear()
```

示例：

```javascript
function process(message) {
    if (message.message === "停止检测") {
        Clear()
        print("已清屏")
        return
    }
    if (message.imageData) {
        ShowImage(message.imageData)
    }
}
```

#### GetTimeStamp

返回当前时间字符串，`format` 与 Java `DateTimeFormatter` 格式一致。若传入空字符串，默认格式为 `yyyyMMdd_HHmmss`。

```javascript
GetTimeStamp(format)
```

示例：

```javascript
GetTimeStamp("yyyyMMdd_HHmmss")      // 20260828_065812
GetTimeStamp("yyyy-MM-dd HH:mm:ss")  // 2026-08-28 06:58:12
GetTimeStamp("yyyyMMdd_HHmmss_SSS")  // 20260828_065812_123
```

### 颜色

`color` 参数支持以下形式：

- 整数 RGB：`0xFF0000`（红色）
- 十六进制字符串：`"#FF0000"` 或 `"FF0000"`

---

## 常用示例

### 示例 1：显示图像

```javascript
function process(message) {
    if (message.imageData) {
        ShowImage(message.imageData)
    }
}
```

### 示例 2：绘制测量结果

在图像上绘制测量数据：配方号、面号、OK 状态、对称度、高度、宽度与时间。

```javascript
function process(message) {
    if (message.productWidth === undefined) {
        return
    }
    // 使用 test1.jpg 的实际尺寸 9344x7000 作为绘图坐标系
    var imgW = 9344
    var imgH = 7000

    // 绘制宽度测量线（红色）
    PlotLine(imgW, imgH,
        message.leftPointX, message.leftPointY,
        message.rightPointX, message.rightPointY,
        8, 0xFF0000)

    // 绘制高度参考线（蓝色）
    PlotLine(imgW, imgH,
        message.topPointX, message.topPointY,
        message.topPointX, message.baseLineLeftPointY,
        8, 0x0000FF)

    // 绘制测量点（黄色）
    PlotPoint(imgW, imgH, message.leftPointX, message.leftPointY, 30, 0xFFFF00)
    PlotPoint(imgW, imgH, message.rightPointX, message.rightPointY, 30, 0xFFFF00)
    PlotPoint(imgW, imgH, message.topPointX, message.topPointY, 30, 0xFFFF00)

    // 左上角信息面板背景框（青色边框）
    PlotPolygon(imgW, imgH,
        [80, 80, 2200, 80, 2200, 900, 80, 900],
        6, 0x00FFFF)

    // 配方号、面号
    PlotString(imgW, imgH, 140, 200,
        "配方号: " + message.productNumber + "    面号: " + message.surfaceNumber,
        90, 0xFFFFFF)

    // OK（绿色大号文字）
    PlotString(imgW, imgH, 140, 380,
        "OK",
        180, 0x00FF00)

    // 对称度、高度、宽度
    PlotString(imgW, imgH, 140, 540,
        "对称度: " + message.symmetry.toFixed(4),
        90, 0xFFFFFF)

    PlotString(imgW, imgH, 140, 660,
        "高度: " + message.productHeight.toFixed(4),
        90, 0xFFFFFF)

    PlotString(imgW, imgH, 140, 780,
        "宽度: " + message.productWidth.toFixed(2),
        90, 0xFFFFFF)

    // 时间
    PlotString(imgW, imgH, 140, 880,
        "时间: " + message.timestamp,
        70, 0xAAAAAA)
}
```

### 示例 3：绘制产品轮廓框

```javascript
function process(message) {
    var imgW = message.imageWidth || 5120
    var imgH = message.imageHeight || 5120

    var left = message.leftPointX
    var top = message.topPointY
    var right = message.rightPointX
    var bottom = message.baseLineLeftPointY

    PlotPolygon(imgW, imgH,
        [left, top, right, top, right, bottom, left, bottom],
        4, 0x00FFFF)
}
```

### 示例 4：不同消息类型分别处理

```javascript
function process(message) {
    if (message.imageData) {
        ShowImage(message.imageData)
    } else if (message.productWidth !== undefined) {
        PlotString(5120, 5120, 100, 100,
            "W=" + message.productWidth.toFixed(2),
            60, 0xFFFFFF)
    } else if (message.message) {
        print("日志: " + message.message)
    }
}
```

### 示例 5：综合绘图函数测试

用于验证所有内置绘图函数，可在脚本编辑器中选择“数据”类型并加载模板后运行测试。

```javascript
function process(message) {
    var imgW = 5120
    var imgH = 5120

    // 1. 绘制参考矩形边框
    PlotPolygon(imgW, imgH,
        [100, 100, 5020, 100, 5020, 5020, 100, 5020],
        10, 0x00FFFF)

    // 2. 绘制对角线
    PlotLine(imgW, imgH, 100, 100, 5020, 5020, 8, 0xFF0000)
    PlotLine(imgW, imgH, 5020, 100, 100, 5020, 8, 0xFF0000)

    // 3. 绘制中心圆
    PlotCircle(imgW, imgH, 2560, 2560, 800, 12, 0x00FF00)

    // 4. 绘制角点
    PlotPoint(imgW, imgH, 100, 100, 40, 0xFFFF00)
    PlotPoint(imgW, imgH, 5020, 100, 40, 0xFFFF00)
    PlotPoint(imgW, imgH, 5020, 5020, 40, 0xFFFF00)
    PlotPoint(imgW, imgH, 100, 5020, 40, 0xFFFF00)

    // 5. 绘制文字标签
    PlotString(imgW, imgH, 200, 200,
        "左上角 (100,100)", 80, 0xFFFFFF)
    PlotString(imgW, imgH, 3200, 200,
        "右上角 (5020,100)", 80, 0xFFFFFF)
    PlotString(imgW, imgH, 200, 4900,
        "左下角 (100,5020)", 80, 0xFFFFFF)

    // 6. 绘制测量数据（假设收到数据消息）
    if (message.productWidth !== undefined) {
        PlotLine(imgW, imgH,
            message.leftPointX, message.leftPointY,
            message.rightPointX, message.rightPointY,
            15, 0xFF00FF)

        PlotString(imgW, imgH, 200, 300,
            "宽度: " + message.productWidth.toFixed(2),
            100, 0xFFAA00)
        PlotString(imgW, imgH, 200, 450,
            "高度: " + message.productHeight.toFixed(2),
            100, 0xFFAA00)
        PlotString(imgW, imgH, 200, 600,
            "对称度: " + message.symmetry.toFixed(3),
            100, 0xFFAA00)
    }

    print("绘图测试完成")
}
```

### 示例 6：显示测试图像

用于验证 `ShowImage` 函数，可在脚本编辑器中选择“图像”类型并粘贴 base64 测试图像后运行。

```javascript
function process(message) {
    if (message.imageData && message.imageData.length > 0) {
        ShowImage(message.imageData)
        print("图像尺寸: " + message.imageWidth + "x" + message.imageHeight)
    } else {
        print("无图像数据")
    }
}
```

### 示例 7：保存图像

在收到测量数据时保存当前渲染图，文件名带时间戳。

```javascript
function process(message) {
    if (message.imageData) {
        ShowImage(message.imageData)
    }
    if (message.productWidth !== undefined) {
        var imgW = message.imageWidth || 5120
        var imgH = message.imageHeight || 5120

        // 绘制测量结果
        PlotLine(imgW, imgH,
            message.leftPointX, message.leftPointY,
            message.rightPointX, message.rightPointY,
            8, 0xFF0000)
        PlotString(imgW, imgH, 100, 100,
            "宽度: " + message.productWidth.toFixed(2),
            60, 0x00FF00)

        // 保存渲染图（底图 + 叠加图形）
        var ts = GetTimeStamp("yyyyMMdd_HHmmss")
        SaveRenderedImage("D:/snap/render_" + ts + ".jpg")

        // 也可以直接保存原始图像
        // SaveImage(message.imageData, "D:/snap/raw_" + ts + ".jpg")

        print("已保存渲染图: " + ts)
    }
}
```

---

## 调试技巧

### 使用 print 输出

脚本中可以使用 `print()` 输出到控制台：

```javascript
function process(message) {
    print("收到消息: " + JSON.stringify(message))
}
```

### 使用运行测试

脚本编辑器提供测试数据模板，可模拟图像、数据、日志消息，无需等待真实 MQTT 数据即可验证脚本。

### 常见错误

- **未找到 process 函数**：脚本必须定义 `process(message)`。
- **颜色不显示**：确认颜色参数为整数或有效十六进制字符串。
- **坐标错位**：检查传入的 `imageWidth/imageHeight` 是否与消息中的图像尺寸一致。
- **图像不显示**：确认 `ShowImage` 接收的是有效 base64 字符串。
- **保存图像失败**：确认路径合法且有写入权限；`SaveImage` 需传入有效 base64，`SaveRenderedImage` 需在格子已有图像时调用。
- **保存渲染图为上一帧**：`SaveRenderedImage` 在 `IMAGE` 消息处理期间调用时保存的是上一帧，建议在 `DATA`/`LOG` 消息中调用。
