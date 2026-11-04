using System.Collections.Generic;

namespace WebHMI;

public class CameraConfig
{
	public int Count { get; set; } = 1;

	public int Delay { get; set; } = 10;

	public List<CamConfigItem> Items { get; set; } = new List<CamConfigItem>();
}
