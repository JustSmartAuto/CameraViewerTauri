# In-Sight Vision Suite — Audit Logging / 审计日志

The Audit Logging utility allows you to track jobs and system level changes, enabling traceability and accountability for modifications made to the vision system.

审计日志实用程序允许您跟踪作业和系统级变更，实现对视觉系统所做修改的可追溯性和可问责性。

## Actions that Trigger Audit Logging / 触发审计日志的操作

The following actions in In-Sight Vision Suite trigger audit logging events, grouped by category:

In-Sight Vision Suite 中的以下操作会触发审计日志事件，按类别分组如下：

### Changes in the Spreadsheet Editor / 电子表格编辑器中的变更
- Cell expression / 单元格表达式
- Enabling or disabling cell state / 启用或禁用单元格状态
- Symbolic Tag / 符号标签
- Value change / 值变更
- Commenting / 添加注释

### System Changes / 系统变更
- From the Sensor Menu / 传感器菜单中的操作:
  - Results Queue / 结果队列
  - Job Status / 作业状态
  - Custom View / 自定义视图
  - Discrete IO / 离散 IO
- Online or Offline status change / 在线或离线状态变更
- Saving or loading a job / 保存或加载作业

### Changing Complex Tools / 复杂工具变更
- EditCompositeRegion / 编辑复合区域
- EditFloat / 编辑浮点数
- EditInt / 编辑整数
- EditMaskedRegion / 编辑掩膜区域
- EditMultiGraphics / 编辑多图形
- EditPolygon / 编辑多边形
- EditPolylinePath / 编辑折线路径
- EditRegion / 编辑区域
- EditString / 编辑字符串
- OCRMax
- ReadIDMax
- Script / 脚本
- TrainExtractColor / 训练颜色提取
- TrainPatMaxRedLine / 训练 PatMax 红线
- ViDiELClassify / ViDiELClassify3D / ViDiEL 分类 / 3D 分类
- ViDiELRead / ViDiEL 读取
- ViDiELSegment / ViDiELSegment3D / ViDiEL 分割 / 3D 分割

### Changing Utilities / 实用程序变更
- Connecting to Spreadsheet and EasyBuilder / 连接电子表格和 EasyBuilder
- Opening the HMI / 打开 HMI
- Audit Logging / 审计日志
- Backup or Restore / 备份或还原
- Communications settings / 通信设置:
  - Network Settings / 网络设置
  - Host name / 主机名
  - Serial Configuration / 串口配置
- Configure SMB / 配置 SMB
- Firmware Update / 固件更新
- HMI Settings / HMI 设置
- Industrial Ethernet / 工业以太网
- IP Licensing / IP 许可
- License Configuration / 许可证配置
- Restart or Factory Reset / 重启或恢复出厂设置
- Startup Settings / 启动设置
- User Settings / 用户设置

---

> **Note / 注意**: Audit logging is enabled by default. To download the audit log or enable forwarding, you must have Full user access level privileges. For more information, see [User Settings](https://docs.cognex.com/is-usp_2440/web/EN/Help_ISVS/Content/Topics/utilities/user-settings.htm).
>
> 审计日志默认已启用。要下载审计日志或启用转发，您必须具有 **Full（完全）** 用户访问权限级别。有关更多信息，请参阅 [User Settings / 用户设置](https://docs.cognex.com/is-usp_2440/web/EN/Help_ISVS/Content/Topics/utilities/user-settings.htm)。

## Procedure / 操作步骤

1. Launch In-Sight Vision Suite if it is not already running.
   若 In-Sight Vision Suite 尚未运行，请先启动它。

2. Left-click the device you want to configure on the [In-Sight® Device Pane](https://docs.cognex.com/is-usp_2440/web/EN/Help_ISVS/Content/Topics/isvs-ui/device-pane.htm) on the left. The list of **Utilities** applicable to the selected vision system then appears at the bottom of the window.
   在左侧 [In-Sight® 设备窗格](https://docs.cognex.com/is-usp_2440/web/EN/Help_ISVS/Content/Topics/isvs-ui/device-pane.htm) 上左键单击要配置的设备，适用于所选视觉系统的 **实用程序** 列表将显示在窗口底部。

   📷 [Image: Utilities panel / 实用程序面板]

3. Click **Audit Logging**.
   The **Audit Logging** window appears.
   单击 **Audit Logging / 审计日志**。
   **Audit Logging / 审计日志** 窗口随即出现。

4. Retrieve the audit log by doing one of the following:
   通过以下任一方式获取审计日志：

   - **Download** the audit log by clicking **Download**.
     单击 **Download / 下载** 以下载审计日志。

     📷 [Image: Download button / 下载按钮]

   - **Enable Forwarding** to send the audit log to a remote server. Set up the IP address and the port number.
     勾选 **Enable Forwarding / 启用转发** 将审计日志发送至远程服务器，设置 IP 地址和端口号。

     > **Note / 注意**: In-Sight Vision Suite uses syslog forwarding protocol to store audit log records. You need to set up a syslog server for collecting audit records.
     >
     > In-Sight Vision Suite 使用 syslog 转发协议存储审计日志记录。您需要搭建一台 syslog 服务器来收集审计记录。

     📷 [Image: Enable Forwarding dialog / 启用转发对话框]

5. Click **OK**.
   单击 **OK / 确定**。
