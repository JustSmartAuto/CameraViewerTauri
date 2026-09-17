# CHANGELOG

## 2026-09-17

### 13:00–14:00
- 初始化 `CameraViewerDotnet` 解决方案：调研 CameraViewerTauri（Tauri 2.x + Rust）功能，确定复刻范围（相机网格核心功能）
- 创建 WPF 主程序 `CameraViewer/`（net8.0-windows，XAML + AntdUI 引用 + WebView2）：主窗口 1200×800 最大化、1–12 路相机网格、错峰延时加载、每格 URL/备注/锁定/刷新/最大化、设置窗口四标签页、明暗主题、中英双语、托盘驻留、JSON 配置（支持 portable.txt 便携模式）
- 创建 .NET Framework 4.8 启动器 `CameraViewer.Launcher/`：内嵌 .NET 8 运行时安装包与主程序 exe，`dotnet --list-runtimes` 检测 → 静默安装 → 释放并启动主程序
- 编写 `build-with-timestamp.sh` 一键打包脚本（publish 单文件主程序 → MSBuild 编译启动器 → 输出带时间戳 exe）
- 修复 Git Bash 下 `/p:` 参数被当路径、for 循环 glob 引号导致的脚本错误
- 修复单文件发布下 `Assembly.Location` 返回空串的问题（IL3000），改用 `GetName().Version`

### 14:00–15:00
- 按需求删除全部 XAML，纯 C# 重写整个 WPF UI（App/MainWindow/CameraCell/SettingsWindow 代码构建）
- 控件库由 AntdUI（经核实无 WPF 版，仅 WinForms）改用 **HandyControls 3.7.0**（HandyControl 官方后继分支，支持 `ThemeManager` 代码切换主题），VS Code 配色以应用级画刷叠加
- 软件全面更名 **CameraViewerDotnet**（程序集/窗口标题/托盘/配置目录/产物命名），启动器与打包脚本同步适配
- 修复打包时旧测试进程占用 WebView2 缓存目录导致的构建失败
- 设置 Logo（`assets\icon.ico`）：主程序 exe、主窗口标题栏、系统托盘、启动器 exe 均内嵌该图标
- 端到端验证构建通过（0 警告 0 错误），产出 `dist/CameraViewerDotnet_202609171437.exe`（约 60MB 便携式单文件）
