using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CameraViewer
{
    public class CameraItem
    {
        public int id { get; set; }
        /// <summary>连接方式：http（WebView2 加载网页）/ gige（OPT SciCamera SDK 实时取流）</summary>
        public string conn { get; set; } = "http";
        /// <summary>http：网页地址；gige：IP 地址/URI</summary>
        public string ip { get; set; } = "";
        /// <summary>gige：相机序列号（用于发现并匹配设备）</summary>
        public string serial { get; set; } = "";
        /// <summary>gige：曝光时间(ms)，0 表示不修改相机默认值</summary>
        public int exposureMs { get; set; }
        public string remark { get; set; } = "";
        public bool locked { get; set; }
    }

    public class CameraConfig
    {
        public int count { get; set; } = 1;
        public int delay { get; set; } = 10;
        public List<CameraItem> items { get; set; } = new List<CameraItem>();
    }

    public class AppConfig
    {
        public string language { get; set; } = "zh";
        public string theme { get; set; } = "light";
    }

    public class JobxCameraConfig
    {
        public string name { get; set; } = "";
        public string ip { get; set; } = "";
        public int ftp_port { get; set; } = 21;
        public string ftp_username { get; set; } = "admin";
        public string ftp_password { get; set; } = "";
        public string backup_directory { get; set; } = "";
        public bool ftps_enabled { get; set; } = true;
        public bool trust_all_certs { get; set; } = true;
    }

    public class JobxBackupConfig
    {
        public List<JobxCameraConfig> cameras { get; set; } = new List<JobxCameraConfig>();
    }

    public static class ConfigService
    {
        public static readonly string ConfigDir;
        public static CameraConfig Camera { get; private set; } = new CameraConfig();
        public static AppConfig App { get; private set; } = new AppConfig();
        public static JobxBackupConfig JobxBackup { get; private set; } = new JobxBackupConfig();

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = true };

        static ConfigService()
        {
            // 配置文件跟随 exe 同目录，便于随程序一起拷贝/编辑
            ConfigDir = AppContext.BaseDirectory;
        }

        public static void Load()
        {
            Directory.CreateDirectory(ConfigDir);

            var camPath = Path.Combine(ConfigDir, "CameraConfig.json");
            if (File.Exists(camPath))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(camPath));
                    if (node != null)
                    {
                        var c = new CameraConfig();
                        c.count = node["count"]?.GetValue<int>() ?? 1;
                        if (c.count < 1) c.count = 1;
                        if (c.count > 16) c.count = 16;
                        c.delay = node["delay"]?.GetValue<int>() ?? 10;
                        if (c.delay < 0) c.delay = 0;
                        foreach (var n in node["items"]?.AsArray() ?? new JsonArray())
                        {
                            if (n == null) continue;
                            c.items.Add(new CameraItem
                            {
                                id = n["id"]?.GetValue<int>() ?? c.items.Count,
                                conn = NormalizeConn(n["conn"]?.GetValue<string>()),
                                ip = n["ip"]?.GetValue<string>() ?? "",
                                serial = n["serial"]?.GetValue<string>() ?? "",
                                exposureMs = n["exposureMs"]?.GetValue<int>() ?? 0,
                                remark = n["remark"]?.GetValue<string>() ?? "",
                                locked = n["locked"]?.GetValue<bool>() ?? false,
                            });
                        }
                        Camera = c;
                    }
                }
                catch { /* 损坏配置使用默认值 */ }
            }
            else
            {
                // 首次运行：生成默认配置（OPT GigE 相机，来源 相机序列号.xlsx）并落盘到 exe 同目录
                Camera = CreateDefaultCamera();
                SaveCamera();
            }

            var appPath = Path.Combine(ConfigDir, "AppConfig.json");
            if (File.Exists(appPath))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(appPath));
                    if (node != null)
                    {
                        var a = new AppConfig();
                        a.language = node["language"]?.GetValue<string>() ?? "zh";
                        a.theme = node["theme"]?.GetValue<string>() ?? "light";
                        App = a;
                    }
                }
                catch { }
            }

            var jobxPath = Path.Combine(ConfigDir, "JobxBackupConfig.json");
            if (File.Exists(jobxPath))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(jobxPath));
                    if (node != null)
                    {
                        var j = new JobxBackupConfig();
                        foreach (var n in node["cameras"]?.AsArray() ?? new JsonArray())
                        {
                            if (n == null) continue;
                            var cam = new JobxCameraConfig();
                            cam.name = n["name"]?.GetValue<string>() ?? "";
                            cam.ip = n["ip"]?.GetValue<string>() ?? "";
                            cam.ftp_port = n["ftp_port"]?.GetValue<int>() ?? 21;
                            cam.ftp_username = n["ftp_username"]?.GetValue<string>() ?? "admin";
                            cam.ftp_password = n["ftp_password"]?.GetValue<string>() ?? "";
                            cam.backup_directory = n["backup_directory"]?.GetValue<string>() ?? "";
                            cam.ftps_enabled = n["ftps_enabled"]?.GetValue<bool>() ?? true;
                            cam.trust_all_certs = n["trust_all_certs"]?.GetValue<bool>() ?? true;
                            j.cameras.Add(cam);
                        }
                        JobxBackup = j;
                    }
                }
                catch { }
            }
        }

        /// <summary>连接方式规范化：仅允许 http / gige，非法值回退 http。</summary>
        public static string NormalizeConn(string conn) =>
            string.Equals(conn, "gige", StringComparison.OrdinalIgnoreCase) ? "gige" : "http";

        /// <summary>默认相机配置：OPT GigE 相机（数据来源 相机序列号.xlsx）。</summary>
        private static CameraConfig CreateDefaultCamera()
        {
            var c = new CameraConfig { count = 11, delay = 10 };
            AddGige(c, "顶部1号相机", "D258060335");
            AddGige(c, "顶部2号相机", "D25A140779");
            AddGige(c, "顶部3号相机", "D258230057");
            AddGige(c, "顶部4号相机", "D25A130373");
            AddGige(c, "侧面5号相机", "D269220474");
            AddGige(c, "侧面6号相机", "D268071288");
            AddGige(c, "侧面7号相机", "D267110314");
            AddGige(c, "侧面8号相机", "D267110325");
            AddGige(c, "侧面9号相机", "D267110321");
            AddGige(c, "侧面10号相机", "D267240823");
            AddGige(c, "侧面面光11号相机", "D25C160674");
            return c;
        }

        private static void AddGige(CameraConfig c, string remark, string serial)
        {
            c.items.Add(new CameraItem
            {
                id = c.items.Count,
                conn = "gige",
                ip = "",
                serial = serial,
                exposureMs = 0,
                remark = remark,
                locked = false,
            });
        }

        public static void SaveCamera() =>
            File.WriteAllText(Path.Combine(ConfigDir, "CameraConfig.json"), JsonSerializer.Serialize(Camera, Opts));

        public static void SaveApp() =>
            File.WriteAllText(Path.Combine(ConfigDir, "AppConfig.json"), JsonSerializer.Serialize(App, Opts));

        public static void SaveJobxBackup() =>
            File.WriteAllText(Path.Combine(ConfigDir, "JobxBackupConfig.json"), JsonSerializer.Serialize(JobxBackup, Opts));

        public static CameraItem GetItem(int id)
        {
            return Camera.items.Find(i => i.id == id);
        }

        public static CameraItem EnsureItem(int id)
        {
            var item = GetItem(id);
            if (item == null)
            {
                item = new CameraItem { id = id };
                Camera.items.Add(item);
            }
            return item;
        }
    }
}
