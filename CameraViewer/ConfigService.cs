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
        public string ip { get; set; } = "";
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
        public bool ftps_enabled { get; set; }
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
            var exeDir = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(exeDir, "portable.txt")))
                ConfigDir = Path.Combine(exeDir, "Configs");
            else
                ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CameraViewerDotnet");
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
                        if (c.count > 12) c.count = 12;
                        c.delay = node["delay"]?.GetValue<int>() ?? 10;
                        if (c.delay < 0) c.delay = 0;
                        foreach (var n in node["items"]?.AsArray() ?? new JsonArray())
                        {
                            if (n == null) continue;
                            c.items.Add(new CameraItem
                            {
                                id = n["id"]?.GetValue<int>() ?? c.items.Count,
                                ip = n["ip"]?.GetValue<string>() ?? "",
                                remark = n["remark"]?.GetValue<string>() ?? "",
                                locked = n["locked"]?.GetValue<bool>() ?? false,
                            });
                        }
                        Camera = c;
                    }
                }
                catch { /* 损坏配置使用默认值 */ }
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
                            cam.ftps_enabled = n["ftps_enabled"]?.GetValue<bool>() ?? false;
                            cam.trust_all_certs = n["trust_all_certs"]?.GetValue<bool>() ?? true;
                            j.cameras.Add(cam);
                        }
                        JobxBackup = j;
                    }
                }
                catch { }
            }
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
