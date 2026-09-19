using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CameraHelper;

public class FileIterator
{
	public bool IsTraverseSub = true;

	public List<FileInfo> FileInfos { get; private set; } = new List<FileInfo>();

	public void GetFiles(string path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			FileInfos.Clear();
			GetDirFile(path);
		}
	}

	private void GetFileName(DirectoryInfo dirInfo)
	{
		FileInfo[] files = dirInfo.GetFiles();
		foreach (FileInfo tFileInfo in files)
		{
			FileInfos.Add(tFileInfo);
		}
	}

	private void GetDirFile(string path)
	{
		DirectoryInfo tDirs = new DirectoryInfo(path);
		if (!tDirs.Exists)
		{
			return;
		}
		GetFileName(tDirs);
		Thread.Sleep(5);
		if (IsTraverseSub)
		{
			DirectoryInfo[] directories = tDirs.GetDirectories();
			foreach (DirectoryInfo tDir in directories)
			{
				GetDirFile(tDir.FullName);
			}
		}
	}
}
