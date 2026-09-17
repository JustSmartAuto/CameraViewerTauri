# 更新日志 / CHANGELOG

> 记录规则：按**小时**记录当日修改，每条注明涉及文件与打包产物，时间取自文件修改时间与构建产物时间戳。
> 版本号规则：`年.月.日`（如 26.9.17 = 2026-09-17）。

---

## 2026-09-17　版本 26.9.17.18

### 重构：UI 框架从 WPF(HandyControls) 迁移到 WinForms + AntdUI

- [CameraViewer/MainWindow.cs](CameraViewer/MainWindow.cs)、[CameraViewer/SettingsWindow.cs](CameraViewer/SettingsWindow.cs)、[CameraViewer/AboutWindow.cs](CameraViewer/AboutWindow.cs)：修复标题栏缺失导致的元素重叠——AntdUI.Window 本身不绘制标题栏，需添加 `AntdUI.PageHeader` 作为标题栏（`ShowButton=true` 显示最小化/最大化/关闭按钮，`DragMove` 默认支持拖动窗口）；主窗口工具栏标题文字移入 PageHeader，功能按钮靠右停靠。
- [CameraViewer/CameraViewer.csproj](CameraViewer/CameraViewer.csproj)：移除 `UseWPF` 与 `HandyControls 3.7.0`，新增 `AntdUI 2.4.10`；新增 `RemoveWebView2WpfReference` Target（移除 WebView2 包对 net5.0+ 无条件引用的 `Microsoft.Web.WebView2.Wpf.dll`，消除 MSB3277 WindowsBase 版本冲突警告）。
- [CameraViewer/App.cs](CameraViewer/App.cs)：WPF Application 改为 WinForms `[STAThread] Main` 入口；保留三处全局异常处理（UI 线程 / 非UI线程 / 未观察 Task 异常）。
- [CameraViewer/ThemeManager.cs](CameraViewer/ThemeManager.cs)：改为设置 `AntdUI.Config.Mode` + `System.Drawing.Color` 调色板（VS Code 配色不变），保留 `ThemeChanged` 事件。
- [CameraViewer/MainWindow.cs](CameraViewer/MainWindow.cs)：继承 `AntdUI.Window`（无边框窗口）；工具栏/状态栏/托盘/网格重排布局（TableLayoutPanel），相机网格 1/2/4/6/9/16 布局与错峰加载逻辑不变。
- [CameraViewer/CameraCell.cs](CameraViewer/CameraCell.cs)：每路格子改用 WinForms WebView2 控件 + AntdUI Input/Button；HMI 语言跟随注入逻辑（`hmi-i18n.js` 握手协议）原样保留。
- [CameraViewer/SettingsWindow.cs](CameraViewer/SettingsWindow.cs)：改为 `AntdUI.Window` + Tabs；JOBX 相机表格从 WPF DataGrid 改为 AntdUI Table（单元格编辑/勾选/按钮事件）；备份目录选择仍用 WinForms `FolderBrowserDialog`。
- [CameraViewer/AboutWindow.cs](CameraViewer/AboutWindow.cs)：改为 `AntdUI.Window`。
- **修复 JOBX "添加相机"无反应**：数据实际已写入配置但表格不刷新——AntdUI.Table 数据绑定要求行模型为 public 类 + public 属性，`JobxRow` 由 private 嵌套类 + 字段改为 public 类 + 属性后表格正常显示/刷新。
- **按钮浅蓝色描边**：所有 `TTypeMini.Default` 按钮（工具栏、相机格、JOBX 备份页）统一 `DefaultBorderColor=#91CAFF` + `BorderWidth=1`（AntdUI 2.4.10 无 `BorderColor` 属性，属性名为 `DefaultBorderColor`）；[ThemeManager.cs](CameraViewer/ThemeManager.cs) 新增 `BtnBorder` 调色板项。
- **修复页面内容残缺**：AntdUI.Radio 默认 `AutoSizeMode=None` 导致 FlowLayoutPanel 测量高度为 0（显示设置/软件设置页单选按钮不显示）→ 显式 `AutoSizeMode=TAutoSize.Auto`；AntdUI.Label 长文本 AutoSize 测量不准导致关于页版本号/描述/技术栈截断 → 改用原生 WinForms `Label`（AutoSizing+MaximumSize 换行可靠）；JOBX 表格空数据时 `EmptyHeader=true` 显示列头；关于页版本号截断 commit 哈希（`InformationalVersion` 去除 `+` 后缀）。
- [CameraViewer/I18n.cs](CameraViewer/I18n.cs)：关于页技术栈文案更新为 "WinForms + AntdUI (.NET 8) + WebView2 + FluentFTP"。
- [README.md](README.md)：技术栈描述 WPF/HandyControls → WinForms/AntdUI。
- 版本号 26.9.17.17 → 26.9.17.18。

