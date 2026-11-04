using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace WebHMI;

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
		foreach (FileInfo item in files)
		{
			FileInfos.Add(item);
		}
	}

	private void GetDirFile(string path)
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(path);
		if (!directoryInfo.Exists)
		{
			return;
		}
		GetFileName(directoryInfo);
		Thread.Sleep(5);
		if (IsTraverseSub)
		{
			DirectoryInfo[] directories = directoryInfo.GetDirectories();
			foreach (DirectoryInfo directoryInfo2 in directories)
			{
				GetDirFile(directoryInfo2.FullName);
			}
		}
	}
}
