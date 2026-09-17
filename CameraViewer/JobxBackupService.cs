using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CameraViewer;

namespace CameraViewerDotnet
{
    public class JobxBackupResult
    {
        public bool success { get; set; }
        public string message { get; set; } = "";
        public string backup_path { get; set; }
    }

    public class JobxLogEntry
    {
        public string timestamp { get; set; }
        public string level { get; set; }
        public string message { get; set; }
    }

    public static class JobxBackupService
    {
        private const int MaxLogEntries = 1000;
        private static readonly Queue<JobxLogEntry> LogBuffer = new Queue<JobxLogEntry>();
        private static readonly object LogLock = new object();

        public static void AddLog(string level, string message)
        {
            lock (LogLock)
            {
                LogBuffer.Enqueue(new JobxLogEntry
                {
                    timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    level = level,
                    message = message,
                });
                while (LogBuffer.Count > MaxLogEntries)
                    LogBuffer.Dequeue();
            }
        }

        public static List<JobxLogEntry> GetLogs()
        {
            lock (LogLock)
                return new List<JobxLogEntry>(LogBuffer);
        }

        private static string TranslateFtpError(string err)
        {
            var lower = err.ToLowerInvariant();
            if (lower.Contains("connection closed"))
                return "备份失败: 连接被服务器意外关闭。可能原因：\n" +
                       "1. 相机未启用 FTP/FTPS 服务\n" +
                       "2. 连接端口不正确（Cognex 默认 FTP:21, FTPS:990）\n" +
                       "3. 相机要求 FTPS 加密连接，请勾选\"使用 FTPS\"\n" +
                       "4. 网络或防火墙阻止了连接\n原始错误: " + err;
            if (lower.Contains("frame size") || lower.Contains("corrupted frame"))
                return "备份失败: FTPS 加密模式不匹配。可能原因：\n" +
                       "1. 端口 21 请使用显式 FTPS（AUTH TLS），端口 990 请使用隐式 FTPS\n" +
                       "2. 若仍失败请确认相机 FTPS 端口与加密模式设置\n" +
                       "3. 勾选\"信任所有 TLS 证书\"可排除证书问题\n原始错误: " + err;
            if (lower.Contains("actively refused") || lower.Contains("connection refused"))
                return "备份失败: 连接被拒绝。请检查 IP 地址和端口是否正确，以及相机是否在线。\n原始错误: " + err;
            if (lower.Contains("timed out") || lower.Contains("timeout"))
                return "备份失败: 连接超时。请检查网络是否通畅，相机是否可达。\n原始错误: " + err;
            if (lower.Contains("unknown host") || lower.Contains("unreachable") || lower.Contains("no such host"))
                return "备份失败: 无法连接到相机主机。请检查 IP 地址是否正确。\n原始错误: " + err;
            if (lower.Contains("ssl") || lower.Contains("tls") || lower.Contains("handshake"))
                return "备份失败: TLS/SSL 握手失败。请检查 FTPS 配置是否正确，或尝试勾选\"信任所有 TLS 证书\"。\n原始错误: " + err;
            if (lower.Contains("login") || lower.Contains("authentication") || lower.Contains("530"))
                return "备份失败: 认证失败。请检查用户名和密码是否正确。\n原始错误: " + err;
            return "备份失败: " + err;
        }

        public static JobxBackupResult BackupCamera(int index)
        {
            JobxCameraConfig camera;
            lock (ConfigService.JobxBackup)
                camera = index >= 0 && index < ConfigService.JobxBackup.cameras.Count
                    ? ConfigService.JobxBackup.cameras[index]
                    : null;
            if (camera == null)
                return new JobxBackupResult { success = false, message = I18n.T("jobxCameraNotFound") };
            return BackupCamera(camera);
        }

