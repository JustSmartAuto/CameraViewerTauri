# CameraViewerLens（相机看板）

基于 Cognex In-Sight 相机的多路 Web HMI 看板，附带「图片自动归档」和「磁盘定时清理」两个后台任务（原软件名 CameraHelper）。
当前可运行版本位于 `publish/`，为 .NET Framework 4.7.2 + WinForms + WebView2 的 x64 桌面程序；
源码版本已更新为 `26.11.4.8`（`publish/CameraHelper.exe` 为旧版 `1.25.0607.1745` 生产存档）。

> 说明：反编译还原后的源码统一整理在 `src/WebHMI/`，可直接阅读、修改；`src/CameraHelper.csproj` 依赖路径已改为指向 `publish/` 下的 DLL，可直接编译（见「构建与运行」）。

## 目录结构

```
CameraHelperLens/
├── src/                      # 源码工程（反编译源码唯一存放处，可直接编译）
│   ├── WebHMI/               # 反编译源码（命名空间 WebHMI）
│   ├── WebHMI.Properties/    # Resources / Settings
│   ├── Properties/           # AssemblyInfo.cs
│   ├── WebHMI.FormMain.resx / WebHMI.Properties.Resources.resx
│   ├── app.config / app.ico
│   └── CameraHelper.csproj   # 依赖引用 ../publish 下的 DLL
├── publish/                    # 现有可运行程序（原 Debug/，二进制 + 独立 git 仓库）
│   ├── CameraHelper.exe        # 主程序（旧版 1.25.0607.1745）
│   ├── Configs/                # 运行时配置（JSON）
│   ├── runtimes/win-x64/native/WebView2Loader.dll
│   ├── CameraHelper.csproj     # 旧工程文件（依赖绝对路径，已废弃，仅供存档）
│   ├── app/                    # 打包脚本生成的主程序收集目录（构建产物）
│   └── CameraHelper.exe.WebView2/  # WebView2 用户数据缓存（运行时生成）
├── launcher/                   # 单文件启动器（net48，内嵌主程序 zip，检测/静默安装 WebView2 运行时）
├── build-with-timestamp.sh     # 一键打包脚本：主程序 + 启动器 → dist/CameraViewerLens_<时间戳>.exe
├── dist/                       # 打包输出
├── reference/                  # 参考资料
│   ├── CogSocket&WebApi.md                          # Cognex CogSocket / HMI Web API
│   ├── Cognex In-Sight 3D 相机电子表格函数参考（整理版）.md
│   ├── 相机数据监控器_web_2610171547.py              # S7 + 相机 DB 监控（Web UI 方案参考）
│   └── retro-design-system-main/                    # 53 套 HTML/CSS 视觉主题
```

## 功能概述

| 模块 | 作用 | 入口 |
|---|---|---|
| 相机显示 | 每个相机一个 WebView2，按 IP 打开相机自带 Web HMI，可单画面放大 | `WebView.cs` |
| 主界面 | 自适应网格布局、相机数量/延时设置、状态栏时钟与版本号 | `FormMain.cs` / `FormSetting.cs` |
| 图片归档 | 监听目录 -> 按文件名规则重组 -> 移动到目标目录 | `FileTransformer.cs` / `TransformAction.cs` |
| 磁盘清理 | 定时扫描目录，按保留天数或剩余空间删除图片，并清理空目录 | `ImageCleaner.cs` |
| 配置管理 | 三个 JSON 的加载/保存，单例 `ProjectMgr` 统一调度 | `ProjectMgr.cs` / `JsonHelper.cs` |

## 运行流程

1. `Program.Main` -> `FormMain`
2. `FormMain_Load`：
   - `ProjectMgr.LoadConfig()` 读取 `Configs/*.json`
   - `CreateCameraWebView()` 按 `CameraConfig.Items` 创建 WebView
   - `LayoutDisplay()` 计算行列并挂载到 `TableLayoutPanel`
   - `CreateFileTransform()` 为每套归档规则启动 `FileSystemWatcher`
   - `Cleaner.StartClean()` 启动清理定时器
