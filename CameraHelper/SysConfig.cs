namespace CameraHelper;

public class SysConfig
{
	public bool AlwaysTop { get; set; }

	public bool IsSelectScreen { get; set; }

	public int ScreenIndex { get; set; }

	public int Language { get; set; }

	public int ViewCount { get; set; } = 2;

	public int ReviewMaxCount { get; set; } = 20;

	public bool IsCloseFirewall { get; set; }

	public string ProcessingName { get; set; } = "CameraHelperJimJack";

	public string Theme { get; set; } = "light";
}
