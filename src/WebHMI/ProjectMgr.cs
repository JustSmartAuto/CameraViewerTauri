using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace WebHMI;

public class ProjectMgr
{
	private static ProjectMgr mgr;

	public CameraConfig CameraConfig = new CameraConfig();

	public List<WebView> CameraWebViews = new List<WebView>();

	public List<TransformConfig> TransformConfigs = new List<TransformConfig>();

	public List<FileTransformer> FileTransformers = new List<FileTransformer>();

	public CleanConfig CleanConfig = new CleanConfig();

	public ImageCleaner Cleaner = new ImageCleaner();

	public static ProjectMgr Inst => mgr ?? (mgr = new ProjectMgr());

	public static string ConfigDir => Path.Combine(Application.StartupPath, "Configs");

	public void LoadConfig()
	{
		LoadCameraConfig();
		LoadCleanConfig();
		LoadTransformConfig();
	}

	public void Close()
	{
		try
		{
			FileTransformers.ForEach(delegate(FileTransformer t)
			{
				t.Close();
			});
			Cleaner?.StopClean();
		}
		catch
		{
		}
	}

	public void LoadCameraConfig()
	{
		try
		{
			string path = Path.Combine(ConfigDir, "CameraConfig.json");
			CameraConfig = JsonHelper.LoadJson<CameraConfig>(path);
		}
		catch
		{
		}
	}

	public void SaveCameraConfig()
	{
		try
		{
			string path = Path.Combine(ConfigDir, "CameraConfig.json");
			JsonHelper.SaveJson(CameraConfig, path);
		}
		catch
		{
		}
	}

	public void CreateCameraConfig(int count)
	{
		CameraConfig.Count = count;
		CameraConfig.Items.Clear();
		for (int i = 0; i < count; i++)
		{
			CamConfigItem item = new CamConfigItem
			{
				ID = i
			};
			CameraConfig.Items.Add(item);
		}
		CreateCameraWebView();
	}

	public void CreateCameraWebView()
	{
		CameraWebViews.Clear();
		foreach (CamConfigItem item in CameraConfig.Items)
		{
			WebView webView = new WebView
			{
				Index = item.ID
			};
			webView.Dock = DockStyle.Fill;
			webView.SetConfig(item);
			CameraWebViews.Add(webView);
		}
	}

	public void LoadTransformConfig()
	{
		try
		{
			string path = Path.Combine(ConfigDir, "TransformConfig.json");
			TransformConfigs = JsonHelper.LoadJson<List<TransformConfig>>(path);
		}
		catch
		{
		}
	}

	public void SaveTransformConfig()
	{
		try
		{
			string path = Path.Combine(ConfigDir, "TransformConfig.json");
			JsonHelper.SaveJson(TransformConfigs, path);
		}
		catch
		{
		}
	}

	public void CreateFileTransform()
	{
		FileTransformers.ForEach(delegate(FileTransformer t)
		{
			t.Close();
		});
		FileTransformers.Clear();
		foreach (TransformConfig transformConfig in TransformConfigs)
		{
			FileTransformer fileTransformer = new FileTransformer();
			fileTransformer.Init(transformConfig);
			fileTransformer.Start();
		}
	}

	public void LoadCleanConfig()
	{
		try
		{
			string path = Path.Combine(ConfigDir, "CleanConfig.json");
			CleanConfig = JsonHelper.LoadJson<CleanConfig>(path);
		}
		catch
		{
		}
		Cleaner.Config = CleanConfig;
	}

	public void SaveCleanConfig()
	{
		try
		{
			string path = Path.Combine(ConfigDir, "CleanConfig.json");
			JsonHelper.SaveJson(CleanConfig, path);
		}
		catch
		{
		}
	}
}
