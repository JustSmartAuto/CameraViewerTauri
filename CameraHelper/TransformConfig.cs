using System.Collections.Generic;

namespace CameraHelper;

public class TransformConfig
{
	public string Name { get; set; } = "";

	public string WatchPath { get; set; } = "";

	public string SourceSpliter { get; set; } = ",";

	public int FolderIndex { get; set; }

	public string FolderSpliter { get; set; } = "+";

	public List<PartItem> FolderPartList { get; set; } = new List<PartItem>();
}
