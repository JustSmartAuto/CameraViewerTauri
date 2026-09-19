using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CameraHelper;

public class TransformAction : IAction
{
	public TransformConfig Config;

	public void RunAction()
	{
		try
		{
			if (!Directory.Exists(Config.WatchPath))
			{
				return;
			}
			FileInfo[] fileInfos = new DirectoryInfo(Config.WatchPath).GetFiles();
			if (fileInfos.Length == 0)
			{
				return;
			}
			long curLen = 0L;
			long lastLen = 0L;
			fileInfos.ToList().ForEach(delegate(FileInfo t)
			{
				curLen += t.Length;
			});
			do
			{
				lastLen = curLen;
				Thread.Sleep(100);
				fileInfos.ToList().ForEach(delegate(FileInfo t)
				{
					t.Refresh();
				});
				curLen = 0L;
				fileInfos.ToList().ForEach(delegate(FileInfo t)
				{
					curLen += t.Length;
				});
			}
			while (lastLen < curLen);
			FileInfo[] array = fileInfos;
			foreach (FileInfo info in array)
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(info.FullName);
				Path.GetExtension(info.FullName);
				string[] dirArr = fileNameWithoutExtension.Split(Config.SourceSpliter[0])[Config.FolderIndex].Split(Config.FolderSpliter[0]);
				string pathDir = string.Empty;
				foreach (PartItem item in Config.FolderPartList)
				{
					if (item.PartType == EPartType.Name)
					{
						pathDir = ((item.SourceIndex != 0) ? (pathDir + dirArr[item.SourceIndex] + "\\") : (pathDir + dirArr[item.SourceIndex] + ":\\"));
					}
					else if (item.PartType == EPartType.DateTime)
					{
						pathDir = pathDir + DateTime.Now.ToString(item.Format) + "\\";
					}
				}
				string newPath = pathDir + info.Name;
				if (!Directory.Exists(pathDir))
				{
					Directory.CreateDirectory(pathDir);
				}
				if (File.Exists(newPath))
				{
					File.Delete(newPath);
				}
				File.Move(info.FullName, newPath);
			}
		}
		catch
		{
		}
	}
}