---

## 2026-09-17　版本 26.9.17.17

### 修复：JOBX 备份选择目录后 DataGrid 报错

- [CameraViewer/SettingsWindow.cs](CameraViewer/SettingsWindow.cs)：新增 `SafeJobxRefresh()` 方法，先 `CommitEdit` + `CancelEdit` 退出编辑事务再 `Items.Refresh()`；添加/选择目录/删除三处调用全部替换，解决"在 AddNew 或 EditItem 事务过程中不允许 Refresh"报错。

### 改进：URL 与备注同行布局

- [CameraViewer/CameraCell.cs](CameraViewer/CameraCell.cs)：网格从 3 行（URL+按钮/备注/WebView）改为 2 行（URL+备注+按钮/WebView）；`top` 网格 5 列：URL(`2*`) | 备注(`1*`) | 锁定 | 刷新 | 最大化，减少垂直空间占用。

### 其它

- 版本号 26.9.17.16 → 26.9.17.17。

---

## 2026-09-17　版本 26.9.17.16

- 新增 [CameraViewer/JobxBackupService.cs](CameraViewer/JobxBackupService.cs)：基于 FluentFTP 50.1.0 的作业文件备份服务，支持 FTP/FTPS（FTPS 默认启用，实际必须启用 FTPS 才能正常备份 .jobx 文件）+ 信任自签证书；递归下载 `.jobx`/`.jobx.sig`；环形日志（最近 500 条，INFO/WARN/ERROR 分级）。
- [CameraViewer/SettingsWindow.cs](CameraViewer/SettingsWindow.cs)：新增「JOBX备份」标签页，DataGrid 配置相机（名称/IP/端口/用户名/密码/备份目录/FTPS/信任证书），添加/删除/备份/全部备份/打开目录按钮，日志区 DispatcherTimer 每秒刷新。
- [CameraViewer/ConfigService.cs](CameraViewer/ConfigService.cs)：新增 `JobxCameraConfig`（`ftps_enabled` 默认 `true`）、`JobxBackupConfig`，加载/保存 `JobxBackupConfig.json`。
- 备份目录列新增「选择」按钮（`DataGridTemplateColumn` + `System.Windows.Forms.FolderBrowserDialog`），点击浏览选择文件夹（WPF 窗口句柄作为 owner，try-catch 防崩溃），写回配置并刷新。初版用 `Microsoft.Win32.OpenFolderDialog`（.NET 8 新增）运行时闪退，改用久经考验的 WinForms 对话框。
- 依赖：[CameraViewer/CameraViewer.csproj](CameraViewer/CameraViewer.csproj) 新增 `FluentFTP 50.1.0`。

### 新功能：HMI 语言跟随（WebView2 注入脚本）

- 新增 [CameraViewer/Assets/hmi-i18n.js](CameraViewer/Assets/hmi-i18n.js)（`EmbeddedResource` 编译期烘焙，不新增运行时资源依赖）：
  - 路径匹配 `/pages/hmi/` 时激活（含顶层 frame 与嵌套 iframe）；默认英文（原文），宿主通过 `chrome.webview` 消息通知语言切换。
  - 只做整串精确匹配替换（字典 ~80 词），不做子串替换，避免破坏作业数据/文件名/数值；MutationObserver 批处理（16ms 合并）框架后续动态渲染；切回英文时按记录原文完整还原。
  - 宿主（C# 侧）协议：子→宿主 `{__hmiI18n:"ready"}`，宿主→子 `{__hmiI18n:"lang",lang:"zh"|"en"}`。
