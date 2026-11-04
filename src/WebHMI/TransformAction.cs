using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace WebHMI;

public class TransformAction
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
			FileInfo[] files = new DirectoryInfo(Config.WatchPath).GetFiles();
			if (files.Length == 0)
			{
				return;
			}
			long curLen = 0L;
			long num = 0L;
			files.ToList().ForEach(delegate(FileInfo t)
			{
				curLen += t.Length;
			});
			do
			{
				num = curLen;
				Thread.Sleep(100);
				files.ToList().ForEach(delegate(FileInfo t)
				{
					t.Refresh();
				});
				curLen = 0L;
				files.ToList().ForEach(delegate(FileInfo t)
				{
					curLen += t.Length;
				});
			}
			while (num < curLen);
			FileInfo[] array = files;
			foreach (FileInfo fileInfo in array)
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileInfo.FullName);
				string extension = Path.GetExtension(fileInfo.FullName);
				string[] array2 = fileNameWithoutExtension.Split(Config.SourceSpliter[0]);
				string[] array3 = array2[Config.FolderIndex].Split(Config.FolderSpliter[0]);
				string[] array4 = array2[Config.FileNameIndex].Split(Config.FileNameSpliter[0]);
				string text = string.Empty;
				foreach (PartItem folderPart in Config.FolderPartList)
				{
					if (folderPart.PartType == EPartType.Name)
					{
						text = ((folderPart.SourceIndex != 0) ? (text + array3[folderPart.SourceIndex] + "\\") : (text + array3[folderPart.SourceIndex] + ":\\"));
					}
					else if (folderPart.PartType == EPartType.DateTime)
					{
						text = text + DateTime.Now.ToString(folderPart.Format) + "\\";
					}
				}
				string text2 = string.Empty;
				foreach (PartItem fileNamePart in Config.FileNamePartList)
				{
					if (fileNamePart.PartType == EPartType.Name)
					{
						text2 = text2 + array4[fileNamePart.SourceIndex] + Config.FileNameSpliter;
					}
					else if (fileNamePart.PartType == EPartType.DateTime)
					{
						text2 = text2 + DateTime.Now.ToString(fileNamePart.Format) + Config.FileNameSpliter;
					}
				}
				text2 = text2.Remove(text2.Length - 1);
				text2 += extension;
				string text3 = text + text2;
				if (!Directory.Exists(text))
				{
					Directory.CreateDirectory(text);
				}
				if (File.Exists(text3))
				{
					File.Delete(text3);
				}
				File.Move(fileInfo.FullName, text3);
			}
		}
		catch
		{
		}
	}
}
