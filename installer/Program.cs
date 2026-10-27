// SvgOverlayPlugin 一键安装/升级器 (.NET Framework 4.8)
// 双击运行: 将内嵌的 SvgOverlayPlugin.dll 安装到系统 nomacs 插件目录。
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SvgOverlayInstaller
{
    internal static class Program
    {
        private const string PluginFileName = "SvgOverlayPlugin.dll";
        private const string PluginDirName = "SvgOverlayPlugin";

        [STAThread]
        private static int Main()
        {
            try
            {
                string nomacsDir = FindNomacsDir();
                if (nomacsDir == null)
                {
                    Fail("未找到 nomacs 安装目录。\n\n" +
                         "查找范围:\n" +
                         "  • 注册表卸载项 (nomacs MSI 不写 InstallLocation)\n" +
                         "  • C:\\Program Files\\nomacs\\bin\\nomacs.exe\n" +
                         "  • C:\\Program Files (x86)\\nomacs\\bin\\nomacs.exe\n" +
                         "  • LocalAppData\\Programs\\nomacs\n" +
                         "  • 安装器所在目录\n\n" +
                         "请确认 nomacs 已安装, 或把本程序放到 nomacs 安装目录运行。");
                    return 1;
                }

                // nomacs loads plugins from <nomacs>\bin\plugins (applicationDirPath + "/plugins"),
                // top-level *.dll only
                string targetDir = Path.Combine(nomacsDir, "bin", "plugins");
                if (!TryCreateDir(targetDir))
                {
                    // 无写权限(如 C:\Program Files 且非管理员) -> 用户目录
                    targetDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "nomacs", "bin", "plugins");
                    Directory.CreateDirectory(targetDir);
                }

                // 清理旧版本安装位置
                foreach (string old in new[]
                {
                    Path.Combine(targetDir, PluginDirName, PluginFileName),
                    Path.Combine(nomacsDir, "plugins", PluginFileName)
                })
                {
                    if (File.Exists(old))
                    {
                        try { File.Delete(old); } catch { }
                        try { Directory.Delete(Path.GetDirectoryName(old)); } catch { }
                    }
                }

                string targetFile = Path.Combine(targetDir, PluginFileName);
                string source = ExtractEmbeddedDll();
                File.Copy(source, targetFile, true);

                MessageBox.Show(
                    "SvgOverlayPlugin 安装/升级完成!\n\n" +
                    "安装位置: " + targetFile + "\n\n" +
                    "nomacs 下次启动后, 在 编辑→设置→插件 中启用 SvgOverlayPlugin,\n" +
                    "然后通过插件菜单 Toggle SVG Overlay 开启叠加显示。",
                    "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                Fail("安装失败: " + ex.Message);
                return 1;
            }
        }

        // 内嵌资源 -> 临时文件
        private static string ExtractEmbeddedDll()
        {
            string tmp = Path.Combine(Path.GetTempPath(), PluginFileName);
            using (Stream res = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("SvgOverlayInstaller." + PluginFileName))
            {
                if (res == null)
                    throw new Exception("内嵌资源缺失 (资源未正确嵌入)");
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    res.CopyTo(fs);
            }
            return tmp;
        }

        private static string FindNomacsDir()
        {
            // 1) 注册表卸载项
            string[] keys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (string k in keys)
                {
                    using (RegistryKey rk = root.OpenSubKey(k))
                    {
                        if (rk == null) continue;
                        foreach (string sub in rk.GetSubKeyNames())
                        {
                            using (RegistryKey app = rk.OpenSubKey(sub))
                            {
                                if (app == null) continue;
                                string name = app.GetValue("DisplayName") as string;
                                if (name == null || !name.StartsWith("nomacs", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                string dir = app.GetValue("InstallLocation") as string;
                                if (string.IsNullOrEmpty(dir))
                                {
                                    // MSI installers (nomacs uses MSI) don't populate InstallLocation.
                                    // UninstallString is "MsiExec.exe /X{...}" – useless. Skip it.
                                }
                                if (!string.IsNullOrEmpty(dir))
                                {
                                    dir = dir.Trim('"');
                                    // nomacs installs as <root>\bin\nomacs.exe, not <root>\nomacs.exe
                                    if (File.Exists(Path.Combine(dir, "bin", "nomacs.exe")))
                                        return dir;
                                    if (File.Exists(Path.Combine(dir, "nomacs.exe")))
                                        return dir;
                                }
                            }
                        }
                    }
                }
            }

            // 2) 常见路径 – 必须绕过 WOW64 重定向:
            //    AnyCPU 进程在 64 位系统上跑 32 位时，Environment.SpecialFolder.ProgramFiles
            //    返回 Program Files (x86)，但 nomacs 64 位 MSI 装到了真正的 Program Files.
            string[] candidates =
            {
                // 直接拼两个 Program Files 路径，避免 WOW64 重定向
                @"C:\Program Files\nomacs",
                @"C:\Program Files (x86)\nomacs",
                // 用 Environment 变量，仅作后备
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nomacs"),
                // 直接的 bin 目录（便携版/手动解压）
                @"C:\Program Files\nomacs\bin",
                @"C:\Program Files (x86)\nomacs\bin"
            };
            foreach (string c in candidates)
            {
                if (File.Exists(Path.Combine(c, "bin", "nomacs.exe")))
                    return c;
                if (File.Exists(Path.Combine(c, "nomacs.exe")))
                {
                    // 直接命中 bin 目录 – 往上一级返回根，插件仍然放 <root>\bin\plugins
                    string parent = Path.GetDirectoryName(c);
                    return parent ?? c;
                }
            }

            // 3) 与安装器同目录 (setup.exe 可能放在 bin 下或根目录)
            string here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (File.Exists(Path.Combine(here, "nomacs.exe")))
            {
                string parent = Path.GetDirectoryName(here);
                return parent ?? here;
            }
            if (File.Exists(Path.Combine(here, "bin", "nomacs.exe")))
                return here;

            return null;
        }

        private static bool TryCreateDir(string dir)
        {
            try { Directory.CreateDirectory(dir); return true; }
            catch { return false; }
        }

        private static void Fail(string msg)
        {
            MessageBox.Show(msg, "SvgOverlayPlugin 安装器", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
