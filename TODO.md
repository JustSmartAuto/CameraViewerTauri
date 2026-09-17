1. ~~新增安全设置标签页，添加"一键关闭/开启所有防火墙"按钮(按钮文本根据防火墙状态变化), 弹窗申请UAC权限~~
2. 获取$A$0单元格图片和结果图svg(取图设置)(CogSocket协议)(添加取图函数到脚本引擎)
3. ~~点击关闭按钮最小化到托盘，右键托盘图标才能真正关闭软件~~
4. 工控机模拟(Hsl Communication)EIP(EtherNet/IP)的IS8900相机通信, 使得工控机也可以直接发送数据到基恩士PLC, 提供发送数据表格界面
5. ~~一键关闭/开启UAC弹窗(修改注册表)~~
6. ~~Cognex HMI网页英文替换为中文(网页语言跟随本软件语言)~~
7. ~~CogSocket模式单元格值设置(参考`D:\JustStupid\CameraHelper相机显示_2606120922\显示软件=20260321`)~~（完成 26.9.20：src/cogsocket_manager.js 纯前端 WebSocket 实现 hello→openSession(cellNames A0:Z599)→login[admin,,false]→resultChanged 监听并每帧回 ready、15s keepAlive，根路径自动兼容 cam0/hmi 与旧版 system；src/cogsocket_cells.js 单元格弹窗支持 EditInt/Float（min/max 约束）/String(maxLength)/CheckBox/Button 单元格的单行写入、批量 setCellValues、手动 JSON 写入、写后回读校验、离线编辑（关窗自动恢复在线）、手动触发与分级日志；每相机凭据经 update_camera_cogsocket_auth 明文持久化于 CameraConfig.json；SDK 用 Function 沙箱加载避免与 Monaco AMD loader 冲突；Node mock 相机 harness 验证会话时序/帧格式/错误 reject/旧版回退）
8. ~~jobx备份功能~~
9. ~~远程操控工控机 + 虚拟显示屏(默认复制显示屏而不是扩展显示屏)~~
10. ~~更新软件版本号~~
11. ~~OnTopReplica窗口镜像~~
12. GigE协议相机支持
13. ~~轻度视觉检测功能(需要在软件设置中启用(持久化配置), 然后才在主界面添加按钮及标签页)(左栏 ECMA SCRIPT (https://github.com/boa-dev/boa)代码编辑区, 中栏 自定义控件区(类似电子表格自定义可交互控件, 声明式数据驱动; 用例为ROI编辑器(EditRegion, EditCircle, EditPoint, EditPolygon, EditLine控件等), 右栏 原图+效果图显示区及脚本测试控制台和日志显示区)~~（完成 26.9.19：AppConfig.vision_inspection_enabled 开关持久化；vision.rs 用 boa_engine 0.22 执行 inspect(context)，image crate 计算各 ROI 灰度统计 mean/stdDev/min/max；脚本返回 {pass,message,overlays,metrics} 声明式叠加层；脚本工作线程 5s 超时）
14. ~~ftp存图功能 新增 自动递归创建文件夹(类似filezilla)~~
15. ~~设置页的标签栏改为两行以优化显示, "安全相关"放置到第二行标签栏，防止字符串意外换行;~~
16. ~~添加”显示设置“标签页(虚拟显示器Virtual VDD, 窗口镜像OnTopReplica, 窗口所属显示屏设置)~~
17. ~~添加”调试工具“标签页(远程操控工控机, 扫描网络设备)~~
18. ~~Advance IP Scanner功能复刻(使用NMap)~~
19. CogSocket模式显示相机胶片视图(前面几次拍照的图片)(单个产品，相机多次拍照)(参考`D:\JustStupid\CameraHelper相机显示_2606120922\显示软件=20260321`)
20. CogSocket模式”相机显示“支持嵌套子区域显示
21. CogSocket通信协议数据传输和相机控制(D:\JustStupid\CameraHelper相机显示_2606120922\CameraViewerTauri\CogSocket&WebApi.md)（传输层/HMI 会话地基已随 #7 于 26.9.20 落地：src/cogsocket_manager.js 的 hello/openSession/login/ready/keepAlive 与单元格 API；取图（#2）、胶片视图（#19）、嵌套子区域（#20）等待做）
22. ~~配置文件可选使用默认文件夹或者跟随exe文件（方便启动多个不同的实例）~~
23. ~~相机网格"锁定/解锁"按钮状态持久化到配置文件，重启后自动恢复~~
24. ~~修复点击设置按钮后弹窗"扫描局域网设备失败"（子网探测改为静默预填，修复中文系统 ipconfig IPv4 带 (首选) 后缀解析失败）~~
25. ~~三种连接相机的模式: http, cogsocket, gige(持久化到配置文件, 默认为http, 新建“相机设置”标签页的连接模式下拉框)~~（完成；CogSocket/GigE 协议实现见 TODO #2/#12/#21）
26. ~~软件启动时显示骨架图或者加载进度条(计时器显示启动时间ms), 防止长时间显示白屏后才显示主界面~~
27. 相机显示设置页添加网页自动刷新间隔设置（秒）
28. ~~“配置文件设置”移动到“软件设置”标签页, 也提供与主界面功能相同的语言切换、主题切换功能~~
29. ~~“设置界面”点击后立即显示骨架图和加载动画，防止点击按钮长时间后才显示界面~~
30. ~~图片清理标签页添加高级版开关, 功能复刻(D:\JustStupid\CameraHelper相机显示_2606120922\ImageCleanerAutoWeld), 使用(https://github.com/boa-dev/boa)和(monaco-editor).~~（完成；qjs_runtime 因 GitHub 不可达改用 boa_engine 0.22.0）
31. ~~修复点击设置按钮后，splashscreen显示全白的问题~~
32. ~~编写脚本编程指南PROGRAMING.md~~（完成 26.9.19：覆盖视觉检测 inspect(context) 的控件/统计/overlays 契约与图片清理高级版 evaluate(context) 的 11 字段 context/逐文件调用语义，含默认脚本、进阶示例与排错表）