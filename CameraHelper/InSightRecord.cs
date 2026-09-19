using System.Drawing;

namespace CameraHelper;

public class InSightRecord
{
	public int CameraIndex;

	public string CameraName = string.Empty;

	public string JobName = string.Empty;

	public string ShotKey = string.Empty;

	public Image SourceImage;

	public Image DumpImage;

	public string SrcImagePath = string.Empty;

	public string DumpImagePath = string.Empty;

	public int Result = 1;
}
