
namespace MiniMonitor.Music;

/// <summary>同名重复音乐清理：保留一份，喜欢 / PerfectMusic 曲目移动到 PerfectMusic。</summary>
public static class DuplicateCleanupService
{
    public static DuplicateCleanupResult Cleanup(MusicLibrary library, MusicSettings settings,
        IReadOnlyList<DuplicateFileGroup> groups)
    {
        var result = new DuplicateCleanupResult();
        string root = settings.EffectiveLibraryPath;
        string perfectDir = settings.PerfectMusicPath;
        try
        {
            Directory.CreateDirectory(perfectDir);
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.Errors.Add("无法创建 PerfectMusic 目录：" + ex.Message);
            return result;
        }

        foreach (var group in groups)
        {
            if (group.Tracks.Count < 2)
            {
                continue;
            }
            result.Groups++;
            var originals = group.Tracks.ToList();
            var keeper = group.Keeper;
            if (!File.Exists(keeper.Path))
            {
                var availableKeeper = originals
                    .Where(track => File.Exists(track.Path))
                    .OrderByDescending(track => track.IsPerfect)
                    .ThenByDescending(track => track.Liked)
                    .ThenByDescending(track => track.Bitrate)
                    .ThenByDescending(track => track.BitsPerSample)
                    .ThenByDescending(track => track.FileSize)
                    .FirstOrDefault();
                if (availableKeeper == null)
                {
                    continue;
                }
                keeper = availableKeeper;
            }
            bool protect = group.HasProtectedTrack;
            bool groupFailed = false;

            if (protect)
            {
                string sourcePath = keeper.Path;
                string targetPath = Path.Combine(perfectDir, Path.GetFileName(sourcePath));
                try
                {
                    var targetTrack = library.GetTrackByPath(targetPath);
                    if (targetTrack != null && targetTrack.Id != keeper.Id
                        && IsUnderLibrary(targetTrack.Path, root))
                    {
                        // PerfectMusic 中已有同名曲目，直接以它作为保留项
                        keeper = targetTrack;
                        library.UpdateTrackLocation(keeper.Id, targetPath, true, true);
                    }
                    else if (!PathsEqual(sourcePath, targetPath))
                    {
                        if (File.Exists(sourcePath))
                        {
                            File.Move(sourcePath, targetPath, overwrite: true);
                            result.Moved++;
                            MoveLrc(sourcePath, targetPath, result);
                        }
                        library.UpdateTrackLocation(keeper.Id, targetPath, true, true);
                    }
                    else
                    {
                        library.UpdateTrackLocation(keeper.Id, targetPath, true, true);
                    }
                }
                catch (Exception ex)
                {
                    groupFailed = true;
                    result.Failed++;
                    AddError(result, $"移动“{group.Name}”失败：{ex.Message}");
                }
            }

            if (groupFailed)
            {
                continue;
            }

            foreach (var track in originals)
            {
                if (track.Id == keeper.Id)
                {
                    continue;
                }
                try
                {
                    if (File.Exists(track.Path))
                    {
                        File.Delete(track.Path);
                        result.Deleted++;
                    }
                    DeleteLrc(track.Path);
                    library.RemoveTrack(track.Id);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    AddError(result, $"删除“{track.Path}”失败：{ex.Message}");
                }
            }
        }
        return result;
    }

    private static bool IsUnderLibrary(string path, string root)
    {
        string cleanRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.StartsWith(cleanRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveLrc(string sourceAudio, string targetAudio, DuplicateCleanupResult result)
    {
        try
        {
            string? sourceLrc = LrcDocument.FindLyricsFile(sourceAudio);
            if (sourceLrc == null)
            {
                return;
            }
            string targetLrc = Path.Combine(
                Path.GetDirectoryName(targetAudio) ?? "",
                Path.GetFileNameWithoutExtension(targetAudio) + ".lrc");
            if (PathsEqual(sourceLrc, targetLrc))
            {
                return;
            }
            File.Move(sourceLrc, targetLrc, overwrite: true);
        }
        catch (Exception ex)
        {
            result.Failed++;
            AddError(result, "移动歌词失败：" + ex.Message);
        }
    }

    private static void DeleteLrc(string audioPath)
    {
        try
        {
            string? lrc = LrcDocument.FindLyricsFile(audioPath);
            if (lrc != null && File.Exists(lrc))
            {
                File.Delete(lrc);
            }
        }
        catch
        {
            // 歌词删除失败不影响音乐清理
        }
    }

    private static void AddError(DuplicateCleanupResult result, string message)
    {
        if (result.Errors.Count < 20)
        {
            result.Errors.Add(message);
        }
    }
}

