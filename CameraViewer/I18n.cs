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
            ["conn"] = new[] { "连接方式:", "Connection:" },
            ["connHttp"] = new[] { "Http", "Http" },
            ["connCogsocket"] = new[] { "CogSocket", "CogSocket" },
            ["connGige"] = new[] { "GigE", "GigE" },
            ["connPlaceholder"] = new[] { "192.168.1.10:3000", "192.168.1.10:3000" },
            ["serial"] = new[] { "序列号:", "Serial:" },
            ["serialPlaceholder"] = new[] { "D258060335", "D258060335" },
            ["exposure"] = new[] { "曝光(ms):", "Exposure(ms):" },
            ["lock"] = new[] { "锁定", "Lock" },
            ["unlock"] = new[] { "解锁", "Unlock" },
            ["refresh"] = new[] { "刷新", "Refresh" },
            ["maximize"] = new[] { "最大化", "Maximize" },
            ["productImage"] = new[] { "产品图", "Product" },
            ["overlayImage"] = new[] { "叠加图", "Overlay" },
            ["fitView"] = new[] { "适合", "Fit" },
            ["webView"] = new[] { "网页", "Web" },
            ["history"] = new[] { "历史", "History" },
            ["restore"] = new[] { "还原", "Restore" },
            ["themeLight"] = new[] { "明亮", "Light" },
            ["themeDark"] = new[] { "暗黑", "Dark" },
            ["themeBeos"] = new[] { "BeOS", "BeOS" },
            ["themeSetting"] = new[] { "界面主题", "Theme" },
            ["languageSetting"] = new[] { "界面语言", "Language" },
            ["windowStartup"] = new[] { "窗口与启动", "Window & Startup" },
            ["topMost"] = new[] { "窗口置顶", "Always on top" },
            ["autoStart"] = new[] { "开机自动启动", "Start on boot" },
            ["startupDelay"] = new[] { "启动延时(秒)", "Startup delay (s)" },
            ["autoStartFailed"] = new[] { "创建开机启动项失败", "Failed to create startup shortcut" },
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
            ["aboutTech"] = new[] { "技术栈：WinForms + AntdUI (.NET 8) + WebView2 + FluentFTP", "Tech stack: WinForms + AntdUI (.NET 8) + WebView2 + FluentFTP" },
            ["aboutRepo"] = new[] { "项目仓库", "Repository" },
            ["aboutClose"] = new[] { "关闭", "Close" },
            ["configDir"] = new[] { "配置目录", "Config Directory" },
            ["configDirPlaceholder"] = new[] { "留空使用默认目录", "Empty = default directory" },
            ["configDirWarn"] = new[] { "修改配置目录后，当前配置将复制到新目录；\n应用重启后生效，不同目录支持多开。", "After changing, configs are copied to the new path.\nTakes effect after restart; different dirs allow multiple instances." },
            ["copyAndRestart"] = new[] { "复制并重启", "Copy & Restart" },
            ["useDefault"] = new[] { "恢复默认目录", "Reset to Default" },
            ["fileTransit"] = new[] { "文件转存", "File Transit" },
            ["transitEnable"] = new[] { "启用转存", "Enable Transit" },
            ["transitWatch"] = new[] { "路径", "Path" },
            ["transitSourceSplit"] = new[] { "内容分隔符", "Content splitter" },
            ["transitFolderIdx"] = new[] { "文件夹内序号", "Folder index" },
            ["transitFileIdx"] = new[] { "文件名内序号", "File name index" },
            ["transitRun"] = new[] { "立即处理一次", "Process Once" },
            ["transitAdd"] = new[] { "添加", "Add" },
            ["transitClear"] = new[] { "清空", "Clear" },
            ["transitOpen"] = new[] { "打开", "Open" },
            ["transitAddName"] = new[] { "添加转存配置", "Add Transfer Config" },
            ["transitNameEmpty"] = new[] { "名称不能为空", "Name cannot be empty" },
            ["transitNameExists"] = new[] { "名称已存在", "Name already exists" },
            ["transitDeleteConfirm"] = new[] { "确定删除当前转存配置？", "Delete the current transfer config?" },
            ["transitDeleteLast"] = new[] { "至少保留一套转存配置", "Keep at least one transfer config" },
            ["transitAddPart"] = new[] { "添加项", "Add Item" },
            ["transitPartTypeLabel"] = new[] { "类型", "Type" },
            ["transitPartName"] = new[] { "名称", "Name" },
            ["transitPartDateTime"] = new[] { "PC时间", "PC Time" },
            ["cleanup"] = new[] { "存图清理", "Image Cleanup" },
            ["cleanupEnable"] = new[] { "启用定时清理", "Enable Scheduled Cleanup" },
            ["cleanupFolder"] = new[] { "清理目录", "Cleanup Folder" },
            ["cleanupInterval"] = new[] { "扫描间隔（小时）:", "Scan interval (h):" },
            ["cleanupHoldDays"] = new[] { "超期保留（天）:", "Hold days:" },
            ["cleanupFreeGB"] = new[] { "剩余空间阈值（GB）:", "Min free space (GB):" },
            ["cleanupDeleteMs"] = new[] { "每文件删除间隔（ms）:", "Delete interval (ms):" },
            ["cleanupEmpty"] = new[] { "清理空目录", "Clean empty folders" },
            ["cleanupHoldDaysEn"] = new[] { "按保留天数删除", "Delete by hold days" },
            ["cleanupFreeEn"] = new[] { "按剩余空间删除", "Delete by free space" },
            ["cleanupNow"] = new[] { "立即清理一次", "Clean Once Now" },
            ["cellSubscribe"] = new[] { "拍照标识单元格:", "Trigger cell:" },
            ["cellSubscribePlaceholder"] = new[] { "如 A21", "e.g. A21" },
            ["subGridCount"] = new[] { "子网格数量:", "Sub-grid count:" },
            ["subGridSlot"] = new[] { "子网格", "Sub-grid" },
            ["subGridValuePlaceholder"] = new[] { "拍照值 如 11", "Shot value, e.g. 11" },
            ["connPlaceholder"] = new[] { "192.168.1.10:80", "192.168.1.10:80" },
            ["cogSocketNotConfigured"] = new[] { "CogSocket 模式尚未配置完整：请在设置中填写拍照标识单元格、子网格数量（>1）及槽位取值绑定", "CogSocket mode is not configured: set the trigger cell, sub-grid count (>1) and slot bindings in Settings" },
            ["transitFolderSplit"] = new[] { "路径分隔符", "Path splitter" },
            ["transitFileSplit"] = new[] { "文件名称分隔符", "File name splitter" },
            ["transitFolderParts"] = new[] { "文件夹", "Folder" },
            ["transitFileParts"] = new[] { "文件名称", "File Name" },
            ["transitPartType"] = new[] { "类型", "Type" },
            ["transitSourceIdx"] = new[] { "原始序号", "Original index" },
            ["transitFormat"] = new[] { "格式", "Format" },
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
