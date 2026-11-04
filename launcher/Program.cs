using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace CameraViewerLens.Launcher
{
    internal static class Program
    {
        private const string AppResource = "App.zip";
        private const string AppExeName = "CameraHelper.exe";
        private const string AppRoot = "CameraViewerLens";
        private const string WebView2BootstrapperUrl =
            "https://go.microsoft.com/fwlink/?LinkId=2120703";

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var form = new LauncherForm())
            {
                form.Shown += async (s, e) =>
                {
                    try
                    {
                        if (!WebView2RuntimeInstalled())
                        {
                            SetStep(form, "正在安装 WebView2 运行时...", "Installing WebView2 runtime...");
                            int code = await InstallWebView2RuntimeAsync(form);
                            if (code != 0)
                            {
                                if (code == -2)
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = WebView2BootstrapperUrl,
                                        UseShellExecute = true,
                                    });
                                }
                                MessageBox.Show(form,
                                    "WebView2 运行时安装失败，退出码：" + code,
                                    "CameraViewerLens", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                Application.Exit();
                                return;
                            }
                        }

                        SetStep(form, "正在启动软件...", "Starting CameraViewerLens...");
                        string appDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            AppRoot, "App");
                        Directory.CreateDirectory(appDir);
                        string appPath = Path.Combine(appDir, AppExeName);
                        ExtractApp(appDir);

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = appPath,
                            WorkingDirectory = appDir,
                            UseShellExecute = true,
                        });
                        Application.Exit();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(form, "启动失败：" + ex.Message, "CameraViewerLens",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Application.Exit();
                    }
                };
                Application.Run(form);
            }
            return 0;
        }

        private static void SetStep(LauncherForm form, string zh, string en)
        {
            form.lblStep.Text = zh;
            form.lblDetail.Text = en;
            form.Refresh();
        }

        private static bool WebView2RuntimeInstalled()
        {
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Wow6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            };
            foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                foreach (var key in keys)
                {
                    using (var k = root.OpenSubKey(key))
                    {
                        if (k == null) continue;
                        var pv = k.GetValue("pv") as string;
                        if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return true;
                    }
                }
            }
            return false;
        }

        private static async System.Threading.Tasks.Task<int> InstallWebView2RuntimeAsync(LauncherForm form)
        {
            string temp = Path.Combine(Path.GetTempPath(), "CameraViewerLens-WebView2RuntimeInstaller.exe");
            try
            {
                SetStep(form, "正在下载 WebView2 运行时...", "Downloading WebView2 runtime...");
                using (var client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(WebView2BootstrapperUrl, temp);
                }
            }
            catch
            {
                return -2;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = temp,
                    Arguments = "/install /quiet /norestart",
                    UseShellExecute = true,
                };
                var tcs = new System.Threading.Tasks.TaskCompletionSource<int>();
                using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    p.Exited += (s, e) => tcs.TrySetResult(p.ExitCode);
                    p.Start();
                    var delay = System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(10));
                    var done = await System.Threading.Tasks.Task.WhenAny(tcs.Task, delay);
                    if (done != tcs.Task)
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    return tcs.Task.Result;
                }
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static void ExtractApp(string appDir)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AppResource))
            {
                if (stream == null)
                    throw new InvalidOperationException("内嵌资源缺失：" + AppResource);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string dest = Path.Combine(appDir, entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        if (File.Exists(dest)) TryDeleteFile(dest);
                        entry.ExtractToFile(dest, overwrite: false);
                    }
                }
            }
        }

        /// <summary>删除文件，若被占用则先结束残留 CameraHelper 进程再重试。</summary>
        private static void TryDeleteFile(string path)
        {
            if (!File.Exists(path)) return;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try { File.Delete(path); return; }
                catch (IOException) { /* 文件被占用 */ }
                catch (UnauthorizedAccessException) { /* 权限/占用 */ }
                if (attempt == 0)
                {
                    foreach (var proc in Process.GetProcessesByName("CameraHelper"))
                    {
                        try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    }
                }
                Thread.Sleep(500);
            }
            File.Delete(path);
        }
    }
}
