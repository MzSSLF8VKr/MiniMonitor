using System.Security.Cryptography;
using System.Text;
using TagLib;
using IOFile = System.IO.File;

namespace MiniMonitor.Music;

/// <summary>封面缓存：把内嵌封面或文件夹封面写入 %APPDATA%\MiniMonitor\music-covers。</summary>
public static class MusicCoverCache
{
    public static string CacheDir { get; } = Path.Combine(AppConfig.ConfigDir, "music-covers");

    private static string Hash(string value)
    {
        byte[] bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    public static string SavePicture(IPicture? picture, string sourcePath)
    {
        try
        {
            if (picture?.Data == null || picture.Data.Count == 0)
            {
                return "";
            }
            Directory.CreateDirectory(CacheDir);
            string extension = picture.MimeType?.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/bmp" => ".bmp",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
            string key = Hash(sourcePath + "|" + picture.Data.Count + "|" + picture.Description) + extension;
            string target = Path.Combine(CacheDir, key);
            if (!IOFile.Exists(target))
            {
                IOFile.WriteAllBytes(target, picture.Data.Data.ToArray());
            }
            return key;
        }
        catch
        {
            return "";
        }
    }

    public static string SaveFolderCover(string folder)
    {
        try
        {
            foreach (string name in new[]
                     {
                         "cover.jpg", "cover.jpeg", "cover.png", "folder.jpg", "folder.png",
                         "front.jpg", "front.png", "album.jpg", "album.png"
                     })
            {
                string candidate = Path.Combine(folder, name);
                if (!IOFile.Exists(candidate))
                {
                    continue;
                }
                Directory.CreateDirectory(CacheDir);
                var info = new FileInfo(candidate);
                string key = Hash(candidate + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks)
                    + Path.GetExtension(candidate).ToLowerInvariant();
                string target = Path.Combine(CacheDir, key);
                if (!IOFile.Exists(target))
                {
                    IOFile.Copy(candidate, target, overwrite: true);
                }
                return key;
            }
        }
        catch
        {
            // 封面缺失不影响播放
        }
        return "";
    }

    public static string? ResolvePath(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }
        string path = Path.Combine(CacheDir, key);
        return IOFile.Exists(path) ? path : null;
    }

