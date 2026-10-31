# CameraCellMonitor 相机数据监控器

基于 **西门子 S7 (snap7)** 的产线相机坐标监控工具：轮询 4 台相机 DB 块的 X0/Y0/X1/Y1 坐标，与参考坐标对比，超差即报警；检测到拍照触发后自动向对应 DB 块回写 OK/NG 结果。提供 **tkinter 桌面界面 + 内嵌 Web 页面** 双操作方式。

> 隶属于 [CameraViewerTauri](https://github.com/JustSmartAuto/CameraViewerTauri) 仓库的 `cell-monitor` 分支。

## 功能

- **4 相机监控**：左/右工位各 2 台相机（DB92/93/97/98），每相机 4 个坐标变量（X0/Y0/X1/Y1）
- **独立阈值**：每个变量可单独设置参考坐标与报警阈值
- **OK/NG 回写**：勾选"发送OK/NG信号"后，检测到拍照触发（DB 偏移 4 的上升沿）1 秒内向相机自身 DB 偏移 2 写入 `2=NG / 3=OK`
- **触发信号灯**：灰=无触发，黄=已接收待发送，绿=已发送，可确认极短触发脉冲是否被采集
- **4×4 趋势图**：虚线基线 = 参考坐标，红色段 = 超差
- **左右工位独立配方**：分别应用/保存，小数位数可切换
- **内嵌 Web 页面**（纯标准库 HTTP，无需 Flask）：`http://<本机IP>:10209/`，功能与桌面版一致，局域网内手机/平板均可操作

## 运行

依赖：Python 3.11+（需内置 `tomllib`）+ python-snap7

```bash
pip install python-snap7
python 相机数据监控器_web_2610171547.py
```

- 桌面窗口与 Web 服务（0.0.0.0:10209）同时启动
- PLC 地址等参数在脚本头部 `配置` 区修改（默认 PLC `192.168.1.70`，Rack/Slot = 0/0）

## 打包

```bash
pyinstaller -F -w --name 相机数据监控器 相机数据监控器.py
# snap7.dll 需与 exe 同目录，或 --add-binary 一并打包
```

## 目录

| 路径 | 说明 |
|---|---|
| `reference/相机数据监控器_web_2610171547.py` | 主程序源码（tkinter + Web） |
| `reference/Cognex In-Sight 3D 相机电子表格函数参考（整理版）.md` | Cognex In-Sight 3D 函数参考文档 |
| `reference/monitor.jpg` / `monitor.png` | 程序图标源图（retro 显示器） |
| `reference/app_icon.ico` | 应用图标（由 monitor.jpg 拉伸方形 + 16% 圆角生成，多尺寸 ICO） |

## License

[LICENSE](LICENSE) © JustSmartAuto