3. 窗口关闭时 `ProjectMgr.Close()`，停止所有监听与清理线程。

### 显示布局

按相机数量选行列（`FormMain.LayoutDisplay`）：

| 数量 | 行 × 列 |
|---|---|
| 1 | 1 × 1 |
| 2 | 1 × 2 |
| 3~4 | 2 × 2 |
| 5~6 | 2 × 3 |
| 7~9 | 3 × 3 |
| 10~12 | 4 × 3 |

每路右上角按钮可把该路放大为整屏，再次点击恢复网格。

### 相机连接

- 启动后按 `CameraConfig.Delay` 秒（≤0 时取 10 秒）延迟初始化 WebView2，再导航到 `http://{IP}`。
- 画面顶部地址栏可直接改 IP：输入符合 `a.b.c.d` 时立即保存配置并重新导航。
- 当前仅做整页导航，未使用 Cognex Web HMI 的查询参数（`view`、`page`、`fit`、`zoom`、`hideCookieWarning` 等），相关可选值见 `reference/CogSocket&WebApi.md` 第 2.2~2.3 节。

## 配置说明（Configs/）

所有配置均位于 `publish/Configs/`，程序启动时加载；修改后多数项即时保存。

### CameraConfig.json

| 字段 | 类型 | 说明 |
|---|---|---|
| `Count` | int | 相机路数 |
| `Delay` | int | 每路 WebView 初始化延迟（秒），≤0 时按 10 秒 |
| `Items[]` | array | 相机列表，元素含 `ID` 与 `IP` |

当前配置为 4 路：`192.168.10.159 / .156 / .157 / .158`，延时 10 秒。

> 注意：在设置界面修改「数量」会重建列表，**原有 IP 会被清空**，需要重新填写。

### CleanConfig.json

| 字段 | 类型 | 说明 |
|---|---|---|
| `IsEnable` | bool | 是否启用清理 |
| `FolderPath` | string | 待清理根目录 |
| `ScanInterval` | double | 扫描周期（**小时**），对应毫秒 = 值 × 3600000 |
| `IsCleanEmptyFolders` | bool | 是否清理空目录（当前实现未读取该开关） |
| `IsEnableHoldDays` / `HoldDays` | bool / int | 删除创建时间超过 N 天的文件 |
| `IsEnableRemainingSpace` / `RemainingSpace` | bool / double | 剩余空间低于 N GB 时继续删除（按创建时间从旧到新） |
| `DeleteInterval` | int | 每删除一个文件后的休眠毫秒数，避免占用 IO |

### TransformConfig.json

数组，每个元素是一套归档规则：

| 字段 | 说明 |
|---|---|
| `Name` | 规则名称（设置界面下拉项） |
| `WatchPath` | 监听目录（不递归） |
| `SourceSpliter` | 文件名主分隔符，代码只取**第一个字符** |
| `FolderIndex` | 用主分隔符切分后，取第几段作为**目录**信息源 |
| `FileNameIndex` | 取第几段作为**文件名**信息源 |
| `FolderSpliter` | 目录信息源的二级分隔符（取第一个字符） |
| `FolderPartList` | 目录拼接片段列表（`PartItem`） |
| `FileNameSpliter` | 文件名信息源的二级分隔符（取第一个字符） |
| `FileNamePartList` | 文件名拼接片段列表（`PartItem`） |

`PartItem`：

