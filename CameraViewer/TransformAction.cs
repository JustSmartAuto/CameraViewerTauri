using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CameraViewer
{
    public class TransformAction
    {
        public TransformConfig Config { get; set; }

        public void RunAction()
        {
            try
            {
                var watchPath = Config?.WatchPath;
                if (string.IsNullOrEmpty(watchPath) || !Directory.Exists(watchPath))
                    return;

                var dir = new DirectoryInfo(watchPath);
                var files = dir.GetFiles();
                if (files.Length == 0)
                    return;

                // Wait for files to stabilize (no more writes)
                long prevLen, curLen = 0;
                do
                {
                    prevLen = curLen;
                    Thread.Sleep(100);
                    foreach (var f in files) f.Refresh();
                    curLen = files.Sum(f => f.Length);
                }
                while (prevLen < curLen);

                foreach (var file in files)
                {
                    try
                    {
                        var nameWoExt = Path.GetFileNameWithoutExtension(file.FullName);
                        var ext = Path.GetExtension(file.FullName);

                        var srcSplitter = string.IsNullOrEmpty(Config.SourceSpliter) ? "," : Config.SourceSpliter;
                        var folderSplitter = string.IsNullOrEmpty(Config.FolderSpliter) ? "+" : Config.FolderSpliter;
                        var fileSplitter = string.IsNullOrEmpty(Config.FileNameSpliter) ? "_" : Config.FileNameSpliter;

                        var srcParts = nameWoExt.Split(new[] { srcSplitter }, StringSplitOptions.None);
                        if (srcParts.Length <= Math.Max(Config.FolderIndex, Config.FileNameIndex))
                            continue;

                        var folderParts = srcParts[Config.FolderIndex].Split(new[] { folderSplitter }, StringSplitOptions.None);
                        var fileParts = srcParts[Config.FileNameIndex].Split(new[] { fileSplitter }, StringSplitOptions.None);

                        // Build target folder
                        var targetFolder = "";
                        foreach (var part in Config.FolderPartList ?? Enumerable.Empty<TransformPart>())
                        {
                            if (part.PartType == TransformPartType.Name)
                            {
                                if (part.SourceIndex >= 0 && part.SourceIndex < folderParts.Length)
                                    targetFolder += folderParts[part.SourceIndex] + Path.DirectorySeparatorChar;
                            }
                            else
                                targetFolder += DateTime.Now.ToString(part.Format ?? "") + Path.DirectorySeparatorChar;
                        }

                        // Build target filename (no extension yet)
                        var targetName = "";
                        foreach (var part in Config.FileNamePartList ?? Enumerable.Empty<TransformPart>())
                        {
                            if (part.PartType == TransformPartType.Name)
                            {
                                if (part.SourceIndex >= 0 && part.SourceIndex < fileParts.Length)
                                    targetName += fileParts[part.SourceIndex] + fileSplitter;
                            }
                            else
                                targetName += DateTime.Now.ToString(part.Format ?? "") + fileSplitter;
                        }
                        if (targetName.EndsWith(fileSplitter))
                            targetName = targetName.Substring(0, targetName.Length - fileSplitter.Length);
                        targetName += ext;

                        var fullTarget = Path.Combine(targetFolder, targetName);
                        if (!string.IsNullOrEmpty(targetFolder) && !Directory.Exists(targetFolder))
                            Directory.CreateDirectory(targetFolder);
                        if (File.Exists(fullTarget))
                            File.Delete(fullTarget);
                        File.Move(file.FullName, fullTarget);
                    }
                    catch { /* skip individual file errors */ }
                }
            }
            catch { /* swallow */ }
        }
    }
}
