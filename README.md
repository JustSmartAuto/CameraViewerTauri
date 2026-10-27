# SvgOverlayPlugin for nomacs

把与当前图片**同名**的 SVG 直接合成到图片像素上，让 nomacs 把合成后的图当普通图片显示——
**缩放/平移/窗口大小变化时标注天然对位，永远不会拉扁或错位**。

支持：`bmp` / `png` / `avif` / `jpg` / `jpeg`；直接双击打开 `.svg` 文件时同样显示叠加效果。

例如目录中同时存在：

```
_NG_01246.bmp
_NG_01246.svg   ← 自动合成到 bmp 上显示
```

## 功能

- 打开 `foo.bmp` 时自动查找同目录的 `foo.svg` 并合成显示
- 直接打开 `foo.svg` 时也显示同样的叠加效果（SVG 自身为内容）
- SVG 直接画进图像像素里，**放大/缩小/平移时标注跟图像内容一起变**，永远等比对齐
- 叠加状态持久化到 `settings.ini`，下次打开 nomacs **只需要开一次插件菜单**（触发加载）就自动合成
- 图片或 SVG 文件变化（如相机检测程序持续覆盖写文件）时自动重载刷新
- 关闭叠加或退出 nomacs 不弹任何"保存修改"提示——合成对用户完全无感知
- 仅对上述扩展名生效，其他格式不叠加

## 在工控机上安装（最简单）

把 **`NomacsSvgOverlayPlugin-Setup.exe`**（约 50 KB，.NET Framework 4.8）拷贝到工控机，
双击运行即可，装完弹窗提示。安装器会：

1. 自动查找 nomacs 安装位置（注册表卸载项 → `Program Files` 等常见路径 → 安装器同目录）；
2. 把内嵌的 `SvgOverlayPlugin.dll` 安装到 `<nomacs>\bin\plugins\`（扁平目录，覆盖即升级）；
3. 无管理员权限/目录不可写时自动回退到 `%LOCALAPPDATA%\nomacs\bin\plugins\`。

> Windows 10 自带 .NET Framework 4.8，无需额外安装运行时。

安装后启动 nomacs：**编辑 → 设置 → 插件** 中启用 SvgOverlayPlugin，
然后通过插件菜单点击 **Toggle SVG Overlay** 开启叠加（只需开一次，状态保持）。

## 开发者：构建

### 环境要求（Windows）

| 依赖 | 版本 | 说明 |
|---|---|---|
| zig | 0.16 | 通过 uv 安装：`uv tool install ziglang`，命令为 `uvx --from ziglang python -m ziglang c++ ...` |
| Qt | 6.9.3 win64_msvc2022_64 | 通过 aqtinstall 安装（见下）；须与目标 nomacs 的 Qt 大版本一致（本机 nomacs 为 Qt 6.9.3 MSVC 构建） |
| nomacs 头文件 | **3.22.1（与目标 nomacs 版本一致！）** | 已在 `third_party/nomacs/`。⚠️ 必须用目标 nomacs 对应 tag 的头文件，master 分支的接口签名不同会导致虚表错位闪退（见"排障经验"） |
| MSVC 工具 | VS2022 | ① 从 nomacsCore.dll 生成导入库（dumpbin/lib，build-zig.ps1 自动调用）② 打包一键安装 exe 时的 MSBuild |

> Qt 6.9 中 QtSvg 已并入 qtbase 包，但必须用 `--archives qtsvg` 单独安装，否则没有 `Qt6Svg.lib`。

### 编译（zig c++，一条命令）

```powershell
# 首次准备：
uv tool install aqtinstall
aqt install-qt windows desktop 6.9.3 win64_msvc2022_64 --archives qtbase qtsvg icu --outputdir D:\Qt

# 编译（默认 Qt 在 D:\Qt\6.9.3\msvc2022_64）：
powershell -ExecutionPolicy Bypass -File build-zig.ps1
# 如 Qt 在其他位置：
.\build-zig.ps1 -QtDir "C:\Qt\6.9.3\msvc2022_64"
```

`build-zig.ps1` 流程：

1. 从已安装的 `nomacsCore.dll` 生成导入库 `build\nomacsCore.lib`
   （dumpbin /exports → .def → lib /def，经 vswhere 找 MSVC 工具；dll 更新时自动重新生成）；
2. 用 Qt 的 `moc.exe` 处理 `src\SvgOverlayPlugin.h`（Q_OBJECT / Q_PLUGIN_METADATA）；
3. 调用 `zig c++ -target x86_64-windows-msvc -O2 -shared -nostdlib++ -DQT_NO_DEBUG ...`
   链接 `Qt6Widgets/Qt6Svg/Qt6Gui/Qt6Core.lib` + `nomacsCore.lib`；
4. 产出 `build\SvgOverlayPlugin.dll`。

关键编译参数说明（踩坑记录）：

- `-target x86_64-windows-msvc`：zig 经 vswhere 自动发现本机 VS2022，用其头文件/
  库 + lld 链接，产物与 nomacs 的 MSVC/Qt6 构建 ABI 完全兼容；
- `-nostdlib++`：禁止 zig 编译自带的 libcxxabi（否则会与 MSVC 的
  `vcruntime_typeinfo.h` 冲突）；
- `-DQT_NO_DEBUG`：Qt6 不再根据 NDEBUG 自动关闭 QT_DEBUG，不显式定义会被
  QPluginLoader 拒绝加载（"Cannot mix debug and release libraries"）；
- 库直接传 `.lib` 全路径（zig 的 `-L`/`-l` 在 Windows 下解析不可靠）。

### 打包一键安装 exe

```powershell
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

