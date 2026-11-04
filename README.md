# DataRouterPy — 文件转存器（文件中转）

监控指定目录中的新文件，按配置把**文件名拆分后重组**，自动移动到目标文件夹并重命名。
对标 CameraViewerDotnet / CameraHelperLens 的「文件转存」功能，界面采用 Windows 95 风格
（设计 token 来自 `02-windows-95.css`，logo `assets/router.png`）。

> 参考工程：`CameraCellMonitor`（工程结构 / uv / PyInstaller 打包方式）；
> 功能语义对标 `CameraViewerDotnet/TransformAction.cs`。

## 转存规则

以文件 `p1,100+200,abc_999.txt` 为例（内容分隔符 `,`、文件夹内容序号 `1`、文件名内容序号 `2`）：

1. 文件主名 `p1,100+200,abc_999` 按**内容分隔符**拆成内容段：`p1 / 100+200 / abc_999`
2. **文件夹内容** = 第 `1` 段 `100+200`，按**路径分隔符** `+` 拆成 `100 / 200`
3. **文件名内容** = 第 `2` 段 `abc_999`，按**文件名分隔符** `_` 拆成 `abc / 999`
4. 「文件夹」表中每行组成一级新目录：「类型=名称」时按行内的**原路径序号**取文件夹段；
   「类型=时间」时按**格式化**（兼容 `.NET` 风格如 `yyyy-MM-dd` 与 strftime 如 `%Y-%m-%d`）插入当前时间
5. 「文件名称」表同理组成新主名（保留原扩展名）
6. 转存到：`<路径>\<文件夹>\…\<新文件名>.<原扩展名>`，同名覆盖
7. 文件写入未完成（大小持续增长）时先等待，稳定后才转存；
   无法解析（段序号超出内容段数量）的文件跳过、留在原地轮询重试

## 界面（对标 文件转存界面.png）

- **名称**：多套转存配置，添加 / 删除（至少保留一套）
- **启用转存**：单套配置开关
- **路径**：监控目录；「浏览」选择、「打开」在资源管理器中打开（不存在则自动创建）
- **内容分隔符 / 文件夹内容序号 / 文件名内容序号**：如上转存规则
- **文件夹 / 文件名称分组框**：路径分隔符（文件夹内容段拆分符）/ 文件名分隔符；
  「添加 / 删除 / 清空」编辑组成段；双击类型行切换 **名称↔时间**；
  选中行后在下方编辑 **原路径序号**（名称类型）或 **格式化**（时间类型）
- **保存应用**：保存到 `TransformConfig.json` 并立即重建监控线程
- **转存日志**：底部实时显示每条 `源文件 → 目标文件`
- 帮助菜单：使用说明 / 关于（版本号）

## 开发 / 运行 / 打包

```bash
uv sync                                # 安装依赖（仅需 dev 组，运行无第三方依赖）
uv run data-router                     # 启动应用
uv run data-router --data-dir D:/x     # 指定数据目录
uv run data-router -v                  # 调试日志
uv run python build_exe.py             # PyInstaller 打包 dist/DataRouterPy_{yyyyMMddHHmmSS}.exe
```

- 默认数据目录：`D:/JustSmart/DataRouterPy`（D 盘不可用则回退 `~/AppData/DataRouterPy`）
- 配置文件：`<数据目录>/TransformConfig.json`（字段与 CameraViewerDotnet 兼容：Name/WatchPath/SourceSpliter/FolderIndex/FileNameIndex/FolderSpliter/FolderPartList/FileNameSpliter/FileNamePartList）

## 目录

| 路径 | 说明 |
|---|---|
| `assets/router.png` | 软件 logo |
| `src/datarouter/config.py` | 转存配置读写（TransformConfig / PartItem） |
| `src/datarouter/router.py` | 文件中转线程（扫描 → 稳定等待 → 拆分重组 → 移动） |
| `src/datarouter/theme.py` | Windows 95 主题（token 对自 02-windows-95.css） |
| `src/datarouter/ui.py` | tkinter 主界面 |
| `src/datarouter/app.py` | 入口 + 数据目录解析 |

## License

与 CameraViewerDotnet 同仓库策略，仅供内部使用。
