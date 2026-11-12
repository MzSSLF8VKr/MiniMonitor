using Microsoft.Win32.SafeHandles;

namespace MiniMonitor.Recovery;

public enum CarveSizeMode
{
    Footer,
    FixedSize,
    BmpSize,
    RiffSize,
    Mp4Size,
    ZipEocd,
    SqliteSize
}

public sealed class CarveType
{
    public string Name = "";
    public string Extension = "";
    public string Category = "";
    public byte[] Header = Array.Empty<byte>();
    public int HeaderOffset;
    public byte[]? Footer;
    public long MaxSize = 20 * 1024 * 1024;
    public CarveSizeMode SizeMode = CarveSizeMode.Footer;
    public bool Enabled = true;

    public override string ToString() => Name + " (*" + Extension + ")";
}

public sealed class CarvedFile
{
    public string TypeName = "";
    public string Extension = "";
    public long Offset;
    public long Size;
    public string OutputPath = "";
    public string Status = "";
}

/// <summary>
/// 文件签名雕刻器（FreeRecover / PhotoRec 核心）：
/// 在原始磁盘或卷中查找文件头/文件尾，提取可恢复文件。
/// </summary>
public static class FileCarver
{
    private const int ChunkSize = 4 * 1024 * 1024;
    private const int MaxHeader = 32;