| 字段 | 说明 |
|---|---|
| `PartType` | `0` = Name（取源片段），`1` = DateTime（取当前 PC 时间） |
| `SourceIndex` | Name 时取二级切分后的第几段；**目录片段中 0 表示盘符**（自动补 `:\`） |
| `Format` | DateTime 时的 `DateTime.ToString` 格式，如 `yyyy-MM-dd` |

### 归档规则的实际拼接方式

以文件名 `a,b,c,d.ext`（已去掉扩展名）为例，`SourceSpliter=","`：

1. 主切分得到 `["a","b","c","d"]`
2. `FolderIndex=1` -> 目录源取 `"b"`，再按 `FolderSpliter`（如 `+`）切分，得到目录片段源
3. `FileNameIndex=2` -> 文件名源取 `"c"`，再按 `FileNameSpliter`（如 `_`）切分，得到文件名片段源
4. 按 `FolderPartList` 顺序拼目录：
   - Name：取目录片段源 `[SourceIndex]`；`SourceIndex=0` 输出 `片段:\`（盘符），其余输出 `片段\`
   - DateTime：输出 `DateTime.Now.ToString(Format)\`
5. 按 `FileNamePartList` 顺序拼文件名：每个片段后都追加 `FileNameSpliter`，最后去掉末尾分隔符并补回原扩展名
6. 目标目录不存在则创建；同名文件先删除；然后 `File.Move` 原文件

触发时机是 `FileSystemWatcher.Created`：每次有新文件就入队一次「扫描整个监听目录 + 等待文件大小稳定后搬运」，因此在批量落图时会重复入队。

## 构建与运行

环境要求：

- Windows x64
- .NET Framework 4.7.2 运行时
- Microsoft Edge WebView2 Runtime（系统级，Evergreen）
- 相机与工控机网络可达

编译：

```powershell
# 推荐：直接构建 src 工程（依赖相对路径指向 ../publish 下 DLL）
dotnet build src\CameraHelper.csproj -c Debug
msbuild src\CameraHelper.csproj /p:Configuration=Debug /p:Platform=x64

# 旧工程文件 publish\CameraHelper.csproj 依赖 W:\待合并\... 绝对路径，已废弃，不用于编译
```

工程文件 [src/CameraHelper.csproj](src/CameraHelper.csproj) 的关键点：

- SDK 为 `Microsoft.NET.Sdk.WindowsDesktop`，`TargetFramework=net472`，`PlatformTarget=x64`，`LangVersion=12.0`
- 依赖为本地 DLL（位于 `publish/`）：`Microsoft.Web.WebView2.WinForms`、`Microsoft.Web.WebView2.Core`、`Newtonsoft.Json`
- resx 中含二进制序列化的图片/图标，需 `GenerateResourceUsePreserializedResources=true` 与 NuGet 包 `System.Resources.Extensions`
- `WebView2Loader.dll` 位于 `publish\runtimes\win-x64\native\`，构建时自动复制到输出目录
- 无 `.sln`、无 `packages.config`（仅 `System.Resources.Extensions` 一个 NuGet 包），不依赖还原到特定机器

直接运行：双击 `publish\CameraHelper.exe`（首次运行会在同目录生成 `CameraHelper.exe.WebView2` 缓存）。

### 单文件打包（launcher/）

`build-with-timestamp.sh` 一键生成便携式单文件启动器（Git Bash 运行）：

```bash
./build-with-timestamp.sh   # 输出 dist/CameraViewerLens_<时间戳>.exe
```

流程与约定：

- **[1/4]** `dotnet build src/CameraHelper.csproj -c Release` 构建主程序（net472，Windows 自带框架，工控机无需装 .NET）。
- **[2/4]** 收集输出到 `publish/app/`（含 `Configs/`），压缩为 `launcher/app.zip`（内嵌用）。
- **[3/4]** MSBuild 编译 `launcher/CameraViewerLens.Launcher.csproj`（net48，内嵌 `app.zip`）。
- **[4/4]** 输出 `dist/CameraViewerLens_<时间戳>.exe`，单文件即拷即用。

启动器行为（`launcher/Program.cs`）：

1. 注册表检测 WebView2 Evergreen 运行时（HKLM/HKCU `...\EdgeUpdate\Clients\{F3017226-...}` 的 `pv`）；
   缺失则下载 Evergreen Bootstrapper（微软官方 CDN）并 `/install /quiet /norestart` 静默安装，下载失败时打开下载页并提示。
2. 释放内嵌 `app.zip` 到 `%LOCALAPPDATA%\CameraViewerLens\App`（同名文件被占用时先结束残留 CameraHelper 进程）。
3. 启动释放出的 `CameraHelper.exe` 后退出。

> 首次运行不联网时必须装好系统级 WebView2 Runtime，否则程序无法显示相机画面。

## 已知问题与改进点

| 位置 | 问题 |
|---|---|
| `ProjectMgr.CreateCameraConfig` | 调整相机数量会重建全部条目并清空已有 IP |
| `WebView.ConnectHMI` | 只访问 `http://IP`，未利用 Cognex HMI 的视图/缩放/免登录参数 |
| `ImageCleaner.CleanEmptyDirectory` | 未判断 `IsCleanEmptyFolders`，总是删除空目录 |
| `ImageCleaner` | 排序使用插入式 O(n²)；`Thread.Abort` 已过时；异常全部被空 `catch` 吞掉 |
| `TransformAction` | 分隔符为空、片段列表为空或索引越界时抛异常并被静默吞掉；先整目录扫描再逐个 Move，批量图片时效率低 |
| `FileTransformer` / `ActionQueueWorker` | 每个文件事件都入队一次全目录扫描；队列超过 1000 直接抛异常（在事件回调中抛出） |
| `JsonHelper` | 读取用 `Encoding.Default`，写入 UTF-8 无 BOM，含中文的配置存在乱码风险 |
| `FormMain` | 每秒 `TopMost=true/false` 强制置顶，属于取巧做法；关闭时未释放 WebView |
| `publish/.git` | 仓库约 96.7 MB，跟踪了 1196 个文件，绝大部分是 WebView2 缓存，应加入 `.gitignore` 并清理历史 |
| `publish/CameraHelper.csproj` | 旧工程文件，依赖绝对路径，已被 `src/CameraHelper.csproj` 取代，仅存档 |

