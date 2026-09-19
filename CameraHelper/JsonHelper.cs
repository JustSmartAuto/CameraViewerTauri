using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace CameraHelper;

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
		string ext = Path.GetExtension(path);
		string name = Path.GetFileNameWithoutExtension(path);
		string dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}
		string tContent = JsonConvert.SerializeObject(json, Formatting.Indented);
		string tTmpPath = Path.Combine(dir, name + "TmpPrj" + ext);
		try
		{
			File.WriteAllText(tTmpPath, tContent, encoding);
			File.WriteAllText(path, tContent, encoding);
		}
		finally
		{
			if (File.Exists(tTmpPath))
			{
				File.Delete(tTmpPath);
			}
		}
	}

	public static void SaveJson<T>(T json, string path)
	{
		SaveJson(json, path, Encoding.UTF8);
	}
}
