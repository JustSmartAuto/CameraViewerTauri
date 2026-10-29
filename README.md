# NtpServerSilent

NTP 服务器一键设置工具（静默安装 / 配置 / 测试），用于在内网环境中快速把一台 Windows PC 搭建为局域网 NTP 授时服务器，供 In-Sight 相机（D900、3D-L4000、IS2800、IS3800 等）进行时间同步。

## 功能特性

- **一键全自动**：双击启动即完成「检测 → 静默安装 → 写配置 → 重启服务 → 时间测试」全流程，无弹框、无需人工干预。
- **静默安装 Meinberg NTP**：安装程序（`ntp-4.2.4p8`）作为内嵌资源打包在单个 exe 中，运行时释放并自动执行（依次尝试 NSIS 的 `/S`、`/SD`、`/VERYSILENT` 等静默参数）。
- **内网专用配置**：以本机时钟为唯一时间源（`server 127.127.1.0`，stratum 10），不依赖外网，允许任意网段（0.0.0.0/0）客户端查询授时。
- **配置幂等**：启动时若检测到配置与目标一致则跳过安装直接测试；不一致则自动重新配置。
- **服务自愈**：安装程序未注册服务时，自动用 `sc create` 手动创建 NTP 服务。
- **内置测试**：自动向 `127.0.0.1:123` 发起 NTP 查询并轮询等待同步完成（最长 5 分钟），显示服务器时间与系统时间的偏差。
- **日志**：界面日志 + `%TEMP%\NtpSetupHelper.log` 文件日志。

## 使用方法

1. 以**管理员权限**运行 `src/NtpSetupHelper/bin/Release/NtpSetupHelper.exe`（或重新构建后运行）。
2. 程序启动后自动完成全部设置并在日志窗口显示测试结果。
3. 防火墙放行 UDP 123 端口，即可让局域网内的相机使用该 PC 的 IP 作为 NTP 服务器。
4. 相机端配置步骤详见 [docs/IS2800-Time-Synchronization.md](docs/IS2800-Time-Synchronization.md)（Vision Suite 时间设置 SOP，含 PDF 版）。

## 项目结构

```
├── src/NtpSetupHelper/      C# WinForms 工具（.NET Framework 4.8）
│   ├── Program.cs           全部逻辑（单文件）
│   ├── NtpSetupHelper.csproj
│   └── app.manifest         请求管理员权限（requiredAdministrator）
├── installer/               Meinberg NTP 4.2.4p8 安装包（内嵌进 exe 的 EmbeddedResource）
├── docs/                    相机时间同步 SOP（Markdown + PDF + 截图）
├── assets/                  图标与示意图
└── tools/                   ResourceHacker、FileZilla Server 等辅助工具
```

## 构建

使用 Visual Studio 或 MSBuild 构建：

```bash
msbuild src/NtpSetupHelper/NtpSetupHelper.csproj /p:Configuration=Release
```

生成的单文件 `bin\Release\NtpSetupHelper.exe` 已包含 NTP 安装包资源，可直接拷贝分发。

## 注意事项

- 必须以管理员身份运行（需要安装软件、创建/重启服务）。
- 授时源为本机系统时钟，适合无互联网的内网产线环境；若需高精度授时请先同步本机时间。
- 测试用例中相机所连网卡需与 PC 同网段，并确保 Windows 防火墙允许 UDP 123。

## License

[MIT](LICENSE) © 2026 JustSmartAuto
