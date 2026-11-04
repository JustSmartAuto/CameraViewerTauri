using System.Collections.Generic;

namespace WebHMI;

public class TransformConfig
{
	public string Name { get; set; } = "";

	public string WatchPath { get; set; } = "";

	public string SourceSpliter { get; set; } = ",";

	public int FolderIndex { get; set; }

	public int FileNameIndex { get; set; } = 1;

	public string FolderSpliter { get; set; } = "+";

	public List<PartItem> FolderPartList { get; set; } = new List<PartItem>();

	public string FileNameSpliter { get; set; } = "_";

	public List<PartItem> FileNamePartList { get; set; } = new List<PartItem>();
}
