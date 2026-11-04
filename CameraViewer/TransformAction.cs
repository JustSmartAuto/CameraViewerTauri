using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CameraViewer
{
    public class TransformAction
    {
        public TransformConfig Config { get; set; }
        /// <summary>配置名称（日志用）</summary>
        public string ConfigName { get; set; } = "";
        /// <summary>触发方式：手动触发 / 监听:xxx</summary>
        public string Trigger { get; set; } = "";

        public void RunAction()
        {
            var name = string.IsNullOrEmpty(ConfigName) ? Config?.Name ?? "" : ConfigName;
            try
            {
                var watchPath = Config?.WatchPath;
                if (string.IsNullOrEmpty(watchPath) || !Directory.Exists(watchPath))
                {
                    LogHelper.Warn($"[转存][{name}] {Trigger} 跳过：watchPath 无效 ({watchPath})");
                    return;
                }

                var dir = new DirectoryInfo(watchPath);
                var files = dir.GetFiles();
                if (files.Length == 0) return;

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
                LogHelper.Info($"[转存][{name}] {Trigger} 文件稳定后 {files.Length} 个待处理");

                var srcSplitter = string.IsNullOrEmpty(Config.SourceSpliter) ? "," : Config.SourceSpliter;
                var folderSplitter = string.IsNullOrEmpty(Config.FolderSpliter) ? "+" : Config.FolderSpliter;
                var fileSplitter = string.IsNullOrEmpty(Config.FileNameSpliter) ? "_" : Config.FileNameSpliter;

                int moved = 0, failed = 0, skipped = 0;
                foreach (var file in files)
                {
                    try
                    {
                        var nameWoExt = Path.GetFileNameWithoutExtension(file.FullName);
                        var ext = Path.GetExtension(file.FullName);

                        var srcParts = nameWoExt.Split(new[] { srcSplitter }, StringSplitOptions.None);
                        if (srcParts.Length <= Math.Max(Config.FolderIndex, Config.FileNameIndex))
                        {
                            skipped++;
                            LogHelper.Warn($"[转存][{name}] 跳过源: 字段数不足 file={file.Name} parts={srcParts.Length} idx=max({Config.FolderIndex},{Config.FileNameIndex})");
                            continue;
                        }

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
                                else
                                    targetFolder += "?" + Path.DirectorySeparatorChar;
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

                        // 解析摘要
                        LogHelper.Info($"[转存][{name}] 解析 ok: src={file.FullName} → folder={targetFolder} file={targetName}");

                        if (!string.IsNullOrEmpty(targetFolder) && !Directory.Exists(targetFolder))
                        {
                            Directory.CreateDirectory(targetFolder);
                            LogHelper.Info($"[转存][{name}] 创建目录: {targetFolder}");
                        }

                        var existed = File.Exists(fullTarget);
                        if (existed)
                        {
                            File.Delete(fullTarget);
                            LogHelper.Warn($"[转存][{name}] 覆盖旧目标: {fullTarget}");
                        }

                        // 跨卷安全移动：先复制再删除源
                        try
                        {
                            File.Move(file.FullName, fullTarget);
                        }
                        catch (IOException)
                        {
                            // 跨卷或目标不存在，退化为复制+删除
                            File.Copy(file.FullName, fullTarget, overwrite: true);
                            File.Delete(file.FullName);
                            LogHelper.Warn($"[转存][{name}] 使用复制+删除回退（疑似跨卷）: {file.Name} → {fullTarget}");
                        }

                        moved++;
                        LogHelper.Info($"[转存][{name}] 成功: {file.Name} → {fullTarget} (原大小 {file.Length} B)");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LogHelper.Error($"[转存][{name}] 失败: src={file.FullName}", ex);
                    }
                }
                LogHelper.Info($"[转存][{name}] {Trigger} 完成 moved={moved} failed={failed} skipped={skipped}");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"[转存][{name}] {Trigger} RunAction 顶层异常", ex);
            }
        }
    }
}