    public static Bitmap? Load(string? key)
    {
        string? path = ResolvePath(key);
        if (path == null)
        {
            return null;
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>本地音乐目录扫描（只读本地文件，不联网）。</summary>
public sealed class MusicScanner
{
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".aif", ".aiff", ".m4a", ".m4b", ".aac",
        ".ogg", ".oga", ".opus", ".wma", ".ape", ".wv", ".dsf", ".dff", ".mpc"
    };

    private readonly MusicLibrary _library;

    public MusicScanner(MusicLibrary library)
    {
        _library = library;
    }

    public MusicScanResult Scan(MusicSettings settings, IProgress<MusicScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        string root = settings.EffectiveLibraryPath;
        string perfectRoot = Path.Combine(root, "PerfectMusic");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool perfectFound = Directory.Exists(perfectRoot);
        if (!Directory.Exists(root))
        {
            return new MusicScanResult { LibraryPath = root, PerfectMusicFound = false };
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = settings.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", options)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .ToList();
            if (!settings.IncludeSubfolders && perfectFound)
            {
                // 即使关闭递归，也始终纳入 PerfectMusic 文件夹
                files.AddRange(Directory.EnumerateFiles(perfectRoot, "*", SearchOption.AllDirectories)
                    .Where(path => SupportedExtensions.Contains(Path.GetExtension(path))));
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return new MusicScanResult { LibraryPath = root, PerfectMusicFound = perfectFound };
        }

        var index = _library.GetTrackIndex();
        _library.BeginScan();
        int added = 0, updated = 0, skipped = 0, failed = 0;
        var unchanged = new List<string>();
        var folderCoverCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        progress?.Report(new MusicScanProgress { Stage = "正在扫描音乐目录", Done = 0, Total = files.Count });

        for (int i = 0; i < files.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            string file = files[i];
            if (i % 10 == 0 || i == files.Count - 1)
            {
                progress?.Report(new MusicScanProgress
                {
                    Stage = "正在读取音乐标签",
                    Done = i + 1,
                    Total = files.Count,
                    CurrentFile = file
                });
            }
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    failed++;
                    continue;
                }
                long modified = info.LastWriteTimeUtc.Ticks;
                if (index.TryGetValue(file, out long known) && known == modified)
                {
                    unchanged.Add(file);
                    skipped++;
                    continue;
                }

                bool perfect = IsUnder(file, perfectRoot);
                using var tagFile = TagLib.File.Create(file);
                var tag = tagFile.Tag;
                var track = new MusicTrack
                {
                    Path = file,
                    Title = tag.Title?.Trim() ?? "",
                    Artist = tag.FirstPerformer?.Trim() ?? "",
                    AlbumArtist = tag.FirstAlbumArtist?.Trim() ?? "",
                    Album = tag.Album?.Trim() ?? "",
                    Genre = tag.FirstGenre?.Trim() ?? "",
                    Year = (int)tag.Year,
                    TrackNo = (int)tag.Track,
                    DiscNo = (int)tag.Disc,
                    DurationSeconds = tagFile.Properties?.Duration.TotalSeconds ?? 0,
                    Bitrate = tagFile.Properties?.AudioBitrate ?? 0,
                    SampleRate = tagFile.Properties?.AudioSampleRate ?? 0,
                    BitsPerSample = tagFile.Properties?.BitsPerSample ?? 0,
                    Channels = tagFile.Properties?.AudioChannels ?? 0,
                    Codec = Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),
                    FileSize = info.Length,
                    Modified = modified,
                    IsPerfect = perfect,
                    Liked = perfect && settings.PerfectMusicAsLiked,
                    Comment = tag.Comment ?? ""
                };
                try
                {
                    double gain = tag.ReplayGainTrackGain;
                    track.GainDb = double.IsFinite(gain) ? Math.Clamp(gain, -60, 60) : 0;
                }
                catch
                {
                    track.GainDb = 0;
                }
                if (!double.IsFinite(track.DurationSeconds) || track.DurationSeconds < 0)
                {
                    track.DurationSeconds = 0;
                }

                string coverKey = "";
                try
                {
                    var picture = tag.Pictures?.FirstOrDefault();
                    if (picture != null)
                    {
                        coverKey = MusicCoverCache.SavePicture(picture, file);
                    }
                }
                catch
                {
                    coverKey = "";
                }
                if (coverKey.Length == 0)
                {
                    string folder = Path.GetDirectoryName(file) ?? root;
                    if (!folderCoverCache.TryGetValue(folder, out string? folderKey))
                    {
                        folderKey = MusicCoverCache.SaveFolderCover(folder);
                        folderCoverCache[folder] = folderKey;
                    }
                    coverKey = folderKey ?? "";
                }
                track.CoverKey = coverKey;

                var result = _library.UpsertTrack(track, settings.PerfectMusicAsLiked);
                if (result.Added) added++;
                else if (result.Changed) updated++;
                else skipped++;
            }
            catch
            {
                failed++;
            }
        }

        _library.MarkSeenBatch(unchanged, settings.PerfectMusicAsLiked);
        int removed = cancellationToken.IsCancellationRequested ? 0 : _library.FinishScan();
        watch.Stop();
        progress?.Report(new MusicScanProgress
        {
            Stage = cancellationToken.IsCancellationRequested ? "扫描已取消" : "扫描完成",
            Done = files.Count,
            Total = files.Count
        });
        return new MusicScanResult
        {
            Added = added,
            Updated = updated,
            Skipped = skipped,
            Removed = removed,
            Failed = failed,
            TotalFiles = files.Count,
            Elapsed = watch.Elapsed,
            LibraryPath = root,
            PerfectMusicFound = perfectFound
        };
    }

    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