## 参考资料（reference/）

- **CogSocket&WebApi.md**：In-Sight 相机三套接口——CogSocket（`ws://IP:端口/ws`，JSON-RPC 风格）、HMI 资源树（`cam0/hmi/...`，会话/结果/单元格/作业/设置）、Platform REST（设备管理）。含 HMI URL 参数、会话流程、`HmiResult` 与图像获取方式；文中示例以 Tauri + 前端直连 CogSocket 为主。
- **Cognex In-Sight 3D 相机电子表格函数参考（整理版）.md**：电子表格函数手册。
- **相机数据监控器_web_2610171547.py**：西门子 S7 读取 4 台相机 DB 块的 X0/Y0/X1/Y1，超差报警并回写 OK/NG，内置 `0.0.0.0:10209` HTTP 服务，页面通过 `/api/state` 轮询、`/api/*` 指令队列与 Tkinter 主线程交互。可作为「无框架轻量 Web UI」的实现参考。
- **retro-design-system-main/**：53 套独立 HTML/CSS 主题及 `tokens/*.css`，可用于新前端选型。

## 项目状态

- `publish/`：现有生产版本存档（原 `Debug/`，旧版 `1.25.0607.1745`），可直接运行；同时存放供 `src` 引用的依赖 DLL 与打包收集目录 `app/`。
- `src/`：反编译源码的唯一存放处 + 可编译工程，软件名 `CameraViewerLens`、版本 `26.11.4.8`；`dotnet build src\CameraHelper.csproj` 即可构建。
- `launcher/` + `build-with-timestamp.sh`：单文件便携式打包，产品为 `dist/CameraViewerLens_<时间戳>.exe`。
- 若计划迁移到 Web / Tauri 版本，建议复用 `reference/CogSocket&WebApi.md` 的接口约定，并把上表的已知问题作为迁移验收清单。
