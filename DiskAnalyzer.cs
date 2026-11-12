using System.Diagnostics;
using System.IO;

namespace MiniMonitor;

/// <summary>目录节点：只保留目录级聚合，避免百万级文件全部驻留内存。</summary>
public sealed class DirNode
{
    public string Name { get; }
    public string FullPath { get; }
    public int Depth { get; }
    public List<DirNode> Children { get; } = new();
    public long DirectFileSize { get; set; }
    public long DirectFileCount { get; set; }
    public long Size { get; set; }
    public long FileCount { get; set; }
    public long FolderCount { get; set; }

    public DirNode(string name, string fullPath, int depth)
    {
        Name = name;
        FullPath = fullPath;
        Depth = depth;
    }
}

public sealed record FileEntry(string FullPath, string Name, long Size);

public sealed record ScanProgress(
    string CurrentPath,
    long Bytes,
    long Files,
    long Folders,
    long Skipped,
    TimeSpan Elapsed);

public sealed class DiskScanResult
{
    public required string RootPath { get; init; }
    public required DirNode Root { get; init; }
    public required long FolderCount { get; init; }
    public required long SkippedCount { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required IReadOnlyList<DirNode> LargestFolders { get; init; }
    public required IReadOnlyList<FileEntry> LargestFiles { get; init; }

    public long TotalBytes => Root.Size;
    public long FileCount => Root.FileCount;
}

/// <summary>
/// 磁盘占用扫描器。按目录聚合大小；只保留最大的若干文件和文件夹，
/// 跳过符号链接/联接点，遇到无权限目录继续扫描。
/// </summary>
public static class DiskAnalyzer
{
    private const int MaxLargestFiles = 300;
    private const int MaxLargestFolders = 300;

    public static DiskScanResult Scan(
        string rootPath,
        CancellationToken cancellationToken,
        IProgress<ScanProgress>? progress = null)
    {
        var rootInfo = new DirectoryInfo(rootPath);
        if (!rootInfo.Exists)
        {
            throw new DirectoryNotFoundException("目录不存在：" + rootPath);
        }

        string rootName = string.IsNullOrWhiteSpace(rootInfo.Name) ? rootPath : rootInfo.Name;
        var root = new DirNode(rootName, rootInfo.FullName, 0);
        var allNodes = new List<DirNode> { root };
        var largestFiles = new List<FileEntry>(MaxLargestFiles);
        long largestFileMin = long.MaxValue;
        long totalBytes = 0;
        long fileCount = 0;
        long folderCount = 0;
        long skipped = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0
        };

        var stack = new Stack<(DirectoryInfo Directory, DirNode Node)>();
        stack.Push((rootInfo, root));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, node) = stack.Pop();
            folderCount++;

            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (entry is DirectoryInfo subDirectory)
                        {
                            if ((subDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                skipped++;
                                continue;
                            }
                            var child = new DirNode(subDirectory.Name, subDirectory.FullName, node.Depth + 1);
                            node.Children.Add(child);
                            allNodes.Add(child);
                            stack.Push((subDirectory, child));
                        }
                        else if (entry is FileInfo file)
                        {
                            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                skipped++;
                                continue;
                            }
                            long length = file.Length;
                            node.DirectFileSize += length;
                            node.DirectFileCount++;
                            totalBytes += length;
                            fileCount++;

                            if (largestFiles.Count < MaxLargestFiles || length > largestFileMin)
                            {
                                var record = new FileEntry(file.FullName, file.Name, length);
                                if (largestFiles.Count < MaxLargestFiles)
                                {
                                    largestFiles.Add(record);
                                    if (length < largestFileMin)
                                    {
                                        largestFileMin = length;
                                    }
                                }
                                else
                                {
                                    int minIndex = 0;
                                    for (int i = 1; i < largestFiles.Count; i++)
                                    {
                                        if (largestFiles[i].Size < largestFiles[minIndex].Size)
                                        {
                                            minIndex = i;
                                        }
                                    }
                                    largestFiles[minIndex] = record;
                                    largestFileMin = largestFiles.Min(f => f.Size);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        skipped++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                skipped++;
            }

            if (progress != null)
            {
                var now = stopwatch.Elapsed;
                if (now - lastReport >= TimeSpan.FromMilliseconds(180))
                {
                    lastReport = now;
                    progress.Report(new ScanProgress(
                        node.FullPath, totalBytes, fileCount, folderCount, skipped, now));
                }
            }
        }

        // 逆序累加：所有子目录都排在父目录之后，反向即可得到完整聚合值。
        for (int i = allNodes.Count - 1; i >= 0; i--)
        {
            var node = allNodes[i];
            node.Size = node.DirectFileSize;
            node.FileCount = node.DirectFileCount;
            node.FolderCount = 0;
            foreach (var child in node.Children)
            {
                node.Size += child.Size;
                node.FileCount += child.FileCount;
                node.FolderCount += 1 + child.FolderCount;
            }
        }

        var topFolders = allNodes
            .Where(node => node != root && node.Size > 0)
            .OrderByDescending(node => node.Size)
            .Take(MaxLargestFolders)
            .ToList();

        largestFiles.Sort((a, b) => b.Size.CompareTo(a.Size));

        var elapsed = stopwatch.Elapsed;
        progress?.Report(new ScanProgress(
            root.FullPath, totalBytes, fileCount, folderCount, skipped, elapsed));

        return new DiskScanResult
        {
            RootPath = rootInfo.FullName,
            Root = root,
            FolderCount = folderCount,
            SkippedCount = skipped,
            Elapsed = elapsed,
            LargestFolders = topFolders,
            LargestFiles = largestFiles
        };
    }
}
