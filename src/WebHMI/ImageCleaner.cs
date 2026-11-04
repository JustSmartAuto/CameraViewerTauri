using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace WebHMI;

public class ImageCleaner : IDisposable
{
	private Timer timer;

	private Thread thread;

	private FileIterator fileIterator;

	private List<string> DirPaths = new List<string>();

	public CleanConfig Config { get; set; }

	public bool IsCleaning { get; protected set; }

	public void StartClean()
	{
		int num = (int)(Config.ScanInterval * 3600000.0);
		timer?.Dispose();
		if (Config.IsEnable)
		{
			timer = new Timer(TimerCallBack, "", num, num);
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
				foreach (FileInfo fileInfo3 in fileIterator.FileInfos)
				{
					if (list.Count == 0)
					{
						list.Add(fileInfo3);
					}
					else if (fileInfo3.CreationTime <= list.First().CreationTime)
					{
						list.Insert(0, fileInfo3);
					}
					else if (fileInfo3.CreationTime >= list.Last().CreationTime)
					{
						list.Add(fileInfo3);
					}
					else
					{
						for (int i = 0; i < list.Count - 1; i++)
						{
							FileInfo fileInfo = list[i];
							FileInfo fileInfo2 = list[i + 1];
							if (fileInfo3.CreationTime >= fileInfo.CreationTime && fileInfo3.CreationTime <= fileInfo2.CreationTime)
							{
								list.Insert(i + 1, fileInfo3);
								break;
							}
						}
					}
				}
				foreach (FileInfo item in list)
				{
					Thread.Sleep(Config.DeleteInterval);
					double totalSizeGB;
					double freeSizeGB;
					if (Config.IsEnableHoldDays && (DateTime.Now - item.CreationTime).TotalDays > (double)Config.HoldDays && File.Exists(item.FullName))
					{
						File.Delete(item.FullName);
					}
					else if (Config.IsEnableRemainingSpace && GetDiskPercent(pathRoot, out totalSizeGB, out freeSizeGB) && freeSizeGB < Config.RemainingSpace && File.Exists(item.FullName))
					{
						File.Delete(item.FullName);
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
			foreach (DirectoryInfo directoryInfo in directories)
			{
				GetEmptyDirectory(directoryInfo.FullName);
			}
			if (DirPaths.Count == 0)
			{
				break;
			}
			foreach (string dirPath in DirPaths)
			{
				try
				{
					if (Directory.Exists(dirPath))
					{
						Directory.Delete(dirPath);
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
		DirectoryInfo directoryInfo = new DirectoryInfo(path);
		DirectoryInfo[] directories = directoryInfo.GetDirectories();
		string[] files = Directory.GetFiles(directoryInfo.FullName);
		if (directories.Length == 0 && files.Length == 0)
		{
			DirPaths.Add(directoryInfo.FullName);
		}
		DirectoryInfo[] array = directories;
		foreach (DirectoryInfo directoryInfo2 in array)
		{
			GetEmptyDirectory(directoryInfo2.FullName);
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
			foreach (DriveInfo driveInfo in array)
			{
				if (driveInfo.Name == diskName)
				{
					totalSizeGB = (double)driveInfo.TotalSize / 1073741824.0;
					freeSizeGB = (double)driveInfo.TotalFreeSpace / 1073741824.0;
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
