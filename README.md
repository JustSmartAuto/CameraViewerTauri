# NomacsNet

一个使用 **.NET 8 / WPF**、**纯 C#（无 XAML）** 从零编写的 FOSS 图片查看器，**界面复刻 [nomacs 3.22.4](https://nomacs.org/)**（菜单条目与文案采用其官方简体中文翻译），使用其原版图标，**默认界面语言为中文**，打包为**单文件 exe**。

## 界面（复刻 nomacs 3.22 布局）

- **深色主题**：深灰菜单栏 + 半透明覆盖面板（与 nomacs 默认外观一致）
- **菜单栏（六个菜单，官方中文文案）**：文件 / 编辑 / 视图 / 面板 / 工具 / 帮助
  - 文件：打开、打开目录、打开方式、近期文件（持久化）、重命名、转到、保存、另存为、重新加载、排序、递归扫描文件夹
  - 编辑：复制 / 粘贴、顺（逆）时针 90°、180°、水平 / 垂直翻转、背景颜色
  - 视图：放大 / 缩小、适配、100%、缩放预设、抗锯齿、全屏、无边框
  - 面板：缩略图、文件信息、元数据信息、状态栏、隐藏所有面板
  - 工具：设置、插件管理器、批处理、幻灯播放
  - 帮助：快捷键（F1）、关于 Nomacs
- **左下信息框**（File Info）：文件名、修改时间、文件大小、像素尺寸、缩放比例
- **底部缩略图条**（默认开启）：异步加载、当前图高亮、点击切换、排序
- **右下缩放标签**：缩放时浮现、约 1 秒后淡出（nomacs 行为）
- **底部导航箭头** ‹ › 与 **右侧元数据面板**（键/值列表，含相机信息）
- 菜单与上下文菜单均为纯 C# 代码构建的自定义深色模板（无 XAML 文件）

## 特性

- 纯 C# 构建 UI（代码即界面，不含任何 XAML 文件）
- 打开文件 / 文件夹 / 拖放 / 命令行参数，自动列出同目录图片序列
- 滚轮缩放（以鼠标指针为锚点）、左键拖拽平移
- 旋转（90° 步进）、水平 / 垂直镜像
- 适配窗口（等比、考虑旋转后的旋转包围盒）
- 幻灯片播放（可设间隔）、全屏切换、覆盖前确认的删除 / 保存
- 复制图像到剪贴板 / 从剪贴板粘贴（转存临时 PNG 后打开）
- 排序（按文件名 / 修改日期 / 大小，可逆序）、递归扫描、近期文件
- 设置持久化（JSON）：背景色、幻灯间隔、抗锯齿、递归扫描、面板可见性、无边框模式
- **AVIF / HEIC / HEIF / HIF** 原生解码（见下文）

## AVIF / HEIC 支持

采用双路径解码：

1. **优先 WIC**：若系统已安装 AV1/AVIF 图像扩展（Windows 11 通常自带），直接由系统解码器解码；
2. **失败时回退 libheif**：程序内置自包含的 libheif 原生库（内置 AV1 dav1d 与 HEVC libde265 解码器，无需任何插件文件），通过 [LibHeifSharp](https://github.com/0xC0000054/libheif-sharp) 绑定解码为 `BitmapSource`。

支持包含 Alpha 通道的 AVIF/HEIC（8 bit，RGBA→Bgra32 转换），端到端验证覆盖 1×1、200×200、403×302（含 ICC/EXIF/XMP 的 paris 样本）多尺寸样本。

## 支持的格式

| 格式 | 解码路径 |
| --- | --- |
| PNG / JPEG / BMP / GIF(静态) / TIFF / ICO / WDP/JXR | WPF 内置 WIC |
| WebP / DNG / CR2 / NEF / ARW | 系统 WIC 编解码器（需已安装） |
| AVIF / HEIC / HEIF / HEIFS / HIF | WIC（如有系统扩展）→ libheif 兜底 |

## 快捷键

| 操作 | 快捷键 |
| --- | --- |
| 打开文件 / 打开目录 | `Ctrl+O` / `Ctrl+D` |
| 上一张 / 下一张 | `←` `↑` `PgUp` / `→` `↓` `Space` `PgDn` |
| 首张 / 末张 | `Home` / `End` |
| 放大 / 缩小（滚轮任意位置） | `+` / `-` |
| 适配窗口 / 原始尺寸 | `0`（或双击）/ `1` |
| 顺时针 90° / 逆时针 90° / 180° | `R` 或 `F` / `Shift+R` / `Shift+H` |
| 水平翻转 / 垂直翻转 | `T` / `Shift+T` |
| 复制 / 粘贴图像 | `Ctrl+C` / `Ctrl+V` |
| 重命名 / 转到 | `F2` / `Ctrl+G` |
| 保存 / 另存为 | `Ctrl+S` / `Ctrl+Shift+S` |
| 重新加载 | `F6` |
| 删除当前图 | `Delete`（弹出确认） |
| 幻灯片播放 | `F5`（`Shift+F5` 强制播放） |
| 全屏 / 无边框 | `F11` 或 `Ctrl+Shift+F`（视图菜单） |
| 缩略图 / 信息框 / 元数据 | `Ctrl+T` / `I` / `Ctrl+M` |
| 设置 / 快捷键帮助 | `Ctrl+,` / `F1` |
| 退出 | `Esc` |

## 构建

需要 .NET 8 SDK（`dotnet --list-sdks` 确认）。

```powershell
# 编译
dotnet build -c Release

# 发布单文件 exe（自包含运行时 + 压缩 + 原生库内嵌自解压）
dotnet publish -c Release
```

产物：

```
bin\Release\net8.0-windows\win-x64\publish\nomacs.exe
```

- 双击或右键打开方式即可使用；_exe_ 图标与程序窗口图标均为 nomacs 原版图标（`src\nomacs.ico` 以 `EmbeddedResource` 嵌入）。
- 交付的是自包含单文件，无需安装 .NET 运行时；约 74 MB（含 .NET 运行时与 libheif）。
- 首次启动时 .NET 会把内嵌的 `libheif.dll` 等原生库解压到 `%TEMP%\.net\` 缓存。
- 用户设置与近期文件保存在 `%LOCALAPPDATA%\NomacsNet\settings.json`。

## 依赖

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| [LibHeifSharp](https://www.nuget.org/packages/LibHeifSharp/) | libheif 的 .NET 绑定 | LGPL-3.0 |
| [LibHeif.Native.Runtime](https://www.nuget.org/packages/LibHeif.Native.Runtime/) | 自包含 libheif 原生库（dav1d/libde265 编译内置） | LGPL-3.0 |
| WPF (.NET 8) | UI 框架 | MIT |
| nomacs 图标与中文界面文案 | 来自 [nomacs 3.22.4](https://github.com/nomacs/nomacs) 源码及其 zh_CN 翻译 | GPLv3+ |

## 许可

本项目源码以 GPL-3.0-or-later 发布（继承 nomacs 图标/文案的兼容性要求）。依赖的 libheif 及其内置解码器遵循 LGPL 条款，随二进制分发。