using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cognex.InSight.Web;
using NetFwTypeLib;

namespace CameraHelper;

public class ProjectMgr
{
	private static ProjectMgr mgr;

	public SysConfig SysConfig = new SysConfig();

	public ActionQueueWorker ActionQueueWorker = new ActionQueueWorker();

	public FrmMain MainForm;

	public bool IsSetting;

	private FrmGrid frmGrid;

	private FrmHistory frmHistory;

	private FrmHMI frmHMI;

	private FrmOperation frmOperation;

	private FrmSysSetting frmSysSetting;

	private FrmViewSetting frmViewSetting;

	public List<InSightConfig> CameraConfigs = new List<InSightConfig>();

	public List<CvsInSightExt> Cameras = new List<CvsInSightExt>();

	public List<CameraViewConfig> ViewConfigs = new List<CameraViewConfig>();

	public List<CameraView> Views = new List<CameraView>();

	public ImageCleaner ImageCleaner = new ImageCleaner();

	public static ProjectMgr Inst
	{
		get
		{
			if (mgr == null)
			{
				mgr = new ProjectMgr();
			}
			return mgr;
		}
	}

	public bool IsOnLine { get; set; }

	public string AppStartupPath => Application.StartupPath;

	public string AppConfigPath => Path.Combine(AppStartupPath, "Config");

	public string SystemConfigPath => Path.Combine(AppConfigPath, "SystemConfig.txt");

	public static string SourceImageDir
	{
		get
		{
			if (Directory.Exists("D:\\"))
			{
				return "D:\\Images\\SourceImages";
			}
			return "C:\\Images\\SourceImages";
		}
	}

	public static string ResultImageDir
	{
		get
		{
			if (Directory.Exists("D:\\"))
			{
				return "D:\\Images\\ResultImages";
			}
			return "C:\\Images\\ResultImages";
		}
	}

	public string CamConfigPath => Path.Combine(AppConfigPath, "CameraConfig.txt");

	public string ViewConfigsPath => Path.Combine(AppConfigPath, "ViewConfig.txt");

	public CleanConfig CleanConfig => ImageCleaner.Config;

	public string CleanConfigsPath => Path.Combine(AppConfigPath, "CleanConfig.txt");

	public void LoadSysConfig()
	{
		try
		{
			SysConfig = JsonHelper.LoadJson<SysConfig>(SystemConfigPath);
		}
		catch
		{
		}
	}

	public void SaveSysConfig()
	{
		JsonHelper.SaveJson(SysConfig, SystemConfigPath);
	}

	public Rectangle GetScreenBounds(int index)
	{
		return Screen.AllScreens[index].Bounds;
	}

	public int GetScreenHeight(int index = -1)
	{
		if (index == -1)
		{
			return Screen.PrimaryScreen.Bounds.Height;
		}
		return Screen.AllScreens[index].Bounds.Height;
	}

	public void ShowFrmGrid(CvsInSight cam)
	{
		try
		{
			frmGrid?.Close();
			frmGrid = new FrmGrid(cam);
			frmGrid.Owner = MainForm;
			frmGrid.Show();
		}
		catch
		{
		}
	}

	public void ShowFrmHistory(string path)
	{
		try
		{
			frmHistory?.Close();
			frmHistory = new FrmHistory(path);
			frmHistory.Owner = MainForm;
			frmHistory.Show();
		}
		catch
		{
		}
	}

	public void ShowFrmHMI(InSightConfig config)
	{
		try
		{
			frmHMI?.Close();
			frmHMI = new FrmHMI(config);
			frmHMI.Owner = MainForm;
			frmHMI.Show();
		}
		catch
		{
		}
	}

	public void ShowFrmOperation()
	{
		try
		{
			frmOperation?.Close();
			frmOperation = new FrmOperation();
			frmOperation.Owner = MainForm;
			frmOperation.Show();
		}
		catch
		{
		}
	}

	public void ShowFrmSysSetting()
	{
		try
		{
			frmSysSetting?.Close();
			frmSysSetting = new FrmSysSetting();
			frmSysSetting.Owner = MainForm;
			frmSysSetting.Show();
			IsSetting = true;
		}
		catch
		{
		}
	}

