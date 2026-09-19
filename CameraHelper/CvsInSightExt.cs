using System;
using System.Threading.Tasks;
using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;

namespace CameraHelper;

public class CvsInSightExt
{
	private string lastURL = string.Empty;

	public CvsInSight InSight { get; private set; }

	public InSightConfig Config { get; set; } = new InSightConfig();

	public int Index => Config.Index;

	public bool IsConnected => InSight.Connected;

	public bool IsConnecting => InSight.Connecting;

	public bool IsNotBusy
	{
		get
		{
			if (InSight.Connected && !InSight.EditorAttached)
			{
				return !InSight.JobLoading;
			}
			return false;
		}
	}

	public bool IsLive => InSight.LiveMode;

	public bool IsOnLine => InSight.Online;

	public CvsInSightExt()
	{
		InSight = new CvsInSight();
		InSight.ResultsChanged += _inSight_ResultsChanged;
	}

	public async Task Connect()
	{
		if (!InSight.Connected)
		{
			HmiSessionInfo sessionInfo = new HmiSessionInfo();
			sessionInfo.SheetName = "Inspection";
			sessionInfo.CellNames = new string[1] { "A0:Z599" };
			sessionInfo.EnableQueuedResults = true;
			sessionInfo.IncludeCustomView = true;
			await InSight.Connect(Config.Address + ":" + Config.Port, Config.UserName, Config.Password, sessionInfo);
		}
	}

	private async void _inSight_ResultsChanged(object sender, EventArgs e)
	{
		try
		{
			_ = 1;
			try
			{
				if (IsLive || !IsOnLine)
				{
					goto end_IL_0045;
				}
				string url = InSight.GetMainImageUrl();
				if (lastURL == url)
				{
					goto end_IL_0045;
				}
				lastURL = url;
				InSightRecord record = new InSightRecord
				{
					CameraIndex = Config.Index,
					CameraName = InSight.CameraInfo.HostName,
					JobName = InSight.JobInfo["name"].ToString()
				};
				CvsCogShape[] shapes = await InSight.GetGraphicsAsync();
				InSightRecord inSightRecord = record;
				inSightRecord.SourceImage = await InSight.GetMainImage();
				ProjectMgr.Inst.ActionQueueWorker.Enqueue(new CreateImageAction
				{
					Configure = Config,
					Record = record,
					Shapes = shapes,
					Token = InSight.Results.DeepClone(),
					Viewport = InSight.ViewPort,
					ImageOffsetY = InSight.ImageOffsetY,
					UsesXYCoordinates = InSight.UsesXYCoordinates
				});
				goto end_IL_0033;
				end_IL_0045:;
			}
			catch (Exception ex)
			{
				LogMgr.Log(Config.Address + "结果返回异常: " + ex.Message);
				goto end_IL_0033;
			}
			end_IL_0033:;
		}
		finally
		{
			try
			{
				await InSight.SendReady().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				await InSight.Disconnect();
			}
		}
	}
}
