using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace WebHMI;

internal class JsonHelper
{
	public static T LoadJson<T>(string path, Encoding encoding)
	{
		if (!File.Exists(path))
		{
			throw new Exception("文件不存在");
		}
		string value = File.ReadAllText(path, encoding);
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new Exception("文件内容为空");
		}
		return JsonConvert.DeserializeObject<T>(value);
	}

	public static T LoadJson<T>(string path)
	{
		return LoadJson<T>(path, Encoding.Default);
	}

	public static void SaveJson<T>(T json, string path, Encoding encoding)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new Exception("路径不能为空");
		}
		string extension = Path.GetExtension(path);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directoryName) && !Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string contents = JsonConvert.SerializeObject(json, Formatting.Indented);
		string path2 = Path.Combine(directoryName, fileNameWithoutExtension + "TmpPrj" + extension);
		try
		{
			File.WriteAllText(path2, contents, encoding);
			File.WriteAllText(path, contents, encoding);
		}
		finally
		{
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
		}
	}

	public static void SaveJson<T>(T json, string path)
	{
		SaveJson(json, path, Encoding.UTF8);
	}
}
