using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CameraHelper.Launcher;

internal static class Program
{
	private const string RuntimeResource = "DotNet8Runtime.exe";

	private const string AppDirName = "CameraHelperJimJack";

	private const string AppExeName = "CameraHelperJimJack.exe";

	private const int MinFrameworkRelease = 528040;

	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		using (LauncherForm form = new LauncherForm())
		{
			form.Shown += async (s, e) =>
			{
				try
				{
					// 1. .NET 8 Windows Desktop 运行时（工控机缺失则静默安装）
					if (!RuntimeInstalled())
					{
						form.SetStep("正在安装 .NET 8 运行时...");
						form.SetDetail("Installing .NET 8 Windows Desktop runtime...");
						form.Refresh();
						int installCode = await InstallRuntimeAsync();
						if (installCode != 0)
						{
							MessageBox.Show(form, ".NET 8 运行时安装失败，退出码：" + installCode,
								"CameraHelperJimJack", MessageBoxButtons.OK, MessageBoxIcon.Error);
							Application.Exit();
							return;
						}
					}

					// 2. .NET Framework 4.8（主程序运行时）
					form.SetStep("正在检查运行环境...");
					form.SetDetail("Checking .NET Framework 4.8...");
					form.Refresh();
					if (!IsFramework48Installed())
					{
						MessageBox.Show(form, "本程序需要 .NET Framework 4.8 或更高版本，请先安装后再运行。",
							"缺少运行环境", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						Application.Exit();
						return;
					}

					// 3. 释放内嵌程序文件
					form.SetStep("正在准备程序文件...");
					form.SetDetail("");
					form.Refresh();
					string appDir = GetAppDir();
					int total = ExtractAll(form, appDir);

					// 4. 启动主程序
					form.SetStep("正在启动 CameraHelperJimJack...");
					form.Refresh();
					string exePath = Path.Combine(appDir, AppExeName);
					if (!File.Exists(exePath))
					{
						MessageBox.Show(form, "未找到主程序：" + exePath, "启动失败",
							MessageBoxButtons.OK, MessageBoxIcon.Error);
						Application.Exit();
						return;
					}
					Process.Start(new ProcessStartInfo
					{
						FileName = exePath,
						WorkingDirectory = appDir,
						UseShellExecute = true
					});
					form.SetDetail($"完成（{total} 个文件）");
					Application.Exit();
				}
				catch (Exception ex)
				{
					MessageBox.Show(form, "启动失败：" + ex.Message, "错误",
						MessageBoxButtons.OK, MessageBoxIcon.Error);
					Application.Exit();
				}
			};
			Application.Run(form);
		}
	}

	/// <summary>
	/// 检测是否已安装 .NET 8 Windows Desktop 运行时（dotnet --list-runtimes 输出含 Microsoft.WindowsDesktop.App 8.）。
	/// </summary>
	private static bool RuntimeInstalled()
	{
		string dotnet = FindDotnet();
		if (dotnet == null)
		{
			return false;
		}
		ProcessStartInfo psi = new ProcessStartInfo
		{
			FileName = dotnet,
			Arguments = "--list-runtimes",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		try
		{
			using (Process p = Process.Start(psi))
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

	/// <summary>
	/// 定位 dotnet.exe：PATH → Program Files → LOCALAPPDATA。
	/// </summary>
	private static string FindDotnet()
	{
		ProcessStartInfo psi = new ProcessStartInfo
		{
			FileName = "dotnet",
			Arguments = "--version",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		try
		{
			using (Process p = Process.Start(psi))
			{
				p.WaitForExit(15000);
				if (p.ExitCode == 0)
				{
					return "dotnet";
				}
			}
		}
		catch
		{
		}
		string defaultPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			"dotnet", "dotnet.exe");
		if (File.Exists(defaultPath))
		{
			return defaultPath;
		}
		string localPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Microsoft", "dotnet", "dotnet.exe");
		if (File.Exists(localPath))
		{
			return localPath;
		}
		return null;
	}

	/// <summary>
	/// 释放内嵌安装包到临时目录并静默安装（/install /quiet /norestart），最多等待 10 分钟。
	/// </summary>
	private static async Task<int> InstallRuntimeAsync()
	{
		string temp = Path.Combine(Path.GetTempPath(), "CameraHelperJimJack-dotnet8-runtime.exe");
		ExtractResource(RuntimeResource, temp);
		ProcessStartInfo psi = new ProcessStartInfo
		{
			FileName = temp,
			Arguments = "/install /quiet /norestart",
			UseShellExecute = true
		};
		TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
		using (Process p = new Process { StartInfo = psi, EnableRaisingEvents = true })
		{
			p.Exited += (s, e) => tcs.TrySetResult(p.ExitCode);
			p.Start();
			Task timeout = Task.Delay(TimeSpan.FromMinutes(10));
			Task done = await Task.WhenAny(tcs.Task, timeout);
			if (!ReferenceEquals(done, tcs.Task))
			{
				try
				{
					p.Kill();
				}
				catch
				{
				}
				return -1;
			}
			return tcs.Task.Result;
		}
	}

	private static string GetAppDir()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirName, "App");
	}

	private static bool IsFramework48Installed()
	{
		try
		{
			using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32).OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full"))
			{
				if (key == null)
				{
					return false;
				}
				object value = key.GetValue("Release");
				if (value == null)
				{
					return false;
				}
				return (int)value >= MinFrameworkRelease;
			}
		}
		catch
		{
			return false;
		}
	}

	private static int ExtractAll(LauncherForm form, string appDir)
	{
		string[] names = typeof(Program).Assembly.GetManifestResourceNames().Where((string n) => n.StartsWith("app\\", StringComparison.OrdinalIgnoreCase)).OrderBy((string n) => n).ToArray();
		int count = 0;
		int total = names.Length;
		Directory.CreateDirectory(appDir);
		string[] array = names;
		foreach (string name in array)
		{
			count++;
			string fileName = name.Substring("app\\".Length);
			form.SetDetail($"解压文件 {count}/{total}: {fileName}");
			Application.DoEvents();
			ExtractResource(name, Path.Combine(appDir, fileName));
		}
		return total;
	}

	private static void ExtractResource(string resourceName, string targetPath)
	{
		using (Stream source = typeof(Program).Assembly.GetManifestResourceStream(resourceName))
		{
			if (source == null)
			{
				throw new FileNotFoundException("内嵌资源缺失：" + resourceName);
			}
			string dir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
			}
			if (resourceName.StartsWith("app\\", StringComparison.OrdinalIgnoreCase)
				&& File.Exists(targetPath) && new FileInfo(targetPath).Length == source.Length)
			{
				return;
			}
			string tmp = targetPath + ".tmp";
			TryDeleteFile(tmp);
			TryDeleteFile(targetPath);
			using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
			{
				source.CopyTo(fs);
			}
			File.Copy(tmp, targetPath, overwrite: true);
			TryDeleteFile(tmp);
		}
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
