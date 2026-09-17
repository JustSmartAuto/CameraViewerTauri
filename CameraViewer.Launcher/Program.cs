using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace CameraViewer.Launcher
{
    internal static class Program
    {
        private const string RuntimeResource = "DotNet8Runtime.exe";
        private const string AppResource = "CameraViewerDotnet.exe";

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
                        if (!RuntimeInstalled())
                        {
                            SetStep(form, "正在安装 .NET8 运行时...", "Installing .NET 8 runtime...");
                            int code = await InstallRuntimeAsync(form);
                            if (code != 0)
                            {
                                MessageBox.Show(form,
                                    ".NET 8 运行时安装失败，退出码：" + code,
                                    "CameraViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                Application.Exit();
                                return;
                            }
                        }

                        SetStep(form, "正在启动软件...", "Starting CameraViewer...");
                        string appDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "CameraViewerDotnet", "App");
                        Directory.CreateDirectory(appDir);
                        string appPath = Path.Combine(appDir, "CameraViewerDotnet.exe");
                        ExtractResource(AppResource, appPath);

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
                        MessageBox.Show(form, "启动失败：" + ex.Message, "CameraViewer",
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

        private static bool RuntimeInstalled()
        {
            var dotnet = FindDotnet();
            if (dotnet == null) return false;

            var psi = new ProcessStartInfo
            {
                FileName = dotnet,
                Arguments = "--list-runtimes",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    return output.Contains("Microsoft.WindowsDesktop.App 8.");
                }
            }
            catch
            {
                return false;
            }
        }

        private static string FindDotnet()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-runtimes",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                    if (p.ExitCode == 0) return "dotnet";
                }
            }
            catch { }

            string defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "dotnet.exe");
            if (File.Exists(defaultPath)) return defaultPath;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localPath = Path.Combine(localAppData, "Microsoft", "dotnet", "dotnet.exe");
            if (File.Exists(localPath)) return localPath;

            return null;
        }

        private static async System.Threading.Tasks.Task<int> InstallRuntimeAsync(LauncherForm form)
        {
            string temp = Path.Combine(Path.GetTempPath(), "CameraViewer-dotnet8-runtime.exe");
            ExtractResource(RuntimeResource, temp);

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

        private static void ExtractResource(string name, string dest)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException("内嵌资源缺失：" + name);
                if (File.Exists(dest)) TryDeleteFile(dest);
                using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        /// <summary>删除文件，若被占用则先结束残留 CameraViewerDotnet 进程再重试。</summary>
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
                    foreach (var proc in Process.GetProcessesByName("CameraViewerDotnet"))
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
