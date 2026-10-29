using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace CognexLogging.Launcher
{
    /// <summary>
    /// .NET Framework 4.8 单文件启动器（Win10 工控机兼容）：
    ///   1. 优先解压内嵌的精简 JRE（payload/jre.zip）到 %LOCALAPPDATA%\CognexLogging\jre\；
    ///   2. 解压内嵌的 fat jar（payload/app.jar）到 %LOCALAPPDATA%\CognexLogging\app\；
    ///   3. 用内嵌 javaw.exe 启动 fat jar（无需系统预装 Java）；
    ///   4. 若缺少内嵌 JRE（开发态），fallback 到 FindJava() 检测系统 Java 25+。
    /// </summary>
    internal static class Program
    {
        private const string JreZipResource = "jre.zip";
        private const string AppJarResource = "app.jar";
        private const string AppDirName = "CognexLogging";
        private const string AppJarName = "audit-log-server.jar";
        private const int RequiredMajor = 25;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var form = new LauncherForm())
            {
                form.Shown += (s, e) =>
                {
                    try { Launch(form); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(form,
                            "启动失败：" + ex.Message + "\n\n" + ex,
                            "Cognex相机日志服务器",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Application.Exit();
                    }
                };
                Application.Run(form);
            }
            return 0;
        }

        private static void Launch(LauncherForm form)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDirName);
            string appDir = Path.Combine(root, "app");
            string jreDir = Path.Combine(root, "jre");
            Directory.CreateDirectory(appDir);

            // --- 1. 准备 JRE：优先内嵌 jre.zip ---
            string javaExe = null;
            bool embeddedJre = HasEmbeddedResource(JreZipResource);
            if (embeddedJre)
            {
                SetStep(form, "正在解压 Java 运行时...", "Extracting embedded JRE...");
                ExtractJreIfNeeded(jreDir);
                javaExe = Path.Combine(jreDir, "bin", "java.exe");
            }

            // 系统 Java fallback
            if (javaExe == null || !File.Exists(javaExe))
            {
                SetStep(form, "正在检测 Java 运行时...", "Locating Java on system...");
                javaExe = FindJava();
                if (javaExe == null)
                {
                    MessageBox.Show(form,
                        "未检测到 Java 25 运行时。\n\n请安装 OpenJDK 25（https://adoptium.net/），" +
                        "或将完整的 JRE 目录放到本软件同目录下的 jre/ 子文件夹。",
                        "Cognex相机日志服务器",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Application.Exit();
                    return;
                }
            }

            // --- 2. 释放 fat jar ---
            SetStep(form, "正在准备应用程序...", "Preparing application...");
            string appJarPath = Path.Combine(appDir, AppJarName);
            ExtractAppJar(appJarPath);

            // --- 3. 启动 javaw ---
            string javaw = Path.Combine(Path.GetDirectoryName(javaExe), "javaw.exe");
            if (!File.Exists(javaw)) javaw = javaExe;

            SetStep(form, "正在启动 Cognex相机日志服务器...", "Starting server...");
            Process.Start(new ProcessStartInfo
            {
                FileName = javaw,
                Arguments = "-jar \"" + appJarPath + "\"",
                WorkingDirectory = appDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            Application.Exit();
        }

        // ---------- JRE 解压 ----------

        /// <summary>判断嵌入资源是否存在（开发态可能没有 jre.zip）。</summary>
        private static bool HasEmbeddedResource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                return s != null;
        }

        /// <summary>解压内嵌 jre.zip 到 jreDir，已解压且结构完整时跳过。</summary>
        private static void ExtractJreIfNeeded(string jreDir)
        {
            string javaMarker = Path.Combine(jreDir, "bin", "java.exe");
            if (File.Exists(javaMarker)) return; // 已解压，跳过

            // 先解压到 temp 再原子替换
            string tempDir = jreDir + "." + DateTime.Now.Ticks;
            string tempZip = Path.GetTempFileName();
            Directory.CreateDirectory(tempDir);
            try
            {
                using (var z = Assembly.GetExecutingAssembly().GetManifestResourceStream(JreZipResource))
                {
                    if (z == null) return;
                    using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
                        z.CopyTo(fs);
                }

                ZipFile.ExtractToDirectory(tempZip, tempDir);

                // zip 可能自带一层顶层目录（如 jre/ 或 zulu25/）——展平
                FlattenSingleTopLevelDir(tempDir);

                if (Directory.Exists(jreDir)) Directory.Delete(jreDir, true);
                Directory.Move(tempDir, jreDir);
            }
            catch
            {
                if (Directory.Exists(tempDir)) { try { Directory.Delete(tempDir, true); } catch { } }
                throw;
            }
            finally
            {
                if (File.Exists(tempZip)) { try { File.Delete(tempZip); } catch { } }
            }
        }

        private static void FlattenSingleTopLevelDir(string dir)
        {
            string[] top = Directory.GetDirectories(dir);
            string[] files = Directory.GetFiles(dir);
            if (top.Length == 1 && files.Length == 0)
            {
                string inner = top[0];
                foreach (string item in Directory.GetFileSystemEntries(inner))
                {
                    string dest = Path.Combine(dir, Path.GetFileName(item));
                    if (Directory.Exists(item)) Directory.Move(item, dest);
                    else File.Move(item, dest);
                }
                Directory.Delete(inner);
            }
        }

        private static void ExtractAppJar(string dest)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(AppJarResource))
            {
                if (s == null) throw new InvalidOperationException("内嵌资源缺失：" + AppJarResource);
                if (File.Exists(dest))
                {
                    try { File.Delete(dest); } catch (IOException) { }
                }
                using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                    s.CopyTo(fs);
            }
        }

        // ---------- 系统 Java 检测（fallback） ----------

        private static string FindJava()
        {
            var candidates = new System.Collections.Generic.List<string>();

            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
                candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));

            string pathJava = FindOnPath("java.exe");
            if (pathJava != null) candidates.Add(pathJava);

            string[] roots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Zulu"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
            };
            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string jdk in Directory.GetDirectories(root))
                    {
                        string j = Path.Combine(jdk, "bin", "java.exe");
                        if (File.Exists(j)) candidates.Add(j);
                    }
                }
                catch { }
            }

            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c) && GetJavaMajor(c) >= RequiredMajor) return c;
                }
                catch { }
            }
            return null;
        }

        private static string FindOnPath(string file)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = file,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string line = p.StandardOutput.ReadLine();
                    p.WaitForExit(10000);
                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(line) && File.Exists(line)) return line;
                }
            }
            catch { }
            return null;
        }

        private static int GetJavaMajor(string javaExe)
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                int idx = output.IndexOf("version \"", StringComparison.Ordinal);
                if (idx < 0) return -1;
                string ver = output.Substring(idx + 9);
                int end = ver.IndexOf('"');
                if (end > 0) ver = ver.Substring(0, end);
                ver = ver.Trim();
                if (ver.StartsWith("1.")) ver = ver.Substring(2);
                int dot = ver.IndexOf('.');
                if (dot > 0) ver = ver.Substring(0, dot);
                int major;
                return int.TryParse(ver, out major) ? major : -1;
            }
        }

        // ---------- UI ----------

        private static void SetStep(LauncherForm form, string zh, string en)
        {
            form.lblStep.Text = zh;
            form.lblDetail.Text = en;
            form.Refresh();
        }
    }

    /// <summary>启动进度窗口（模态，几秒后自动关闭）。</summary>
    internal class LauncherForm : Form
    {
        public Label lblStep;
        public Label lblDetail;

        public LauncherForm()
        {
            Text = "Cognex相机日志服务器";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 160);
            BackColor = Color.White;

            var title = new Label
            {
                Text = "Cognex相机日志服务器",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 22),
            };
            lblStep = new Label
            {
                Text = "正在准备...",
                Font = new Font("Microsoft YaHei UI", 10F),
                AutoSize = true,
                Location = new Point(24, 70),
            };
            lblDetail = new Label
            {
                Text = "Preparing...",
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(24, 96),
            };
            Controls.Add(title);
            Controls.Add(lblStep);
            Controls.Add(lblDetail);
        }
    }
}