自动把 `build\SvgOverlayPlugin.dll` 拷入 `installer\` 作为内嵌资源，用 vswhere 找到
的 MSBuild 编译 .NET 4.8 工程，产出根目录 `NomacsSvgOverlayPlugin-Setup.exe`。**升级插件 = 重跑 build-zig.ps1 + build-installer.ps1。**

### 安装 dll 到本机 nomacs（开发者调试用）

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
# 或指定：
.\install.ps1 -PluginPath .\build\SvgOverlayPlugin.dll -NomacsDir "C:\Program Files\nomacs"
```

### 方式二：CMake（可选）

```powershell
cmake -B build-cmake -DNOMACS_ROOT=C:\path\to\nomacs
cmake --build build-cmake --config Release
```

## 验证

- `build\test_load.exe`（zig 编译的小工具）用 `QPluginLoader` 实测加载：
  IID `com.nomacs.ImageLounge.DkPluginInterface/3.6`，实例 `nso::SvgOverlayPlugin`；
- 依赖检查（dumpbin /dependents）：仅依赖 nomacs 自带的
  `Qt6Widgets/Qt6Svg/Qt6Gui/Qt6Core.dll`、`nomacsCore.dll`、`VCRUNTIME140.dll` 和系统 DLL，无需附带 Qt 运行库；
- GUI 冒烟：nomacs 打开样例图正常，插件出现在插件列表；开启 Toggle SVG Overlay 后
  红绿标注文字正确叠加在图像显示区域内，缩放/平移时跟随。

## 使用

1. 首次：启动 nomacs → **编辑 → 设置 → 插件**（或 面板 → 插件管理），勾选启用 **SvgOverlayPlugin**（只需一次）。
2. 通过 **插件 → SVG Overlay → Toggle SVG Overlay** 开启/关闭合成。
3. 打开任一 `foo.bmp/png/avif/jpg/jpeg`，若同目录存在 `foo.svg` 即自动合成显示；直接打开 `foo.svg` 效果相同。

### 状态持久化

叠加状态写在 `%APPDATA%\nomacs\Image Lounge\settings.ini`：

```ini
[SvgOverlayPlugin]
active=true
```

下次启动 nomacs 后 **只要打开一次"插件"菜单**（触发插件 DLL 懒加载），插件会自动应用上次的状态。
（nomacs 架构上插件只在首次打开插件菜单时才加载，所以无法做到"启动即自动合成"——
这是 nomacs 本身的限制，不是插件缺陷。）

### 退出时无保存提示

合成是视觉增强，nomacs 不会把它当成"图片被修改"来弹"保存修改"对话框。插件内部在每次
`setImage()` 合成/还原后调 `DkImageContainerT::setEdited(false)` +
`DkBasicLoader::pruneEditHistory()`，清脏标和编辑历史，对用户完全无感知。

## 实现说明

### 合成方案（overlay widget 已废弃）

插件开启时**把 SVG 直接画进图像像素**，然后用 `DkImageContainer::setImage(composited, "SVG Overlay")`
替换 nomacs 正在显示的图像。之后 nomacs 自己负责缩放/平移/宽高比保持——标注跟着图像像素走，
**永远不会拉扁或错位**。关闭叠加时调 `setImage(mOriginalImage, "")` 还原原图。

> 早期版本用过透明子窗口 + `setWindow/setViewport`、再到 `getImageMatrix()` 世界变换，
> 都在非默认缩放级别下出现比例或对位偏差。合成方案一换就彻底消除。

### 路径获取

nomacs 3.22 窗口标题只有文件名（无路径），不能从标题解析。
插件通过链接 `nomacsCore.lib`（导入库）直接调 `DkViewPort::imageContainer()->filePath()`
获取当前文件全路径。

### 退出不弹保存提示

`setImage()` 每次都会把图像标为 edited（dirty）。插件在每次 `setImage()` 后立即调：

```cpp
mCurrentImgC->setEdited(false);           // 清 dirty 标志
mCurrentImgC->getLoader()->pruneEditHistory();  // 删我们的 setImage 记录
```