- [CameraViewer/CameraCell.cs](CameraViewer/CameraCell.cs)：`CoreWebView2InitializationCompleted` 改为 async，注入脚本（`AddScriptToExecuteOnDocumentCreatedAsync`）→ 挂载 `WebMessageReceived` → 导航；收到 `ready` 握手立即 `PostWebMessageAsJson` 下发当前语言；`OnLanguageChanged` 末尾调用 `PostHmiLang` 实时通知。
- **修复 .NET 版架构适配**：.NET 版每个 CameraCell 的 WebView2 顶层 frame 直接就是相机 HMI 页面（无父页面），移除 `if (window.self === window.top) return;`（Tauri 版保留该检查因顶层 frame 是软件 UI）；通信改为 `chrome.webview` 通道（Tauri 版用标准 `window.postMessage` 父子 frame 通信）。

### 新功能：关于页面

- 新增 [CameraViewer/AboutWindow.cs](CameraViewer/AboutWindow.cs)：显示应用名/版本号（`AssemblyInformationalVersionAttribute`）/功能描述/技术栈/GitHub 仓库链接（`Hyperlink` + `Process.Start`），关闭按钮，跟随主题色。
- [CameraViewer/MainWindow.cs](CameraViewer/MainWindow.cs)：工具栏新增「关于」按钮（`AntIcon.InfoCircle` 图标），语言切换时同步刷新。
- [CameraViewer/AntIcon.cs](CameraViewer/AntIcon.cs)：新增 `InfoCircle` 图标常量。
- [CameraViewer/I18n.cs](CameraViewer/I18n.cs)：新增 `about`/`aboutDesc`/`aboutTech`/`aboutRepo`/`aboutClose` 中英文键。

### 改进：网格布局 12 → 16

- [CameraViewer/MainWindow.cs](CameraViewer/MainWindow.cs)：`Layouts` 新增 `[16] = (4, 4)` 预设。
- [CameraViewer/ConfigService.cs](CameraViewer/ConfigService.cs)：相机数量上限 12 → 16。
- [CameraViewer/SettingsWindow.cs](CameraViewer/SettingsWindow.cs)：数量选项增加 16。

### 改进：URL/备注输入框水印提示

- [CameraViewer/CameraCell.cs](CameraViewer/CameraCell.cs)：`AttachPlaceholder` 方法用 `VisualBrush` 实现水印（空内容显示占位文本），URL 框 `192.168.1.10 或 http://...`，备注框 `备注...`；语言切换时同步刷新占位文本。
- [CameraViewer/I18n.cs](CameraViewer/I18n.cs)：新增 `urlPlaceholder`/`remarkPlaceholder` 中英文键。

### 修复：崩溃产生僵尸进程导致无法重启

- [CameraViewer/App.cs](CameraViewer/App.cs)：新增全局未处理异常处理（`DispatcherUnhandledException` + `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`），崩溃时弹错误框后 `Shutdown()` + `Environment.Exit(1)` 优雅退出，释放文件锁，避免产生僵尸进程。
- [CameraViewer.Launcher/Program.cs](CameraViewer.Launcher/Program.cs)：`ExtractResource` 的 `File.Delete` 改为 `TryDeleteFile`——若 exe 被占用则先 `Process.Kill()` 结束残留 `CameraViewerDotnet` 进程再重试删除（最多 4 次，间隔 500ms），解决崩溃后重启提示"访问被拒绝"的问题。

### 其它

- [CameraViewer/I18n.cs](CameraViewer/I18n.cs)：新增 `select`/`selectBackupDir`/`error` 中英文键。
- 版本号 26.9.17.14 → 26.9.17.16。
- 验证：`dotnet build`（主程序）+ `MSBuild`（Launcher）0 警告 0 错误。

---

## 2026-09-17　版本 26.9.17.14（初始提交 a1b5087）

- CameraViewerDotnet：纯 C# WPF + HandyControls 相机 HMI 查看器，.NET 4 启动器内嵌 .NET 8 运行时，单文件打包。
- 1–12 路相机网格布局、错峰加载、锁定/刷新/最大化、主题切换、中英文、系统托盘、配置持久化。
