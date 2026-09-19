namespace CameraHelper;

public class CleanConfig
{
	public bool IsEnable { get; set; }

	public string FolderPath { get; set; } = string.Empty;

	public double ScanInterval { get; set; } = 2.0;

	public bool IsCleanEmptyFolders { get; set; } = true;

	public bool IsEnableHoldDays { get; set; }

	public int HoldDays { get; set; } = 30;

	public bool IsEnableRemainingSpace { get; set; } = true;

	public double RemainingSpace { get; set; } = 5.0;

	public int DeleteInterval { get; set; } = 5;
}