合成/还原/重合成等所有 setImage 调用点都跟这两行，彻底无感知。

### 状态持久化

- `toggleOverlay()` 时写入 `QSettings(settings.ini 路径, IniFormat)`
- 构造函数时读回；若上次是 active，启动 **300 ms 轮询定时器** 等待 main window → viewport → imageContainer 全部就绪后自动合成
- 定时器会一直重试直到合成成功（构造函数调用时图像可能还在加载中）

### 文件变化检测

激活期间 400 ms 轮询：检测到用户翻到新图或同名 SVG 被覆盖写后，自动还原原图 → 重新合成。

### ABI bridge（nomacsCore.dll 最小化声明）

nomacs 不安装 DkViewPort.h / DkBasicLoader.h，插件内按导出符号做最小化重声明：

```cpp
class DkViewPort {
public:
    __declspec(dllimport) QSharedPointer<DkImageContainerT> imageContainer() const;
};
class DkBasicLoader {
public:
    __declspec(dllimport) void pruneEditHistory();
    __declspec(dllimport) bool  isDirty() const;
};
```

签名正确性在链接时由 `build\nomacsCore.lib`（从安装的 dll 自动生成）保证。
nomacs 版本升级后重跑 build-zig.ps1 会自动重建导入库。

### 已知限制

- **插件懒加载**：nomacs 只在首次打开插件菜单时才加载插件 DLL，
  持久化的"自动应用"必须在用户打开过一次插件菜单后才会发生。
- Qt SVG（Tiny profile）不支持 `dominant-baseline` 等属性；
  若 SVG 文字用 `y=0 + dominant-baseline="hanging"` 定位，会画到可视区外。
  SVG 源文件应使用 alphabetic 基线坐标（如 `y=20`）。

## 更新 nomacs 头文件

`third_party/nomacs/` 只含插件编译必需的少量头文件（DkPluginInterface.h 及其传递依赖）。
**必须与目标 nomacs 的 git tag 对应**（当前为 3.22.1），不能用 master。
GitHub 直连超时的情况下用 jsDelivr 拉取：

```bash
BASE="https://fastly.jsdelivr.net/gh/nomacs/nomacs@3.22.1/ImageLounge/src"
curl -fsSL -o DkCore/DkPluginInterface.h "$BASE/DkCore/DkPluginInterface.h"
# 再按其 #include "..." 逐个拉取 DkCore/DkGui 下的依赖；
# nmc_config.h 是 CMake 生成的，插件编译用仓库自带的 stub 即可。
```

## 常见问题

- **加载报 "Cannot mix debug and release libraries"**：编译时缺 `-DQT_NO_DEBUG`
  （build-zig.ps1 已内置）。
