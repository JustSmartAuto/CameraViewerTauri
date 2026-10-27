# NomacsNet

一个使用 **.NET 8 / WPF**、**纯 C#（无 XAML）** 从零编写的 FOSS 图片查看器，交互风格与 [nomacs](https://nomacs.org/) 一致，使用其原版图标，并打包为**单文件 exe**。

## 特性

- 纯 C# 构建 UI（代码即界面，不含任何 XAML 文件）
- 打开文件 / 文件夹 / 拖放 / 命令行参数，自动列出同目录图片序列
- 滚轮缩放（以鼠标指针为锚点）、左键拖拽平移
- 旋转（90° 步进）、水平镜像
- 适配窗口（等比、考虑旋转后的旋转包围盒）
- 幻灯片播放、全屏切换、删除当前图片（带确认）
- 右键菜单；状态栏显示文件名 / 序号 / 尺寸 / 缩放比例
- GIF/PNG/JPEG/BMP/TIFF/ICo 等格式由 WIC（Windows 解码器）直接支持
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
| 打开 | `Ctrl+O` |
| 上一张 / 下一张 | `←` `↑` `PgUp` / `→` `↓` `Space` `PgDn` |
| 首张 / 末张 | `Home` / `End` |
| 放大 / 缩小 | `+` / `-`（滚轮在任何位置） |
| 适配窗口 / 原始尺寸 | `0`（或双击）/ `1` |
| 旋转 90° CW / CCW | `R` 或 `F` / `Shift+R` |
| 水平镜像 | `T` |
| 删除当前图 | `Delete`（弹出确认） |
| 幻灯片播放 | `F5` |
| 全屏切换 | `F11` |
| 退出 | `Esc` |
| 右键菜单 | 鼠标右键 |

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

## 依赖

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| [LibHeifSharp](https://www.nuget.org/packages/LibHeifSharp/) | libheif 的 .NET 绑定 | LGPL-3.0 |
| [LibHeif.Native.Runtime](https://www.nuget.org/packages/LibHeif.Native.Runtime/) | 自包含 libheif 原生库（dav1d/libde265 编译内置） | LGPL-3.0 |
| WPF (.NET 8) | UI 框架 | MIT |
| nomacs 图标 | 来自 [nomacs 3.22.4](https://github.com/nomacs/nomacs) 源码 | GPLv3+ |

## 许可

本项目源码以 GPL-3.0-or-later 发布（继承 nomacs 图标/风格的兼容性要求）。依赖的 libheif 及其内置解码器遵循 LGPL 条款，随二进制分发。
