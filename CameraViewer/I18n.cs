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
