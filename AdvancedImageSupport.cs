using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniMonitor;

/// <summary>
/// 扩展图片格式支持：PSD / PSB、XCF、CDR、AI、EPS 与相机 RAW。
/// 设计原则是不依赖任何外部程序：优先解码文件本身，其次提取文件内嵌的预览 / 缩略图。
/// 这些格式在 QuickLook 预览与通用格式转换中复用同一套解码逻辑。
/// </summary>
internal static class AdvancedImageSupport
{
    /// <summary>需要走扩展解码器的格式（不含 GDI+ 原生支持的格式）。</summary>
    private static readonly string[] AdvancedExtensions =
    {
        ".psd", ".psb", ".xcf", ".cdr", ".ai", ".eps",
        ".cr2", ".cr3", ".nef", ".arw", ".srf", ".sr2", ".rw2", ".orf", ".dng",
        ".raf", ".raw", ".pef", ".srw", ".3fr", ".erf", ".kdc", ".dcr", ".mef",
        ".mos", ".mrw", ".nrw", ".x3f", ".crw", ".ari", ".bay", ".fff", ".iiq",
        ".rwl"
    };

    private static readonly string[] RawExtensions = AdvancedExtensions
        .Where(extension => extension is not (".psd" or ".psb" or ".xcf" or ".cdr" or ".ai" or ".eps"))
        .ToArray();

    private const long MaxEmbeddedScan = 64L * 1024 * 1024;
    private const long MaxImagePixels = 60L * 1000 * 1000;

    public static bool IsAdvanced(string path)
    {
        return IsAdvancedExtension(Path.GetExtension(path));
    }

    public static bool IsAdvancedExtension(string extension)
    {
        return Array.IndexOf(AdvancedExtensions, extension.ToLowerInvariant()) >= 0;
    }