        public static JobxBackupResult BackupCamera(JobxCameraConfig camera)
        {
            AddLog("INFO", $"开始备份: {camera.name} ({camera.ip}) {(camera.ftps_enabled ? "[FTPS]" : "[FTP]")}");

            var backupDir = !string.IsNullOrWhiteSpace(camera.backup_directory)
                ? camera.backup_directory
                : ConfigService.ConfigDir;
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var targetDir = Path.Combine(backupDir, camera.name, timestamp);

            FluentFTP.FtpClient client = null;
            try
            {
                client = new FluentFTP.FtpClient(camera.ip, camera.ftp_username, camera.ftp_password, camera.ftp_port);
                client.Config.ConnectTimeout = 30000;
                client.Config.ReadTimeout = 30000;
                client.Config.DataConnectionConnectTimeout = 30000;
                client.Config.DataConnectionReadTimeout = 30000;
                if (camera.ftps_enabled)
                {
                    // 990 端口 = 隐式 FTPS（连接即 TLS）；其它端口（如 21）= 显式 FTPS（AUTH TLS 升级）
                    client.Config.EncryptionMode = camera.ftp_port == 990
                        ? FluentFTP.FtpEncryptionMode.Implicit
                        : FluentFTP.FtpEncryptionMode.Explicit;
                    client.Config.DataConnectionEncryption = true;
                    if (camera.trust_all_certs)
                        client.Config.ValidateAnyCertificate = true;
                }
                client.Connect();
                client.SetWorkingDirectory("/");

                var downloaded = DownloadDirectory(client, "/", targetDir, camera.name);
                try { client.Disconnect(); } catch { }

                if (downloaded == 0)
                {
                    var msg = I18n.T("jobxNoFiles");
                    AddLog("WARN", $"✗ {camera.name}: {msg}");
                    return new JobxBackupResult { success = false, message = msg };
                }

                AddLog("INFO", $"✓ {camera.name}: {string.Format(I18n.T("jobxSuccessFmt"), downloaded)} -> {targetDir}");
                return new JobxBackupResult { success = true, message = string.Format(I18n.T("jobxSuccessFmt"), downloaded), backup_path = targetDir };
            }
            catch (Exception ex)
            {
                var msg = TranslateFtpError(ex.Message);
                AddLog("ERROR", $"✗ {camera.name}: {msg}");
                return new JobxBackupResult { success = false, message = msg };
            }
            finally
            {
                try { client?.Dispose(); } catch { }
            }
        }

        private static int DownloadDirectory(FluentFTP.FtpClient client, string remotePath, string localDir, string cameraName)
        {
            Directory.CreateDirectory(localDir);

            var count = 0;
            var baseRemote = remotePath.TrimEnd('/');
            FluentFTP.FtpListItem[] items;
            try
            {
                items = client.GetListing(remotePath);
            }
            catch (Exception ex)
            {
                throw new Exception(TranslateFtpError(ex.Message));
            }

            foreach (var item in items)
            {
                var name = item.Name;
                if (string.IsNullOrEmpty(name) || name == "." || name == "..") continue;

                var childRemote = string.IsNullOrEmpty(baseRemote) || baseRemote == "/"
                    ? "/" + name
                    : baseRemote + "/" + name;

                if (item.Type == FluentFTP.FtpObjectType.Directory)
                {
                    count += DownloadDirectory(client, childRemote, Path.Combine(localDir, name), cameraName);
                }
                else if (item.Type == FluentFTP.FtpObjectType.File)
                {
                    var lower = name.ToLowerInvariant();
                    if (!lower.EndsWith(".jobx") && !lower.EndsWith(".jobx.sig")) continue;

                    var localFile = Path.Combine(localDir, name);
                    try
                    {
                        var status = client.DownloadFile(localFile, childRemote, FluentFTP.FtpLocalExists.Overwrite, FluentFTP.FtpVerify.None);
                        if (status == FluentFTP.FtpStatus.Success)
                        {
                            count++;
                        }
                        else
                        {
                            AddLog("ERROR", $"[{cameraName}] 下载失败 {name}: {status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog("ERROR", $"[{cameraName}] 下载失败 {name}: {ex.Message}");
                    }
                }
            }

            return count;
        }

        public static void BackupAll()
        {
            List<JobxCameraConfig> cameras;
            lock (ConfigService.JobxBackup)
                cameras = new List<JobxCameraConfig>(ConfigService.JobxBackup.cameras);

            var thread = new Thread(() =>
            {
                foreach (var camera in cameras)
                {
                    BackupCamera(camera);
                    Thread.Sleep(500);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        public static void OpenBackupDirectory(string path)
        {
            var dir = !string.IsNullOrWhiteSpace(path) ? path : ConfigService.ConfigDir;
            try { Directory.CreateDirectory(dir); } catch { }
            try { Process.Start("explorer.exe", dir); }
            catch (Exception ex) { AddLog("ERROR", I18n.T("openDirFailed") + ": " + ex.Message); }
        }
    }
}
