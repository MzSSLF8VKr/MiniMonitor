using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace MiniMonitor;

internal enum MediaCategory
{
    Image,
    Audio,
    Video
}

internal sealed record OutputFormat(string DisplayName, string Extension, bool DirectImage = false)
{
    public override string ToString() => DisplayName;
}

/// <summary>通用格式转换支持的输入扩展名、输出格式与右键菜单注册。</summary>
internal static class ConverterFormats
{
    public static readonly string[] ImageInputExtensions =
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".apng", ".bmp", ".gif", ".tif", ".tiff",
        ".webp", ".heic", ".heif", ".avif", ".ico",
        ".psd", ".psb", ".xcf", ".cdr", ".ai", ".eps",
        ".cr2", ".cr3", ".nef", ".arw", ".srf", ".sr2", ".rw2", ".orf", ".dng",
        ".raf", ".raw", ".pef", ".srw", ".3fr", ".erf", ".kdc", ".dcr", ".mef",
        ".mos", ".mrw", ".nrw", ".x3f", ".crw", ".ari", ".bay", ".fff", ".iiq", ".rwl"
    };

    public static readonly string[] AudioInputExtensions =
    {
        ".mp3", ".wav", ".flac", ".aac", ".m4a", ".m4b", ".ogg", ".oga",
        ".opus", ".wma", ".aiff", ".aif", ".amr", ".ape", ".ac3", ".mka", ".weba"
    };

    public static readonly string[] VideoInputExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".3gp", ".ogv", ".vob",
        ".rm", ".rmvb", ".asf", ".f4v"
    };

    public static readonly OutputFormat[] ImageFormats =
    {
        new("JPG / JPEG", "jpg", true),
        new("PNG", "png", true),
        new("BMP", "bmp", true),
        new("GIF", "gif", true),
        new("TIFF", "tif", true),
        new("ICO 图标", "ico", true),
        new("WebP（需要 FFmpeg）", "webp"),
        new("AVIF（需要 FFmpeg）", "avif"),
        new("APNG 动图（需要 FFmpeg）", "apng")
    };

    public static readonly OutputFormat[] AudioFormats =
    {
        new("MP3", "mp3"),
        new("WAV", "wav"),
        new("FLAC", "flac"),
        new("AAC / M4A", "m4a"),
        new("OGG Vorbis", "ogg"),
        new("Opus", "opus"),
        new("WMA", "wma"),
        new("AIFF", "aiff")
    };

    public static readonly OutputFormat[] VideoFormats =
    {
        new("MP4（H.264）", "mp4"),
        new("MKV（H.264）", "mkv"),
        new("MOV（H.264）", "mov"),
        new("WebM（VP9）", "webm"),
        new("AVI", "avi"),
        new("GIF 动图", "gif"),
        new("WMV", "wmv"),
        new("FLV", "flv"),
        new("TS", "ts")
    };

    private static readonly string[] DirectImageInputExtensions =
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".apng", ".bmp", ".gif", ".tif", ".tiff", ".ico"
    };

    /// <summary>音频选项卡额外接受视频文件，用于提取音轨。</summary>
    public static string[] AudioSourceExtensions { get; } = AudioInputExtensions
        .Concat(VideoInputExtensions)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>视频选项卡额外接受 GIF 动图。</summary>
    public static string[] VideoSourceExtensions { get; } = VideoInputExtensions
        .Append(".gif")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string[] AllInputExtensions { get; } = ImageInputExtensions
        .Concat(AudioInputExtensions)
        .Concat(VideoInputExtensions)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static MediaCategory? DetectCategory(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (ImageInputExtensions.Contains(extension))
        {
            return MediaCategory.Image;
        }
        if (AudioInputExtensions.Contains(extension))
        {
            return MediaCategory.Audio;
        }
        if (VideoInputExtensions.Contains(extension))
        {
            return MediaCategory.Video;
        }
        return null;
    }

    public static bool IsDirectImageInput(string path)
    {
        return DirectImageInputExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    public static OutputFormat[] FormatsFor(MediaCategory category)
    {
        return category switch
        {
            MediaCategory.Audio => AudioFormats,
            MediaCategory.Video => VideoFormats,
            _ => ImageFormats
        };
    }

    public static string CategoryName(MediaCategory category)
    {
        return category switch
        {
            MediaCategory.Audio => "音频",
            MediaCategory.Video => "视频",
            _ => "图片"
        };
    }

    public static string OpenFileFilter(MediaCategory category)
    {
        string label;
        string[] extensions;
        switch (category)
        {
            case MediaCategory.Audio:
                label = "音频 / 视频文件";
                extensions = AudioSourceExtensions;
                break;
            case MediaCategory.Video:
                label = "视频 / GIF 文件";
                extensions = VideoSourceExtensions;
                break;
            default:
                label = "图片文件";
                extensions = ImageInputExtensions;
                break;
        }
        string all = string.Join(";", extensions.Select(extension => "*" + extension));
        return label + "|" + all + "|所有文件|*.*";
    }

    public static bool AcceptsFile(MediaCategory category, string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return category switch
        {
            MediaCategory.Image => ImageInputExtensions.Contains(extension),
            MediaCategory.Audio => AudioSourceExtensions.Contains(extension),
            MediaCategory.Video => VideoSourceExtensions.Contains(extension),
            _ => false
        };
    }

    public static ImageFormat? DirectImageFormat(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" or "jfif" => ImageFormat.Jpeg,
            "png" or "apng" => ImageFormat.Png,
            "bmp" => ImageFormat.Bmp,
            "gif" => ImageFormat.Gif,
            "tif" or "tiff" => ImageFormat.Tiff,
            _ => null
        };
    }
}

/// <summary>
/// 资源管理器右键菜单：为支持的图片 / 音频 / 视频扩展名注册
/// “通用格式转换”命令（HKCU，无需管理员权限）。默认关闭。
/// </summary>
internal static class ExplorerContextMenu
{
    private const string VerbKeyName = "MiniMonitorFormatConvert";
    private const string MenuText = "通用格式转换";

    public static bool SetEnabled(bool enabled, string exePath, out string? error)
    {
        error = null;
        try
        {
            foreach (string extension in ConverterFormats.AllInputExtensions)
            {
                string keyPath = "Software\\Classes\\SystemFileAssociations\\"
                    + extension + "\\shell\\" + VerbKeyName;
                if (enabled)
                {
                    using var verbKey = Registry.CurrentUser.CreateSubKey(keyPath);
                    if (verbKey == null)
                    {
                        error = "无法创建注册表项：" + keyPath;
                        return false;
                    }
                    verbKey.SetValue(null, MenuText);
                    verbKey.SetValue("MUIVerb", MenuText);
                    verbKey.SetValue("Icon", "\"" + exePath + "\"");
                    using var commandKey = Registry.CurrentUser.CreateSubKey(keyPath + "\\command");
                    if (commandKey == null)
                    {
                        error = "无法创建注册表命令项：" + keyPath + "\\command";
                        return false;
                    }
                    commandKey.SetValue(null, "\"" + exePath + "\" --convert \"%1\"");
                }
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
