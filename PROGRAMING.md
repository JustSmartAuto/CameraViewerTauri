# 脚本编程指南（PROGRAMING.md）

CameraViewerTauri 目前有两处可由用户编写 ECMAScript 自定义逻辑的地方：

| 位置 | 入口函数 | 用途 | 持久化 |
|------|---------|------|--------|
| 视觉检测工作台（软件设置中启用功能开关） | `inspect(context)` | 对图片上的 ROI 做检测判定，输出 OK/NG、指标与效果图叠加层 | `VisionConfig.json` |
| 图片清理 → 高级版（脚本判定） | `evaluate(context)` | 决定每一轮是否删除当前最旧文件，复刻 ImageCleanerAutoWeld 的 `delete_conditions.js` | 随清理配置持久化（`CleanConfig.json` 的 `delete_script` 字段） |

两处脚本均由纯 Rust 实现的 **boa_engine 0.22**（[boa-dev/boa](https://github.com/boa-dev/boa)）在本地执行，不上传任何数据。

---

## 〇、运行环境与通用规则

### 1. 语言能力

- 是完整 ECMAScript 的一个现代子集：`let/const`、箭头函数、模板字符串、解构、`JSON`、`Math`、`Date`、`Array` 的 `forEach/map/filter/reduce`、`Map/Set` 等标准库均可使用。
- **没有**浏览器或 Node.js 的宿主 API：不存在 `window`、`document`、`require`/`import` 模块、文件读写、网络请求、`setTimeout` 等。脚本只能基于传入的 `context` 计算并返回结果。
- 每次运行都在全新的沙箱中执行，脚本内的全局变量**不会**保留到下一次运行，所有输入只能来自 `context`。

### 2. console 输出

沙箱提供 `console` 对象，输出会显示在界面的日志/控制台区域（多个参数以空格拼接，非字符串自动转字符串）：

| 调用 | 界面表现 |
|------|---------|
| `console.log(...)` / `console.info(...)` | 普通日志 |
| `console.warn(...)` | 警告（自动加 `[warn] ` 前缀，黄色） |
| `console.error(...)` | 错误（自动加 `[error] ` 前缀，红色） |

> 没有 `console.debug`，需要分级时用 `log/info/warn/error` 即可。

### 3. 错误与超时

- 语法错误、运行时异常、入口函数缺失、返回值类型错误都会被捕获并显示，不会导致软件崩溃。
- 视觉检测脚本有 **5 秒超时**：请不要写死循环；超时后本次判定失败。注意 boa 0.22 没有指令中断机制，超时只能丢弃本次结果。
- 图片清理脚本在后台逐文件调用：脚本异常或返回非布尔值时本轮清理**立即停止**（不会误删），可据此快速失败保护文件。

### 4. 编辑器

两处都使用 Monaco Editor 并提供"载入默认"按钮：编辑内容跟随软件主题；视觉检测工作台点"保存"后脚本随配置持久化，点"运行检测"立即执行。

---

## 一、视觉检测脚本 `inspect(context)`

### 1.1 最小示例

```js
function inspect(ctx) {
  var c = ctx.controls[0];
  if (!c || c.stats.count === 0) {
    return { pass: null, message: '无有效 ROI', overlays: [], metrics: [] };
  }
  var ok = c.stats.mean >= 50 && c.stats.mean <= 200;
  return {
    pass: ok,
    message: ok ? 'OK' : 'NG',
    overlays: [{ type: 'rect', x: c.x, y: c.y, w: c.w, h: c.h, color: ok ? '#22c55e' : '#ef4444' }],
    metrics: [{ name: 'mean', value: c.stats.mean }]
  };
}
```

要求：**必须定义 `inspect(context)` 函数，并返回一个对象**。不定义函数或返回非对象都会报错。

### 1.2 context 结构

```js
context = {
  nowMs: 1726560000000,          // 运行时刻的本地时间毫秒数（Date.now() 风格）
  image: {
    path: 'D:/photos/a.bmp',     // 图片绝对路径（仅信息展示，脚本不能读文件）
    fileName: 'a.bmp',
    width: 1280,
    height: 960
  },
  controls: [ /* ROI 控件数组，顺序同中栏控件表 */ ]
}
```

每个控件对象除公共字段 `id / name / type / stats` 外，只带与自身类型对应的几何字段：

| type | 含义 | 几何字段 |
|------|------|---------|
| `region` | 轴对齐矩形 | `x, y, w, h`（左上角 + 宽高） |
| `circle` | 圆 | `cx, cy, r`（圆心 + 半径） |
| `point` | 点 | `x, y` |
| `line` | 线段 | `x1, y1, x2, y2`（两端点） |
| `polygon` | 多边形 | `points: [[x, y], ...]`（顶点数组，至少 3 个点） |

所有坐标单位均为**原图像素**，原点在左上角，与中栏画布/属性表里的数值完全一致（声明式数据驱动，脚本拿到的就是控件数据本身）。

### 1.3 stats 灰度统计

Rust 端先把像素按 `gray = 0.299 R + 0.587 G + 0.114 B` 转成 0–255 的灰度，再按控件几何收集像素，`stats` 为：

| 字段 | 含义 |
|------|------|
| `count` | 实际参与统计的像素数（越界像素自动裁剪；同一像素只计一次） |
| `mean` | 灰度平均值（保留两位小数）；无像素时为 `null` |
| `stdDev` | 灰度**总体**标准差（除以 N，保留两位小数）；无像素时为 `null` |
| `min` / `max` | 灰度最小/最大值；无像素时为 `null` | |

各形状的像素口径（决定 `count` 与统计范围）：

- **region**：覆盖 `[x, x+w) × [y, y+h)` 范围的整数像素，超出图像边界的部分裁剪。
- **circle**：像素中心（坐标加 0.5）落入圆盘 `(px-cx)² + (py-cy)² ≤ r²` 的像素。
- **point**：`x, y` 四舍五入对应的 1 个像素；坐标在图外则 `count = 0`。
- **line**：沿线按 0.5 像素步长采样并去重，线宽约 1 像素。
- **polygon**：包围盒内逐像素取像素中心做射线法判定，顶点少于 3 个时为空。

> 编写脚本时建议先判断 `c.stats.count === 0`（ROI 拖到图外或为空），避免对 `null` 调用 `.toFixed()` 等方法。

### 1.4 返回值

| 字段 | 类型 | 说明 |
|------|------|------|
| `pass` | boolean \| null | `true` 显示绿色 **OK**，`false` 显示红色 **NG**，`null`/省略显示中性徽标（取 `message` 文本） |
| `message` | string | 结论短语，如 `'OK'`、`'划痕超差'`；会打印到控制台 |
| `overlays` | array | 绘制到右栏"效果图"上的叠加层，见 1.5 |
| `metrics` | array | 指标列表 `{ name: string, value: number|string }`，逐条打印到控制台 |

### 1.5 overlays 叠加层

坐标同样使用原图像素，会随画布缩放。`color` 可以是任何 Canvas 接受的颜色字符串（`'#ef4444'`、`'rgb(...)'`、`'red'` 等）；省略时用默认橙色 `#f97316`。`label` 为可选文字标签。

```js
{ type: 'rect',      x, y, w, h,                  color?, label? }
{ type: 'circle',    cx, cy, r,                   color?, label? }
{ type: 'point',     x, y,                        color?, label? }  // 固定 4px 实心圆点
{ type: 'line',      x1, y1, x2, y2,              color?, label? }
{ type: 'polygon',   points: [[x,y], ...],        color?, label? }
{ type: 'text',      x, y, text: '...',           color? }
```

叠加层是**声明式**的：脚本只描述"画什么"，渲染由软件完成。可以回显 ROI 轮廓、标记缺陷位置、写批注文字；未知 `type` 或字段缺失的条目会被跳过。

### 1.6 默认脚本（完整参考）

新建脚本为空时点"载入默认"得到的就是下面这份：对每个 ROI 做灰度均值阈值判定（50–200），回显轮廓并按结果着绿/红色，输出 mean/stdDev 指标。

```js
var MEAN_MIN = 50;
var MEAN_MAX = 200;

function outlineOf(c) {
  if (c.type === 'region')  return { type:'rect', x:c.x, y:c.y, w:c.w, h:c.h, label:c.name };
  if (c.type === 'circle')  return { type:'circle', cx:c.cx, cy:c.cy, r:c.r, label:c.name };
  if (c.type === 'point')   return { type:'point', x:c.x, y:c.y, label:c.name };
  if (c.type === 'line')    return { type:'line', x1:c.x1, y1:c.y1, x2:c.x2, y2:c.y2, label:c.name };
  if (c.type === 'polygon') return { type:'polygon', points:c.points, label:c.name };
  return null;
}

function inspect(ctx) {
  var overlays = [];
  var metrics = [];
  var allOk = true;
  var checked = 0;

  ctx.controls.forEach(function (c) {
    var o = outlineOf(c);
    if (o) overlays.push(o);
    if (!c.stats || c.stats.count === 0) {
      console.log('[warn] 控件 ' + c.name + ' 区域内无像素，已跳过');
      return;
    }
    checked++;
    metrics.push({ name: c.name + '.mean', value: c.stats.mean });
    metrics.push({ name: c.name + '.stdDev', value: c.stats.stdDev });
    var ok = c.stats.mean >= MEAN_MIN && c.stats.mean <= MEAN_MAX;
    if (o) o.color = ok ? '#22c55e' : '#ef4444';
    if (!ok) {
      allOk = false;
      console.log('[NG] ' + c.name + ' 灰度均值 ' + c.stats.mean.toFixed(2) + ' 超出阈值');
    }
  });

  if (checked === 0) {
    return { pass: null, message: '无有效 ROI', overlays: overlays, metrics: metrics };
  }
  return { pass: allOk, message: allOk ? 'OK' : 'NG', overlays: overlays, metrics: metrics };
}
```

### 1.7 进阶示例

**按控件名分别设置阈值，并在 NG 位置写文字：**

```js
var RULES = {
  ROI_A: { min: 60,  max: 200 },
  ROI_B: { min: 120, max: 240 }
};

function inspect(ctx) {
  var overlays = [], metrics = [], bad = [];

  ctx.controls.forEach(function (c) {
    if (!c.stats || c.stats.count === 0) return;
    var rule = RULES[c.name] || { min: 50, max: 200 };
    var ok = c.stats.mean >= rule.min && c.stats.mean <= rule.max;
    overlays.push({
      type: 'rect', x: c.x, y: c.y, w: c.w, h: c.h,
      color: ok ? '#22c55e' : '#ef4444', label: c.name
    });
    metrics.push({ name: c.name + '.mean', value: c.stats.mean });
    if (!ok) {
      bad.push(c.name);
      overlays.push({ type: 'text', x: c.x, y: c.y, text: 'NG ' + c.stats.mean.toFixed(1), color: '#ef4444' });
    }
  });

  return {
    pass: bad.length === 0,
    message: bad.length === 0 ? 'OK' : 'NG: ' + bad.join(','),
    overlays: overlays,
    metrics: metrics
  };
}
```

**用标准差检测"区域内是否有异物/纹理突变"：**

```js
var STD_MAX = 18;   // 经验阈值，按实际光源/产品标定

// 按控件类型生成轮廓叠加层（矩形/圆/点/线/多边形通用）
function outlineOf(c, color) {
  if (c.type === 'region')  return { type: 'rect',    x: c.x, y: c.y, w: c.w, h: c.h, color: color, label: c.name };
  if (c.type === 'circle')  return { type: 'circle',  cx: c.cx, cy: c.cy, r: c.r, color: color, label: c.name };
  if (c.type === 'point')   return { type: 'point',   x: c.x, y: c.y, color: color, label: c.name };
  if (c.type === 'line')    return { type: 'line',    x1: c.x1, y1: c.y1, x2: c.x2, y2: c.y2, color: color, label: c.name };
  if (c.type === 'polygon') return { type: 'polygon', points: c.points, color: color, label: c.name };
  return null;
}

function inspect(ctx) {
  var overlays = [];
  var metrics = [];
  var bad = [];
  var checked = 0;

  ctx.controls.forEach(function (c) {
    if (!c.stats || c.stats.count === 0) return;
    checked++;
    // 先画一层绿色轮廓，NG 时再改为红色
    overlays.push(outlineOf(c, '#22c55e'));
    metrics.push({ name: c.name + '.stdDev', value: c.stats.stdDev });
    metrics.push({ name: c.name + '.contrast', value: c.stats.max - c.stats.min });
    if (c.stats.stdDev > STD_MAX) {
      bad.push(c.name);
      overlays.push(outlineOf(c, '#ef4444'));
      overlays.push({ type: 'text', x: c.x || c.cx || c.x1 || 0, y: c.y || c.cy || c.y1 || 0,
                      text: 'std=' + c.stats.stdDev.toFixed(1), color: '#ef4444' });
      console.warn(c.name, 'stdDev=', c.stats.stdDev, 'min/max=', c.stats.min, c.stats.max);
    }
  });

  if (checked === 0) {
    return { pass: null, message: '无有效 ROI', overlays: overlays, metrics: metrics };
  }
  return {
    pass: bad.length === 0,
    message: bad.length === 0 ? 'OK' : '疑似异物: ' + bad.join(','),
    overlays: overlays,
    metrics: metrics
  };
}
```

> 受沙箱限制，脚本只能拿到各 ROI 的统计量而拿不到逐像素数据。需要新的统计维度（如连通域、模板匹配）时，请在软件侧扩展后再暴露到 context。

---

## 二、图片清理高级版脚本 `evaluate(context)`

### 2.1 调用流程（重要）

清理按文件**创建时间从旧到新**逐个处理，每准备删一个文件之前调用一次脚本：

1. 脚本返回 `true` → 删除当前这个最旧文件，然后带着**更新后**的 `context`（剩余文件数、过期数、磁盘余量都会变）调用下一次。
2. 脚本返回 `false` → 本轮清理立即停止，其余文件保留。
3. 脚本报错或返回非布尔值 → 同样立即停止（安全策略，防止误删）。
4. 每次调用之间按设置的删除间隔等待；每次调用都是全新沙箱，**不要依赖全局变量记录进度**，需要的状态都在 context 里。

### 2.2 context 字段（与 ImageCleanerAutoWeld 完全对齐，驼峰命名）

| 字段 | 类型 | 含义 |
|------|------|------|
| `nowMs` | number | 当前本地时间毫秒数 |
| `path` | string | 监控目录路径 |
| `cleanupMode` | string | 清理模式，本软件固定为 `'Image'` |
| `storageTimeSeconds` | number | 保存时间阈值（秒）= 保留天数 × 86400 |
| `expiredCount` | number | **剩余待评估文件**中创建时间早于阈值的数量（含当前文件） |
| `imageCount` | number | 当前剩余文件总数（含当前文件） |
| `imageCountEnabled` | boolean | 是否启用"数量上限"条件 |
| `imageCountThreshold` | number | 数量阈值 |
| `freeSpaceGb` | number | 所在磁盘当前剩余空间（GB） |
| `diskSpaceEnabled` | boolean | 是否启用"剩余空间"条件 |
| `diskSpaceThresholdGb` | number | 剩余空间阈值（GB） |

编辑器旁的"测试"按钮使用由当前清理配置生成的示例 context（`expiredCount=1, imageCount=100`，磁盘余量为实测值），不删除任何文件。

### 2.3 默认脚本（AND 逻辑）

```js
function evaluate(c) {
    var ageMet = c.expiredCount > 0;
    var countMet = !c.imageCountEnabled || c.imageCount > c.imageCountThreshold;
    var diskMet = !c.diskSpaceEnabled || c.freeSpaceGb < c.diskSpaceThresholdGb;
    return ageMet && countMet && diskMet;
}
```

语义：**已过期** 且 **（未启用数量条件 或 数量超阈值）** 且 **（未启用空间条件 或 剩余空间低于阈值）** 才删除当前文件。

### 2.4 进阶示例

**只按数量清理：始终删除最旧文件，直到剩余数量降到阈值：**

```js
function evaluate(c) {
  return c.imageCount > c.imageCountThreshold;
}
```

**过期文件只删到磁盘余量恢复到 50GB 为止（忽略界面上的空间阈值设置）：**

```js
var TARGET_FREE_GB = 50;

function evaluate(c) {
  console.log('剩余', c.imageCount, '个文件，空闲', c.freeSpaceGb.toFixed(1), 'GB');
  if (c.freeSpaceGb >= TARGET_FREE_GB) {
    console.info('空间已恢复，停止删除');
    return false;
  }
  return c.expiredCount > 0;   // 空间不足时只删已过期的
}
```

> 清理脚本的判定必须是确定的布尔结果——不要把删除条件建立在 `Math.random()` 等不确定值上；每轮 context 都会刷新，条件应只依赖 context 字段。

---

## 三、排错速查

| 现象/报错 | 原因与处理 |
|-----------|-----------|
| `脚本必须定义 inspect(context) 函数` | 函数名拼写错误或根本没定义；入口名固定为 `inspect`（视觉）/ `evaluate`（清理） |
| `inspect(context) 必须返回对象 { pass, message, overlays, metrics }` | 返回了 `undefined`/数字/字符串；至少返回 `{ pass: null, message: '', overlays: [], metrics: [] }` |
| `Cannot read properties of null (reading 'toFixed')` | ROI 无像素时 `stats.mean` 为 `null`，先判断 `stats.count === 0` |
| `脚本执行超时（>5s）` | 视觉检测脚本有死循环或单次计算量过大；检查循环边界 |
| 效果图上没有叠加层 | 检查 overlay 的 `type` 拼写、几何字段是否与类型匹配、`overlays` 是否是数组 |
| 控制台中文乱码/无输出 | 脚本文件即编辑器文本，无需转码；`console.warn/error` 的 `[warn]/[error]` 前缀是自动加的，不要重复拼接 |
| 图片清理一轮后很早就停止 | 脚本返回了 `false`，或对某个 context 抛错/返回了非布尔值；用"测试"按钮先验证逻辑 |
| 改了脚本不生效 | 视觉检测需点"保存"持久化（运行检测用编辑器当前内容）；清理脚本需在清理标签页保存后，下一轮清理才使用新脚本 |

### 能力边界清单

- 可用：ECMAScript 标准库、传入的 context、纯计算、`console.*` 日志。
- 不可用：文件/网络/进程/定时器、DOM、模块加载、跨次运行的内存状态、逐像素图像数据（视觉脚本仅提供 ROI 统计量）。
- 支持载入的图片格式：PNG / BMP / JPEG（.jpg/.jpeg）。
