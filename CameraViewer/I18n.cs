using System;
using System.Collections.Generic;

namespace CameraViewer
{
    public static class I18n
    {
        public static string Language = "zh";

        public static event Action LanguageChanged;

        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            ["appTitle"] = new[] { "CameraViewerDotnet", "CameraViewerDotnet" },
            ["setting"] = new[] { "设置", "Settings" },
            ["systemSettings"] = new[] { "系统设置", "System Settings" },
            ["cameraDisplay"] = new[] { "相机显示", "Camera Display" },
            ["cameraSettings"] = new[] { "相机设置", "Camera Settings" },
            ["displaySettings"] = new[] { "显示设置", "Display Settings" },
            ["appSettings"] = new[] { "软件设置", "App Settings" },
            ["count"] = new[] { "数量：", "Count:" },
            ["delay"] = new[] { "错峰延时（秒）：", "Stagger delay (s):" },
            ["saveApply"] = new[] { "保存应用", "Save & Apply" },
            ["updateView"] = new[] { "更新视图", "Update View" },
            ["url"] = new[] { "URL:", "URL:" },
            ["remark"] = new[] { "备注:", "Remark:" },
            ["remarkPlaceholder"] = new[] { "备注...", "Remark..." },
            ["urlPlaceholder"] = new[] { "192.168.1.10 或 http://...", "192.168.1.10 or http://..." },
            ["lock"] = new[] { "锁定", "Lock" },
            ["unlock"] = new[] { "解锁", "Unlock" },
            ["refresh"] = new[] { "刷新", "Refresh" },
            ["maximize"] = new[] { "最大化", "Maximize" },
            ["restore"] = new[] { "还原", "Restore" },
            ["themeLight"] = new[] { "明亮", "Light" },
            ["themeDark"] = new[] { "暗黑", "Dark" },
            ["themeSetting"] = new[] { "界面主题", "Theme" },
            ["languageSetting"] = new[] { "界面语言", "Language" },
            ["languageZh"] = new[] { "中文", "中文" },
            ["languageEn"] = new[] { "English", "English" },
            ["systemTime"] = new[] { "系统时间：", "System Time:" },
            ["version"] = new[] { "版本号：", "Version:" },
            ["showWindow"] = new[] { "显示界面", "Show Window" },
            ["exit"] = new[] { "退出", "Exit" },
            ["apply"] = new[] { "应用", "Apply" },
            ["cancel"] = new[] { "取消", "Cancel" },
            ["camera"] = new[] { "相机", "Camera" },
            ["locked"] = new[] { "锁定", "Locked" },
            ["jobxBackup"] = new[] { "JOBX备份", "JOBX Backup" },
            ["addCamera"] = new[] { "添加相机", "Add Camera" },
            ["backup"] = new[] { "备份", "Backup" },
            ["backupAll"] = new[] { "全部备份", "Backup All" },
            ["openDir"] = new[] { "打开目录", "Open Directory" },
            ["log"] = new[] { "日志", "Log" },
            ["name"] = new[] { "名称", "Name" },
            ["ip"] = new[] { "IP", "IP" },
            ["port"] = new[] { "端口", "Port" },
            ["username"] = new[] { "用户名", "Username" },
            ["password"] = new[] { "密码", "Password" },
            ["backupDir"] = new[] { "备份目录", "Backup Directory" },
            ["ftps"] = new[] { "FTPS", "FTPS" },
            ["trustCerts"] = new[] { "信任证书", "Trust Certs" },
            ["delete"] = new[] { "删除", "Delete" },
            ["edit"] = new[] { "编辑", "Edit" },
            ["jobxCameraNotFound"] = new[] { "未找到指定相机", "Camera not found" },
            ["jobxNoFiles"] = new[] { "未找到任何 jobx 文件", "No jobx files found" },
            ["jobxSuccessFmt"] = new[] { "备份成功，共下载 {0} 个文件", "Backup succeeded, downloaded {0} file(s)" },
            ["openDirFailed"] = new[] { "打开目录失败", "Failed to open directory" },
            ["jobxSelectCamera"] = new[] { "请先选择要备份的相机", "Please select a camera to back up" },
            ["select"] = new[] { "选择", "Select" },
            ["selectBackupDir"] = new[] { "选择备份目录", "Select backup directory" },
            ["error"] = new[] { "错误", "Error" },
            ["about"] = new[] { "关于", "About" },
            ["aboutDesc"] = new[] { "多路网络相机监控查看工具，支持 MJPG/RTSP 视频流、JOBX 作业备份、HMI 语言跟随等功能。", "Multi-channel network camera monitoring tool with MJPG/RTSP streams, JOBX backup, and HMI language-following." },
            ["aboutTech"] = new[] { "技术栈：WPF (.NET 8) + WebView2 + FluentFTP", "Tech stack: WPF (.NET 8) + WebView2 + FluentFTP" },
            ["aboutRepo"] = new[] { "项目仓库", "Repository" },
            ["aboutClose"] = new[] { "关闭", "Close" },
        };

        public static string T(string key)
        {
            if (Map.TryGetValue(key, out var v))
                return Language == "en" ? v[1] : v[0];
            return key;
        }

        public static void SetLanguage(string lang)
        {
            if (lang != "en") lang = "zh";
            if (Language == lang) return;
            Language = lang;
            LanguageChanged?.Invoke();
        }

        public static void RaiseChanged() => LanguageChanged?.Invoke();
    }
}
