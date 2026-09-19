# CameraHelperJimJack

<p align="center">
<a href="#中文"><b>中文</b></a> | <a href="#english">English</a>
</p>

---

<a id="中文"></a>

# CameraHelper — Cognex In-Sight 相机检测显示助手

CameraHelper 是一款基于 **.NET Framework 4.8 / WinForms + AntdUI（x64）** 的桌面软件，用于**集中连接、实时显示并自动保存多台 Cognex In-Sight 工业相机的检测结果图像**。支持最多 16 路相机视图自动宫格布局、图像旋转与文字保持水平、OK/NG 缩略图回看、自动存图与磁盘清理、一键重启/备份相机、深浅色主题、中英双语等功能。

程序采用**启动器（Launcher）+ 主程序**的分发模式：`dist/` 下的单文件 `CameraHelperJimJack_时间戳.exe` 内嵌 **.NET 8 Windows Desktop 运行时安装包**与全部程序、依赖。启动器运行时先检测工控机是否装有 .NET 8 桌面运行时，缺失则自动静默安装（需管理员授权）；再确认 .NET Framework 4.8 后，把程序文件释放到 `%LOCALAPPDATA%\CameraHelperJimJack\App` 并启动主程序，避免工控机因缺少运行时而无法运行。

## 目录

- [软件截图](#软件截图)
- [功能特性](#功能特性)
- [软件架构](#软件架构)
- [核心数据流](#核心数据流)
- [配置说明](#配置说明)
- [目录结构](#目录结构)
- [构建与打包](#构建与打包)
- [模块详解](#模块详解)
- [缩略图回看详解](#缩略图回看详解)
- [图像获取与 CogSocket 通信](#图像获取与-cogsocket-通信)
- [第三方依赖](#第三方依赖)
- [许可证](#许可证)

## 软件截图

| 主界面（宫格布局 + 深色主题） | 单视图放大（圆角显示） |
|---|---|
| ![主界面](assets/screenshot_main.png) | ![圆角视图](assets/screenshot_round.png) |

| 程序图标 | 图标预览 |
|---|---|
| ![图标](assets/icon.ico) | ![图标预览](assets/icon_preview.jpg) |

## 功能特性

1. **多相机集中显示** — 最多 16 路视图，按视图数量自动排成 1/2/4/6/9/12/16 宫格，单击可放大为单视图。
2. **AntdUI 现代界面** — 主窗体与各设置/对话框窗体采用 AntdUI 控件（PageHeader / Button / Select / Input / Tabs 等），浅色主题为柔和的**马卡龙浅黄色**配色，按钮统一**浅蓝色描边**（#91CAFF），支持深色/浅色主题一键切换并全局跟随。
3. **检测结果显示** — 实时把 Cognex 图形（CvsCogShape）叠加绘制到相机原图上；支持 90°/180°/270° 旋转，且以特定字符（默认 `$`）开头的文字在旋转后仍保持水平显示。
4. **结果判定与路由** — 从相机电子表格单元格读取 Result（`1`=OK，其余=NG）与 ShotKey（区分同一相机的多次拍照），按「相机序号 + 拍照标识」把记录路由到对应视图。
5. **缩略图回看** — 每条记录保留缩略图（缓存上限可配，默认 20），绿/红边框区分 OK/NG，支持只看 OK / 只看 NG 过滤及前后翻页；另有独立的历史图片浏览窗口。
6. **自动存图** — 按 `yyyy_MM\dd\相机名\Job名\OK|NG\` 分层保存；支持只存 NG、只存结果图，或从相机单元格读取自定义保存路径。
7. **断线自动重连** — 主窗体 1 秒定时器检测连接状态，掉线自动重连；支持上线/离线软切换（`SO1`/`SendReady` 握手）。
8. **一键操作** — 一键重启所有相机（Telnet 发送 `RT`）、一键备份所有在线相机的 Job 文件（按 日期/相机名/作业名 分层）。
9. **图片自动清理** — 按「保留天数 + 磁盘剩余空间」双阈值定期删除旧图，并可清理空文件夹。
10. **文件归档（FileTransformer）** — 监视指定目录，把第三方设备产生的文件按「文件名拆分规则 + 时间规则」搬到归档目录。
11. **系统级便利功能** — 单实例运行（Mutex）、可选关闭 Windows 防火墙 / Defender、多屏指定显示、软件置顶、中英双语切换。
12. **相机维护入口** — 内嵌电子表格视图（可离线编辑并保存作业）、WebView2 打开相机 HMI 网页。

## 软件架构

整体为**单例枢纽 + 动作队列**架构，核心枢纽为 `ProjectMgr.Inst`；UI 层与后台完全解耦，所有耗时操作（图像绘制、存盘、文件搬运）都封装为 `IAction` 进入后台队列串行执行，不阻塞 UI。

```
┌────────────────────── UI 层（主线程）──────────────────────┐
│ FrmMain（AntdUI.Window 主窗体 / 宫格布局 / 1s 定时器）      │
│ CameraView × N（图像显示、缩略图回看条、存图配置）          │
│ FrmSysSetting / FrmViewSetting / FrmOperation / FrmGrid     │
│ FrmHMI / FrmHistory / FrmCamCount                           │
│ ThemeManager（AntdUI 深浅主题 + 原生控件调色板）             │
└───────────────┬───────────────────────┬─────────────────────┘
                │ 读写配置 / 状态        │ ProjectMgr.SetRecordChanged（Invoke）
┌───────────────▼───────────────┐       │
│ ProjectMgr（全局单例）         │       │
│ 配置加载保存 / 相机与视图创建   │       │
│ 防火墙 & Defender 开关         │       │
└───────────────┬───────────────┘       │
                │ 入队 IAction          │
┌───────────────▼───────────────────────▼───────────────────┐
│ ActionQueueWorker（单后台线程 + ConcurrentQueue<IAction>）│
│   CreateImageAction  结果图绘制（缩放/旋转/图形叠加）      │
│   SaveImageAction    原图/结果图异步落盘                   │
│   TransformAction    第三方文件归档搬移                    │
└───────────────┬───────────────────────────────────────────┘
                │ Cognex In-Sight Web SDK（ResultsChanged 推送）
┌───────────────▼───────────────┐   ┌─────────────────────────┐
│ CvsInSightExt × N（相机封装）  │   │ FileTransformer         │
│ 连接 / 结果订阅 / SendReady    │   │（FileSystemWatcher）    │
│ NativeMode（Telnet SO1/RT）   │   │ ImageCleaner（定时清理） │
└───────────────────────────────┘   └─────────────────────────┘
```

关键设计：

- **动作队列**（`ActionQueueWorker`）：单后台线程消费 `ConcurrentQueue<IAction>`，支持 Start/Pause/Continue/Close，队列上限 1000，保证绘制/存图/搬移的串行一致性。
- **ProjectMgr 单例**：持有系统配置、相机配置列表、相机实例列表、视图配置/视图列表、动作队列、清理器，以及各子窗体的单例管理。
- **ThemeManager 主题管理**：静态类统一管理深/浅色主题；AntdUI 控件自动跟随 `Config.Mode`（浅色另经 `Config.Theme().Light(back, fore)` 应用马卡龙黄色窗口底色），原生控件（DataGridView/ToolStrip/GroupBox 等）通过调色板事件 `ThemeChanged` 手动配色；浅色调色板为奶黄系（Bg `#FDF6D8`、Bg2 `#FFFBEA`、Bg3 `#F9EDBE`），全部 AntdUI 按钮与 CameraView 翻页按钮统一浅蓝描边（BtnBorder `#91CAFF`，BorderWidth=1；在线状态红绿按钮除外）；主题选择持久化在 `SysConfig.Theme`。
- **1 秒定时器**（FrmMain）：刷新状态栏时间、处理置顶、调用 `CheckCameraConnection()` 断线重连、更新各视图信息。

## 核心数据流

```
Cognex In-Sight 相机（HTTP/Web，ResultsChanged 推送）
  → CvsInSightExt 接收：原图 + 图形 + 结果 cells（跳过 Live/离线/重复 URL）
  → Enqueue(CreateImageAction)
      · 解析 cells：ShotKey、Result（OK/NG）、自定义保存路径
      · ViewPort 一半尺寸缩放居中（letterbox）
      · 绘制原图 + Cognex 图形；若启用旋转：先绘无文字图形 → RotateFlip(90/180/270)
        → 再单独绘制文字（CharWithoutRotation 前缀文字做反向旋转补偿）
  → ProjectMgr.SetRecordChanged（按 CameraIndex + ShotKey 路由）
  → CameraView（UI Invoke）：主图刷新 + 缩略图回看条
  → 按配置 Enqueue(SaveImageAction)：异步写盘
  → D:\Images\SourceImages | ResultImages\yyyy_MM\dd\相机\Job\OK|NG\*.jpg
```

## 配置说明

配置目录为 exe 同级 `Config\`，四个 JSON 文件（Newtonsoft.Json，保存时先写临时文件再覆盖，防止写坏）：

| 文件 | 对应类 | 内容 |
|---|---|---|
| `SystemConfig.txt` | `SysConfig` | 置顶、多屏选择、语言（0中文/1英文）、主题（light/dark）、视图数量、回看缓存上限、是否关防火墙、进程名 |
| `CameraConfig.txt` | `List<InSightConfig>` | 每台相机的 IP/端口/账号密码、ShotKey/Result/路径单元格名、旋转角度、不旋转字符 |
| `ViewConfig.txt` | `List<CameraViewConfig>` | 每视图的相机序号、拍照标识、显示模式（全部/只NG）、保存模式/类型、回看模式 |
| `CleanConfig.txt` | `CleanConfig` | 清理启用、路径、扫描间隔（小时）、保留天数、磁盘剩余空间阈值（GB）、删除间隔 |

默认系统配置：

```json
{ "AlwaysTop": false, "IsSelectScreen": false, "ScreenIndex": 0,
  "Language": 0, "Theme": "light", "ViewCount": 2, "ReviewMaxCount": 20,
  "IsCloseFirewall": false, "ProcessingName": "CameraHelperJimJack" }
```

其他默认值：图像保存路径 `D:\Images\SourceImages` 与 `D:\Images\ResultImages`（无 D 盘回退 C 盘）；清理策略：扫描间隔 2 小时、保留 30 天、剩余空间低于 5GB 触发删除、删除节流 5ms。`app.config` 仅声明 .NET Framework 4.8 运行时。

## 目录结构

```
CameraHelper/               # 主程序源码（命名空间 CameraHelper，SDK 风格 net48 WinExe x64）
├── Program.cs              # 入口：全局异常处理 + FrmMain
├── FrmMain.cs              # 主窗体（AntdUI.Window：PageHeader/工具栏/状态栏、宫格布局、1s 定时器）
├── ThemeManager.cs         # 深浅主题管理（AntdUI Config.Mode + 原生控件调色板）
├── ProjectMgr.cs           # 全局单例管理器
├── SysConfig.cs            # 系统配置（含 Theme）
├── JsonHelper.cs           # JSON 读写封装
├── CvsInSightExt.cs        # Cognex 相机封装（连接/结果订阅/SendReady）
├── InSightConfig.cs        # 单台相机配置
├── NativeMode.cs           # Telnet 原生命令（SO1 上线 / RT 重启）
├── CameraView.cs           # 相机显示视图（主图 + 缩略图回看条，主题跟随）
├── CameraViewConfig.cs     # 视图配置
├── InSightRecord.cs        # 一条检测记录
├── IAction.cs              # 动作接口
├── ActionQueueWorker.cs    # 后台动作队列
├── CreateImageAction.cs    # 结果图绘制动作
├── SaveImageAction.cs      # 存图动作
├── FileTransformer.cs      # 目录监视（FileSystemWatcher）
├── TransformAction.cs      # 文件归档搬移动作
├── TransformConfig.cs      # 归档配置
├── PartItem.cs             # 归档路径片段项
├── FileIterator.cs         # 目录递归枚举
├── ImageCleaner.cs         # 磁盘定时清理
├── CleanConfig.cs          # 清理配置
├── ERotation.cs / EPartType.cs  # 枚举：旋转角度 / 路径片段类型
├── FrmSysSetting.cs        # 系统设置（AntdUI.Tabs：相机/窗口/清理/其他 4 页）
├── FrmViewSetting.cs       # 单视图设置（AntdUI 控件）
├── FrmOperation.cs         # 一键重启 / 一键备份
├── FrmGrid.cs              # 相机电子表格视图
├── FrmHMI.cs               # WebView2 打开相机网页（AntdUI.Window）
├── FrmHistory.cs           # 历史图片浏览
├── FrmCamCount.cs          # 相机数量对话框
├── LogMgr.cs / Logger.cs   # log4net 滚动文件日志
├── GraphicsHelper.cs       # Cognex 图形绘制（由 SDK 提供/引用）
├── NetFwTypeLib/           # Windows 防火墙 COM 互操作（项目内源码）
├── Properties/             # AssemblyInfo 与资源
└── CameraHelper.csproj     # 项目文件（AntdUI 2.4.10 NuGet + 本地 DLL 引用）
CameraHelper.Launcher/      # 启动器源码（net48 单文件 exe）
├── Program.cs              # 检测/安装 .NET8 运行时 → 检测 .NET 4.8 → 释放内嵌资源 → 启动主程序
├── LauncherForm.cs         # 安装与释放进度窗体
└── CameraHelper.Launcher.csproj  # 嵌入运行时安装包 + 通配符嵌入 publish/app/** 全部文件
assets/                     # 图标、软件截图（README 使用）、.NET8 桌面运行时安装包（56MB，启动器内嵌）
docs/                       # 项目文档
libs/                       # 本地依赖 DLL（Cognex/WebView2/log4net/Newtonsoft.Json + runtimes）
publish/app/                # 主程序构建产物暂存（打包脚本生成，启动器嵌入源）
dist/                       # 打包输出：CameraHelperJimJack_时间戳.exe 单文件启动器
Release/                    # 旧版编译产物（历史留存）
build-with-timestamp.sh     # 一键构建 + 打包脚本（Git Bash）
LICENSE                     # MIT 许可证
README.md
```

## 构建与打包

### 环境要求

- Windows x64，安装 .NET SDK 6+（用于编译 net48 目标，需装有 VS2022 或 net48 targeting pack）
- Git Bash（运行打包脚本）
- 目标机器需 .NET Framework 4.8 运行时（启动器会自动检测并提示）；若工控机缺少 .NET 8 Windows Desktop 运行时，启动器会自动静默安装，安装时弹出 UAC，请使用管理员账户授权

### 构建主程序

```bash
dotnet build CameraHelper/CameraHelper.csproj -c Release
```

产物位于 `CameraHelper/bin/Release/net48/`。

### 一键打包单文件启动器

```bash
./build-with-timestamp.sh
```

脚本执行 4 步：

1. `dotnet build` 编译主程序（Release）；
2. 清空并复制主程序产物到 `publish/app/`；
3. `dotnet build` 编译启动器——启动器把 `assets/windowsdesktop-runtime-8.0.29-win-x64.exe`（逻辑名 `DotNet8Runtime.exe`）与 `publish/app/**` 全部文件（含 `runtimes` 子目录）嵌入自身；
4. 复制启动器 exe 到 `dist/CameraHelperJimJack_<yyyyMMddHHmm>.exe`。

生成的 `dist/CameraHelperJimJack_时间戳.exe` 为**单文件分发版**，启动流程：

1. 执行 `dotnet --list-runtimes`（依次尝试 PATH、`C:\Program Files\dotnet`、`%LOCALAPPDATA%\Microsoft\dotnet`），输出不含 `Microsoft.WindowsDesktop.App 8.` 则判定缺失；
2. 缺失时把内嵌安装包释放到 `%TEMP%`，以 `/install /quiet /norestart` 静默安装（ShellExecute 触发 UAC，需管理员授权），最多等待 10 分钟；
3. 检测 .NET Framework 4.8（注册表 Release ≥ 528040）；
4. 把程序文件释放到 `%LOCALAPPDATA%\CameraHelperJimJack\App`（同名同大小文件跳过，支持增量更新），启动 `CameraHelperJimJack.exe`。

之后再次运行运行时检测会直接通过、无需重复安装或释放。

## 模块详解

### 相机通信层

- **`CvsInSightExt`**：内部持有 `Cognex.InSight.Web.CvsInSight`，连接时构造 `HmiSessionInfo`（Sheet "Inspection"、单元格区域 A0:Z599、启用队列结果、含 CustomView）。订阅 `ResultsChanged` 事件，在回调中跳过 Live/离线状态与重复 URL，异步取主图与图形，组装 `InSightRecord` 并入队 `CreateImageAction`；完成后调用 `SendReady()` 通知相机准备下一次触发，失败则断开重连。
- **`NativeMode`**：通过 Telnet（23 端口）发送原生命令控制相机：`SO1` 上线、`RT` 重启；执行前先 Ping 再连接并以 admin 登录。

### 图像处理与存图

- **`CreateImageAction`**：核心绘制动作。按配置从结果 cells 中读取 ShotKey、Result、自定义保存路径；以 ViewPort 一半尺寸计算缩放并居中；把原图与 Cognex 图形绘制到 Bitmap。启用旋转时先绘制不含文字的图形再 `RotateFlip`，随后单独绘制文字——以 `CharWithoutRotation`（默认 `$`）开头的文字做反向旋转补偿，保证旋转后文字依然水平。
- **`SaveImageAction`**：异步落盘原图/结果图，自动创建目录，按扩展名选择 Jpeg/Png/Bmp 格式。
- **`CameraView`**：记录到达时按 `SaveImageMode`（0不存/1全部/2只NG）与 `SaveImageType`（0原图+结果图/1只结果图）决定是否及如何入队存图；缩略图条缓存上限由 `ReviewMaxCount` 控制。

### 界面与主题

- **`FrmMain`**：继承 `AntdUI.Window`，顶部 `PageHeader`（图标 + 标题），工具栏提供「上线/离线」「系统设置」「一键操作」「主题切换」「语言切换」，底部状态栏显示时间与版本；中部 `TableLayoutPanel` 承载宫格视图。
- **`ThemeManager`**：`Apply(theme)` 切换 `AntdUI.Config.Mode` 并刷新原生控件调色板（Bg/Bg2/Bg3/Fg/FgDim/Border/BtnBorder），广播 `ThemeChanged` 事件；浅色为马卡龙奶黄配色，`StyleButton`/`StyleButtons` 递归为全部 AntdUI 按钮设置浅蓝描边（`DefaultBorderColor=#91CAFF`、`BorderWidth=1`），`StyleGrid()` 统一美化原生 DataGridView。

### 文件归档与清理

- **`FileTransformer` + `TransformAction`**：监视目录新文件（不递归），文件出现即入队；先等待目录文件总大小稳定（每 100ms 比较，确认写入完成），再按 `SourceSpliter`（默认 `,`）拆分文件名取第 `FolderIndex` 段，按 `FolderSpliter`（默认 `+`）拆分后依 `FolderPartList`（片段类型：取自文件名 / 当前时间格式化）拼装目标路径并覆盖搬移。
- **`ImageCleaner` + `FileIterator`**：按 `ScanInterval` 定时触发，在低优先级后台线程递归枚举文件并按创建时间排序，逐个按 `DeleteInterval` 节流删除——超过 `HoldDays` 天或磁盘剩余空间低于阈值即删，最后循环清理空文件夹。

### 系统控制

- **`ProjectMgr.OperateFirewall`**：经 NetFwTypeLib COM 接口开关域/专用/公用配置文件的防火墙。
- **`ProjectMgr.OperateDefender`**：通过注册表开关 Windows Defender。
- **单实例**：启动时以 `ProcessingName` 创建 Mutex 互斥。

### 缩略图回看详解

缩略图回看位于每个 `CameraView` 视图底部，整体界面布局如下（自上而下）：

```
┌────────────────────────────────────────────────────────┐
│ 工具栏 ToolStrip（高 38px，微软雅黑 10.5pt）              │
│ [放大/还原] [视图设置] [电子表格] [相机网页] [历史]      │
│              | 相机名：xxx | 拍照：ShotKey | Job：xxx    │
├────────────────────────────────────────────────────────┤
│                                                        │
│              picMain 主显示区（黑底，Zoom 缩放）          │
│                                                        │
├────────────────────────────────────────────────────────┤
│ pnlBottom 回看条（Dock=Bottom，高 20~220px 自适应）      │
│ [▲前一张] ┌──────────────────────────────┐             │
│ [▼后一张] │ pnlRecord 缩略图条             │             │
│ (左 35px) │ FlowLayoutPanel，              │             │
│           │ RightToLeft，不换行，          │             │
│           │ 新图从右侧追加、自动滚动到最新 │             │
│           │ ┌──┐┌──┐┌──┐  ←──── 时间顺序  │             │
│           │ └──┘└──┘└──┘                 │             │
│           └──────────────────────────────┘             │
└────────────────────────────────────────────────────────┘
```

界面构成与交互逻辑（`CameraView.cs`）：

- **缩略图条 `pnlRecord`**：`FlowLayoutPanel`，`FlowDirection = RightToLeft` 且不换行——最新的记录追加在最右侧，并自动 `ScrollControlIntoView` 滚到最新，视觉上形成「从右往左时间流」。每个缩略图是一个无文字的扁平 `Button`，宽高等于回看条高度（即正方形），缩略图本身来自 `ImageList imageListRecord`（尺寸随条高在 16~256px 之间按相机 ViewPort 纵横比自适应计算）。
- **OK/NG 颜色标识**：缩略图按钮边框 2px，`Result == 1`（OK）为**绿色**，其余（NG）为**红色**；被选中回看的缩略图边框加粗为 4px。
- **回看过滤 `ReviewMode`**：0=全部显示，1=只显示 OK，2=只显示 NG；不满足条件的缩略图 `Visible = false` 隐藏。
- **点击回看**：点击缩略图把 `picMain.Image` 切换为该记录的 `DumpImage`（`Clone()` 副本），并置 `IsEnableReview = true`——回看期间新记录到来**不再刷新主图**（`AddRecord` 中判断），避免打断检查；鼠标移出回看条后自动恢复实时刷新。
- **翻页按钮**：回看条左侧 35px 宽的 `TableLayoutPanel` 内置上/下两个按钮（`btnNext` / `btnLast`，`currentIndex` 加减并 `SelectBtn` 切换显示、自动滚动到对应缩略图）。
- **缓存淘汰**：`Records` 与缩略图控件数量均以 `SysConfig.ReviewMaxCount`（默认 20）为上限，超出时从头部（最旧）移除。
- **主图显示策略 `DisplayMode`**：0=新记录始终刷新主图，1=仅 NG 记录刷新主图；两者都在回看状态下让位于手动回看。
- **历史回看**：工具栏「历史」按钮取最近一条记录的存图目录，打开 `FrmHistory`（指定目录图片列表 + 筛选 + 打开目录），浏览磁盘上已保存的历史图像。

### 图像获取与 CogSocket 通信

CameraHelper 自身**不直接编写 Socket 代码**，图像获取全部经由 Cognex 官方 `Cognex.InSight.Web` SDK（`CvsInSight` 类）。该 SDK 内部使用其专有的 **CogSocket** 消息协议与相机通信（协议实现封装在 `Cognex.InSight.Web.dll` 内，包含 `CogSocketClientEndpoint`、`CogSocketGetMessage`、`CogSocketPostMessage`、`CogSocketPutMessage`、`CogSocketListenMessage`、`CogSocketUnlistenMessage`、`CogSocketResponseMessage` 等消息类型，并包含 WebSocket 传输支持）——即以 GET / POST / PUT / LISTEN / UNLISTEN 等请求-响应与订阅消息，在相机 HTTP 服务（默认 80 端口）上交换电子表格单元格数据、图像与图形。

应用层的完整图像获取流程（`CvsInSightExt.cs`）：

1. **建立会话**：`Connect()` 携带 `HmiSessionInfo` 连接 `地址:端口`：
   - `SheetName = "Inspection"`（订阅检测页）；
   - `CellNames = ["A0:Z599"]`（订阅整个结果单元格区，Result / ShotKey / 自定义路径等都在此读取）；
   - `EnableQueuedResults = true`（相机端结果排队，配合 SendReady 握手防丢帧）；
   - `IncludeCustomView = true`（结果含 CustomView）。
2. **结果订阅**：SDK 通过 CogSocket 的 LISTEN 机制向相机注册结果变更推送（内部长连接/WebSocket）；相机每完成一次检测触发即产生 `ResultsChanged` 事件。
3. **事件处理与去重**：`_inSight_ResultsChanged` 中先过滤——Live 模式或相机离线直接跳过；用 `GetMainImageUrl()` 与上一次 URL 比对去重，防止同一结果重复处理。
4. **拉取图像数据**（CogSocket GET 请求）：
   - `GetGraphicsAsync()` 异步获取本次结果的图形集合（`CvsCogShape[]`，检测框、文字等叠加图形）；
   - `GetMainImage()` 异步获取相机主图像（原图 `SourceImage`）。
5. **入队绘制**：把原图、图形、结果 cells 快照（`Results.DeepClone()`）、ViewPort、图像偏移、坐标系标记封装为 `CreateImageAction` 进入 `ActionQueueWorker`，由后台线程完成叠加绘制，UI 全程无阻塞。
6. **SendReady 握手**：finally 中调用 `InSight.SendReady()`（经 CogSocket POST 通知相机“本端已就绪”），相机收到后才释放队列中的下一帧结果——实现应用侧消费速率与相机触发速率的背压同步；SendReady 失败则断开连接，交由主窗体定时器重连。

简言之：**控制通道**为 CogSocket 请求-响应（连接、订阅、SendReady、上线/离线），**事件通道**为相机主动推送结果变更，**图像通道**为应用按事件去重后通过 SDK 异步拉取（GetMainImage / GetGraphicsAsync），三者协作保证多相机高频触发下不丢帧、不重复、不卡 UI。

## 第三方依赖

| 依赖 | 来源 | 用途 |
|---|---|---|
| AntdUI 2.4.10 | NuGet | Ant Design 风格 WinForms 控件库（Window/PageHeader/Button/Select/Input/Tabs 等），支撑深浅主题 |
| System.Resources.Extensions 4.7.1 | NuGet | net48 SDK 风格项目反序列化 resx 非字符串资源（图标/图片）所需；其生成的 .resources 头引用 4.0.0.0 而包内运行时 DLL 为 4.0.1.0，已在 app.config 配置 bindingRedirect 到 4.0.1.0 |
| Cognex.InSight.Web / .Web.Controls | 本地 DLL（libs/） | Cognex In-Sight 相机的 .NET Web 远程 SDK（连接、结果订阅、图像/图形获取、电子表格控件） |
| Newtonsoft.Json | 本地 DLL（libs/） | JSON 配置与结果 cells 解析 |
| Microsoft.Web.WebView2（Core/WinForms） | 本地 DLL（libs/，含 runtimes） | FrmHMI 内嵌浏览器 |
| log4net | 本地 DLL（libs/） | 滚动文件日志（Composite 滚动，50MB，文件名 `Systemyyyy-MM-dd-HH.log`） |
| NetFwTypeLib | 项目内源码 | Windows 防火墙 COM 互操作 |

目标框架 **.NET Framework 4.8，x64，WinExe**；AntdUI 与 System.Resources.Extensions 来自 NuGet，其余 Cognex/WebView2/log4net/Newtonsoft.Json 以本地 DLL 引用（HintPath 指向 `libs/`）。

## 许可证

本项目基于 [MIT License](LICENSE) 开源。

---

<p align="center">
<a href="#中文"><b>中文</b></a> | <a href="#english">English</a>
</p>

---

<a id="english"></a>

# CameraHelper — Cognex In-Sight Camera Inspection Display Assistant

CameraHelper is a desktop application built on **.NET Framework 4.8 / WinForms + AntdUI (x64)** for **centrally connecting to, displaying in real time, and automatically saving inspection result images from multiple Cognex In-Sight industrial cameras**. It supports up to 16 camera views in an automatic grid layout, image rotation with horizontal-text preservation, OK/NG thumbnail review, automatic image saving and disk cleanup, one-click camera restart/backup, light/dark themes, and bilingual Chinese/English UI.

The program is distributed as a **Launcher + main application**: the single file `CameraHelperJimJack_<timestamp>.exe` in `dist/` embeds the **.NET 8 Windows Desktop runtime installer** together with the whole application and its dependencies. At startup, the launcher first checks whether the .NET 8 desktop runtime is installed on the industrial PC and silently installs it if missing (administrator authorization required); after confirming .NET Framework 4.8, it extracts the application files to `%LOCALAPPDATA%\CameraHelperJimJack\App` and starts the main program — preventing failures caused by missing runtimes on industrial PCs.

## Contents

- [Screenshots](#screenshots)
- [Features](#features)
- [Architecture](#architecture)
- [Core Data Flow](#core-data-flow)
- [Configuration](#configuration)
- [Directory Layout](#directory-layout)
- [Build & Packaging](#build--packaging)
- [Module Details](#module-details)
- [Thumbnail Review in Detail](#thumbnail-review-in-detail)
- [Image Acquisition and the CogSocket Protocol](#image-acquisition-and-the-cogsocket-protocol)
- [Third-Party Dependencies](#third-party-dependencies)
- [License](#license)

## Screenshots

| Main window (grid layout + dark theme) | Enlarged single view (rounded display) |
|---|---|
| ![Main window](assets/screenshot_main.png) | ![Rounded view](assets/screenshot_round.png) |

| Application icon | Icon preview |
|---|---|
| ![Icon](assets/icon.ico) | ![Icon preview](assets/icon_preview.jpg) |

## Features

1. **Centralized multi-camera display** — up to 16 views, automatically arranged into a 1/2/4/6/9/12/16-cell grid according to the view count; click a cell to enlarge it to a single view.
2. **Modern AntdUI interface** — the main window and all settings/dialog forms use AntdUI controls (PageHeader / Button / Select / Input / Tabs, etc.). The light theme uses a soft **macaron light-yellow** palette, and all buttons get a unified **light-blue border** (#91CAFF); one-click dark/light theme switching is applied globally.
3. **Inspection result display** — Cognex graphics (CvsCogShape) are overlaid onto the original camera image in real time; supports 90°/180°/270° rotation, and text starting with a designated character (`$` by default) stays horizontal after rotation.
4. **Result judgment and routing** — reads Result (`1` = OK, otherwise NG) and ShotKey (distinguishing multiple shots from the same camera) from camera spreadsheet cells, and routes each record to the corresponding view by "camera index + shot key".
5. **Thumbnail review** — each record keeps a thumbnail (configurable cache limit, 20 by default), with green/red borders for OK/NG; supports OK-only / NG-only filtering and paging back and forth; a separate history image browser is also included.
6. **Automatic image saving** — saved in a hierarchical structure `yyyy_MM\dd\CameraName\JobName\OK|NG\`; supports NG-only saving, result-image-only saving, or reading a custom save path from a camera cell.
7. **Automatic reconnection** — a 1-second timer in the main window checks connection status and reconnects automatically after drops; supports soft online/offline switching (`SO1` / `SendReady` handshake).
8. **One-click operations** — restart all cameras with one click (Telnet `RT` command) and back up Job files of all online cameras with one click (organized by date/camera name/job name).
9. **Automatic image cleanup** — periodically deletes old images using dual thresholds of "retention days + free disk space", and can remove empty folders.
10. **File archiving (FileTransformer)** — watches a specified directory and moves files produced by third-party devices to the archive directory according to "filename split rules + time rules".
11. **System-level conveniences** — single-instance execution (Mutex), optional disabling of Windows Firewall / Defender, multi-monitor selection, always-on-top, and Chinese/English language switching.
12. **Camera maintenance entries** — an embedded spreadsheet view (jobs can be edited offline and saved) and a WebView2 window opening the camera HMI web page.

## Architecture

The overall architecture is a **singleton hub + action queue**, with `ProjectMgr.Inst` as the core hub. The UI layer is fully decoupled from background work: all time-consuming operations (image drawing, disk saving, file moving) are encapsulated as `IAction` and executed serially in a background queue without blocking the UI.

```
┌────────────────────── UI Layer (main thread) ──────────────┐
│ FrmMain (AntdUI.Window main form / grid layout / 1s timer) │
│ CameraView × N (image display, thumbnail review strip,     │
│                 image-saving configuration)                │
│ FrmSysSetting / FrmViewSetting / FrmOperation / FrmGrid    │
│ FrmHMI / FrmHistory / FrmCamCount                          │
│ ThemeManager (AntdUI dark/light themes + native-control    │
│                 color palette)                             │
└───────────────┬───────────────────────┬────────────────────┘
                │ read/write config     │ ProjectMgr.SetRecordChanged (Invoke)
┌───────────────▼───────────────┐       │
│ ProjectMgr (global singleton) │       │
│ Config load/save, camera &    │       │
│ view creation, Firewall &     │       │
│ Defender switches             │       │
└───────────────┬───────────────┘       │
                │ enqueue IAction       │
┌───────────────▼───────────────────────▼───────────────────┐
│ ActionQueueWorker (single background thread +             │
│   ConcurrentQueue<IAction>)                               │
│   CreateImageAction  result-image drawing (scale/rotate/  │
│                       graphics overlay)                   │
│   SaveImageAction    async original/result image saving   │
│   TransformAction    third-party file archival            │
└───────────────┬───────────────────────────────────────────┘
                │ Cognex In-Sight Web SDK (ResultsChanged push)
┌───────────────▼───────────────┐   ┌─────────────────────────┐
│ CvsInSightExt × N (camera     │   │ FileTransformer         │
│ wrapper): connection / result │   │ (FileSystemWatcher)     │
│ subscription / SendReady;     │   │ ImageCleaner (scheduled │
│ NativeMode (Telnet SO1/RT)    │   │ cleanup)                │
└───────────────────────────────┘   └─────────────────────────┘
```

Key design points:

- **Action queue** (`ActionQueueWorker`): a single background thread consumes a `ConcurrentQueue<IAction>` with Start/Pause/Continue/Close support and a queue limit of 1,000, ensuring serial consistency of drawing/saving/moving.
- **ProjectMgr singleton**: holds the system configuration, camera configuration list, camera instance list, view configuration/view list, action queue, cleaner, and singleton management of child forms.
- **ThemeManager**: a static class that centrally manages dark/light themes; AntdUI controls automatically follow `Config.Mode` (in light mode, `Config.Theme().Light(back, fore)` additionally applies the macaron-yellow window background), while native controls (DataGridView/ToolStrip/GroupBox, etc.) are colored manually via the `ThemeChanged` palette event. The light palette is creamy yellow (Bg `#FDF6D8`, Bg2 `#FFFBEA`, Bg3 `#F9EDBE`); all AntdUI buttons and the CameraView paging buttons get a unified light-blue border (BtnBorder `#91CAFF`, BorderWidth=1; the red/green online-status button is excluded); the chosen theme is persisted in `SysConfig.Theme`.
- **1-second timer** (FrmMain): refreshes the status-bar clock, handles always-on-top, calls `CheckCameraConnection()` for reconnection, and updates view information.

## Core Data Flow

```
Cognex In-Sight camera (HTTP/Web, ResultsChanged push)
  → CvsInSightExt receives: original image + graphics + result cells
      (Live/offline/duplicate-URL records skipped)
  → Enqueue(CreateImageAction)
      · Parse cells: ShotKey, Result (OK/NG), custom save path
      · Scale to half the ViewPort size and center (letterbox)
      · Draw original image + Cognex graphics; if rotation enabled:
        first draw graphics without text → RotateFlip(90/180/270)
        → then draw text separately (text prefixed with
          CharWithoutRotation is counter-rotated to compensate)
  → ProjectMgr.SetRecordChanged (route by CameraIndex + ShotKey)
  → CameraView (UI Invoke): main image refresh + thumbnail strip
  → Enqueue(SaveImageAction) according to config: async disk write
  → D:\Images\SourceImages | ResultImages\yyyy_MM\dd\Camera\Job\OK|NG\*.jpg
```

## Configuration

The configuration directory is `Config\` next to the exe, containing four JSON files (Newtonsoft.Json; saves go through a temporary file before overwriting to avoid corruption):

| File | Corresponding class | Contents |
|---|---|---|
| `SystemConfig.txt` | `SysConfig` | Always-on-top, multi-monitor selection, language (0 Chinese/1 English), theme (light/dark), view count, review cache limit, firewall-disable flag, process name |
| `CameraConfig.txt` | `List<InSightConfig>` | Per-camera IP/port/credentials, ShotKey/Result/path cell names, rotation angle, no-rotation character |
| `ViewConfig.txt` | `List<CameraViewConfig>` | Per-view camera index, shot key, display mode (all/NG-only), save mode/type, review mode |
| `CleanConfig.txt` | `CleanConfig` | Cleanup enable flag, path, scan interval (hours), retention days, free-disk-space threshold (GB), deletion interval |

Default system configuration:

```json
{ "AlwaysTop": false, "IsSelectScreen": false, "ScreenIndex": 0,
  "Language": 0, "Theme": "light", "ViewCount": 2, "ReviewMaxCount": 20,
  "IsCloseFirewall": false, "ProcessingName": "CameraHelperJimJack" }
```

Other defaults: image save paths `D:\Images\SourceImages` and `D:\Images\ResultImages` (falls back to the C: drive when no D: drive exists); cleanup policy: scan every 2 hours, retain 30 days, delete when free space drops below 5GB, deletion throttle 5ms. `app.config` only declares the .NET Framework 4.8 runtime.

## Directory Layout

```
CameraHelper/               # Main app source (namespace CameraHelper; SDK-style net48 WinExe x64)
├── Program.cs              # Entry point: global exception handling + FrmMain
├── FrmMain.cs              # Main form (AntdUI.Window: PageHeader/toolbar/status bar, grid, 1s timer)
├── ThemeManager.cs         # Dark/light theme management (AntdUI Config.Mode + native palette)
├── ProjectMgr.cs           # Global singleton manager
├── SysConfig.cs            # System configuration (includes Theme)
├── JsonHelper.cs           # JSON read/write wrapper
├── CvsInSightExt.cs        # Cognex camera wrapper (connect/result subscription/SendReady)
├── InSightConfig.cs        # Per-camera configuration
├── NativeMode.cs           # Telnet native commands (SO1 online / RT restart)
├── CameraView.cs           # Camera display view (main image + thumbnail strip, theme-aware)
├── CameraViewConfig.cs     # View configuration
├── InSightRecord.cs        # One inspection record
├── IAction.cs              # Action interface
├── ActionQueueWorker.cs    # Background action queue
├── CreateImageAction.cs    # Result-image drawing action
├── SaveImageAction.cs      # Image-saving action
├── FileTransformer.cs      # Directory watcher (FileSystemWatcher)
├── TransformAction.cs      # File archival move action
├── TransformConfig.cs      # Archival configuration
├── PartItem.cs             # Archive path fragment item
├── FileIterator.cs         # Recursive directory enumeration
├── ImageCleaner.cs         # Scheduled disk cleanup
├── CleanConfig.cs          # Cleanup configuration
├── ERotation.cs / EPartType.cs  # Enums: rotation angle / path fragment type
├── FrmSysSetting.cs        # System settings (AntdUI.Tabs: camera/view/cleanup/others, 4 pages)
├── FrmViewSetting.cs       # Per-view settings (AntdUI controls)
├── FrmOperation.cs         # One-click restart / one-click backup
├── FrmGrid.cs              # Camera spreadsheet view
├── FrmHMI.cs               # WebView2 camera web page (AntdUI.Window)
├── FrmHistory.cs           # History image browser
├── FrmCamCount.cs          # Camera count dialog
├── LogMgr.cs / Logger.cs   # log4net rolling file logging
├── GraphicsHelper.cs       # Cognex graphics drawing (provided/referenced by SDK)
├── NetFwTypeLib/           # Windows Firewall COM interop (in-project source)
├── Properties/             # AssemblyInfo and resources
└── CameraHelper.csproj     # Project file (AntdUI 2.4.10 NuGet + local DLL references)
CameraHelper.Launcher/      # Launcher source (net48 single-file exe)
├── Program.cs              # Check/install .NET8 runtime → check .NET 4.8 → extract embedded files → start app
├── LauncherForm.cs         # Install/extraction progress form
└── CameraHelper.Launcher.csproj  # Embeds runtime installer + wildcard-embeds publish/app/**
assets/                     # Icons, screenshots (used by README), .NET8 desktop runtime installer (56MB, embedded by launcher)
docs/                       # Project documentation
libs/                       # Local dependency DLLs (Cognex/WebView2/log4net/Newtonsoft.Json + runtimes)
publish/app/                # Staged main-app build output (generated by packaging script; launcher embedding source)
dist/                       # Packaging output: CameraHelperJimJack_<timestamp>.exe single-file launcher
Release/                    # Legacy build output (kept for history)
build-with-timestamp.sh     # One-click build + packaging script (Git Bash)
LICENSE                     # MIT License
README.md
```

## Build & Packaging

### Requirements

- Windows x64 with .NET SDK 6+ installed (for building the net48 target; VS2022 or the net48 targeting pack required)
- Git Bash (to run the packaging script)
- Target machines require the .NET Framework 4.8 runtime (the launcher detects and prompts for it); if an industrial PC lacks the .NET 8 Windows Desktop runtime, the launcher silently installs it — a UAC prompt will appear, so an administrator account is required to authorize it

### Build the main application

```bash
dotnet build CameraHelper/CameraHelper.csproj -c Release
```

Output is placed in `CameraHelper/bin/Release/net48/`.

### One-click packaging of the single-file launcher

```bash
./build-with-timestamp.sh
```

The script runs four steps:

1. `dotnet build` the main application (Release);
2. Clear and copy the main application output to `publish/app/`;
3. `dotnet build` the launcher — the launcher embeds `assets/windowsdesktop-runtime-8.0.29-win-x64.exe` (logical name `DotNet8Runtime.exe`) and all files in `publish/app/**` (including the `runtimes` subdirectory);
4. Copy the launcher exe to `dist/CameraHelperJimJack_<yyyyMMddHHmm>.exe`.

The resulting `dist/CameraHelperJimJack_<timestamp>.exe` is a **single-file distribution** with the following startup flow:

1. Runs `dotnet --list-runtimes` (trying PATH, then `C:\Program Files\dotnet`, then `%LOCALAPPDATA%\Microsoft\dotnet`); output without `Microsoft.WindowsDesktop.App 8.` means the runtime is missing;
2. If missing, extracts the embedded installer to `%TEMP%` and silently installs it with `/install /quiet /norestart` (ShellExecute triggers a UAC prompt; administrator authorization required), waiting up to 10 minutes;
3. Checks for .NET Framework 4.8 (registry Release ≥ 528040);
4. Extracts application files to `%LOCALAPPDATA%\CameraHelperJimJack\App` (same-name, same-size files are skipped, supporting incremental updates) and starts `CameraHelperJimJack.exe`.

On subsequent runs, the runtime checks pass immediately — no repeated installation or extraction is needed.

## Module Details

### Camera communication layer

- **`CvsInSightExt`**: wraps `Cognex.InSight.Web.CvsInSight`; on connect it builds an `HmiSessionInfo` (Sheet "Inspection", cell range A0:Z599, queued results enabled, CustomView included). It subscribes to the `ResultsChanged` event, skipping Live/offline states and duplicate URLs in the callback, asynchronously fetches the main image and graphics, assembles an `InSightRecord`, and enqueues a `CreateImageAction`; afterwards it calls `SendReady()` to tell the camera to prepare the next trigger, and disconnects for reconnection on failure.
- **`NativeMode`**: controls the camera through Telnet (port 23) native commands: `SO1` to go online and `RT` to restart; it pings before connecting and logs in as admin.

### Image processing and saving

- **`CreateImageAction`**: the core drawing action. Reads ShotKey, Result, and the custom save path from result cells; scales to half the ViewPort size and centers the image; draws the original image and Cognex graphics onto a Bitmap. When rotation is enabled, graphics without text are drawn first, then `RotateFlip` is applied, and text is drawn separately — text prefixed with `CharWithoutRotation` (`$` by default) is counter-rotated to compensate, so it remains horizontal after rotation.
- **`SaveImageAction`**: asynchronously writes original/result images to disk, creates directories automatically, and selects Jpeg/Png/Bmp format by extension.
- **`CameraView`**: when a record arrives, decides whether and how to enqueue saving according to `SaveImageMode` (0 none/1 all/2 NG only) and `SaveImageType` (0 original + result/1 result only); the thumbnail strip cache limit is controlled by `ReviewMaxCount`.

### UI and themes

- **`FrmMain`**: inherits `AntdUI.Window`, with a top `PageHeader` (icon + title); the toolbar provides "Online/Offline", "System Settings", "One-Click Operations", "Theme Switch", and "Language Switch"; the bottom status bar shows the clock and version; a central `TableLayoutPanel` hosts the grid views.
- **`ThemeManager`**: `Apply(theme)` switches `AntdUI.Config.Mode` and refreshes the native-control palette (Bg/Bg2/Bg3/Fg/FgDim/Border/BtnBorder), broadcasting the `ThemeChanged` event; light mode uses the macaron creamy-yellow palette, while `StyleButton`/`StyleButtons` recursively set the light-blue border on all AntdUI buttons (`DefaultBorderColor=#91CAFF`, `BorderWidth=1`) and `StyleGrid()` uniformly styles native DataGridViews.

### File archival and cleanup

- **`FileTransformer` + `TransformAction`**: watches for new files in a directory (non-recursive) and enqueues them as they appear; it first waits until the total file size is stable (compared every 100ms to confirm writing is complete), then splits the filename by `SourceSpliter` (`,` by default) to take the `FolderIndex` segment, splits it by `FolderSpliter` (`+` by default), assembles the target path according to `FolderPartList` (fragment types: from filename / formatted current time), and moves the file with overwrite.
- **`ImageCleaner` + `FileIterator`**: triggered on schedule by `ScanInterval`; a low-priority background thread recursively enumerates files, sorts them by creation time, and deletes them one by one throttled by `DeleteInterval` — files older than `HoldDays` or when free disk space is below the threshold are deleted; empty folders are cleaned up in a final pass.

### System control

- **`ProjectMgr.OperateFirewall`**: enables/disables the firewall for domain/private/public profiles through the NetFwTypeLib COM interface.
- **`ProjectMgr.OperateDefender`**: enables/disables Windows Defender through the registry.
- **Single instance**: a Mutex named by `ProcessingName` is created at startup.

### Thumbnail Review in Detail

Thumbnail review is located at the bottom of every `CameraView`, with the following layout (top to bottom):

```
┌────────────────────────────────────────────────────────┐
│ ToolStrip toolbar (38px high, Microsoft YaHei 10.5pt)   │
│ [Enlarge/Restore] [View Settings] [Spreadsheet]        │
│ [Camera Web] [History]                                 │
│        | Camera: xxx | Shot: ShotKey | Job: xxx        │
├────────────────────────────────────────────────────────┤
│                                                        │
│           picMain display area (black bg, Zoom)         │
│                                                        │
├────────────────────────────────────────────────────────┤
│ pnlBottom review strip (Dock=Bottom, 20~220px auto)    │
│ [▲Prev] ┌──────────────────────────────┐               │
│ [▼Next] │ pnlRecord thumbnail strip    │               │
│ (35px   │ FlowLayoutPanel,             │               │
│  left)  │ RightToLeft, no wrapping,    │               │
│         │ new items appended on the   │               │
│         │ right, auto-scrolled to the │               │
│         │ latest                       │               │
│         │ ┌──┐┌──┐┌──┐  ← time order   │               │
│         │ └──┘└──┘└──┘                 │               │
│         └──────────────────────────────┘               │
└────────────────────────────────────────────────────────┘
```

UI components and interaction logic (`CameraView.cs`):

- **Thumbnail strip `pnlRecord`**: a `FlowLayoutPanel` with `FlowDirection = RightToLeft` and no wrapping — the newest record is appended on the right and automatically scrolled into view via `ScrollControlIntoView`, visually forming a "right-to-left timeline". Each thumbnail is a flat textless `Button` whose width and height equal the strip height (i.e., square); thumbnails come from the `imageListRecord` ImageList (size adapted to the camera ViewPort aspect ratio between 16 and 256px according to strip height).
- **OK/NG color coding**: thumbnail buttons have 2px borders — green for `Result == 1` (OK) and red otherwise (NG); the selected thumbnail gets a thicker 4px border.
- **Review filter `ReviewMode`**: 0 = show all, 1 = show OK only, 2 = show NG only; non-matching thumbnails are hidden with `Visible = false`.
- **Click to review**: clicking a thumbnail switches `picMain.Image` to that record's `DumpImage` (a `Clone()` copy) and sets `IsEnableReview = true` — during review, newly arriving records **no longer refresh the main image** (checked in `AddRecord`), preventing interruption of inspection; real-time refreshing resumes automatically when the mouse leaves the review strip.
- **Paging buttons**: a 35px-wide `TableLayoutPanel` on the left of the strip holds up/down buttons (`btnNext` / `btnLast`), adjusting `currentIndex`, switching display via `SelectBtn`, and auto-scrolling to the corresponding thumbnail.
- **Cache eviction**: both `Records` and the thumbnail controls are capped at `SysConfig.ReviewMaxCount` (20 by default); the oldest entries are removed from the head when exceeded.
- **Main image display policy `DisplayMode`**: 0 = always refresh with new records, 1 = refresh with NG records only; both yield to manual review while reviewing.
- **History review**: the toolbar's "History" button takes the image directory of the most recent record and opens `FrmHistory` (image list for a specified directory + filtering + open directory) to browse saved history images on disk.

### Image Acquisition and the CogSocket Protocol

CameraHelper itself **does not write Socket code directly** — all image acquisition goes through Cognex's official `Cognex.InSight.Web` SDK (the `CvsInSight` class). Internally, the SDK uses its proprietary **CogSocket** message protocol to communicate with cameras (the protocol is encapsulated inside `Cognex.InSight.Web.dll`, with message types including `CogSocketClientEndpoint`, `CogSocketGetMessage`, `CogSocketPostMessage`, `CogSocketPutMessage`, `CogSocketListenMessage`, `CogSocketUnlistenMessage`, and `CogSocketResponseMessage`, plus WebSocket transport support) — i.e., request-response and subscription messages such as GET / POST / PUT / LISTEN / UNLISTEN exchange spreadsheet cell data, images, and graphics over the camera HTTP service (port 80 by default).

The complete application-level image acquisition flow (`CvsInSightExt.cs`):

1. **Establish a session**: `Connect()` connects to `address:port` with an `HmiSessionInfo`:
   - `SheetName = "Inspection"` (subscribe to the inspection sheet);
   - `CellNames = ["A0:Z599"]` (subscribe to the whole result-cell area where Result / ShotKey / custom paths are read);
   - `EnableQueuedResults = true` (camera-side result queuing, paired with the SendReady handshake to prevent frame loss);
   - `IncludeCustomView = true` (results include a CustomView).
2. **Result subscription**: the SDK registers result-change pushes with the camera through the CogSocket LISTEN mechanism (internal persistent connection/WebSocket); the camera raises a `ResultsChanged` event for every completed inspection trigger.
3. **Event handling and deduplication**: `_inSight_ResultsChanged` filters first — Live mode or offline cameras are skipped; `GetMainImageUrl()` is compared with the previous URL to prevent processing the same result twice.
4. **Fetch image data** (CogSocket GET requests):
   - `GetGraphicsAsync()` asynchronously fetches this result's graphics collection (`CvsCogShape[]` — inspection boxes, text, and other overlay graphics);
   - `GetMainImage()` asynchronously fetches the camera's main image (the original `SourceImage`).
5. **Enqueue for drawing**: the original image, graphics, result-cell snapshot (`Results.DeepClone()`), ViewPort, image offset, and coordinate-system marker are packaged as a `CreateImageAction` entering the `ActionQueueWorker`, where a background thread performs the overlay drawing — the UI is never blocked.
6. **SendReady handshake**: in the finally block, `InSight.SendReady()` is called (a CogSocket POST notifying the camera that "this side is ready"); the camera releases the next queued result only after receiving it — implementing back-pressure synchronization between the application consumption rate and the camera trigger rate; if SendReady fails, the connection is dropped and the main window's timer handles reconnection.

In short: the **control channel** consists of CogSocket request-responses (connect, subscribe, SendReady, online/offline), the **event channel** is the camera actively pushing result changes, and the **image channel** is the application asynchronously fetching via the SDK after event-based deduplication (GetMainImage / GetGraphicsAsync). Together they ensure no frame loss, no duplicates, and no UI freezes under high-frequency multi-camera triggers.

## Third-Party Dependencies

| Dependency | Source | Purpose |
|---|---|---|
| AntdUI 2.4.10 | NuGet | Ant Design-style WinForms control library (Window/PageHeader/Button/Select/Input/Tabs, etc.), powering dark/light themes |
| System.Resources.Extensions 4.7.1 | NuGet | Required by net48 SDK-style projects to deserialize non-string resx resources (icons/images); the generated .resources header references 4.0.0.0 while the in-package runtime DLL is 4.0.1.0 — a bindingRedirect to 4.0.1.0 is configured in app.config |
| Cognex.InSight.Web / .Web.Controls | Local DLL (libs/) | Cognex In-Sight camera .NET web remote SDK (connection, result subscription, image/graphics acquisition, spreadsheet controls) |
| Newtonsoft.Json | Local DLL (libs/) | JSON configuration and result-cell parsing |
| Microsoft.Web.WebView2 (Core/WinForms) | Local DLL (libs/, incl. runtimes) | Embedded browser for FrmHMI |
| log4net | Local DLL (libs/) | Rolling file logging (composite rolling, 50MB, filename `Systemyyyy-MM-dd-HH.log`) |
| NetFwTypeLib | In-project source | Windows Firewall COM interop |

Target framework: **.NET Framework 4.8, x64, WinExe**; AntdUI and System.Resources.Extensions come from NuGet, while Cognex/WebView2/log4net/Newtonsoft.Json are referenced as local DLLs (HintPath pointing to `libs/`).

## License

This project is open-sourced under the [MIT License](LICENSE).

---

<p align="center">
<a href="#中文"><b>中文</b></a> | <a href="#english">English</a>
</p>