    public static bool IsRaw(string path)
    {
        return Array.IndexOf(RawExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
    }

    /// <summary>文件头是否包含 PDF 标记（AI 9 及以上通常是 PDF 兼容格式）。</summary>
    public static bool IsPdfBased(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int length = (int)Math.Min(8192, stream.Length);
            if (length <= 0)
            {
                return false;
            }
            var data = new byte[length];
            stream.ReadExactly(data, 0, length);
            return IndexOf(data, "%PDF-"u8) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把扩展格式加载为 Bitmap。失败时抛出带原因的 NotSupportedException。</summary>
    public static Bitmap LoadBitmap(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string? hint = null;
        Bitmap? bitmap = null;
        try
        {
            switch (extension)
            {
                case ".psd":
                case ".psb":
                    if (PsdDecoder.TryDecode(path, out bitmap, out string psdError) && bitmap != null)
                    {
                        return bitmap;
                    }
                    hint = psdError;
                    break;
                case ".xcf":
                    if (TryXcfThumbnail(path, out bitmap) && bitmap != null)
                    {
                        return bitmap;
                    }
                    hint = "XCF 中未找到 GIMP 保存的缩略图。";
                    break;
                case ".cdr":
                    if (TryCdrPreview(path, out bitmap) && bitmap != null)
                    {
                        return bitmap;
                    }
                    hint = "CDR 中未找到内嵌预览图（旧版 CorelDRAW 文件可能不包含预览）。";
                    break;
                case ".ai":
                {
                    bool pdfBased = IsPdfBased(path);
                    if (!pdfBased && TryEpsPreview(path, out bitmap) && bitmap != null)
                    {
                        return bitmap;
                    }
                    if (TryGhostscript(path, out bitmap) && bitmap != null)
                    {
                        return bitmap;
                    }
                    if (pdfBased)
                    {
                        throw new NotSupportedException(
                            "AI 是 PDF 兼容的矢量文件，无法直接解码为位图；QuickLook 会用内置 PDF 查看器显示。");
                    }
                    hint = "AI 中未找到内嵌预览图，也未检测到 Ghostscript。";
                    break;
                }
                case ".eps":
                    if (TryEpsPreview(path, out bitmap) && bitmap != null)
                    {
                        return bitmap;
                    }
                    if (TryGhostscript(path, out bitmap) && bitmap != null)
                    {
                        return bitmap;
                    }
                    hint = "EPS 中未找到内嵌预览图（EPSI / TIFF 预览），也未检测到 Ghostscript。";
                    break;
                default:
                    if (IsRaw(extension))
                    {
                        if (TryRawPreview(path, out bitmap) && bitmap != null)
                        {
                            return bitmap;
                        }
                        hint = "RAW 文件中未找到内嵌 JPEG 预览。";
                    }
                    break;
            }

            if (TryLoadEmbeddedImage(path, out bitmap) && bitmap != null)
            {
                return bitmap;
            }
            throw new NotSupportedException(hint ?? "未找到可显示的内嵌预览图。");
        }
        catch (NotSupportedException)
        {
            bitmap?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            throw new NotSupportedException("无法解码此图片：" + ex.Message, ex);
        }
    }

    // ================= PSD / PSB =================

    private static class PsdDecoder
    {
        public static bool TryDecode(string path, out Bitmap? bitmap, out string error)
        {
            bitmap = null;
            error = "";
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length < 26)
                {
                    error = "PSD 文件过小或已损坏。";
                    return false;
                }
                var header = ReadExactly(stream, 26);
                if (Encoding.ASCII.GetString(header, 0, 4) != "8BPS")
                {
                    error = "不是有效的 PSD / PSB 文件。";
                    return false;
                }
                int version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                if (version is not (1 or 2))
                {
                    error = "不支持的 PSD 版本：" + version;
                    return false;
                }
                int channels = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(12));
                int height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(14));
                int width = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(18));
                int depth = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(22));
                int colorMode = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(24));
                if (channels <= 0 || width <= 0 || height <= 0)
                {
                    error = "PSD 尺寸或通道数无效。";
                    return false;
                }
                if ((long)width * height > MaxImagePixels)
                {
                    error = "PSD 尺寸过大，无法预览。";
                    return false;
                }

                long colorModeLength = ReadUInt32(stream);
                byte[]? palette = null;
                if (colorModeLength < 0 || colorModeLength > stream.Length)
                {
                    error = "PSD 调色板数据损坏。";
                    return false;
                }
                if (colorMode == 2)
                {
                    int paletteBytes = (int)Math.Min(colorModeLength, 768);
                    if (paletteBytes > 0)
                    {
                        var raw = ReadExactly(stream, paletteBytes);
                        palette = new byte[768];
                        Array.Copy(raw, palette, raw.Length);
                    }
                    Skip(stream, colorModeLength - paletteBytes);
                }
                else
                {
                    Skip(stream, colorModeLength);
                }
                long resourceLength = ReadUInt32(stream);
                if (resourceLength < 0 || resourceLength > stream.Length)
                {
                    error = "PSD 图像资源数据损坏。";
                    return false;
                }
                Skip(stream, resourceLength);
                long layerLength = version == 2 ? ReadUInt64(stream) : ReadUInt32(stream);
                if (layerLength < 0 || layerLength > stream.Length)
                {
                    error = "PSD 图层数据损坏。";
                    return false;
                }
                Skip(stream, layerLength);
                int compression = ReadUInt16(stream);

                if (colorMode == 9)
                {
                    error = "暂不支持 Lab 色彩模式的 PSD，请另存为 RGB 后重试。";
                    return false;
                }
                if (colorMode is not (0 or 1 or 2 or 3 or 4 or 7 or 8))
                {
                    error = "暂不支持该 PSD 色彩模式（模式 " + colorMode + "）。";
                    return false;
                }

                bool bitmapMode = colorMode == 0;
                if (!bitmapMode && depth is not (8 or 16))
                {
                    error = "暂不支持 " + depth + " 位通道的 PSD（支持 8 / 16 位）。";
                    return false;
                }
                if (bitmapMode && depth != 1)
                {
                    error = "PSD 位图模式的通道深度应为 1 位。";
                    return false;
                }

                int bytesPerSample = bitmapMode ? 1 : depth / 8;
                int rowBytes = bitmapMode ? (width + 7) / 8 : width * bytesPerSample;
                long planeSize = (long)rowBytes * height * channels;
                if (planeSize <= 0 || planeSize > int.MaxValue - 8)
                {
                    error = "PSD 图像数据过大。";
                    return false;
                }
                var plane = new byte[planeSize];
                switch (compression)
                {
                    case 0:
                        ReadExactly(stream, plane);
                        break;
                    case 1:
                        DecodeRle(stream, plane, channels, height, rowBytes, version == 2);
                        break;
                    case 2:
                    case 3:
                        var compressed = ReadToEnd(stream);
                        var inflated = Inflate(compressed, (int)planeSize);
                        Array.Copy(inflated, plane, Math.Min(inflated.Length, plane.Length));
                        if (compression == 3)
                        {
                            Unpredict(plane, channels, height, rowBytes, Math.Max(1, bytesPerSample));
                        }
                        break;
                    default:
                        error = "不支持的 PSD 压缩方式：" + compression;
                        return false;
                }

                bitmap = BuildPsdBitmap(plane, width, height, channels, rowBytes,
                    bytesPerSample, bitmapMode, colorMode, palette);
                return true;
            }
            catch (Exception ex)
            {
                error = "PSD 解码失败：" + ex.Message;
                return false;
            }
        }

        private static Bitmap BuildPsdBitmap(byte[] plane, int width, int height, int channels,
            int rowBytes, int bytesPerSample, bool bitmapMode, int colorMode, byte[]? palette)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var pixels = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * rowBytes;
                int target = y * width;
                for (int x = 0; x < width; x++)
                {
                    byte r, g, b, a = 255;
                    if (bitmapMode)
                    {
                        int bit = (plane[rowOffset + (x >> 3)] >> (7 - (x & 7))) & 1;
                        // PSD 位图模式：1 = 黑，0 = 白
                        byte value = bit == 1 ? (byte)0 : (byte)255;
                        r = g = b = value;
                    }
                    else
                    {
                        switch (colorMode)
                        {
                            case 2: // Indexed
                            {
                                int index = PsdSample(plane, 0, y, x, height, rowBytes, bytesPerSample);
                                if (palette != null && palette.Length >= 768)
                                {
                                    r = palette[index * 3];
                                    g = palette[index * 3 + 1];
                                    b = palette[index * 3 + 2];
                                }
                                else
                                {
                                    r = g = b = (byte)index;
                                }
                                if (channels >= 2)
                                {
                                    a = PsdSample(plane, 1, y, x, height, rowBytes, bytesPerSample);
                                }
                                break;
                            }
                            case 3: // RGB
                            case 7: // Multichannel（取前三个通道近似 RGB）
                            {
                                r = PsdSample(plane, 0, y, x, height, rowBytes, bytesPerSample);
                                g = channels >= 2
                                    ? PsdSample(plane, 1, y, x, height, rowBytes, bytesPerSample) : r;
                                b = channels >= 3
                                    ? PsdSample(plane, 2, y, x, height, rowBytes, bytesPerSample) : r;
                                if (channels >= 4)
                                {
                                    a = PsdSample(plane, 3, y, x, height, rowBytes, bytesPerSample);
                                }
                                break;
                            }
                            case 4: // CMYK
                            {
                                byte c = PsdSample(plane, 0, y, x, height, rowBytes, bytesPerSample);
                                byte m = channels >= 2
                                    ? PsdSample(plane, 1, y, x, height, rowBytes, bytesPerSample) : (byte)0;
                                byte yy = channels >= 3
                                    ? PsdSample(plane, 2, y, x, height, rowBytes, bytesPerSample) : (byte)0;
                                byte k = channels >= 4
                                    ? PsdSample(plane, 3, y, x, height, rowBytes, bytesPerSample) : (byte)0;
                                r = (byte)Math.Max(0, 255 - Math.Min(255, c + k));
                                g = (byte)Math.Max(0, 255 - Math.Min(255, m + k));
                                b = (byte)Math.Max(0, 255 - Math.Min(255, yy + k));
                                if (channels >= 5)
                                {
                                    a = PsdSample(plane, 4, y, x, height, rowBytes, bytesPerSample);
                                }
                                break;
                            }
                            default: // Grayscale / Duotone
                            {
                                r = g = b = PsdSample(plane, 0, y, x, height, rowBytes, bytesPerSample);
                                if (channels >= 2)
                                {
                                    a = PsdSample(plane, 1, y, x, height, rowBytes, bytesPerSample);
                                }
                                break;
                            }
                        }
                    }
                    pixels[target + x] = (a << 24) | (r << 16) | (g << 8) | b;
                }
            }
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * width, IntPtr.Add(data.Scan0, y * data.Stride), width);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        private static byte PsdSample(byte[] plane, int channel, int y, int x, int height,
            int rowBytes, int bytesPerSample)
        {
            return plane[(channel * height + y) * rowBytes + x * bytesPerSample];
        }

        private static void DecodeRle(Stream stream, byte[] plane, int channels, int height,
            int rowBytes, bool large)
        {
            int rows = channels * height;
            var lengths = new int[rows];
            for (int i = 0; i < rows; i++)
            {
                lengths[i] = large ? (int)ReadUInt32(stream) : ReadUInt16(stream);
            }
            for (int channel = 0; channel < channels; channel++)
            {
                for (int y = 0; y < height; y++)
                {
                    int rowIndex = channel * height + y;
                    int offset = rowIndex * rowBytes;
                    DecodePackBits(stream, plane, offset, rowBytes, lengths[rowIndex]);
                }
            }
        }

        private static void DecodePackBits(Stream stream, byte[] target, int offset, int length,
            int encodedLength)
        {
            int written = 0;
            int consumed = 0;
            while (written < length && consumed < encodedLength)
            {
                int value = stream.ReadByte();
                if (value < 0)
                {
                    break;
                }
                consumed++;
                if (value <= 127)
                {
                    int count = value + 1;
                    for (int i = 0; i < count && written < length; i++)
                    {
                        int data = stream.ReadByte();
                        consumed++;
                        if (data < 0)
                        {
                            break;
                        }
                        target[offset + written++] = (byte)data;
                    }
                }
                else if (value >= 129)
                {
                    int count = 257 - value;
                    int data = stream.ReadByte();
                    consumed++;
                    if (data < 0)
                    {
                        break;
                    }
                    for (int i = 0; i < count && written < length; i++)
                    {
                        target[offset + written++] = (byte)data;
                    }
                }
            }
            while (consumed < encodedLength)
            {
                if (stream.ReadByte() < 0)
                {
                    break;
                }
                consumed++;
            }
        }

        private static void Unpredict(byte[] plane, int channels, int height, int rowBytes, int bpp)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                for (int y = 0; y < height; y++)
                {
                    int offset = (channel * height + y) * rowBytes;
                    for (int i = bpp; i < rowBytes; i++)
                    {
                        plane[offset + i] = (byte)(plane[offset + i] + plane[offset + i - bpp]);
                    }
                }
            }
        }

        private static byte[] Inflate(byte[] compressed, int expected)
        {
            byte[]? Try(Func<Stream, Stream> create)
            {
                try
                {
                    using var input = new MemoryStream(compressed, writable: false);
                    using var decompressor = create(input);
                    var result = new byte[expected];
                    int total = 0;
                    while (total < expected)
                    {
                        int read = decompressor.Read(result, total, expected - total);
                        if (read <= 0)
                        {
                            break;
                        }
                        total += read;
                    }
                    return total > 0 ? result : null;
                }
                catch
                {
                    return null;
                }
            }

            return Try(input => new ZLibStream(input, CompressionMode.Decompress))
                ?? Try(input => new DeflateStream(input, CompressionMode.Decompress))
                ?? new byte[expected];
        }
    }

    // ================= XCF =================

    private static bool TryXcfThumbnail(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 32)
            {
                return false;
            }
            var header = ReadExactly(stream, 14);
            if (Encoding.ASCII.GetString(header, 0, 9) != "gimp xcf ")
            {
                return false;
            }
            long width = ReadUInt32(stream);
            long height = ReadUInt32(stream);
            long baseType = ReadUInt32(stream);
            _ = width;
            _ = height;
            _ = baseType;
            while (stream.Position + 8 <= stream.Length)
            {
                long type = ReadUInt32(stream);
                long size = ReadUInt32(stream);
                if (type == 0)
                {
                    break;
                }
                if (size < 0 || size > 128L * 1024 * 1024 || stream.Position + size > stream.Length)
                {
                    return false;
                }
                long propertyEnd = stream.Position + size;
                if (type == 21) // PROP_PARASITES
                {
                    while (stream.Position + 4 <= propertyEnd)
                    {
                        long nameLength = ReadUInt32(stream);
                        if (nameLength < 0 || nameLength > 4096 || stream.Position + nameLength + 8 > propertyEnd)
                        {
                            break;
                        }
                        string name = Encoding.UTF8.GetString(ReadExactly(stream, (int)nameLength));
                        ReadUInt32(stream); // flags
                        long dataLength = ReadUInt32(stream);
                        if (dataLength < 0 || dataLength > 32L * 1024 * 1024
                            || stream.Position + dataLength > propertyEnd)
                        {
                            break;
                        }
                        var data = ReadExactly(stream, (int)dataLength);
                        if (name.Contains("thumb", StringComparison.OrdinalIgnoreCase)
                            && TryBitmapFromBytes(data, out bitmap) && bitmap != null)
                        {
                            return true;
                        }
                    }
                }
                stream.Position = propertyEnd;
            }
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
        }
        return false;
    }

    // ================= CDR =================

    private static bool TryCdrPreview(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var signature = new byte[4];
            if (stream.Read(signature, 0, 4) < 4)
            {
                return false;
            }
            bool zip = signature[0] == (byte)'P' && signature[1] == (byte)'K'
                && (signature[2] == 3 || signature[2] == 5);
            if (zip)
            {
                using var archive = ZipFile.OpenRead(path);
                var entries = archive.Entries
                    .Where(entry => entry.Length > 0 && entry.Length <= 32L * 1024 * 1024
                        && IsPreviewImageName(entry.FullName))
                    .OrderByDescending(entry => PreviewPriority(entry.FullName))
                    .ThenByDescending(entry => entry.Length)
                    .Take(8);
                foreach (var entry in entries)
                {
                    using var entryStream = entry.Open();
                    using var buffer = new MemoryStream();
                    entryStream.CopyTo(buffer);
                    if (TryBitmapFromBytes(buffer.ToArray(), out bitmap) && bitmap != null)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
        }
        return TryLoadEmbeddedImage(path, out bitmap);
    }

    private static bool IsPreviewImageName(string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".gif";
    }

    private static int PreviewPriority(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("preview"))
        {
            return 2;
        }
        return lower.Contains("thumb") ? 1 : 0;
    }

    // ================= RAW =================

    private static bool TryRawPreview(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            if (Path.GetExtension(path).Equals(".raf", StringComparison.OrdinalIgnoreCase)
                && TryFujiPreview(path, out bitmap) && bitmap != null)
            {
                return true;
            }
            if (TryTiffPreview(path, out bitmap) && bitmap != null)
            {
                return true;
            }
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
        }
        return TryLoadEmbeddedImage(path, out bitmap);
    }

    private static bool TryFujiPreview(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = ReadExactly(stream, 16);
            if (Encoding.ASCII.GetString(header, 0, 8) != "FUJIFILM")
            {
                return false;
            }
            stream.Position = 84;
            var offsets = ReadExactly(stream, 8);
            long start = BinaryPrimitives.ReadUInt32BigEndian(offsets);
            long length = BinaryPrimitives.ReadUInt32BigEndian(offsets.AsSpan(4));
            if (start <= 0 || length <= 512 || start + length > stream.Length)
            {
                return false;
            }
            stream.Position = start;
            var data = ReadExactly(stream, (int)length);
            return TryBitmapFromBytes(data, out bitmap) && bitmap != null;
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
    }

    private static bool TryTiffPreview(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 8)
            {
                return false;
            }
            var head = ReadExactly(stream, 8);
            bool little;
            if (head[0] == (byte)'I' && head[1] == (byte)'I')
            {
                little = true;
            }
            else if (head[0] == (byte)'M' && head[1] == (byte)'M')
            {
                little = false;
            }
            else
            {
                return false;
            }
            int magic = ReadUInt16(head, 2, little);
            if (magic != 42)
            {
                return false;
            }
            long offset = ReadUInt32(head, 4, little);
            var candidates = new List<(long Offset, long Length)>();
            CollectTiffIfds(stream, offset, little, 0, candidates, new HashSet<long>());
            foreach (var candidate in candidates
                .Where(item => item.Offset > 0 && item.Length > 512 && item.Length <= 32L * 1024 * 1024)
                .OrderByDescending(item => item.Length)
                .Take(12))
            {
                if (candidate.Offset + candidate.Length > stream.Length)
                {
                    continue;
                }
                stream.Position = candidate.Offset;
                var data = ReadExactly(stream, (int)candidate.Length);
                if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
                {
                    continue;
                }
                if (TryBitmapFromBytes(data, out bitmap) && bitmap != null)
                {
                    if ((long)bitmap.Width * bitmap.Height >= 160 * 120)
                    {
                        return true;
                    }
                    bitmap.Dispose();
                    bitmap = null;
                }
            }
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
        }
        return false;
    }

    private static void CollectTiffIfds(Stream stream, long offset, bool little, int depth,
        List<(long Offset, long Length)> candidates, HashSet<long> visited)
    {
        if (depth > 2 || offset <= 0 || offset + 2 > stream.Length || !visited.Add(offset))
        {
            return;
        }
        var countBytes = ReadExactly(stream, 2);
        int count = ReadUInt16(countBytes, 0, little);
        if (count <= 0 || count > 512)
        {
            return;
        }
        long jpegOffset = 0;
        long jpegLength = 0;
        var subIfds = new List<long>();
        for (int i = 0; i < count; i++)
        {
            var entry = ReadExactly(stream, 12);
            int tag = ReadUInt16(entry, 0, little);
            int type = ReadUInt16(entry, 2, little);
            long valueCount = ReadUInt32(entry, 4, little);
            long value = ReadUInt32(entry, 8, little);
            if (tag == 0x0201 && valueCount == 1 && type is 3 or 4)
            {
                jpegOffset = type == 3 ? ReadUInt16(entry, 8, little) : value;
            }
            else if (tag == 0x0202 && valueCount == 1 && type is 3 or 4)
            {
                jpegLength = type == 3 ? ReadUInt16(entry, 8, little) : value;
            }
            else if (tag == 0x014A && type == 4 && valueCount > 0 && valueCount <= 32)
            {
                if (valueCount == 1)
                {
                    subIfds.Add(value);
                }
                else
                {
                    long save = stream.Position;
                    if (value > 0 && value + valueCount * 4 <= stream.Length)
                    {
                        stream.Position = value;
                        for (int k = 0; k < valueCount; k++)
                        {
                            subIfds.Add(ReadUInt32(ReadExactly(stream, 4), 0, little));
                        }
                    }
                    stream.Position = save;
                }
            }
        }
        var nextBytes = ReadExactly(stream, 4);
        long next = ReadUInt32(nextBytes, 0, little);
        if (jpegOffset > 0 && jpegLength > 0)
        {
            candidates.Add((jpegOffset, jpegLength));
        }
        foreach (long sub in subIfds)
        {
            CollectTiffIfds(stream, sub, little, depth + 1, candidates, visited);
        }
        if (next > 0)
        {
            CollectTiffIfds(stream, next, little, depth, candidates, visited);
        }
    }

    // ================= EPS / AI =================

    private static bool TryEpsPreview(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        if (TryDosEpsPreview(path, out bitmap) && bitmap != null)
        {
            return true;
        }
        if (TryEpsHexPreview(path, out bitmap) && bitmap != null)
        {
            return true;
        }
        return TryLoadEmbeddedImage(path, out bitmap);
    }

    private static bool TryDosEpsPreview(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 30)
            {
                return false;
            }
            var header = ReadExactly(stream, 30);
            if (header[0] != 0xC5 || header[1] != 0xD0 || header[2] != 0xD3 || header[3] != 0xC6)
            {
                return false;
            }
            long tiffStart = ReadUInt32(header, 20, true);
            long tiffLength = ReadUInt32(header, 24, true);
            if (tiffStart <= 0 || tiffLength <= 512 || tiffStart + tiffLength > stream.Length)
            {
                return false;
            }
            stream.Position = tiffStart;
            var data = ReadExactly(stream, (int)tiffLength);
            return TryBitmapFromBytes(data, out bitmap) && bitmap != null;
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
    }

    private static bool TryEpsHexPreview(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int length = (int)Math.Min(stream.Length, 16L * 1024 * 1024);
            var data = ReadExactly(stream, length);
            string text = Encoding.Latin1.GetString(data);
            int begin = text.IndexOf("%%BeginPreview:", StringComparison.OrdinalIgnoreCase);
            if (begin < 0)
            {
                return false;
            }
            int specStart = begin + "%%BeginPreview:".Length;
            int specEnd = text.IndexOf('\n', specStart);
            if (specEnd < 0)
            {
                return false;
            }
            string spec = text[specStart..specEnd].Trim();
            var parts = spec.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3
                || !int.TryParse(parts[0], out int width)
                || !int.TryParse(parts[1], out int height)
                || !int.TryParse(parts[2], out int depth))
            {
                return false;
            }
            if (width <= 0 || height <= 0 || (long)width * height > MaxImagePixels
                || depth is not (1 or 4 or 8 or 24 or 32))
            {
                return false;
            }
            int end = text.IndexOf("%%EndPreview", specEnd, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return false;
            }
            var hex = new StringBuilder(end - specEnd);
            for (int i = specEnd + 1; i < end; i++)
            {
                char ch = text[i];
                if (Uri.IsHexDigit(ch))
                {
                    hex.Append(ch);
                }
            }
            int rowBytes = (int)Math.Ceiling(width * depth / 8.0);
            int expected = rowBytes * height;
            if (hex.Length < 2)
            {
                return false;
            }
            int bytes = Math.Min(expected, hex.Length / 2);
            var pixels = new byte[bytes];
            for (int i = 0; i < bytes; i++)
            {
                int high = HexValue(hex[i * 2]);
                int low = HexValue(hex[i * 2 + 1]);
                if (high < 0 || low < 0)
                {
                    return false;
                }
                pixels[i] = (byte)((high << 4) | low);
            }
            bitmap = BuildEpsBitmap(pixels, width, height, rowBytes, depth);
            return bitmap != null;
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
    }

    private static Bitmap? BuildEpsBitmap(byte[] data, int width, int height, int rowBytes, int depth)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;
                switch (depth)
                {
                    case 1:
                    {
                        byte packed = row + (x >> 3) < data.Length ? data[row + (x >> 3)] : (byte)0;
                        int bit = (packed >> (7 - (x & 7))) & 1;
                        r = g = b = bit == 1 ? (byte)255 : (byte)0;
                        break;
                    }
                    case 4:
                    {
                        byte packed = row + (x >> 1) < data.Length ? data[row + (x >> 1)] : (byte)0;
                        byte value = (byte)(((packed >> (x % 2 == 0 ? 4 : 0)) & 0x0F) * 17);
                        r = g = b = value;
                        break;
                    }
                    case 8:
                    {
                        byte value = row + x < data.Length ? data[row + x] : (byte)255;
                        r = g = b = value;
                        break;
                    }
                    case 24:
                    {
                        int offset = row + x * 3;
                        if (offset + 2 >= data.Length)
                        {
                            r = g = b = 255;
                        }
                        else
                        {
                            r = data[offset];
                            g = data[offset + 1];
                            b = data[offset + 2];
                        }
                        break;
                    }
                    default: // 32 位 CMYK
                    {
                        int offset = row + x * 4;
                        if (offset + 3 >= data.Length)
                        {
                            r = g = b = 255;
                        }
                        else
                        {
                            byte c = data[offset];
                            byte m = data[offset + 1];
                            byte yy = data[offset + 2];
                            byte k = data[offset + 3];
                            r = (byte)Math.Max(0, 255 - Math.Min(255, c + k));
                            g = (byte)Math.Max(0, 255 - Math.Min(255, m + k));
                            b = (byte)Math.Max(0, 255 - Math.Min(255, yy + k));
                        }
                        break;
                    }
                }
                pixels[y * width + x] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }
        var locked = bitmap.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, y * width, IntPtr.Add(locked.Scan0, y * locked.Stride), width);
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
        return bitmap;
    }

    private static int HexValue(char ch)
    {
        if (ch >= '0' && ch <= '9') return ch - '0';
        if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
        if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
        return -1;
    }

    /// <summary>
    /// EPS / AI 没有内嵌预览时，若系统安装了 Ghostscript 则调用它渲染为 PNG。
    /// 不强制依赖：未安装时返回 false，由调用方给出可读的提示。
    /// </summary>
    private static bool TryGhostscript(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        string? ghostscript = FindGhostscript();
        if (ghostscript == null)
        {
            return false;
        }
        string temp = Path.Combine(AppPaths.Temp,
            "MiniMonitor_gs_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ghostscript,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-dSAFER");
            startInfo.ArgumentList.Add("-dBATCH");
            startInfo.ArgumentList.Add("-dNOPAUSE");
            startInfo.ArgumentList.Add("-sDEVICE=pngalpha");
            startInfo.ArgumentList.Add("-r150");
            startInfo.ArgumentList.Add("-dTextAlphaBits=4");
            startInfo.ArgumentList.Add("-dGraphicsAlphaBits=4");
            if (Path.GetExtension(path).Equals(".eps", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("-dEPSCrop");
            }
            startInfo.ArgumentList.Add("-sOutputFile=" + temp);
            startInfo.ArgumentList.Add(path);
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }
            if (!process.WaitForExit(20000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }
                return false;
            }
            if (process.ExitCode != 0 || !File.Exists(temp))
            {
                return false;
            }
            return TryBitmapFromBytes(File.ReadAllBytes(temp), out bitmap) && bitmap != null;
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string? FindGhostscript()
    {
        string[] names = { "gswin64c.exe", "gswin32c.exe" };
        foreach (string name in names)
        {
            string[] direct =
            {
                Path.Combine(AppContext.BaseDirectory, name),
                Path.Combine(Environment.CurrentDirectory, name)
            };
            foreach (string candidate in direct)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (string directory in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in names)
                {
                    string candidate = Path.Combine(directory.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }
            try
            {
                foreach (string folder in Directory.GetDirectories(root, "gs*"))
                {
                    foreach (string name in names)
                    {
                        string candidate = Path.Combine(folder, "bin", name);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
        return null;
    }

    // ================= 通用内嵌预览提取 =================

    private static bool TryLoadEmbeddedImage(string path, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int length = (int)Math.Min(stream.Length, MaxEmbeddedScan);
            if (length < 64)
            {
                return false;
            }
            var data = new byte[length];
            stream.ReadExactly(data, 0, length);
            var candidates = new List<(int Start, int Length)>();
            ReadOnlySpan<byte> pngSignature = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
            };
            ReadOnlySpan<byte> pngEnd = new byte[] { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };
            for (int i = 0; i + 8 <= data.Length; i++)
            {
                if (data.AsSpan(i, 8).SequenceEqual(pngSignature))
                {
                    int end = IndexOf(data, pngEnd, i + 8);
                    if (end > i)
                    {
                        candidates.Add((i, end + 8 - i));
                    }
                }
            }
            var starts = new List<int>();
            var ends = new List<int>();
            for (int i = 0; i + 2 < data.Length; i++)
            {
                if (data[i] == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF)
                {
                    if (starts.Count < 64)
                    {
                        starts.Add(i);
                    }
                }
                else if (data[i] == 0xFF && data[i + 1] == 0xD9 && ends.Count < 256)
                {
                    ends.Add(i);
                }
            }
            foreach (int start in starts)
            {
                int first = -1;
                int last = -1;
                foreach (int end in ends)
                {
                    if (end <= start)
                    {
                        continue;
                    }
                    if (first < 0)
                    {
                        first = end;
                    }
                    last = end;
                }
                if (first > 0)
                {
                    candidates.Add((start, first + 2 - start));
                }
                if (last > first)
                {
                    candidates.Add((start, last + 2 - start));
                }
            }
            foreach (var candidate in candidates
                .Where(item => item.Length >= 512 && item.Length <= 48 * 1024 * 1024)
                .OrderByDescending(item => item.Length)
                .Take(16))
            {
                if (candidate.Start + candidate.Length > data.Length)
                {
                    continue;
                }
                var slice = new byte[candidate.Length];
                Buffer.BlockCopy(data, candidate.Start, slice, 0, candidate.Length);
                if (TryBitmapFromBytes(slice, out bitmap) && bitmap != null)
                {
                    if ((long)bitmap.Width * bitmap.Height >= 10000)
                    {
                        return true;
                    }
                    bitmap.Dispose();
                    bitmap = null;
                }
            }
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
        }
        return false;
    }

    private static bool TryBitmapFromBytes(byte[] data, out Bitmap? bitmap)
    {
        bitmap = null;
        if (data.Length < 64)
        {
            return false;
        }
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false,
                validateImageData: true);
            if (image.Width <= 0 || image.Height <= 0
                || (long)image.Width * image.Height > MaxImagePixels)
            {
                return false;
            }
            var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(copy))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
            }
            bitmap = copy;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ================= 基础读写 =================

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var buffer = new byte[length];
        stream.ReadExactly(buffer, 0, length);
        return buffer;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        stream.ReadExactly(buffer, 0, buffer.Length);
    }

    private static byte[] ReadToEnd(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Skip(Stream stream, long count)
    {
        if (count <= 0)
        {
            return;
        }
        if (stream.CanSeek)
        {
            stream.Seek(count, SeekOrigin.Current);
            return;
        }
        var buffer = new byte[8192];
        long remaining = count;
        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                break;
            }
            remaining -= read;
        }
    }

    private static int ReadUInt16(Stream stream)
    {
        int high = stream.ReadByte();
        int low = stream.ReadByte();
        if (high < 0 || low < 0)
        {
            throw new EndOfStreamException();
        }
        return (high << 8) | low;
    }

    private static long ReadUInt32(Stream stream)
    {
        var buffer = ReadExactly(stream, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private static long ReadUInt64(Stream stream)
    {
        var buffer = ReadExactly(stream, 8);
        return (long)BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private static int ReadUInt16(byte[] buffer, int offset, bool little)
    {
        return little
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset));
    }

    private static long ReadUInt32(byte[] buffer, int offset, bool little)
    {
        return little
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern, int start = 0)
    {
        if (pattern.Length == 0)
        {
            return -1;
        }
        for (int i = Math.Max(0, start); i + pattern.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }
        return -1;
    }
}
