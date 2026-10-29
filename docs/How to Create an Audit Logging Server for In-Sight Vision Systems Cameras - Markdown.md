# How to Create an Audit Logging Server for In-Sight Vision Systems Cameras

> 发布日期：2025-05-28
> 来源：https://support.cognex.com/en/help-articles/how-to-create-an-audit-logging-server-for-in-sight-vision-systems-cameras

本文档说明如何为 In-Sight Vision Systems 相机搭建一个审计日志服务器（Audit Logging Server）。

---

## Question

Is possible to use an external Audit server to save logging from In-Sight Vision Systems cameras?

（是否可以使用外部审计服务器保存 In-Sight Vision Systems 相机的日志？）

## Answer

可以。以下示例使用 **Fluentd** 搭建日志服务器。

### 软件版本

| 组件 | 说明 |
| --- | --- |
| Fluentd | 下载最新版（撰写本文时为 **5.2.0-x64**）：<https://www.fluentd.org/download/fluent_package> |
| In-Sight Vision Systems | 使用最新版（撰写本文时为 **24.4.2**）：<https://support.cognex.com/en/downloads/in-sight/software-firmware> |

### 网络要求

运行 Fluentd 的 PC 与相机必须处于同一子网：

| 设备 | IP | Subnet Mask |
| --- | --- | --- |
| PC（Fluentd） | 192.168.10.100 | 255.255.255.0 |
| Camera | 192.168.10.110 | 255.255.255.0 |

### 文件准备

Fluentd 安装完成后，在 `C:\opt\fluent\` 下创建 **Files** 文件夹，并将本文附件中的三个文件（**Config**、**Certification**、**Key**）放入该目录。

---

## In-Sight Vision Suite 设置

1. 在 In-Sight Vision Suite 的 Home 页面，进入 **Utilities → Audit Logging**，弹出配置窗口。

   [图 1 — Audit Logging 配置窗口]

2. 勾选 **Enable Forwarding**。

3. 填写 Fluentd 所在 PC 的 IP 地址，端口设为 **5140**，点击 **OK**。

   [图 2 — IP 与端口设置]

---

## Audit Server 配置

### 步骤 1：修改 fluentd.conf

打开 **fluentd.conf**，将其中引用另外两个文件（Certification、Key）的路径改为实际位置。

示例：

- **原始配置**：
  [图 3 — 原始 fluentd.conf]
- **修改后**：
  [图 4 — 修改后的 fluentd.conf]

> 💡 **提示**：端口也可在此文件中修改。

### 步骤 2：启动 Fluentd

1. 保存 `fluentd.conf`。
2. 进入 Fluentd 安装目录，运行 **fluent-package-prompt**。

   [图 5 — fluent-package-prompt 文件]

3. 在该目录打开终端，执行：

   ```powershell
   .\fluentd -c "C:\opt\fluent\Files\fluentd.conf"
   ```

   [图 6 — 执行命令]

4. Fluentd 启动成功后，相机的每次修改将实时记录到日志服务器。

   [图 7 — 服务器启动成功]

> 💡 **提示**：需要在 In-Sight Vision Systems 中配置 **NTP Server**，确保相机时间准确。

### 步骤 3：停止 Fluentd

在命令行窗口按 `Ctrl + C`，输入 `Y` 确认即可停止。
