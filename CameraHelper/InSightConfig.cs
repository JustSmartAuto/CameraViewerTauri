using Newtonsoft.Json;

namespace CameraHelper;

public class InSightConfig
{
	public int Index { get; set; } = 1;

	public string Address { get; set; } = "127.0.0.1";

	public string Port { get; set; } = "80";

	public string UserName { get; set; } = "admin";

	public string Password { get; set; } = "";

	public string ShotKeyCellName { get; set; } = "";

	public string ResultCellName { get; set; } = "Result";

	public string SrcImagePathCellName { get; set; } = "SourceImagePath";

	public string DumpImagePathCellName { get; set; } = "ResultImagePath";

	[JsonIgnore]
	public bool EnableRotaion => Rotation != ERotation.R0;

	public ERotation Rotation { get; set; }

	public string CharWithoutRotation { get; set; } = "$";
}
