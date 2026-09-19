using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace CameraHelper;

public class SaveImageAction : IAction
{
	public string SaveSrcPath = string.Empty;

	public Image SaveSrcImage;

	public string SaveDumpPath = string.Empty;

	public Image SaveDumpImage;

	public void RunAction()
	{
		try
		{
			if (!string.IsNullOrEmpty(SaveSrcPath))
			{
				CreateDir(SaveSrcPath);
				if (SaveSrcImage != null)
				{
					new Bitmap(SaveSrcImage).Save(SaveSrcPath, GetImageFormat(SaveSrcPath));
				}
			}
			if (!string.IsNullOrEmpty(SaveDumpPath))
			{
				CreateDir(SaveDumpPath);
				if (SaveDumpImage != null)
				{
					new Bitmap(SaveDumpImage).Save(SaveDumpPath, GetImageFormat(SaveDumpPath));
				}
			}
		}
		catch (Exception ex)
		{
			LogMgr.Log(ex.Message + "\r\n" + ex.StackTrace);
		}
	}

	private void CreateDir(string path)
	{
		string dir = Path.GetDirectoryName(path);
		if (!Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}
	}

	private ImageFormat GetImageFormat(string path)
	{
		string ext = Path.GetExtension(path);
		if (ext.Contains("png"))
		{
			return ImageFormat.Png;
		}
		if (ext.Contains("bmp"))
		{
			return ImageFormat.Bmp;
		}
		return ImageFormat.Jpeg;
	}
}
