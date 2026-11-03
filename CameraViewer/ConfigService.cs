using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CameraViewer
{
    // ==================== Camera ====================

    /// <summary>拍照标识取值 → 子网格 PictureBox 位置绑定。</summary>
    public class CellBinding
    {
        /// <summary>拍照标识单元格的取值，如 "11"、"21"、"31"、"41"。</summary>
        public string value { get; set; } = "";
        /// <summary>子网格中的槽位索引（0-based）。</summary>
        public int slotIndex { get; set; }
    }

    public class CameraItem
    {
        public int id { get; set; }
        public string ip { get; set; } = "";
        public string remark { get; set; } = "";
        public bool locked { get; set; }
        // Extended: connection type + camera identity
        public string conn { get; set; } = "http";  // "http" | "cogsocket" | "gige"
        public string serial { get; set; } = "";
        public int exposure { get; set; } = 100;
        // CogSocket: 子网格 PictureBox 数量（1=主图覆盖, >1=多子图模式）
        public int subGridCount { get; set; } = 1;
        // CogSocket: 拍照标识单元格地址（如 A21），其取值标识第几次拍照
        public string identifierCell { get; set; } = "";
        // CogSocket: 标识单元格取值 → 子网格槽位 绑定列表
        public List<CellBinding> cellBindings { get; set; } = new List<CellBinding>();
        // CogSocket 子网格显示模式："product"=产品图（默认） | "overlay"=叠加图
        public string viewMode { get; set; } = "product";
        // 历史图片胶片条开关（仅 CogSocket 子网格模式生效）
        public bool enableHistory { get; set; } = false;
        // 历史图片胶片条槽位数：4~10
        public int historyCount { get; set; } = 6;
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

    // ==================== File Transit ====================

    public class TransformConfig
    {
        public string Name { get; set; } = "";
        public bool IsEnable { get; set; }
        public string WatchPath { get; set; } = "";
        public string SourceSpliter { get; set; } = ",";
        public int FolderIndex { get; set; }
        public int FileNameIndex { get; set; } = 1;
        public string FolderSpliter { get; set; } = "+";
        public List<TransformPart> FolderPartList { get; set; } = new List<TransformPart>();
        public string FileNameSpliter { get; set; } = "_";
        public List<TransformPart> FileNamePartList { get; set; } = new List<TransformPart>();
    }

    // ==================== Image Cleanup ====================

    public class CleanConfig
    {
        public bool IsEnable { get; set; }
        public string FolderPath { get; set; } = "";
        public double ScanInterval { get; set; } = 2.0;
        public bool IsCleanEmptyFolders { get; set; } = true;
        public bool IsEnableHoldDays { get; set; }
        public int HoldDays { get; set; } = 30;
        public bool IsEnableRemainingSpace { get; set; } = true;
        public double RemainingSpace { get; set; } = 5.0;
        public int DeleteInterval { get; set; } = 5;
    }

    // ==================== Jobx Backup ====================

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

    // ==================== ConfigService ====================

    public static class ConfigService
    {
        public static string ConfigDir { get; private set; }
        public static CameraConfig Camera { get; private set; } = new CameraConfig();
        public static AppConfig App { get; private set; } = new AppConfig();
        public static TransformConfig Transform { get; private set; } = new TransformConfig();
        public static CleanConfig Clean { get; private set; } = new CleanConfig();
        public static JobxBackupConfig JobxBackup { get; private set; } = new JobxBackupConfig();

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = true };

        static ConfigService()
        {
            ConfigDir = ResolveConfigDir();
        }

        private static string ResolveConfigDir()
        {
            var exeDir = AppContext.BaseDirectory;
            // bootstrap.json allows per-instance config dir (multi-instance support)
            var bootstrapPath = Path.Combine(exeDir, "bootstrap.json");
            if (File.Exists(bootstrapPath))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(bootstrapPath));
                    var custom = node?["configDir"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
                        return custom;
                }
                catch { }
            }
            // portable or roaming default
            if (File.Exists(Path.Combine(exeDir, "portable.txt")))
                return Path.Combine(exeDir, "Configs");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CameraViewerDotnet");
        }

        public static void SetConfigDir(string newDir)
        {
            if (string.IsNullOrWhiteSpace(newDir))
            {
                ResetConfigDir();
                return;
            }
            Directory.CreateDirectory(newDir);
            // Copy existing configs if target is empty
            if (Directory.GetFiles(newDir).Length == 0 && Directory.Exists(ConfigDir))
            {
                foreach (var f in Directory.GetFiles(ConfigDir))
                {
                    try { File.Copy(f, Path.Combine(newDir, Path.GetFileName(f)), true); } catch { }
                }
            }
            ConfigDir = newDir;
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "bootstrap.json");
            File.WriteAllText(bootstrapPath, JsonSerializer.Serialize(new { configDir = newDir }, Opts));
        }

        public static void ResetConfigDir()
        {
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "bootstrap.json");
            try { if (File.Exists(bootstrapPath)) File.Delete(bootstrapPath); } catch { }
            ConfigDir = ResolveConfigDir();
        }

        public static void Load()
        {
            Directory.CreateDirectory(ConfigDir);

            LoadCamera();
            LoadApp();
            LoadTransform();
            LoadClean();
            LoadJobxBackup();
        }

        // ---------- Loaders ----------

        private static void LoadCamera()
        {
            var path = Path.Combine(ConfigDir, "CameraConfig.json");
            if (!File.Exists(path)) return;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node == null) return;
                var c = new CameraConfig
                {
                    count = node["count"]?.GetValue<int>() ?? 1,
                    delay = node["delay"]?.GetValue<int>() ?? 10,
                };
                if (c.count < 1) c.count = 1;
                if (c.count > 36) c.count = 36;
                if (c.delay < 0) c.delay = 0;
                foreach (var n in node["items"]?.AsArray() ?? new JsonArray())
                {
                    if (n == null) continue;
                    var item = new CameraItem
                    {
                        id = n["id"]?.GetValue<int>() ?? c.items.Count,
                        ip = n["ip"]?.GetValue<string>() ?? "",
                        remark = n["remark"]?.GetValue<string>() ?? "",
                        locked = n["locked"]?.GetValue<bool>() ?? false,
                        conn = n["conn"]?.GetValue<string>() ?? "http",
                        serial = n["serial"]?.GetValue<string>() ?? "",
                        exposure = n["exposure"]?.GetValue<int>() ?? 100,
                        subGridCount = n["subGridCount"]?.GetValue<int>() ?? 1,
                        // 兼容旧配置：cellSubscribe 曾用于填写拍照标识单元格（取首个）
                        identifierCell = n["identifierCell"]?.GetValue<string>()
                            ?? (n["cellSubscribe"]?.GetValue<string>() ?? "").Split(',').FirstOrDefault()?.Trim() ?? "",
                        viewMode = n["viewMode"]?.GetValue<string>() ?? "product",
                        enableHistory = n["enableHistory"]?.GetValue<bool>() ?? false,
                        historyCount = Math.Clamp(n["historyCount"]?.GetValue<int>() ?? 6, 4, 10),
                    };
                    foreach (var b in n["cellBindings"]?.AsArray() ?? new JsonArray())
                    {
                        if (b == null) continue;
                        item.cellBindings.Add(new CellBinding
                        {
                            value = b["value"]?.GetValue<string>() ?? "",
                            slotIndex = b["slotIndex"]?.GetValue<int>() ?? item.cellBindings.Count,
                        });
                    }
                    c.items.Add(item);
                }
                Camera = c;
            }
            catch { }
        }

        private static void LoadApp()
        {
            var path = Path.Combine(ConfigDir, "AppConfig.json");
            if (!File.Exists(path)) return;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node == null) return;
                App = new AppConfig
                {
                    language = node["language"]?.GetValue<string>() ?? "zh",
                    theme = node["theme"]?.GetValue<string>() ?? "light",
                };
            }
            catch { }
        }

        private static void LoadTransform()
        {
            var path = Path.Combine(ConfigDir, "TransformConfig.json");
            if (!File.Exists(path)) { Transform = new TransformConfig(); return; }
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node == null) { Transform = new TransformConfig(); return; }
                var t = new TransformConfig
                {
                    Name = node["Name"]?.GetValue<string>() ?? "",
                    IsEnable = node["IsEnable"]?.GetValue<bool>() ?? false,
                    WatchPath = node["WatchPath"]?.GetValue<string>() ?? "",
                    SourceSpliter = node["SourceSpliter"]?.GetValue<string>() ?? ",",
                    FolderIndex = node["FolderIndex"]?.GetValue<int>() ?? 0,
                    FileNameIndex = node["FileNameIndex"]?.GetValue<int>() ?? 1,
                    FolderSpliter = node["FolderSpliter"]?.GetValue<string>() ?? "+",
                    FileNameSpliter = node["FileNameSpliter"]?.GetValue<string>() ?? "_",
                };
                foreach (var n in node["FolderPartList"]?.AsArray() ?? new JsonArray())
                {
                    if (n == null) continue;
                    t.FolderPartList.Add(new TransformPart
                    {
                        PartType = n["PartType"]?.GetValue<string>() == "DateTime" ? TransformPartType.DateTime : TransformPartType.Name,
                        SourceIndex = n["SourceIndex"]?.GetValue<int>() ?? 0,
                        Format = n["Format"]?.GetValue<string>() ?? "",
                    });
                }
                foreach (var n in node["FileNamePartList"]?.AsArray() ?? new JsonArray())
                {
                    if (n == null) continue;
                    t.FileNamePartList.Add(new TransformPart
                    {
                        PartType = n["PartType"]?.GetValue<string>() == "DateTime" ? TransformPartType.DateTime : TransformPartType.Name,
                        SourceIndex = n["SourceIndex"]?.GetValue<int>() ?? 0,
                        Format = n["Format"]?.GetValue<string>() ?? "",
                    });
                }
                Transform = t;
            }
            catch { Transform = new TransformConfig(); }
        }

        private static void LoadClean()
        {
            var path = Path.Combine(ConfigDir, "CleanConfig.json");
            if (!File.Exists(path)) { Clean = new CleanConfig(); return; }
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node == null) { Clean = new CleanConfig(); return; }
                Clean = new CleanConfig
                {
                    IsEnable = node["IsEnable"]?.GetValue<bool>() ?? false,
                    FolderPath = node["FolderPath"]?.GetValue<string>() ?? "",
                    ScanInterval = node["ScanInterval"]?.GetValue<double>() ?? 2.0,
                    IsCleanEmptyFolders = node["IsCleanEmptyFolders"]?.GetValue<bool>() ?? true,
                    IsEnableHoldDays = node["IsEnableHoldDays"]?.GetValue<bool>() ?? false,
                    HoldDays = node["HoldDays"]?.GetValue<int>() ?? 30,
                    IsEnableRemainingSpace = node["IsEnableRemainingSpace"]?.GetValue<bool>() ?? true,
                    RemainingSpace = node["RemainingSpace"]?.GetValue<double>() ?? 5.0,
                    DeleteInterval = node["DeleteInterval"]?.GetValue<int>() ?? 5,
                };
            }
            catch { Clean = new CleanConfig(); }
        }

        private static void LoadJobxBackup()
        {
            var path = Path.Combine(ConfigDir, "JobxBackupConfig.json");
            if (!File.Exists(path)) return;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node == null) return;
                var j = new JobxBackupConfig();
                foreach (var n in node["cameras"]?.AsArray() ?? new JsonArray())
                {
                    if (n == null) continue;
                    j.cameras.Add(new JobxCameraConfig
                    {
                        name = n["name"]?.GetValue<string>() ?? "",
                        ip = n["ip"]?.GetValue<string>() ?? "",
                        ftp_port = n["ftp_port"]?.GetValue<int>() ?? 21,
                        ftp_username = n["ftp_username"]?.GetValue<string>() ?? "admin",
                        ftp_password = n["ftp_password"]?.GetValue<string>() ?? "",
                        backup_directory = n["backup_directory"]?.GetValue<string>() ?? "",
                        ftps_enabled = n["ftps_enabled"]?.GetValue<bool>() ?? true,
                        trust_all_certs = n["trust_all_certs"]?.GetValue<bool>() ?? true,
                    });
                }
                JobxBackup = j;
            }
            catch { }
        }

        // ---------- Savers ----------

        public static void SaveCamera() =>
            File.WriteAllText(Path.Combine(ConfigDir, "CameraConfig.json"), JsonSerializer.Serialize(Camera, Opts));

        public static void SaveApp() =>
            File.WriteAllText(Path.Combine(ConfigDir, "AppConfig.json"), JsonSerializer.Serialize(App, Opts));

        public static void SaveTransform()
        {
            // JsonNode doesn't support enum serialization easily — use JsonSerializer directly with string converter
            var obj = new
            {
                Transform.Name,
                Transform.IsEnable,
                Transform.WatchPath,
                Transform.SourceSpliter,
                Transform.FolderIndex,
                Transform.FileNameIndex,
                Transform.FolderSpliter,
                FolderPartList = Transform.FolderPartList?.Select(p => new
                {
                    PartType = p.PartType.ToString(),
                    p.SourceIndex,
                    p.Format,
                }).ToList(),
                Transform.FileNameSpliter,
                FileNamePartList = Transform.FileNamePartList?.Select(p => new
                {
                    PartType = p.PartType.ToString(),
                    p.SourceIndex,
                    p.Format,
                }).ToList(),
            };
            File.WriteAllText(Path.Combine(ConfigDir, "TransformConfig.json"), JsonSerializer.Serialize(obj, Opts));
        }

        public static void SaveClean() =>
            File.WriteAllText(Path.Combine(ConfigDir, "CleanConfig.json"), JsonSerializer.Serialize(Clean, Opts));

        public static void SaveJobxBackup() =>
            File.WriteAllText(Path.Combine(ConfigDir, "JobxBackupConfig.json"), JsonSerializer.Serialize(JobxBackup, Opts));

        // ---------- Helpers ----------

        public static CameraItem GetItem(int id) => Camera.items.Find(i => i.id == id);

        public static string NormalizeConn(string conn)
        {
            if (string.IsNullOrWhiteSpace(conn)) return "http";
            var v = conn.Trim().ToLowerInvariant();
            return v switch
            {
                "cogsocket" or "cognex" or "cog" => "cogsocket",
                "gige" or "gigevision" or "genicam" => "gige",
                _ => "http",
            };
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