- **nomacs 里看不到插件**：确认 dll 在 `<nomacs>\bin\plugins\`（顶层，不能在子目录）
  且重启过 nomacs；并用 插件 → 插件管理器 查看是否启用。
- **换了 Qt 大版本的 nomacs**：需用对应大版本的 Qt 重新编译
  （Qt6 内 minor 版本兼容，例如用 6.9.3 编译的插件可在任意 Qt 6.9.x 运行）。
- **换了 nomacs 版本**：需确认 `DkPluginInterface` IID 与接口签名是否变化
  （见下节"排障经验"），并重跑 build-zig.ps1（nomacsCore.dll 更新后导入库会自动重建）。
- **工控机无法联网**：NomacsSvgOverlayPlugin-Setup.exe 完全离线运行，无需网络。
- **退出 nomacs 弹"是否保存修改"**：旧版本的 bug，已在 commit ba95df4 修复。
  原因是 `DkImageContainer::setImage()` 每次都会把 dirty 标置 true，必须在每次
  setImage 后跟 `setEdited(false)` + `pruneEditHistory()`。
- **叠加状态下次启动没自动应用**：正常——nomacs 插件是懒加载架构，
  必须先打开一次"插件"菜单才会加载 DLL 并读取持久化设置（见"状态持久化"小节）。

## 排障经验（nomacs 插件开发要点）

本次开发在 nomacs 3.22.1 上踩过的坑，按症状索引：

### 插件管理器里看不到插件

1. **IID 必须匹配目标版本**：`Q_PLUGIN_METADATA(IID "...")` 与 `Q_DECLARE_INTERFACE`
   中的版本号要与已装 nomacs 一致。3.22.1 是 `com.nomacs.ImageLounge.DkPluginInterface/3.6`，
   master 分支已是 `/3.7`——直接用 master 头文件会导致 `qobject_cast` 失败、插件被跳过。
2. **插件 json 不要嵌套 `MetaData`**：`Q_PLUGIN_METADATA(FILE ...)` 的 json 内容会被
   moc 自动包进 `MetaData` 键；若 json 里再写一层 `"MetaData": {...}` 就变成双重嵌套，
   `DkPluginContainer::loadMetaData()` 解析不到 `PluginName`（日志有 `unknown key "IID"` 警告）。
   json 只写扁平字段（PluginName/Version/...）即可。
3. **插件目录是扁平的**：nomacs 3.22 扫描 `<appdir>/plugins/*.dll`（即 `bin\plugins`），
   dll 必须直接放在该目录顶层，放子目录不会被发现。

### 点击"插件"菜单 nomacs 闪退

接口头文件版本与已装二进制不一致导致的 **C++ ABI 虚表错位**：master 分支
`createActions(const QString&, QWidget*)` 是 2 参，3.22.1 是 `createActions(QWidget*)` 1 参，
虚表布局不同，nomacs 调 `plugin()->createActions(mainWindow)` 时跳到错误地址。
**对策：第三方头文件一律用目标 nomacs 对应 git tag 的版本**（raw.githubusercontent.com/
nomacs/nomacs/3.22.1/...；PowerShell 5.1 直连需先 `[Net.ServicePointManager]::SecurityProtocol = Tls12`）。
另外 nomacs 建插件子菜单用的是 `pluginActions()` 的返回值（不是 `createActions` 的返回值），
两者都要 override。

### 如何观察 nomacs 内部日志

```powershell
$env:QT_FORCE_STDERR_LOGGING = "1"
Start-Process nomacs.exe -RedirectStandardError nomacs_err.log
```

插件加载（`loadPlugins()`）是**懒加载**——首次打开"插件"菜单时才触发，之前日志里
看不到插件是正常的。插件名、加载耗时、`Invalid:` 原因都会打在 stderr。

### overlay widget 方案在缩放后标注拉扁/错位

**症状**：用透明子窗口 + `getImageViewRect()`/`getImageMatrix()` 叠加 SVG，
在非 100% 缩放或平移后，标注比例不对或对位偏移。

**根因**：nomacs 的视口变换不仅是缩放矩阵，还带 aspect ratio 处理、scroll area margin、
可能的 transform stack（如 rotate、flip）。透明子窗口在"世界空间"跟 `getImageViewRect()`
（像素空间）之间做 `setWindow/setViewport` 映射时，漏了其中某一级变换。

**解决**：彻底放弃 overlay widget → **把 SVG 直接合成到图像像素**。
`setImage(composited, ...)` 之后 nomacs 把合成图当普通图片，所有缩放/平移/窗口大小变化
都由 nomacs 原生 viewport 处理——标注跟着像素走，永远等比对齐。

### 调试技巧

- `dumpbin /exports nomacsCore.dll` 确认要调用的 nomacsCore 符号是否存在、
  由哪个类声明（决定最小化重声明怎么写）；
- 从 dll 生成导入库：`dumpbin /exports` → 解析出 4 列的符号行 → 写 .def →
  `lib /def:x.def /machine:x64`（build-zig.ps1 已内置此流程）；
- zig 构建的**测试 exe** 加载 nomacs 的 Qt DLL 跑会崩（CRT 环境差异），
  这是测试环境问题不是插件问题——插件 dll 本身在 nomacs 进程内工作正常；
- UI 自动化验证：`shot.ps1`（AppActivate + SetForegroundWindow 置前、SendKeys 导航菜单、
  鼠标滚轮缩放、CopyFromScreen 截图）；多 nomacs 窗口并存时用 `-ProcessId` 指定，
  且要注意 `Get-Process` 会返回已退出进程的残留对象（用 `MainWindowHandle -ne 0` 过滤）。

## 文件结构

```
CMakeLists.txt            CMake 构建脚本（可选方式）
build-zig.ps1             zig c++ 构建脚本（推荐；自动生成 nomacsCore 导入库 + 复制到插件目录）
build-installer.ps1       打包一键安装 exe（嵌入最新 dll）
install.ps1               安装 dll 到系统 nomacs
shot.ps1                  UI 自动化验证工具（激活窗口/发键/滚轮/截图）
installer/                .NET 4.8 一键安装器源码（Program.cs + installer.csproj）
src/SvgOverlayPlugin.h    插件声明 + nomacsCore 最小化 ABI bridge 声明
src/SvgOverlayPlugin.cpp  合成逻辑实现（SVG 画进像素 → setImage → 清 dirty）
src/SvgOverlayPlugin.json Qt 插件元数据（扁平字段，勿嵌套 MetaData）
third_party/nomacs/       nomacs 3.22.1 插件头文件（jsDelivr 拉取 + nmc_config.h stub）
build/                    编译产物（SvgOverlayPlugin.dll、nomacsCore.lib）
NomacsSvgOverlayPlugin-Setup.exe 一键安装器（发布给工控机的即此文件）
```
