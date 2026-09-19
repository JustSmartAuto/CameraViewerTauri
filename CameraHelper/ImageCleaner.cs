using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CameraHelper;

public class ImageCleaner : IDisposable
{
	private Timer timer;

	private Thread thread;

	private FileIterator fileIterator;

	private List<string> DirPaths = new List<string>();

	public CleanConfig Config { get; set; } = new CleanConfig();

	public bool IsCleaning { get; protected set; }

	public void StartClean()
	{
		int ms = (int)(Config.ScanInterval * 3600000.0);
		timer?.Dispose();
		if (Config.IsEnable)
		{
			timer = new Timer(TimerCallBack, "", ms, ms);
		}
	}

	public void StopClean()
	{
		timer?.Dispose();
		thread?.Abort();
		IsCleaning = false;
	}

	private void TimerCallBack(object state)
	{
		CleanOnce();
	}

	public void CleanOnce()
	{
		if (IsCleaning || string.IsNullOrEmpty(Config.FolderPath) || !Directory.Exists(Config.FolderPath))
		{
			return;
		}
		thread?.Abort();
		thread = new Thread((ThreadStart)delegate
		{
			IsCleaning = true;
			try
			{
				string pathRoot = Path.GetPathRoot(Config.FolderPath);
				fileIterator = new FileIterator();
				fileIterator.GetFiles(Config.FolderPath);
				List<FileInfo> list = new List<FileInfo>();
				foreach (FileInfo current in fileIterator.FileInfos)
				{
					if (list.Count == 0)
					{
						list.Add(current);
					}
					else if (current.CreationTime <= list.First().CreationTime)
					{
						list.Insert(0, current);
					}
					else if (current.CreationTime >= list.Last().CreationTime)
					{
						list.Add(current);
					}
					else
					{
						for (int i = 0; i < list.Count - 1; i++)
						{
							FileInfo fileInfo = list[i];
							FileInfo fileInfo2 = list[i + 1];
							if (current.CreationTime >= fileInfo.CreationTime && current.CreationTime <= fileInfo2.CreationTime)
							{
								list.Insert(i + 1, current);
								break;
							}
						}
					}
				}
				foreach (FileInfo current2 in list)
				{
					Thread.Sleep(Config.DeleteInterval);
					double totalSizeGB;
					double freeSizeGB;
					if (Config.IsEnableHoldDays && (DateTime.Now - current2.CreationTime).TotalDays > (double)Config.HoldDays && File.Exists(current2.FullName))
					{
						File.Delete(current2.FullName);
					}
					else if (Config.IsEnableRemainingSpace && GetDiskPercent(pathRoot, out totalSizeGB, out freeSizeGB) && freeSizeGB < Config.RemainingSpace && File.Exists(current2.FullName))
					{
						File.Delete(current2.FullName);
					}
				}
				CleanEmptyDirectory();
			}
			catch
			{
			}
			finally
			{
				IsCleaning = false;
			}
		})
		{
			IsBackground = true,
			Priority = ThreadPriority.Lowest
		};
		thread.Start();
	}

	public void CleanEmptyDirectory()
	{
		while (true)
		{
			DirPaths.Clear();
			DirectoryInfo[] directories = new DirectoryInfo(Config.FolderPath).GetDirectories();
			foreach (DirectoryInfo folder in directories)
			{
				GetEmptyDirectory(folder.FullName);
			}
			if (DirPaths.Count == 0)
			{
				break;
			}
			foreach (string tmp in DirPaths)
			{
				try
				{
					if (Directory.Exists(tmp))
					{
						Directory.Delete(tmp);
					}
				}
				catch
				{
				}
			}
		}
	}

	private void GetEmptyDirectory(string path)
	{
		DirectoryInfo dirs = new DirectoryInfo(path);
		DirectoryInfo[] directories = dirs.GetDirectories();
		string[] files = Directory.GetFiles(dirs.FullName);
		if (directories.Length == 0 && files.Length == 0)
		{
			DirPaths.Add(dirs.FullName);
		}
		DirectoryInfo[] array = directories;
		foreach (DirectoryInfo tDir in array)
		{
			GetEmptyDirectory(tDir.FullName);
		}
	}

	private bool GetDiskPercent(string diskName, out double totalSizeGB, out double freeSizeGB)
	{
		totalSizeGB = (freeSizeGB = 0.0);
		if (string.IsNullOrWhiteSpace(diskName))
		{
			return false;
		}
		try
		{
			DriveInfo[] drives = DriveInfo.GetDrives();
			if (drives.Length == 0)
			{
				return false;
			}
			DriveInfo[] array = drives;
			foreach (DriveInfo drive in array)
			{
				if (drive.Name == diskName)
				{
					totalSizeGB = (double)drive.TotalSize / 1073741824.0;
					freeSizeGB = (double)drive.TotalFreeSpace / 1073741824.0;
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public void Dispose()
	{
		timer?.Dispose();
		thread?.Abort();
	}
}