	public void ShowFrmViewSetting(CameraViewConfig config)
	{
		try
		{
			frmViewSetting?.Close();
			frmViewSetting = new FrmViewSetting();
			frmViewSetting.Owner = MainForm;
			frmViewSetting.Init(config);
			frmViewSetting.Show();
		}
		catch
		{
		}
	}

	public void CloseForms()
	{
		frmGrid?.Close();
		frmHistory?.Close();
		frmHMI?.Close();
		frmOperation?.Close();
		frmSysSetting?.Close();
		frmViewSetting?.Close();
	}

	public void LoadCameraConfigs()
	{
		try
		{
			CameraConfigs = JsonHelper.LoadJson<List<InSightConfig>>(CamConfigPath);
		}
		catch
		{
		}
	}

	public void SaveCameraConfigs()
	{
		try
		{
			JsonHelper.SaveJson(CameraConfigs, CamConfigPath);
		}
		catch
		{
		}
	}

	public void CreateCameras()
	{
		Cameras.ForEach(async delegate(CvsInSightExt t)
		{
			await t.InSight.Disconnect();
		});
		Cameras.Clear();
		foreach (InSightConfig cfg in CameraConfigs)
		{
			CvsInSightExt cam = new CvsInSightExt();
			cam.Config = cfg;
			Cameras.Add(cam);
		}
	}

	public void SetRecordChanged(InSightRecord e)
	{
		foreach (CameraView view in Views)
		{
			if (view.Config.Index == e.CameraIndex)
			{
				if (string.IsNullOrEmpty(e.ShotKey))
				{
					view.AddRecord(e);
					break;
				}
				if (view.Config.ShotKey == e.ShotKey)
				{
					view.AddRecord(e);
					break;
				}
			}
		}
	}

	public async Task CheckCameraConnection()
	{
		try
		{
			foreach (CvsInSightExt item in Cameras)
			{
				if (!item.IsConnected && !item.IsConnecting)
				{
					try
					{
						await item.Connect();
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}

	public void LoadViewConfigs()
	{
		try
		{
			ViewConfigs = JsonHelper.LoadJson<List<CameraViewConfig>>(ViewConfigsPath);
		}
		catch
		{
		}
	}

	public void SaveViewConfigs()
	{
		try
		{
			JsonHelper.SaveJson(ViewConfigs, ViewConfigsPath);
		}
		catch
		{
		}
	}

	public void CreateViewConfigs()
	{
		ViewConfigs.Clear();
		for (int i = 0; i < SysConfig.ViewCount; i++)
		{
			ViewConfigs.Add(new CameraViewConfig());
		}
	}

	public void CreateViews()
	{
		Views.Clear();
		foreach (CameraViewConfig item in ViewConfigs)
		{
			CameraView ctrl = new CameraView();
			ctrl.SetConfig(item);
			Views.Add(ctrl);
		}
	}

	public void LoadCleanConfigs()
	{
		try
		{
			ImageCleaner.Config = JsonHelper.LoadJson<CleanConfig>(CleanConfigsPath);
		}
		catch
		{
		}
	}

	public void SaveCleanConfigs()
	{
		try
		{
			JsonHelper.SaveJson(ImageCleaner.Config, CleanConfigsPath);
		}
		catch
		{
		}
	}

	public static bool OperateDefender(bool isOpen)
	{
		try
		{
			string cmd = ((!isOpen) ? "reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows Defender\" /v \"DisableAntiSpyware\" /d 1 /t REG_DWORD /f\r\n" : "reg delete \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows Defender\" /v \"DisableAntiSpyware\" /f\r\n");
			cmd = cmd.Trim().TrimEnd('&') + "&exit";
			using (Process p = new Process())
			{
				p.StartInfo.FileName = "C:\\Windows\\System32\\cmd.exe";
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.RedirectStandardInput = true;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.RedirectStandardError = true;
				p.StartInfo.CreateNoWindow = true;
				p.Start();
				p.StandardInput.WriteLine(cmd);
				p.StandardInput.AutoFlush = true;
				p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				p.Close();
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool OperateFirewall(bool isOpen)
	{
		try
		{
			INetFwPolicy2 obj = (INetFwPolicy2)Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
			obj.set_FirewallEnabled(NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PRIVATE, isOpen);
			obj.set_FirewallEnabled(NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PUBLIC, isOpen);
			obj.set_FirewallEnabled(NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_DOMAIN, isOpen);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
