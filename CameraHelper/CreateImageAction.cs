using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
using Newtonsoft.Json.Linq;

namespace CameraHelper;

public class CreateImageAction : IAction
{
	public CvsCogShape[] Shapes;

	public JToken Token;

	public CvsCogViewPort Viewport;

	public int ImageOffsetY;

	public bool UsesXYCoordinates;

	public InSightConfig Configure { get; set; }

	public InSightRecord Record { get; set; }

	public void RunAction()
	{
		try
		{
			if (!(Token["cells"] is JArray { Count: var numCells } cellsObj))
			{
				return;
			}
			DateTime now = DateTime.Now;
			HmiCellResult[] cells = new HmiCellResult[numCells];
			for (int index = 0; index < numCells; index++)
			{
				string cellObj = cellsObj[index].ToString();
				HmiCellResult cell = CvsInSight.JsonSerializer.DeserializeObject(cellObj) as HmiCellResult;
				cells[index] = cell;
			}
			if (!string.IsNullOrEmpty(Configure.ShotKeyCellName))
			{
				HmiCellResult cell2 = cells.FirstOrDefault((HmiCellResult t) => t?.Location == Configure.ShotKeyCellName || t?.Name == Configure.ShotKeyCellName);
				if (cell2 != null)
				{
					Record.ShotKey = cell2.Value.ToString();
				}
			}
			if (!string.IsNullOrEmpty(Configure.ResultCellName))
			{
				HmiCellResult cell3 = cells.FirstOrDefault((HmiCellResult t) => t?.Location == Configure.ResultCellName || t?.Name == Configure.ResultCellName);
				if (cell3 != null)
				{
					Record.Result = Convert.ToInt32(cell3.Value);
				}
			}
			string jobname = Record.JobName.TrimStart('\\');
			Record.SrcImagePath = Path.Combine(ProjectMgr.SourceImageDir, now.ToString("yyyy_MM"), now.ToString("dd"), Record.CameraName, jobname, (Record.Result == 1) ? "OK" : "NG", string.Format("{0}_{1:yyyyMMdd_HHmmss_fff}.jpg", (Record.Result == 1) ? "OK" : "NG", now));
			Record.DumpImagePath = Path.Combine(ProjectMgr.ResultImageDir, now.ToString("yyyy_MM"), now.ToString("dd"), Record.CameraName, jobname, (Record.Result == 1) ? "OK" : "NG", string.Format("{0}_{1:yyyyMMdd_HHmmss_fff}.jpg", (Record.Result == 1) ? "OK" : "NG", now));
			if (!string.IsNullOrEmpty(Configure.SrcImagePathCellName))
			{
				HmiCellResult cell4 = cells.FirstOrDefault((HmiCellResult t) => t?.Location == Configure.SrcImagePathCellName || t?.Name == Configure.SrcImagePathCellName);
				if (cell4 != null)
				{
					Record.SrcImagePath = cell4.Value.ToString();
				}
			}
			if (!string.IsNullOrEmpty(Configure.DumpImagePathCellName))
			{
				HmiCellResult cell5 = cells.FirstOrDefault((HmiCellResult t) => t?.Location == Configure.DumpImagePathCellName || t?.Name == Configure.DumpImagePathCellName);
				if (cell5 != null)
				{
					Record.DumpImagePath = cell5.Value.ToString();
				}
			}
			int imageWidth = 800;
			int imageHeight = 600;
			CvsCogViewPort viewPort = Viewport;
			if (viewPort != null)
			{
				imageWidth = Math.Max(viewPort.Width / 2, 1);
				imageHeight = Math.Max(viewPort.Height / 2, 1);
			}
			int picWidth = imageWidth;
			int picHeight = imageHeight;
			float xMax = imageWidth;
			float yMax = imageHeight;
			int startX = 0;
			int startY = 0;
			int endX = imageWidth;
			int endY = imageHeight;
			float num = xMax / yMax;
			float containerRatio = (float)picWidth / (float)picHeight;
			float scaleFactor;
			if (num >= containerRatio)
			{
				scaleFactor = (float)picWidth / xMax;
				float scaledHeight = yMax * scaleFactor;
				float filler = Math.Abs((float)picHeight - scaledHeight) / 2f;
				startX = (int)((float)startX * scaleFactor);
				endX = (int)((float)endX * scaleFactor);
				startY = (int)((float)startY * scaleFactor + filler);
				endY = (int)((float)endY * scaleFactor + filler);
			}
			else
			{
				scaleFactor = (float)picHeight / yMax;
				float scaledWidth = xMax * scaleFactor;
				float filler2 = Math.Abs((float)picWidth - scaledWidth) / 2f;
				startX = (int)((float)startX * scaleFactor + filler2);
				endX = (int)((float)endX * scaleFactor + filler2);
				startY = (int)((float)startY * scaleFactor);
				endY = (int)((float)endY * scaleFactor);
			}
			scaleFactor /= 2f;
			startY -= (int)((float)ImageOffsetY * scaleFactor);
			DisplayContext dc = new DisplayContext(UsesXYCoordinates, scaleFactor, 0, 0, new Rectangle(0, 0, imageWidth, imageHeight));
			Bitmap img = new Bitmap(imageWidth, imageHeight);
			if (Configure.EnableRotaion)
			{
				using (Graphics g = Graphics.FromImage(img))
				{
					g.DrawImage(Record.SourceImage, new Rectangle(0, 0, imageWidth, imageHeight), new Rectangle(0, 0, Record.SourceImage.Width, Record.SourceImage.Height), GraphicsUnit.Pixel);
					GraphicsHelper.DrawGraphics(g, Shapes, dc, hasText: false);
				}
				int r = 0;
				if (Configure.Rotation == ERotation.R90)
				{
					img.RotateFlip(RotateFlipType.Rotate90FlipNone);
					r = 90;
				}
				else if (Configure.Rotation == ERotation.R180)
				{
					img.RotateFlip(RotateFlipType.Rotate180FlipNone);
					r = 180;
				}
				else if (Configure.Rotation == ERotation.R270)
				{
					img.RotateFlip(RotateFlipType.Rotate270FlipNone);
					r = 270;
				}
				using Graphics gr = Graphics.FromImage(img);
				CvsCogShape[] shapes = Shapes;
				for (int num2 = 0; num2 < shapes.Length; num2++)
				{
					if (!(shapes[num2] is CvsCogText txt))
					{
						continue;
					}
					if (Configure.EnableRotaion)
					{
						if (txt.Text.StartsWith(Configure.CharWithoutRotation))
						{
							GraphicsHelper.DrawText(gr, txt, dc, txt.Color, txt.BackgroundColor);
						}
						else
						{
							GraphicsHelper.DrawRotationText(gr, txt, dc, txt.Color, txt.BackgroundColor, new PointF(imageWidth / 2, imageHeight / 2), r);
						}
					}
					else
					{
						GraphicsHelper.DrawText(gr, txt, dc, txt.Color, txt.BackgroundColor);
					}
				}
			}
			else
			{
				using Graphics g2 = Graphics.FromImage(img);
				g2.DrawImage(Record.SourceImage, new Rectangle(0, 0, imageWidth, imageHeight), new Rectangle(0, 0, Record.SourceImage.Width, Record.SourceImage.Height), GraphicsUnit.Pixel);
				GraphicsHelper.DrawGraphics(g2, Shapes, dc, hasText: true);
			}
			Record.DumpImage = img;
			ProjectMgr.Inst.SetRecordChanged(Record);
		}
		catch (Exception ex)
		{
			LogMgr.Log("结果图像绘制异常：" + ex.Message + "\r\n" + ex.StackTrace);
		}
	}
}