    public static List<CarveType> CreateTypes()
    {
        return new List<CarveType>
        {
            new()
            {
                Name = "JPEG 图片", Extension = ".jpg", Category = "图片",
                Header = new byte[] { 0xFF, 0xD8, 0xFF },
                Footer = new byte[] { 0xFF, 0xD9 },
                MaxSize = 80L * 1024 * 1024
            },
            new()
            {
                Name = "PNG 图片", Extension = ".png", Category = "图片",
                Header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                Footer = new byte[] { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 },
                MaxSize = 80L * 1024 * 1024
            },
            new()
            {
                Name = "GIF 图片", Extension = ".gif", Category = "图片",
                Header = System.Text.Encoding.ASCII.GetBytes("GIF89a"),
                Footer = new byte[] { 0x3B },
                MaxSize = 40L * 1024 * 1024
            },
            new()
            {
                Name = "BMP 图片", Extension = ".bmp", Category = "图片",
                Header = new byte[] { 0x42, 0x4D },
                MaxSize = 200L * 1024 * 1024,
                SizeMode = CarveSizeMode.BmpSize
            },
            new()
            {
                Name = "TIFF 图片", Extension = ".tif", Category = "图片",
                Header = new byte[] { 0x49, 0x49, 0x2A, 0x00 },
                MaxSize = 80L * 1024 * 1024,
                SizeMode = CarveSizeMode.FixedSize
            },
            new()
            {
                Name = "WebP 图片", Extension = ".webp", Category = "图片",
                Header = System.Text.Encoding.ASCII.GetBytes("RIFF"),
                MaxSize = 80L * 1024 * 1024,
                SizeMode = CarveSizeMode.RiffSize
            },
            new()
            {
                Name = "PDF 文档", Extension = ".pdf", Category = "文档",
                Header = System.Text.Encoding.ASCII.GetBytes("%PDF-"),
                Footer = System.Text.Encoding.ASCII.GetBytes("%%EOF"),
                MaxSize = 300L * 1024 * 1024
            },
            new()
            {
                Name = "ZIP / Office 压缩包", Extension = ".zip", Category = "压缩包",
                Header = new byte[] { 0x50, 0x4B, 0x03, 0x04 },
                MaxSize = 2L * 1024 * 1024 * 1024,
                SizeMode = CarveSizeMode.ZipEocd
            },
            new()
            {
                Name = "RAR 压缩包", Extension = ".rar", Category = "压缩包",
                Header = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 },
                MaxSize = 200L * 1024 * 1024,
                SizeMode = CarveSizeMode.FixedSize
            },
            new()
            {
                Name = "7-Zip 压缩包", Extension = ".7z", Category = "压缩包",
                Header = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C },
                MaxSize = 200L * 1024 * 1024,
                SizeMode = CarveSizeMode.FixedSize
            },
            new()
            {
                Name = "Word / Excel / PPT (OLE)", Extension = ".doc", Category = "文档",
                Header = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 },
                MaxSize = 200L * 1024 * 1024,
                SizeMode = CarveSizeMode.FixedSize
            },
            new()
            {
                Name = "SQLite 数据库", Extension = ".db", Category = "文档",
                Header = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\u0000"),
                MaxSize = 1024L * 1024 * 1024,
                SizeMode = CarveSizeMode.SqliteSize
            },
            new()
            {
                Name = "MP3 音频", Extension = ".mp3", Category = "音频",
                Header = System.Text.Encoding.ASCII.GetBytes("ID3"),
                MaxSize = 100L * 1024 * 1024,
                SizeMode = CarveSizeMode.FixedSize
            },
            new()
            {
                Name = "MP4 / MOV 视频", Extension = ".mp4", Category = "视频",
                Header = System.Text.Encoding.ASCII.GetBytes("ftyp"),
                HeaderOffset = 4,
                MaxSize = 500L * 1024 * 1024,
                SizeMode = CarveSizeMode.Mp4Size
            },
            new()
            {
                Name = "AVI 视频", Extension = ".avi", Category = "视频",
                Header = System.Text.Encoding.ASCII.GetBytes("RIFF"),
                MaxSize = 1024L * 1024 * 1024,
                SizeMode = CarveSizeMode.RiffSize
            },
            new()
            {
                Name = "WAV 音频", Extension = ".wav", Category = "音频",
                Header = System.Text.Encoding.ASCII.GetBytes("RIFF"),
                MaxSize = 1024L * 1024 * 1024,
                SizeMode = CarveSizeMode.RiffSize
            },
            new()
            {
                Name = "MKV 视频", Extension = ".mkv", Category = "视频",
                Header = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 },
                MaxSize = 200L * 1024 * 1024,
                SizeMode = CarveSizeMode.FixedSize
            }
        };
    }

    public static List<CarvedFile> Carve(SafeFileHandle handle, long sourceLength,
        List<CarveType> types, string outputDirectory, long maxBytes,
        CancellationToken token, Action<int, string>? progress)
    {
        var results = new List<CarvedFile>();
        var enabled = types.Where(type => type.Enabled).ToList();
        if (enabled.Count == 0 || sourceLength <= 0)
        {
            return results;
        }
        Directory.CreateDirectory(outputDirectory);
        long limit = maxBytes > 0
            ? Math.Min(sourceLength, maxBytes) : sourceLength;
        long scan = 0;
        int index = 0;
        while (scan < limit)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            int want = (int)Math.Min(ChunkSize + MaxHeader, limit - scan);
            if (want < 4)
            {
                break;
            }
            byte[] buffer = RecoveryDisk.ReadAt(handle, scan, want);
            if (buffer.Length < 4)
            {
                break;
            }
            CarveType? bestType = null;
            int bestIndex = int.MaxValue;
            foreach (var type in enabled)
            {
                int found = Find(buffer, type.Header, 0);
                if (found < type.HeaderOffset)
                {
                    continue;
                }
                int start = found - type.HeaderOffset;
                if (start < bestIndex)
                {
                    bestIndex = start;
                    bestType = type;
                }
            }
            if (bestType == null)
            {
                scan += ChunkSize;
                int percent = (int)Math.Min(100, scan * 100 / Math.Max(1, limit));
                progress?.Invoke(percent, "扫描偏移 " + RecoveryDisk.FormatSize(scan));
                continue;
            }
            long absolute = scan + bestIndex;
            CarveType chosen = bestType;
            if (bestType.Extension is ".webp" or ".avi" or ".wav")
            {
                byte[] sub = RecoveryDisk.ReadAt(handle, absolute + 8, 4);
                string tag = sub.Length == 4
                    ? System.Text.Encoding.ASCII.GetString(sub) : "";
                string extension = tag switch
                {
                    "WEBP" => ".webp",
                    "AVI " => ".avi",
                    "WAVE" => ".wav",
                    _ => ""
                };
                var matched = enabled.FirstOrDefault(type =>
                    type.Extension.Equals(extension,
                        StringComparison.OrdinalIgnoreCase));
                if (matched == null)
                {
                    scan = absolute + 1;
                    continue;
                }
                chosen = matched;
            }
            var carved = Extract(handle, absolute, chosen, outputDirectory,
                index, token);
            if (carved != null && carved.Size > 0)
            {
                results.Add(carved);
                index++;
                scan = absolute + Math.Max(1, carved.Size);
                progress?.Invoke((int)Math.Min(100,
                        scan * 100 / Math.Max(1, limit)),
                    "已恢复 " + carved.TypeName + "（"
                    + RecoveryDisk.FormatSize(carved.Size) + "）");
            }
            else
            {
                scan = absolute + 1;
            }
        }
        progress?.Invoke(100, "扫描完成，共恢复 " + results.Count + " 个文件");
        return results;
    }

    private static CarvedFile? Extract(SafeFileHandle handle, long offset,
        CarveType type, string outputDirectory, int index,
        CancellationToken token)
    {
        long size;
        long footerExtra = 0;
        switch (type.SizeMode)
        {
            case CarveSizeMode.BmpSize:
            {
                byte[] header = RecoveryDisk.ReadAt(handle, offset, 10);
                if (header.Length < 10)
                {
                    return null;
                }
                size = BitConverter.ToUInt32(header, 2);
                break;
            }
            case CarveSizeMode.RiffSize:
            {
                byte[] header = RecoveryDisk.ReadAt(handle, offset, 12);
                if (header.Length < 12)
                {
                    return null;
                }
                size = BitConverter.ToUInt32(header, 4) + 8L;
                break;
            }
            case CarveSizeMode.Mp4Size:
            {
                byte[] header = RecoveryDisk.ReadAt(handle, offset, 16);
                if (header.Length < 16)
                {
                    return null;
                }
                uint boxSize = ReadBigEndian32(header, 0);
                if (boxSize == 1)
                {
                    size = (long)ReadBigEndian64(header, 8);
                }
                else if (boxSize == 0)
                {
                    size = type.MaxSize;
                }
                else
                {
                    size = boxSize;
                }
                if (size < 8 || size > type.MaxSize)
                {
                    return null;
                }
                break;
            }
            case CarveSizeMode.SqliteSize:
            {
                byte[] header = RecoveryDisk.ReadAt(handle, offset, 32);
                if (header.Length < 32)
                {
                    return null;
                }
                int pageSize = ReadBigEndian16(header, 16);
                if (pageSize == 1)
                {
                    pageSize = 65536;
                }
                uint pages = ReadBigEndian32(header, 28);
                size = (long)pageSize * pages;
                break;
            }
            case CarveSizeMode.ZipEocd:
            {
                var (length, extra) = FindZipSize(handle, offset, type.MaxSize, token);
                size = length;
                footerExtra = extra;
                break;
            }
            case CarveSizeMode.Footer:
            {
                var (length, extra) = FindFooterSize(handle, offset,
                    type.Footer!, type.MaxSize, token);
                size = length;
                footerExtra = extra;
                break;
            }
            default:
                size = type.MaxSize;
                break;
        }
        if (size < 4)
        {
            return null;
        }
        size = Math.Min(size, type.MaxSize);
        string baseName = Path.Combine(outputDirectory,
            "f" + offset.ToString("X") + "_" + index);
        string path = baseName + type.Extension;
        int suffix = 1;
        while (File.Exists(path))
        {
            path = baseName + "_" + suffix++ + type.Extension;
        }
        bool created = false;
        try
        {
            using var output = new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            created = true;
            long written = 0;
            long remaining = size + footerExtra;
            while (remaining > 0)
            {
                if (token.IsCancellationRequested)
                {
                    output.Close();
                    try { File.Delete(path); } catch { }
                    return null;
                }
                int count = (int)Math.Min(1024 * 1024, remaining);
                byte[] data = RecoveryDisk.ReadAt(handle,
                    offset + written, count);
                if (data.Length == 0)
                {
                    break;
                }
                output.Write(data, 0, data.Length);
                written += data.Length;
                remaining -= data.Length;
                if (data.Length < count)
                {
                    break;
                }
            }
            output.Flush();
            if (written <= 0)
            {
                output.Close();
                try { File.Delete(path); } catch { }
                return null;
            }
            return new CarvedFile
            {
                TypeName = type.Name,
                Extension = type.Extension,
                Offset = offset,
                Size = written,
                OutputPath = path,
                Status = "已恢复"
            };
        }
        catch
        {
            if (created)
            {
                try { File.Delete(path); } catch { }
            }
            return null;
        }
    }

    private static (long Size, long Extra) FindFooterSize(SafeFileHandle handle,
        long offset, byte[] footer, long maxSize, CancellationToken token)
    {
        long read = 0;
        const int chunk = 1024 * 1024;
        int overlap = footer.Length - 1;
        byte[] carry = Array.Empty<byte>();
        while (read < maxSize)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            int want = (int)Math.Min(chunk, maxSize - read);
            byte[] data = RecoveryDisk.ReadAt(handle, offset + read, want);
            if (data.Length == 0)
            {
                break;
            }
            byte[] combined = carry.Length == 0
                ? data : carry.Concat(data).ToArray();
            int found = Find(combined, footer, 0);
            if (found >= 0)
            {
                long absolute = read - carry.Length + found + footer.Length;
                return (absolute, 0);
            }
            if (data.Length < want)
            {
                return (read + data.Length, 0);
            }
            read += data.Length;
            int keep = Math.Min(overlap, combined.Length);
            carry = combined[^keep..];
        }
        return (Math.Min(read, maxSize), 0);
    }

    private static (long Size, long Extra) FindZipSize(SafeFileHandle handle,
        long offset, long maxSize, CancellationToken token)
    {
        long read = 0;
        const int chunk = 1024 * 1024;
        byte[] signature = { 0x50, 0x4B, 0x05, 0x06 };
        byte[] carry = Array.Empty<byte>();
        while (read < maxSize)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            int want = (int)Math.Min(chunk, maxSize - read);
            byte[] data = RecoveryDisk.ReadAt(handle, offset + read, want);
            if (data.Length == 0)
            {
                break;
            }
            byte[] combined = carry.Length == 0
                ? data : carry.Concat(data).ToArray();
            int found = Find(combined, signature, 0);
            if (found >= 0)
            {
                long eocdAbsolute = read - carry.Length + found;
                byte[] eocd = RecoveryDisk.ReadAt(handle, offset + eocdAbsolute, 22);
                long comment = eocd.Length >= 22 ? BitConverter.ToUInt16(eocd, 20) : 0;
                return (eocdAbsolute + 22 + comment, 0);
            }
            if (data.Length < want)
            {
                return (read + data.Length, 0);
            }
            read += data.Length;
            int keep = Math.Min(3, combined.Length);
            carry = combined[^keep..];
        }
        return (Math.Min(read, maxSize), 0);
    }

    private static bool IsAsciiAt(SafeFileHandle handle, long offset, string text)
    {
        byte[] data = RecoveryDisk.ReadAt(handle, offset, text.Length);
        return data.Length == text.Length
            && System.Text.Encoding.ASCII.GetString(data) == text;
    }

    private static bool IsRiffSubtype(SafeFileHandle handle, long offset,
        string extension)
    {
        byte[] data = RecoveryDisk.ReadAt(handle, offset, 12);
        if (data.Length < 12)
        {
            return false;
        }
        string subtype = System.Text.Encoding.ASCII.GetString(data, 8, 4);
        return extension == ".avi" ? subtype == "AVI " : subtype == "WAVE";
    }

    private static int Find(byte[] buffer, byte[] pattern, int start)
    {
        if (pattern.Length == 0 || buffer.Length < pattern.Length)
        {
            return -1;
        }
        start = Math.Max(0, start);
        if (start > buffer.Length - pattern.Length)
        {
            return -1;
        }
        int index = buffer.AsSpan(start).IndexOf(pattern);
        return index < 0 ? -1 : start + index;
    }

    private static ushort ReadBigEndian16(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadBigEndian32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16)
        | (data[offset + 2] << 8) | data[offset + 3]);

    private static ulong ReadBigEndian64(byte[] data, int offset)
    {
        ulong value = 0;
        for (int i = 0; i < 8 && offset + i < data.Length; i++)
        {
            value = (value << 8) | data[offset + i];
        }
        return value;
    }
}
