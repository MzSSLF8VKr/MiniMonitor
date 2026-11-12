using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace MiniMonitor;

/// <summary>照片处理用到的照片滤镜。</summary>
internal enum PhotoFilter
{
    None,
    Grayscale,
    Sepia,
    Cool,
    Warm,
    Invert
}

/// <summary>亮度 / 对比度 / 饱和度 / 滤镜，非破坏性调整参数。</summary>
internal sealed class PhotoAdjustment
{
    public PhotoFilter Filter { get; set; } = PhotoFilter.None;

    /// <summary>亮度 -50 ~ 50</summary>
    public int Brightness { get; set; }

    /// <summary>对比度 -50 ~ 50</summary>
    public int Contrast { get; set; }

    /// <summary>饱和度 -100 ~ 100</summary>
    public int Saturation { get; set; }

    public bool IsNeutral => Filter == PhotoFilter.None
        && Brightness == 0 && Contrast == 0 && Saturation == 0;

    public PhotoAdjustment Clone()
    {
        return new PhotoAdjustment
        {
            Filter = Filter,
            Brightness = Brightness,
            Contrast = Contrast,
            Saturation = Saturation
        };
    }
}

/// <summary>保存参数：格式、质量、是否保留 EXIF（可只剔除 GPS）。</summary>
internal sealed class PhotoSaveOptions
{
    public ImageFormat Format { get; set; } = ImageFormat.Jpeg;
    public int Quality { get; set; } = 90;

    /// <summary>要复制的 EXIF 来源；为 null 表示不保留任何元数据。</summary>
    public Bitmap? MetadataSource { get; set; }

    /// <summary>保留元数据时是否包含 GPS 定位信息。</summary>
    public bool IncludeGps { get; set; } = true;
}

internal sealed class PhotoFileInfo
{
    public required string Path { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileSize { get; init; }
    public string FormatName { get; init; } = "";
}

internal sealed class ExifEntry
{
    public int Id { get; init; }
    public string Tag { get; init; } = "";
    public string Value { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Length { get; init; }
}

internal sealed class ExifInfo
{
    public List<ExifEntry> Entries { get; } = new();
    public bool HasGps { get; set; }
    public string Camera { get; set; } = "";
    public string DateTaken { get; set; } = "";
    public string Orientation { get; set; } = "";
    public string Dimensions { get; set; } = "";
}

/// <summary>
/// 照片处理公共能力：格式识别、EXIF 方向 / 信息读取、缩放裁剪、色彩调整、编码保存。
/// 全部基于 System.Drawing（GDI+），不依赖第三方图像库。
/// </summary>
internal static class PhotoToolkit
{
    public const int OrientationTag = 0x0112;
    public const int GpsIfdPointerTag = 0x8825;

    public static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
    };

    public const string OpenFileFilter =
        "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|"
        + "JPEG|*.jpg;*.jpeg|PNG|*.png|BMP|*.bmp|GIF|*.gif|TIFF|*.tif;*.tiff|所有文件|*.*";

    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path);
        return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public static ImageFormat? FormatFromPath(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".bmp" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            _ => null
        };
    }

    public static string ExtensionFor(ImageFormat format)
    {
        if (format.Guid == ImageFormat.Jpeg.Guid) return ".jpg";
        if (format.Guid == ImageFormat.Png.Guid) return ".png";
        if (format.Guid == ImageFormat.Bmp.Guid) return ".bmp";
        if (format.Guid == ImageFormat.Gif.Guid) return ".gif";
        if (format.Guid == ImageFormat.Tiff.Guid) return ".tif";
        return ".jpg";
    }

    public static string FormatName(ImageFormat format)
    {
        if (format.Guid == ImageFormat.Jpeg.Guid) return "JPEG";
        if (format.Guid == ImageFormat.Png.Guid) return "PNG";
        if (format.Guid == ImageFormat.Bmp.Guid) return "BMP";
        if (format.Guid == ImageFormat.Gif.Guid) return "GIF";
        if (format.Guid == ImageFormat.Tiff.Guid) return "TIFF";
        return "图片";
    }

    /// <summary>读取图片宽高与文件大小（不解码全部像素的场合由 GDI+ 延迟处理）。</summary>
    public static PhotoFileInfo ReadFileInfo(string path)
    {
        using var image = Image.FromFile(path);
        return new PhotoFileInfo
        {
            Path = path,
            Width = image.Width,
            Height = image.Height,
            FileSize = new FileInfo(path).Length,
            FormatName = FormatName(image.RawFormat)
        };
    }

    /// <summary>
    /// 加载图片为可自由处理的 Bitmap。autoOrient=true 时按 EXIF 方向把像素摆正，
    /// 并移除方向标记，后续旋转/缩放不会被再次旋转。
    /// </summary>
    public static Bitmap LoadBitmap(string path, bool autoOrient = true)
    {
        using var image = Image.FromFile(path);
        var bitmap = (Bitmap)image.Clone();
        CopyPropertyItems(image, bitmap);
        if (autoOrient)
        {
            ApplyOrientationToBitmap(bitmap, removeTag: true);
        }
        return bitmap;
    }

    /// <summary>
    /// GDI+ 的 Clone 在不同系统上对 PropertyItems 的保留行为不一致，
    /// 这里显式补齐元数据，确保“保留 EXIF”和 EXIF 查看功能可靠。
    /// </summary>
    private static void CopyPropertyItems(Image source, Bitmap dest)
    {
        try
        {
            var existing = new HashSet<int>(dest.PropertyItems.Select(item => item.Id));
            foreach (var item in source.PropertyItems)
            {
                if (existing.Contains(item.Id))
                {
                    continue;
                }
                try
                {
                    dest.SetPropertyItem(item);
                }
                catch
                {
                    // 个别标签 GDI+ 不接受，忽略
                }
            }
        }
        catch
        {
            // 来源格式不支持元数据时忽略
        }
    }

    private static int ReadOrientation(Image image)
    {
        try
        {
            var item = image.GetPropertyItem(OrientationTag);
            if (item?.Value is { Length: >= 2 })
            {
                return BitConverter.ToUInt16(item.Value, 0);
            }
        }
        catch
        {
            // 无方向标记
        }
        return 1;
    }

    private static void ApplyOrientationToBitmap(Bitmap bitmap, bool removeTag)
    {
        int orientation = ReadOrientation(bitmap);
        var mode = OrientationToRotateFlip(orientation);
        if (mode.HasValue)
        {
            bitmap.RotateFlip(mode.Value);
        }
        if (removeTag)
        {
            try
            {
                bitmap.RemovePropertyItem(OrientationTag);
            }
            catch
            {
                // 没有该标记时忽略
            }
        }
    }

    private static RotateFlipType? OrientationToRotateFlip(int orientation)
    {
        return orientation switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.RotateNoneFlipY,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => null
        };
    }

    /// <summary>按 EXIF 方向把像素摆正并返回新图；无方向标记时返回副本。</summary>
    public static Bitmap AutoOrient(Bitmap source)
    {
        int orientation = ReadOrientation(source);
        var clone = (Bitmap)source.Clone();
        CopyPropertyItems(source, clone);
        var mode = OrientationToRotateFlip(orientation);
        if (mode.HasValue)
        {
            clone.RotateFlip(mode.Value);
        }
        try
        {
            clone.RemovePropertyItem(OrientationTag);
        }
        catch
        {
            // 没有方向标记时忽略
        }
        return clone;
    }

    public static Bitmap RotateFlip(Bitmap source, RotateFlipType type)
    {
        var clone = (Bitmap)source.Clone();
        clone.RotateFlip(type);
        return clone;
    }

    public static Bitmap Crop(Bitmap source, Rectangle rect)
    {
        var bounds = new Rectangle(Point.Empty, source.Size);
        var safe = Rectangle.Intersect(rect, bounds);
        if (safe.Width < 1 || safe.Height < 1)
        {
            return (Bitmap)source.Clone();
        }
        var dest = new Bitmap(safe.Width, safe.Height, PixelFormat.Format32bppArgb);
        CopyResolution(source, dest);
        using var g = Graphics.FromImage(dest);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, safe.Width, safe.Height),
            safe.X, safe.Y, safe.Width, safe.Height, GraphicsUnit.Pixel);
        return dest;
    }

    public static Bitmap Resize(Bitmap source, int width, int height)
    {
        width = Math.Clamp(width, 1, 20000);
        height = Math.Clamp(height, 1, 20000);
        if (width == source.Width && height == source.Height)
        {
            return (Bitmap)source.Clone();
        }
        var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        CopyResolution(source, dest);
        using var g = Graphics.FromImage(dest);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height));
        return dest;
    }

    /// <summary>
    /// 等比缩放到不超过指定边长；原图本来就足够小时直接返回原图（不复制，调用方需判断引用）。
    /// </summary>
    public static Bitmap ScaleToFit(Bitmap source, int maxWidth, int maxHeight)
    {
        if (source.Width <= maxWidth && source.Height <= maxHeight)
        {
            return source;
        }
        double scale = Math.Min(maxWidth / (double)source.Width, maxHeight / (double)source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return Resize(source, width, height);
    }

    private static void CopyResolution(Image source, Bitmap dest)
    {
        try
        {
            float dx = source.HorizontalResolution;
            float dy = source.VerticalResolution;
            if (dx is > 1 and < 4000 && dy is > 1 and < 4000)
            {
                dest.SetResolution(dx, dy);
            }
        }
        catch
        {
            // 保持 96 DPI
        }
    }

    /// <summary>
    /// 按目标宽高做“裁剪填满”构图：按目标比例从原图裁出最大区域，
    /// horizontalOffset / verticalOffset 为 -100 ~ 100 的构图偏移，0 表示居中。
    /// </summary>
    public static Bitmap FitCover(Bitmap source, int targetWidth, int targetHeight,
        int horizontalOffsetPercent, int verticalOffsetPercent)
    {
        return FitCover(source, targetWidth, targetHeight,
            horizontalOffsetPercent, verticalOffsetPercent, null);
    }

    public static Bitmap FitCover(Bitmap source, int targetWidth, int targetHeight,
        int horizontalOffsetPercent, int verticalOffsetPercent, Rectangle? sourceCrop)
    {
        targetWidth = Math.Clamp(targetWidth, 1, 20000);
        targetHeight = Math.Clamp(targetHeight, 1, 20000);
        double targetAspect = targetWidth / (double)targetHeight;

        var bounds = sourceCrop.HasValue
            ? Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), sourceCrop.Value)
            : new Rectangle(0, 0, source.Width, source.Height);
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            bounds = new Rectangle(0, 0, source.Width, source.Height);
        }
        double sourceAspect = bounds.Width / (double)bounds.Height;

        int cropWidth;
        int cropHeight;
        if (sourceAspect > targetAspect)
        {
            cropHeight = bounds.Height;
            cropWidth = Math.Max(1, (int)Math.Round(cropHeight * targetAspect));
        }
        else
        {
            cropWidth = bounds.Width;
            cropHeight = Math.Max(1, (int)Math.Round(cropWidth / targetAspect));
        }
        cropWidth = Math.Min(cropWidth, bounds.Width);
        cropHeight = Math.Min(cropHeight, bounds.Height);

        int slackX = bounds.Width - cropWidth;
        int slackY = bounds.Height - cropHeight;
        double px = (Math.Clamp(horizontalOffsetPercent, -100, 100) + 100) / 200.0;
        double py = (Math.Clamp(verticalOffsetPercent, -100, 100) + 100) / 200.0;
        int cropX = bounds.X + Math.Clamp((int)Math.Round(slackX * px), 0, slackX);
        int cropY = bounds.Y + Math.Clamp((int)Math.Round(slackY * py), 0, slackY);

        var dest = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        CopyResolution(source, dest);
        using var g = Graphics.FromImage(dest);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight),
            cropX, cropY, cropWidth, cropHeight, GraphicsUnit.Pixel);
        return dest;
    }

    /// <summary>从图片四周边缘采样，自动估计纯色背景色（取出现次数最多的颜色区间）。</summary>
    public static Color DetectBackgroundColor(Bitmap source)
    {
        var bitmap = ToArgb32(source);
        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            if (width < 2 || height < 2)
            {
                return Color.White;
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] bytes;
            int stride;
            try
            {
                stride = Math.Abs(data.Stride);
                bytes = new byte[stride * height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            int band = Math.Max(2, (int)(Math.Min(width, height) * 0.05));
            int step = Math.Max(1, Math.Min(width, height) / 260);
            var buckets = new Dictionary<int, ColorBucket>();

            void Sample(int x, int y)
            {
                int offset = y * stride + x * 4;
                if (offset + 3 >= bytes.Length)
                {
                    return;
                }
                if (bytes[offset + 3] < 128)
                {
                    return;
                }
                byte b = bytes[offset];
                byte g = bytes[offset + 1];
                byte r = bytes[offset + 2];
                int key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new ColorBucket();
                    buckets[key] = bucket;
                }
                bucket.Add(r, g, b);
            }

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < band; x += step)
                {
                    Sample(x, y);
                    Sample(width - 1 - x, y);
                }
            }
            for (int x = 0; x < width; x += step)
            {
                for (int y = 0; y < band; y += step)
                {
                    Sample(x, y);
                    Sample(x, height - 1 - y);
                }
            }

            var dominant = buckets.Values
                .OrderByDescending(bucket => bucket.Count)
                .FirstOrDefault();
            return dominant == null
                ? Color.White
                : Color.FromArgb(dominant.R, dominant.G, dominant.B);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// 自动纯色背景替换。默认启用“发丝级”精细抠图：只从与画面边缘连通的背景区域开始去除，
    /// 内部同色衣服/皮肤不会被误删；边缘带使用前景色采样与 alpha 估算，再经边缘保持滤波细化。
    /// tolerance 为 0 ~ 100，越大去掉的颜色范围越广。
    /// </summary>
    public static Bitmap ReplaceBackground(Bitmap source, Color background,
        Color replacement, int tolerance)
    {
        return ReplaceBackground(source, background, replacement, tolerance, true);
    }

    public static Bitmap ReplaceBackground(Bitmap source, Color background,
        Color replacement, int tolerance, bool fineDetail)
    {
        if (!fineDetail || (long)source.Width * source.Height > 6_000_000)
        {
            return ReplaceBackgroundLegacy(source, background, replacement, tolerance);
        }
        return ReplaceBackgroundFine(source, background, replacement, tolerance);
    }

    private static Bitmap ReplaceBackgroundLegacy(Bitmap source, Color background,
        Color replacement, int tolerance)
    {
        var bitmap = ToArgb32(source);
        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] sourceBytes;
            int stride;
            try
            {
                stride = Math.Abs(data.Stride);
                sourceBytes = new byte[stride * height];
                Marshal.Copy(data.Scan0, sourceBytes, 0, sourceBytes.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            float threshold = Math.Clamp(tolerance, 0, 100) / 100f * 0.35f;
            const float feather = 0.06f;
            float limit = threshold + feather;
            var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            CopyResolution(source, dest);

            var outData = dest.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int outStride = Math.Abs(outData.Stride);
                var outBytes = new byte[outStride * height];
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    int outRow = y * outStride;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = row + x * 4;
                        int outOffset = outRow + x * 4;
                        byte b = sourceBytes[offset];
                        byte g = sourceBytes[offset + 1];
                        byte r = sourceBytes[offset + 2];
                        int dr = r - background.R;
                        int dg = g - background.G;
                        int db = b - background.B;
                        float distance = (float)Math.Sqrt(
                            (dr * dr + dg * dg + db * db) / 3.0) / 255f;

                        float alpha;
                        if (distance <= threshold)
                        {
                            alpha = 0f;
                        }
                        else if (distance < limit)
                        {
                            alpha = (distance - threshold) / feather;
                        }
                        else
                        {
                            alpha = 1f;
                        }
                        alpha *= sourceBytes[offset + 3] / 255f;

                        outBytes[outOffset] = (byte)(b * alpha + replacement.B * (1f - alpha));
                        outBytes[outOffset + 1] = (byte)(g * alpha + replacement.G * (1f - alpha));
                        outBytes[outOffset + 2] = (byte)(r * alpha + replacement.R * (1f - alpha));
                        outBytes[outOffset + 3] = 255;
                    }
                }
                Marshal.Copy(outBytes, 0, outData.Scan0, outBytes.Length);
            }
            finally
            {
                dest.UnlockBits(outData);
            }
            return dest;
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// 发丝级背景替换：
    /// 1) 仅把与画面边缘连通的背景色区域判为背景，避免同色衣服、皮肤被整片误删；
    /// 2) 在背景边缘附近建立窄 “未知带”，用最近前景色做颜色投影估算半透明 alpha；
    /// 3) 对 alpha 做边缘保持的双边滤波，并用去背景色污染的前景再合成，减少白边/蓝边。
    /// </summary>
    private static Bitmap ReplaceBackgroundFine(Bitmap source, Color background,
        Color replacement, int tolerance)
    {
        var bitmap = ToArgb32(source);
        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int count = width * height;
            if (width < 2 || height < 2)
            {
                return (Bitmap)source.Clone();
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels;
            int stride;
            try
            {
                stride = Math.Abs(data.Stride);
                pixels = new byte[stride * height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            float toleranceValue = Math.Clamp(tolerance, 0, 100);
            // 颜色距离阈值：容差越大，判定为背景的颜色范围越宽。
            float threshold = 0.018f + toleranceValue / 100f * 0.30f;
            float seedThreshold = Math.Min(0.62f, threshold + 0.050f);
            float foregroundCut = Math.Min(0.78f, seedThreshold + 0.18f);

            float[] distance = new float[count];
            bool[] backgroundLike = new bool[count];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                int index = y * width;
                for (int x = 0; x < width; x++)
                {
                    int offset = row + x * 4;
                    int dr = pixels[offset + 2] - background.R;
                    int dg = pixels[offset + 1] - background.G;
                    int db = pixels[offset] - background.B;
                    float value = (float)Math.Sqrt(
                        (dr * dr + dg * dg + db * db) / 3.0) / 255f;
                    distance[index + x] = value;
                    backgroundLike[index + x] = value <= seedThreshold;
                }
            }

            // 只从与画面边缘连通的背景色区域开始泛洪。
            // 底边中间区域通常是肩膀/衣服，不作为种子，避免同色白衬衫被删掉。
            bool[] isBackground = new bool[count];
            var queue = new Queue<int>();
            void Seed(int x, int y)
            {
                int index = y * width + x;
                if (!isBackground[index] && backgroundLike[index])
                {
                    isBackground[index] = true;
                    queue.Enqueue(index);
                }
            }

            for (int x = 0; x < width; x++)
            {
                Seed(x, 0);
                if (x < (int)(width * 0.12) || x >= (int)(width * 0.88))
                {
                    Seed(x, height - 1);
                }
            }
            for (int y = 0; y < height; y++)
            {
                Seed(0, y);
                Seed(width - 1, y);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width;
                int y = index / width;
                if (x > 0)
                {
                    Seed(x - 1, y);
                }
                if (x + 1 < width)
                {
                    Seed(x + 1, y);
                }
                if (y > 0)
                {
                    Seed(x, y - 1);
                }
                if (y + 1 < height)
                {
                    Seed(x, y + 1);
                }
            }

            int backgroundCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (isBackground[i])
                {
                    backgroundCount++;
                }
            }
            if (backgroundCount < count / 200)
            {
                // 背景没有与边缘连通（主体几乎占满画面或背景被完全裁掉），退化为颜色判定。
                for (int i = 0; i < count; i++)
                {
                    if (backgroundLike[i])
                    {
                        isBackground[i] = true;
                        backgroundCount++;
                    }
                }
            }
            if (backgroundCount == 0)
            {
                return (Bitmap)source.Clone();
            }

            // 到背景区域的距离（像素），用于区分“背景边缘带”和内部前景。
            short[] distanceToBackground = new short[count];
            Array.Fill(distanceToBackground, (short)-1);
            var distanceQueue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (isBackground[i])
                {
                    distanceToBackground[i] = 0;
                    distanceQueue.Enqueue(i);
                }
            }
            while (distanceQueue.Count > 0)
            {
                int index = distanceQueue.Dequeue();
                int next = distanceToBackground[index] + 1;
                int x = index % width;
                int y = index / width;
                if (x > 0)
                {
                    TrySetDistance(index - 1, next);
                }
                if (x + 1 < width)
                {
                    TrySetDistance(index + 1, next);
                }
                if (y > 0)
                {
                    TrySetDistance(index - width, next);
                }
                if (y + 1 < height)
                {
                    TrySetDistance(index + width, next);
                }
            }

            void TrySetDistance(int index, int value)
            {
                if (distanceToBackground[index] < 0)
                {
                    distanceToBackground[index] = (short)Math.Min(value, short.MaxValue);
                    distanceQueue.Enqueue(index);
                }
            }

            int edgeRadius = Math.Clamp(Math.Min(width, height) / 60, 3, 12);
            byte[] gray = new byte[count];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int offset = row + x * 4;
                    gray[y * width + x] = (byte)(
                        pixels[offset + 2] * 0.299
                        + pixels[offset + 1] * 0.587
                        + pixels[offset] * 0.114);
                }
            }

            // 局部背景色：在未知带附近取邻域背景像素的均值，
            // 这样渐变、暗角或轻微不均匀的纯色背景也能贴近真实边缘。
            float[] localBackgroundR = new float[count];
            float[] localBackgroundG = new float[count];
            float[] localBackgroundB = new float[count];
            int sampleRadius = Math.Max(8, Math.Min(width, height) / 16);
            int sampleStep = Math.Max(2, sampleRadius / 6);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    localBackgroundR[index] = background.R;
                    localBackgroundG[index] = background.G;
                    localBackgroundB[index] = background.B;
                    if (isBackground[index]
                        || distanceToBackground[index] < 0
                        || distanceToBackground[index] > edgeRadius + 2)
                    {
                        continue;
                    }
                    int sumR = 0;
                    int sumG = 0;
                    int sumB = 0;
                    int samples = 0;
                    int minX = Math.Max(0, x - sampleRadius);
                    int maxX = Math.Min(width - 1, x + sampleRadius);
                    int minY = Math.Max(0, y - sampleRadius);
                    int maxY = Math.Min(height - 1, y + sampleRadius);
                    for (int sy = minY; sy <= maxY; sy += sampleStep)
                    {
                        int row = sy * stride;
                        for (int sx = minX; sx <= maxX; sx += sampleStep)
                        {
                            int sampleIndex = sy * width + sx;
                            if (!isBackground[sampleIndex])
                            {
                                continue;
                            }
                            int offset = row + sx * 4;
                            sumB += pixels[offset];
                            sumG += pixels[offset + 1];
                            sumR += pixels[offset + 2];
                            samples++;
                        }
                    }
                    if (samples > 0)
                    {
                        localBackgroundR[index] = sumR / (float)samples;
                        localBackgroundG[index] = sumG / (float)samples;
                        localBackgroundB[index] = sumB / (float)samples;
                    }
                }
            }

            // 为未知带中的每个像素寻找最近的前景色，用颜色投影估算 alpha。
            short[] nearestForegroundDistance = new short[count];
            Array.Fill(nearestForegroundDistance, (short)-1);
            byte[] foregroundR = new byte[count];
            byte[] foregroundG = new byte[count];
            byte[] foregroundB = new byte[count];
            var foregroundQueue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (!isBackground[i]
                    && distanceToBackground[i] > edgeRadius
                    && distanceToBackground[i] >= 0)
                {
                    nearestForegroundDistance[i] = 0;
                    int x = i % width;
                    int y = i / width;
                    int offset = y * stride + x * 4;
                    foregroundB[i] = pixels[offset];
                    foregroundG[i] = pixels[offset + 1];
                    foregroundR[i] = pixels[offset + 2];
                    foregroundQueue.Enqueue(i);
                }
            }
            int maxForegroundDistance = edgeRadius * 4 + 4;
            while (foregroundQueue.Count > 0)
            {
                int index = foregroundQueue.Dequeue();
                int next = nearestForegroundDistance[index] + 1;
                if (next > maxForegroundDistance)
                {
                    continue;
                }
                int x = index % width;
                int y = index / width;
                TryPropagateForeground(index - 1, x > 0, next, index);
                TryPropagateForeground(index + 1, x + 1 < width, next, index);
                TryPropagateForeground(index - width, y > 0, next, index);
                TryPropagateForeground(index + width, y + 1 < height, next, index);
            }

            void TryPropagateForeground(int neighbour, bool valid, int value, int source)
            {
                if (!valid || isBackground[neighbour]
                    || nearestForegroundDistance[neighbour] >= 0
                    || distanceToBackground[neighbour] < 0
                    || distanceToBackground[neighbour] > edgeRadius)
                {
                    return;
                }
                nearestForegroundDistance[neighbour] = (short)value;
                foregroundR[neighbour] = foregroundR[source];
                foregroundG[neighbour] = foregroundG[source];
                foregroundB[neighbour] = foregroundB[source];
                foregroundQueue.Enqueue(neighbour);
            }

            float[] alpha = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (isBackground[i])
                {
                    alpha[i] = 0f;
                    continue;
                }
                if (distanceToBackground[i] > edgeRadius
                    || distanceToBackground[i] < 0)
                {
                    alpha[i] = 1f;
                    continue;
                }

                float cDistance = distance[i];
                float result;
                if (nearestForegroundDistance[i] >= 0)
                {
                    float cr = pixels[(i / width) * stride + (i % width) * 4 + 2] / 255f;
                    float cg = pixels[(i / width) * stride + (i % width) * 4 + 1] / 255f;
                    float cb = pixels[(i / width) * stride + (i % width) * 4] / 255f;
                    float br = localBackgroundR[i] / 255f;
                    float bgc = localBackgroundG[i] / 255f;
                    float bb = localBackgroundB[i] / 255f;
                    float fr = foregroundR[i] / 255f;
                    float fg = foregroundG[i] / 255f;
                    float fb = foregroundB[i] / 255f;

                    float fbr = fr - br;
                    float fbg = fg - bgc;
                    float fbb = fb - bb;
                    float fLength = fbr * fbr + fbg * fbg + fbb * fbb;
                    if (fLength > 0.004f)
                    {
                        float cbr = cr - br;
                        float cbg = cg - bgc;
                        float cbb = cb - bb;
                        result = (cbr * fbr + cbg * fbg + cbb * fbb) / fLength;
                        float fDistance = (float)Math.Sqrt(fLength);
                        if (fDistance > 0.001f)
                        {
                            result = Math.Min(result, cDistance / fDistance + 0.06f);
                        }
                    }
                    else
                    {
                        result = SmoothStep(threshold, foregroundCut, cDistance);
                    }
                }
                else
                {
                    result = SmoothStep(threshold, foregroundCut, cDistance);
                }
                alpha[i] = Math.Clamp(result, 0f, 1f);
            }

            RefineAlpha(alpha, gray, isBackground, distanceToBackground,
                width, height, edgeRadius);

            var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            CopyResolution(source, dest);
            var output = dest.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int outStride = Math.Abs(output.Stride);
                var outBytes = new byte[outStride * height];
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    int outRow = y * outStride;
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        int offset = row + x * 4;
                        int outOffset = outRow + x * 4;
                        float a = alpha[index];
                        if (a <= 0.002f)
                        {
                            outBytes[outOffset] = (byte)replacement.B;
                            outBytes[outOffset + 1] = (byte)replacement.G;
                            outBytes[outOffset + 2] = (byte)replacement.R;
                        }
                        else if (a >= 0.998f)
                        {
                            outBytes[outOffset] = pixels[offset];
                            outBytes[outOffset + 1] = pixels[offset + 1];
                            outBytes[outOffset + 2] = pixels[offset + 2];
                        }
                        else
                        {
                            // 用 (C - (1-alpha)B) / alpha 去掉背景色污染，减少发丝白边。
                            float br = localBackgroundR[index];
                            float bgc = localBackgroundG[index];
                            float bb = localBackgroundB[index];
                            float fr = (pixels[offset + 2] - (1f - a) * br) / a;
                            float fg = (pixels[offset + 1] - (1f - a) * bgc) / a;
                            float fb = (pixels[offset] - (1f - a) * bb) / a;
                            float rr = Math.Clamp(fr, 0f, 255f) * a + replacement.R * (1f - a);
                            float gg = Math.Clamp(fg, 0f, 255f) * a + replacement.G * (1f - a);
                            float outB = Math.Clamp(fb, 0f, 255f) * a + replacement.B * (1f - a);
                            outBytes[outOffset] = (byte)Math.Clamp(outB, 0f, 255f);
                            outBytes[outOffset + 1] = (byte)Math.Clamp(gg, 0f, 255f);
                            outBytes[outOffset + 2] = (byte)Math.Clamp(rr, 0f, 255f);
                        }
                        outBytes[outOffset + 3] = 255;
                    }
                }
                Marshal.Copy(outBytes, 0, output.Scan0, outBytes.Length);
            }
            finally
            {
                dest.UnlockBits(output);
            }
            return dest;
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// 复杂背景抠图（手动开启）：适合背景有明显纹理、杂物、光照不均或多种颜色的照片。
    /// 1) 从画面上方、左右边缘与底部两角采样，建立多聚类背景颜色模型；
    /// 2) 从画面中部且明显不同于背景的像素建立人物颜色模型（头发、皮肤、衣物）；
    /// 3) 用马尔可夫随机场（ICM 迭代）结合颜色代价与邻域平滑标记前景 / 背景；
    /// 4) 按连通域只保留人物主体，清除背景杂物与孤立噪点，并纠正被误判的封闭衣物区域；
    /// 5) 将结果放大到原尺寸后做发丝级 alpha 合成，得到干净的替换背景。
    /// </summary>
    public static Bitmap ReplaceBackgroundComplex(Bitmap source, Color replacement, int tolerance)
    {
        if (source.Width < 8 || source.Height < 8)
        {
            return (Bitmap)source.Clone();
        }
        var bitmap = ToArgb32(source);
        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int stride;
            byte[] pixels;
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = Math.Abs(data.Stride);
                pixels = new byte[stride * height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            float toleranceValue = Math.Clamp(tolerance, 0, 100);
            float threshold = 0.030f + toleranceValue / 100f * 0.30f;

            // 分析分辨率：超大图先做盒式降采样，保证迭代次数与像素规模可控。
            int scale = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / 720.0));
            int sw = Math.Max(8, (width + scale - 1) / scale);
            int sh = Math.Max(8, (height + scale - 1) / scale);
            int smallCount = sw * sh;
            var smallR = new float[smallCount];
            var smallG = new float[smallCount];
            var smallB = new float[smallCount];
            var smallCounts = new int[smallCount];
            for (int y = 0; y < height; y++)
            {
                int sy = (int)((long)y * sh / height);
                int row = y * stride;
                int rowBase = sy * sw;
                for (int x = 0; x < width; x++)
                {
                    int sx = x * sw / width;
                    int index = rowBase + sx;
                    int offset = row + x * 4;
                    smallR[index] += pixels[offset + 2];
                    smallG[index] += pixels[offset + 1];
                    smallB[index] += pixels[offset];
                    smallCounts[index]++;
                }
            }
            for (int i = 0; i < smallCount; i++)
            {
                int c = Math.Max(1, smallCounts[i]);
                smallR[i] /= c * 255f;
                smallG[i] /= c * 255f;
                smallB[i] /= c * 255f;
            }

            // ---------- 背景颜色模型 ----------
            int band = Math.Max(2, Math.Min(sw, sh) / 14);
            var bgSamples = new List<(float R, float G, float B)>();
            void AddBgSample(int x, int y)
            {
                int index = y * sw + x;
                if (smallCounts[index] == 0)
                {
                    return;
                }
                bgSamples.Add((smallR[index], smallG[index], smallB[index]));
            }
            for (int y = 0; y < band; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    AddBgSample(x, y);
                }
            }
            int sideBottom = Math.Max(band + 1, (int)(sh * 0.72));
            for (int x = 0; x < band; x++)
            {
                for (int y = 0; y < sideBottom; y++)
                {
                    AddBgSample(x, y);
                    AddBgSample(sw - 1 - x, y);
                }
            }
            for (int y = (int)(sh * 0.9); y < sh; y++)
            {
                for (int x = 0; x < band; x++)
                {
                    AddBgSample(x, y);
                    AddBgSample(sw - 1 - x, y);
                }
            }
            if (bgSamples.Count > 8000)
            {
                int step = (bgSamples.Count + 7999) / 8000;
                var reduced = new List<(float R, float G, float B)>(
                    (bgSamples.Count + step - 1) / step);
                for (int i = 0; i < bgSamples.Count; i += step)
                {
                    reduced.Add(bgSamples[i]);
                }
                bgSamples = reduced;
            }
            var bgCenters = ClusterColors(bgSamples, 8, 8);
            if (bgCenters.Length == 0)
            {
                return (Bitmap)source.Clone();
            }

            // ---------- 人物颜色模型 ----------
            float fgFilter = Math.Max(0.08f, threshold * 0.75f);
            var fgSamples = new List<(float R, float G, float B)>();
            int fx0 = (int)(sw * 0.18);
            int fx1 = Math.Max(fx0 + 1, (int)(sw * 0.82));
            int fy0 = (int)(sh * 0.10);
            int fy1 = Math.Max(fy0 + 1, (int)(sh * 0.96));
            int fgStep = Math.Max(1, (int)Math.Sqrt(
                Math.Max(1, (fx1 - fx0) * (fy1 - fy0) / 6000.0)));
            for (int y = fy0; y < fy1; y += fgStep)
            {
                for (int x = fx0; x < fx1; x += fgStep)
                {
                    int index = y * sw + x;
                    if (smallCounts[index] == 0)
                    {
                        continue;
                    }
                    if (NearestColorDistance(smallR[index], smallG[index],
                        smallB[index], bgCenters) >= fgFilter)
                    {
                        fgSamples.Add((smallR[index], smallG[index], smallB[index]));
                    }
                }
            }
            var fgCenters = fgSamples.Count >= 60
                ? ClusterColors(fgSamples, 5, 6)
                : Array.Empty<float[]>();

            // ---------- 颜色代价 ----------
            float bgScale = Math.Max(0.035f, threshold);
            const float fgScale = 0.16f;
            var dataBg = new float[smallCount];
            var dataFg = new float[smallCount];
            int border = Math.Max(1, Math.Min(sw, sh) / 120);
            bool hasFg = fgCenters.Length > 0;
            for (int y = 0; y < sh; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    int index = y * sw + x;
                    float db = NearestColorDistance(smallR[index], smallG[index],
                        smallB[index], bgCenters) / bgScale;
                    float df = hasFg
                        ? NearestColorDistance(smallR[index], smallG[index],
                            smallB[index], fgCenters) / fgScale
                        : 0.75f;
                    bool onBorder = x < border || x >= sw - border
                        || y < border || y >= sh - border;
                    if (onBorder)
                    {
                        // 底边中间通常是肩膀 / 衣服，不强制判为背景；其余边缘优先背景。
                        bool bottomCenter = y >= sh - border && x >= band && x < sw - band;
                        if (bottomCenter)
                        {
                            df -= 0.18f;
                        }
                        else
                        {
                            db -= 0.30f;
                        }
                    }
                    // 构图先验：画面下方中央更可能是衣服。仅在颜色与背景接近时起作用，
                    // 可避免白衬衫配白背景被整体判成背景。
                    if (hasFg && y > sh * 0.6f)
                    {
                        float weight = (y / (float)sh - 0.6f) / 0.4f;
                        if (x > sw * 0.12f && x < sw * 0.88f)
                        {
                            df -= 0.30f * weight;
                        }
                    }
                    dataBg[index] = Math.Min(db, 2.5f);
                    dataFg[index] = Math.Min(df, 2.5f);
                }
            }

            // ---------- MRF / ICM 标记优化 ----------
            var label = new byte[smallCount];
            var next = new byte[smallCount];
            for (int i = 0; i < smallCount; i++)
            {
                label[i] = dataBg[i] <= dataFg[i] ? (byte)1 : (byte)0;
            }
            const float lambda = 0.62f;
            float EdgePenalty(int a, int b)
            {
                float dr = smallR[a] - smallR[b];
                float dg = smallG[a] - smallG[b];
                float db = smallB[a] - smallB[b];
                float diff2 = (dr * dr + dg * dg + db * db) / 3f;
                return lambda / (1f + diff2 * 220f);
            }
            for (int iteration = 0; iteration < 5; iteration++)
            {
                Array.Copy(label, next, smallCount);
                for (int y = 1; y < sh - 1; y++)
                {
                    int row = y * sw;
                    for (int x = 1; x < sw - 1; x++)
                    {
                        int index = row + x;
                        float energyBg = dataBg[index];
                        float energyFg = dataFg[index];
                        int neighbour = index - 1;
                        float weight = EdgePenalty(index, neighbour);
                        if (label[neighbour] == 0) energyBg += weight; else energyFg += weight;
                        neighbour = index + 1;
                        weight = EdgePenalty(index, neighbour);
                        if (label[neighbour] == 0) energyBg += weight; else energyFg += weight;
                        neighbour = index - sw;
                        weight = EdgePenalty(index, neighbour);
                        if (label[neighbour] == 0) energyBg += weight; else energyFg += weight;
                        neighbour = index + sw;
                        weight = EdgePenalty(index, neighbour);
                        if (label[neighbour] == 0) energyBg += weight; else energyFg += weight;
                        next[index] = energyBg <= energyFg ? (byte)1 : (byte)0;
                    }
                }
                var swap = label;
                label = next;
                next = swap;
            }

            // ---------- 只保留人物主体 ----------
            var component = new int[smallCount];
            Array.Fill(component, -1);
            var componentSizes = new List<int>();
            var queue = new Queue<int>();
            for (int start = 0; start < smallCount; start++)
            {
                if (label[start] != 0 || component[start] >= 0)
                {
                    continue;
                }
                int id = componentSizes.Count;
                int size = 0;
                component[start] = id;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    size++;
                    int cx = current % sw;
                    int cy = current / sw;
                    if (cx > 0) VisitForeground(current - 1, id);
                    if (cx + 1 < sw) VisitForeground(current + 1, id);
                    if (cy > 0) VisitForeground(current - sw, id);
                    if (cy + 1 < sh) VisitForeground(current + sw, id);
                }
                componentSizes.Add(size);
            }
            void VisitForeground(int index, int id)
            {
                if (label[index] == 0 && component[index] < 0)
                {
                    component[index] = id;
                    queue.Enqueue(index);
                }
            }
            int largest = componentSizes.Count == 0 ? 0 : componentSizes.Max();
            int keepThreshold = Math.Max(24, (int)(largest * 0.03));
            for (int i = 0; i < smallCount; i++)
            {
                int id = component[i];
                if (id >= 0 && componentSizes[id] < keepThreshold)
                {
                    label[i] = 1;
                }
            }

            // 封闭在人物内部、明显不同于背景色的小块（如深色衣服）收回为前景，避免被挖洞。
            var visited = new bool[smallCount];
            for (int start = 0; start < smallCount; start++)
            {
                if (label[start] != 1 || visited[start])
                {
                    continue;
                }
                bool touchesBorder = false;
                int size = 0;
                double sumR = 0, sumG = 0, sumB = 0;
                var region = new List<int>();
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    region.Add(current);
                    size++;
                    int cx = current % sw;
                    int cy = current / sw;
                    if (cx == 0 || cy == 0 || cx == sw - 1 || cy == sh - 1)
                    {
                        touchesBorder = true;
                    }
                    sumR += smallR[current];
                    sumG += smallG[current];
                    sumB += smallB[current];
                    if (cx > 0) VisitBackground(current - 1);
                    if (cx + 1 < sw) VisitBackground(current + 1);
                    if (cy > 0) VisitBackground(current - sw);
                    if (cy + 1 < sh) VisitBackground(current + sw);
                }
                if (!touchesBorder && size < smallCount * 0.02)
                {
                    float meanR = (float)(sumR / size);
                    float meanG = (float)(sumG / size);
                    float meanB = (float)(sumB / size);
                    if (NearestColorDistance(meanR, meanG, meanB, bgCenters) > threshold * 1.4f)
                    {
                        foreach (int index in region)
                        {
                            label[index] = 0;
                        }
                    }
                }
                region.Clear();
            }
            void VisitBackground(int index)
            {
                if (label[index] == 1 && !visited[index])
                {
                    visited[index] = true;
                    queue.Enqueue(index);
                }
            }

            // ---------- 还原到原尺寸 ----------
            var fullMask = new bool[width * height];
            int backgroundPixels = 0;
            int foregroundPixels = 0;
            for (int y = 0; y < height; y++)
            {
                int sy = (int)((long)y * sh / height);
                int sourceRow = sy * sw;
                int destRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    int sx = x * sw / width;
                    bool isBg = label[sourceRow + sx] == 1;
                    fullMask[destRow + x] = isBg;
                    if (isBg)
                    {
                        backgroundPixels++;
                    }
                    else
                    {
                        foregroundPixels++;
                    }
                }
            }
            if (backgroundPixels < width * height / 200
                || foregroundPixels < width * height / 100)
            {
                // 背景或人物几乎没识别出来（主体占满画面、背景与主体同色），保持原图。
                return (Bitmap)source.Clone();
            }

            float avgR = 0, avgG = 0, avgB = 0;
            foreach (var center in bgCenters)
            {
                avgR += center[0];
                avgG += center[1];
                avgB += center[2];
            }
            var fallback = Color.FromArgb(
                (int)Math.Clamp(avgR / bgCenters.Length * 255f, 0f, 255f),
                (int)Math.Clamp(avgG / bgCenters.Length * 255f, 0f, 255f),
                (int)Math.Clamp(avgB / bgCenters.Length * 255f, 0f, 255f));
            return ComposeBackgroundMask(source, pixels, stride, fullMask,
                fallback, replacement, tolerance);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static float[][] ClusterColors(
        IReadOnlyList<(float R, float G, float B)> samples, int k, int iterations)
    {
        int n = samples.Count;
        if (n == 0)
        {
            return Array.Empty<float[]>();
        }
        int clusters = Math.Min(k, n);
        var centers = new float[clusters][];
        centers[0] = new[] { samples[0].R, samples[0].G, samples[0].B };
        var minDistances = new float[n];
        for (int i = 0; i < n; i++)
        {
            minDistances[i] = float.MaxValue;
        }
        for (int c = 1; c < clusters; c++)
        {
            float best = -1f;
            int bestIndex = 0;
            for (int i = 0; i < n; i++)
            {
                float distance = ColorDistanceSquared(samples[i].R, samples[i].G,
                    samples[i].B, centers[c - 1]);
                if (distance < minDistances[i])
                {
                    minDistances[i] = distance;
                }
                if (minDistances[i] > best)
                {
                    best = minDistances[i];
                    bestIndex = i;
                }
            }
            centers[c] = new[] { samples[bestIndex].R, samples[bestIndex].G, samples[bestIndex].B };
        }

        var sums = new double[clusters][];
        var counts = new int[clusters];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int c = 0; c < clusters; c++)
            {
                sums[c] = new double[3];
                counts[c] = 0;
            }
            for (int i = 0; i < n; i++)
            {
                int nearest = 0;
                float best = float.MaxValue;
                for (int c = 0; c < clusters; c++)
                {
                    float distance = ColorDistanceSquared(samples[i].R, samples[i].G,
                        samples[i].B, centers[c]);
                    if (distance < best)
                    {
                        best = distance;
                        nearest = c;
                    }
                }
                sums[nearest][0] += samples[i].R;
                sums[nearest][1] += samples[i].G;
                sums[nearest][2] += samples[i].B;
                counts[nearest]++;
            }
            for (int c = 0; c < clusters; c++)
            {
                if (counts[c] == 0)
                {
                    continue;
                }
                centers[c][0] = (float)(sums[c][0] / counts[c]);
                centers[c][1] = (float)(sums[c][1] / counts[c]);
                centers[c][2] = (float)(sums[c][2] / counts[c]);
            }
        }
        return centers;
    }

    private static float ColorDistanceSquared(float r, float g, float b, float[] center)
    {
        float dr = r - center[0];
        float dg = g - center[1];
        float db = b - center[2];
        return (dr * dr + dg * dg + db * db) / 3f;
    }

    private static float NearestColorDistance(float r, float g, float b, float[][] centers)
    {
        float best = float.MaxValue;
        for (int i = 0; i < centers.Length; i++)
        {
            float distance = ColorDistanceSquared(r, g, b, centers[i]);
            if (distance < best)
            {
                best = distance;
            }
        }
        return centers.Length == 0 ? 1f : (float)Math.Sqrt(Math.Max(0f, best));
    }

    /// <summary>
    /// 已知背景掩膜时共用的合成阶段：距离变换、局部背景色采样、
    /// 最近前景色传播、颜色投影 alpha、边缘保持滤波与去背景色污染。
    /// </summary>
    private static Bitmap ComposeBackgroundMask(Bitmap source, byte[] pixels, int stride,
        bool[] isBackground, Color background, Color replacement, int tolerance)
    {
        int width = source.Width;
        int height = source.Height;
        int count = width * height;
        float toleranceValue = Math.Clamp(tolerance, 0, 100);
        float threshold = 0.018f + toleranceValue / 100f * 0.30f;
        float foregroundCut = Math.Min(0.78f, threshold + 0.23f);

        var distanceToBackground = new short[count];
        Array.Fill(distanceToBackground, (short)-1);
        var queue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if (isBackground[i])
            {
                distanceToBackground[i] = 0;
                queue.Enqueue(i);
            }
        }
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            short next = (short)Math.Min(distanceToBackground[index] + 1, short.MaxValue);
            int x = index % width;
            int y = index / width;
            if (x > 0)
            {
                TrySetDistance(index - 1);
            }
            if (x + 1 < width)
            {
                TrySetDistance(index + 1);
            }
            if (y > 0)
            {
                TrySetDistance(index - width);
            }
            if (y + 1 < height)
            {
                TrySetDistance(index + width);
            }

            void TrySetDistance(int neighbour)
            {
                if (distanceToBackground[neighbour] < 0)
                {
                    distanceToBackground[neighbour] = next;
                    queue.Enqueue(neighbour);
                }
            }
        }

        int edgeRadius = Math.Clamp(Math.Min(width, height) / 60, 3, 12);
        var gray = new byte[count];
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            int index = y * width;
            for (int x = 0; x < width; x++)
            {
                int offset = row + x * 4;
                gray[index + x] = (byte)(
                    pixels[offset + 2] * 0.299
                    + pixels[offset + 1] * 0.587
                    + pixels[offset] * 0.114);
            }
        }

        // 局部背景色：只对靠近背景边界的像素采样，贴合渐变和光照不均的真实背景。
        var localR = new float[count];
        var localG = new float[count];
        var localB = new float[count];
        int sampleRadius = Math.Max(8, Math.Min(width, height) / 16);
        int sampleStep = Math.Max(2, sampleRadius / 6);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                localR[index] = background.R;
                localG[index] = background.G;
                localB[index] = background.B;
                int distance = distanceToBackground[index];
                if (isBackground[index] || distance < 1 || distance > edgeRadius + 2)
                {
                    continue;
                }
                int sumR = 0, sumG = 0, sumB = 0, samples = 0;
                int minX = Math.Max(0, x - sampleRadius);
                int maxX = Math.Min(width - 1, x + sampleRadius);
                int minY = Math.Max(0, y - sampleRadius);
                int maxY = Math.Min(height - 1, y + sampleRadius);
                for (int sy = minY; sy <= maxY; sy += sampleStep)
                {
                    int row = sy * stride;
                    for (int sx = minX; sx <= maxX; sx += sampleStep)
                    {
                        int sampleIndex = sy * width + sx;
                        if (!isBackground[sampleIndex])
                        {
                            continue;
                        }
                        int offset = row + sx * 4;
                        sumB += pixels[offset];
                        sumG += pixels[offset + 1];
                        sumR += pixels[offset + 2];
                        samples++;
                    }
                }
                if (samples > 0)
                {
                    localR[index] = sumR / (float)samples;
                    localG[index] = sumG / (float)samples;
                    localB[index] = sumB / (float)samples;
                }
            }
        }

        // 为边界带中的像素寻找最近的前景色，用颜色投影估算半透明 alpha。
        var nearestForeground = new short[count];
        Array.Fill(nearestForeground, (short)-1);
        var foregroundR = new byte[count];
        var foregroundG = new byte[count];
        var foregroundB = new byte[count];
        var foregroundQueue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if (!isBackground[i]
                && (distanceToBackground[i] < 0 || distanceToBackground[i] > edgeRadius))
            {
                nearestForeground[i] = 0;
                int x = i % width;
                int y = i / width;
                int offset = y * stride + x * 4;
                foregroundB[i] = pixels[offset];
                foregroundG[i] = pixels[offset + 1];
                foregroundR[i] = pixels[offset + 2];
                foregroundQueue.Enqueue(i);
            }
        }
        int maxForegroundDistance = edgeRadius * 4 + 4;
        while (foregroundQueue.Count > 0)
        {
            int index = foregroundQueue.Dequeue();
            short next = (short)(nearestForeground[index] + 1);
            if (next > maxForegroundDistance)
            {
                continue;
            }
            int x = index % width;
            int y = index / width;
            if (x > 0) TryPropagateForeground(index - 1, index);
            if (x + 1 < width) TryPropagateForeground(index + 1, index);
            if (y > 0) TryPropagateForeground(index - width, index);
            if (y + 1 < height) TryPropagateForeground(index + width, index);

            void TryPropagateForeground(int neighbour, int sourceIndex)
            {
                if (isBackground[neighbour]
                    || nearestForeground[neighbour] >= 0
                    || distanceToBackground[neighbour] < 0
                    || distanceToBackground[neighbour] > edgeRadius)
                {
                    return;
                }
                nearestForeground[neighbour] = next;
                foregroundR[neighbour] = foregroundR[sourceIndex];
                foregroundG[neighbour] = foregroundG[sourceIndex];
                foregroundB[neighbour] = foregroundB[sourceIndex];
                foregroundQueue.Enqueue(neighbour);
            }
        }

        var alpha = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (isBackground[i])
            {
                alpha[i] = 0f;
                continue;
            }
            if (distanceToBackground[i] > edgeRadius || distanceToBackground[i] < 0)
            {
                alpha[i] = 1f;
                continue;
            }
            float colorDistance = 0f;
            int offset = (i / width) * stride + (i % width) * 4;
            int dr = pixels[offset + 2] - background.R;
            int dg = pixels[offset + 1] - background.G;
            int db = pixels[offset] - background.B;
            colorDistance = (float)Math.Sqrt((dr * dr + dg * dg + db * db) / 3.0) / 255f;

            float result;
            if (nearestForeground[i] >= 0)
            {
                float cr = pixels[offset + 2] / 255f;
                float cg = pixels[offset + 1] / 255f;
                float cb = pixels[offset] / 255f;
                float br = localR[i] / 255f;
                float bgc = localG[i] / 255f;
                float bb = localB[i] / 255f;
                float fr = foregroundR[i] / 255f;
                float fgd = foregroundG[i] / 255f;
                float fb = foregroundB[i] / 255f;
                float fbr = fr - br;
                float fbg = fgd - bgc;
                float fbb = fb - bb;
                float fLength = fbr * fbr + fbg * fbg + fbb * fbb;
                if (fLength > 0.004f)
                {
                    float cbr = cr - br;
                    float cbg = cg - bgc;
                    float cbb = cb - bb;
                    result = (cbr * fbr + cbg * fbg + cbb * fbb) / fLength;
                    float fDistance = (float)Math.Sqrt(fLength);
                    if (fDistance > 0.001f)
                    {
                        result = Math.Min(result, colorDistance / fDistance + 0.06f);
                    }
                }
                else
                {
                    result = SmoothStep(threshold, foregroundCut, colorDistance);
                }
            }
            else
            {
                result = SmoothStep(threshold, foregroundCut, colorDistance);
            }
            alpha[i] = Math.Clamp(result, 0f, 1f);
        }

        RefineAlpha(alpha, gray, isBackground, distanceToBackground,
            width, height, edgeRadius);

        var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        CopyResolution(source, dest);
        var output = dest.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int outStride = Math.Abs(output.Stride);
            var outBytes = new byte[outStride * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                int outRow = y * outStride;
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    int offset = row + x * 4;
                    int outOffset = outRow + x * 4;
                    float a = alpha[index];
                    if (a <= 0.002f)
                    {
                        outBytes[outOffset] = (byte)replacement.B;
                        outBytes[outOffset + 1] = (byte)replacement.G;
                        outBytes[outOffset + 2] = (byte)replacement.R;
                    }
                    else if (a >= 0.998f)
                    {
                        outBytes[outOffset] = pixels[offset];
                        outBytes[outOffset + 1] = pixels[offset + 1];
                        outBytes[outOffset + 2] = pixels[offset + 2];
                    }
                    else
                    {
                        float br = localR[index];
                        float bgc = localG[index];
                        float bb = localB[index];
                        float fr = (pixels[offset + 2] - (1f - a) * br) / a;
                        float fg = (pixels[offset + 1] - (1f - a) * bgc) / a;
                        float fb = (pixels[offset] - (1f - a) * bb) / a;
                        float rr = Math.Clamp(fr, 0f, 255f) * a + replacement.R * (1f - a);
                        float gg = Math.Clamp(fg, 0f, 255f) * a + replacement.G * (1f - a);
                        float outB = Math.Clamp(fb, 0f, 255f) * a + replacement.B * (1f - a);
                        outBytes[outOffset] = (byte)Math.Clamp(outB, 0f, 255f);
                        outBytes[outOffset + 1] = (byte)Math.Clamp(gg, 0f, 255f);
                        outBytes[outOffset + 2] = (byte)Math.Clamp(rr, 0f, 255f);
                    }
                    outBytes[outOffset + 3] = 255;
                }
            }
            Marshal.Copy(outBytes, 0, output.Scan0, outBytes.Length);
        }
        finally
        {
            dest.UnlockBits(output);
        }
        return dest;
    }

    private static void RefineAlpha(float[] alpha, byte[] gray, bool[] isBackground,
        short[] distanceToBackground, int width, int height, int edgeRadius)
    {
        var input = (float[])alpha.Clone();
        const int radius = 2;
        const float spatialSigma = 1.7f;
        const float rangeSigma = 18f;
        float spatialDenominator = 2f * spatialSigma * spatialSigma;
        float rangeDenominator = 2f * rangeSigma * rangeSigma;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (isBackground[index])
                {
                    alpha[index] = 0f;
                    continue;
                }
                if (distanceToBackground[index] > edgeRadius + 1
                    || distanceToBackground[index] < 0)
                {
                    alpha[index] = 1f;
                    continue;
                }
                int centerGray = gray[index];
                float weighted = 0f;
                float weightSum = 0f;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= width)
                        {
                            continue;
                        }
                        int nIndex = ny * width + nx;
                        float spatial = (float)Math.Exp(
                            -(dx * dx + dy * dy) / spatialDenominator);
                        int diff = gray[nIndex] - centerGray;
                        float range = (float)Math.Exp(-(diff * diff) / rangeDenominator);
                        float weight = spatial * range;
                        float sample = isBackground[nIndex] ? 0f : input[nIndex];
                        weighted += sample * weight;
                        weightSum += weight;
                    }
                }
                if (weightSum > 0f)
                {
                    alpha[index] = weighted / weightSum;
                }
            }
        }
        for (int i = 0; i < alpha.Length; i++)
        {
            if (isBackground[i])
            {
                alpha[i] = 0f;
            }
            else if (distanceToBackground[i] > edgeRadius
                || distanceToBackground[i] < 0)
            {
                alpha[i] = 1f;
                continue;
            }
            alpha[i] = Math.Clamp(alpha[i], 0f, 1f);
        }
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if (edge1 <= edge0)
        {
            return value >= edge1 ? 1f : 0f;
        }
        float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Bitmap ToArgb32(Bitmap source)
    {
        var dest = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        CopyResolution(source, dest);
        using var g = Graphics.FromImage(dest);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        return dest;
    }

    private sealed class ColorBucket
    {
        private long _r;
        private long _g;
        private long _b;
        public int Count { get; private set; }

        public void Add(byte r, byte g, byte b)
        {
            _r += r;
            _g += g;
            _b += b;
            Count++;
        }

        public byte R => (byte)(_r / Math.Max(1, Count));
        public byte G => (byte)(_g / Math.Max(1, Count));
        public byte B => (byte)(_b / Math.Max(1, Count));
    }

    /// <summary>把滤镜与亮度/对比度/饱和度合成一次绘制，返回新图。</summary>
    public static Bitmap ApplyAdjustments(Bitmap source, PhotoAdjustment adjustment)
    {
        if (adjustment.IsNeutral)
        {
            return (Bitmap)source.Clone();
        }
        var dest = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        CopyResolution(source, dest);
        using var g = Graphics.FromImage(dest);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(BuildColorMatrix(adjustment));
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return dest;
    }

    private static ColorMatrix BuildColorMatrix(PhotoAdjustment adjustment)
    {
        return Multiply(FilterMatrix(adjustment.Filter), AdjustmentMatrix(adjustment));
    }

    private static ColorMatrix FilterMatrix(PhotoFilter filter)
    {
        float[,] m = Identity();
        switch (filter)
        {
            case PhotoFilter.Grayscale:
                m = SaturationMatrix(0);
                break;
            case PhotoFilter.Sepia:
                m = new[,]
                {
                    { 0.393f, 0.769f, 0.189f, 0f, 0f },
                    { 0.349f, 0.686f, 0.168f, 0f, 0f },
                    { 0.272f, 0.534f, 0.131f, 0f, 0f },
                    { 0f, 0f, 0f, 1f, 0f },
                    { 0f, 0f, 0f, 0f, 1f }
                };
                break;
            case PhotoFilter.Cool:
                m = new[,]
                {
                    { 0.88f, 0f, 0f, 0f, 0f },
                    { 0f, 0.98f, 0f, 0f, 0f },
                    { 0f, 0f, 1.16f, 0f, 0f },
                    { 0f, 0f, 0f, 1f, 0f },
                    { 0f, 0.01f, 0.02f, 0f, 1f }
                };
                break;
            case PhotoFilter.Warm:
                m = new[,]
                {
                    { 1.14f, 0f, 0f, 0f, 0f },
                    { 0f, 1.01f, 0f, 0f, 0f },
                    { 0f, 0f, 0.86f, 0f, 0f },
                    { 0f, 0f, 0f, 1f, 0f },
                    { 0.02f, 0.01f, 0f, 0f, 1f }
                };
                break;
            case PhotoFilter.Invert:
                m = new[,]
                {
                    { -1f, 0f, 0f, 0f, 0f },
                    { 0f, -1f, 0f, 0f, 0f },
                    { 0f, 0f, -1f, 0f, 0f },
                    { 0f, 0f, 0f, 1f, 0f },
                    { 1f, 1f, 1f, 0f, 1f }
                };
                break;
        }
        return FromArray(m);
    }

    private static ColorMatrix AdjustmentMatrix(PhotoAdjustment adjustment)
    {
        float saturation = 1f + Math.Clamp(adjustment.Saturation, -100, 100) / 100f;
        float contrast = 1f + Math.Clamp(adjustment.Contrast, -50, 50) / 50f;
        float brightness = Math.Clamp(adjustment.Brightness, -50, 50) / 100f * 0.5f;
        float offset = 0.5f * (1f - contrast) + brightness;

        float[,] sat = SaturationMatrix(saturation);
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                sat[row, col] *= contrast;
            }
        }
        // ColorMatrix 的平移量位于第 4 行（常数项），而不是第 4 列（alpha 输入）。
        for (int col = 0; col < 3; col++)
        {
            sat[4, col] = offset;
        }
        return FromArray(sat);
    }

    private static float[,] SaturationMatrix(float saturation)
    {
        float s = saturation;
        float inv = 1f - s;
        const float lr = 0.299f;
        const float lg = 0.587f;
        const float lb = 0.114f;
        return new[,]
        {
            { lr * inv + s, lg * inv, lb * inv, 0f, 0f },
            { lr * inv, lg * inv + s, lb * inv, 0f, 0f },
            { lr * inv, lg * inv, lb * inv + s, 0f, 0f },
            { 0f, 0f, 0f, 1f, 0f },
            { 0f, 0f, 0f, 0f, 1f }
        };
    }

    private static float[,] Identity()
    {
        return new[,]
        {
            { 1f, 0f, 0f, 0f, 0f },
            { 0f, 1f, 0f, 0f, 0f },
            { 0f, 0f, 1f, 0f, 0f },
            { 0f, 0f, 0f, 1f, 0f },
            { 0f, 0f, 0f, 0f, 1f }
        };
    }

    private static ColorMatrix FromArray(float[,] m)
    {
        var matrix = new ColorMatrix();
        matrix.Matrix00 = m[0, 0];
        matrix.Matrix01 = m[0, 1];
        matrix.Matrix02 = m[0, 2];
        matrix.Matrix03 = m[0, 3];
        matrix.Matrix04 = m[0, 4];
        matrix.Matrix10 = m[1, 0];
        matrix.Matrix11 = m[1, 1];
        matrix.Matrix12 = m[1, 2];
        matrix.Matrix13 = m[1, 3];
        matrix.Matrix14 = m[1, 4];
        matrix.Matrix20 = m[2, 0];
        matrix.Matrix21 = m[2, 1];
        matrix.Matrix22 = m[2, 2];
        matrix.Matrix23 = m[2, 3];
        matrix.Matrix24 = m[2, 4];
        matrix.Matrix30 = m[3, 0];
        matrix.Matrix31 = m[3, 1];
        matrix.Matrix32 = m[3, 2];
        matrix.Matrix33 = m[3, 3];
        matrix.Matrix34 = m[3, 4];
        matrix.Matrix40 = m[4, 0];
        matrix.Matrix41 = m[4, 1];
        matrix.Matrix42 = m[4, 2];
        matrix.Matrix43 = m[4, 3];
        matrix.Matrix44 = m[4, 4];
        return matrix;
    }

    private static ColorMatrix Multiply(ColorMatrix a, ColorMatrix b)
    {
        float[,] av = ToArray(a);
        float[,] bv = ToArray(b);
        float[,] result = new float[5, 5];
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                float sum = 0f;
                for (int k = 0; k < 5; k++)
                {
                    sum += av[i, k] * bv[k, j];
                }
                result[i, j] = sum;
            }
        }
        return FromArray(result);
    }

    private static float[,] ToArray(ColorMatrix m)
    {
        return new[,]
        {
            { m.Matrix00, m.Matrix01, m.Matrix02, m.Matrix03, m.Matrix04 },
            { m.Matrix10, m.Matrix11, m.Matrix12, m.Matrix13, m.Matrix14 },
            { m.Matrix20, m.Matrix21, m.Matrix22, m.Matrix23, m.Matrix24 },
            { m.Matrix30, m.Matrix31, m.Matrix32, m.Matrix33, m.Matrix34 },
            { m.Matrix40, m.Matrix41, m.Matrix42, m.Matrix43, m.Matrix44 }
        };
    }

    /// <summary>
    /// 生成多尺寸 ICO 图标（16/24/32/48/64/128/256），每帧使用 PNG 压缩，
    /// 非正方形原图按比例居中留透明边。
    /// </summary>
    public static void SaveIco(Bitmap source, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<(int Size, byte[] Data)>();
        foreach (int size in sizes)
        {
            using var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                double scale = Math.Min((size - 2) / (double)source.Width,
                    (size - 2) / (double)source.Height);
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                g.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
            }
            using var stream = new MemoryStream();
            canvas.Save(stream, ImageFormat.Png);
            frames.Add((size, stream.ToArray()));
        }

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon type
        writer.Write((ushort)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var frame in frames)
        {
            byte dimension = (byte)(frame.Size >= 256 ? 0 : frame.Size);
            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0); // color count
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bit count
            writer.Write((uint)frame.Data.Length);
            writer.Write((uint)offset);
            offset += frame.Data.Length;
        }
        foreach (var frame in frames)
        {
            writer.Write(frame.Data);
        }
    }

    /// <summary>保存图片；JPEG 按质量重新编码，并按需要保留 / 剔除 EXIF。</summary>
    public static void Save(Bitmap bitmap, string path, PhotoSaveOptions options)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        bool alpha = options.Format.Guid == ImageFormat.Png.Guid;
        var prepared = new Bitmap(bitmap.Width, bitmap.Height,
            alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        try
        {
            CopyResolution(bitmap, prepared);
            using (var g = Graphics.FromImage(prepared))
            {
                if (!alpha)
                {
                    g.Clear(Color.White);
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
            if (options.MetadataSource != null)
            {
                CopyMetadata(options.MetadataSource, prepared, options.IncludeGps);
            }
            SavePrepared(prepared, path, options);
        }
        finally
        {
            prepared.Dispose();
        }
    }

    private static void SavePrepared(Bitmap bitmap, string path, PhotoSaveOptions options)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == options.Format.Guid);
        if (codec == null)
        {
            bitmap.Save(path, options.Format);
            return;
        }
        if (options.Format.Guid == ImageFormat.Jpeg.Guid)
        {
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(options.Quality, 1, 100));
            bitmap.Save(path, codec, parameters);
        }
        else
        {
            bitmap.Save(path, codec, null);
        }
    }

    private static readonly int[] SkippedMetadataTags =
    {
        OrientationTag,
        0x0100, // ImageWidth
        0x0101, // ImageLength
        0x0201, // JPEGInterchangeFormat
        0x0202, // JPEGInterchangeFormatLength
        0x8769, // Exif IFD 指针（由编码器重新计算）
        GpsIfdPointerTag, // GPS IFD 指针（由编码器重新计算）
        0xA005, // 互操作性 IFD 指针
        0xA002, // PixelXDimension
        0xA003  // PixelYDimension
    };

    /// <summary>
    /// 把来源图片的 EXIF 复制到新图：跳过方向标记、旧尺寸和缩略图偏移（避免文件损坏），
    /// 并按新图尺寸补写宽高。includeGps=false 时剔除全部 GPS 定位标签。
    /// </summary>
    public static int CopyMetadata(Bitmap source, Bitmap dest, bool includeGps)
    {
        int copied = 0;
        PropertyItem[] items;
        try
        {
            items = source.PropertyItems;
        }
        catch
        {
            return 0;
        }

        bool hadWidth = false;
        bool hadHeight = false;
        bool hadPixelWidth = false;
        bool hadPixelHeight = false;

        foreach (var item in items)
        {
            if (item.Id == 0x0100) hadWidth = true;
            else if (item.Id == 0x0101) hadHeight = true;
            else if (item.Id == 0xA002) hadPixelWidth = true;
            else if (item.Id == 0xA003) hadPixelHeight = true;
            if (SkippedMetadataTags.Contains(item.Id))
            {
                continue;
            }
            if (!includeGps && IsGpsTag(item.Id))
            {
                continue;
            }
            try
            {
                dest.SetPropertyItem(item);
                copied++;
            }
            catch
            {
                // 个别标签 GDI+ 不接受，跳过
            }
        }

        try
        {
            if (hadWidth) SetLongTag(dest, 0x0100, dest.Width);
            if (hadHeight) SetLongTag(dest, 0x0101, dest.Height);
            if (hadPixelWidth) SetLongTag(dest, 0xA002, dest.Width);
            if (hadPixelHeight) SetLongTag(dest, 0xA003, dest.Height);
        }
        catch
        {
            // 尺寸标签补写失败不影响照片
        }
        return copied;
    }

    private static void SetLongTag(Bitmap bitmap, int id, int value)
    {
#pragma warning disable SYSLIB0050
        var item = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
#pragma warning restore SYSLIB0050
        item.Id = id;
        item.Type = 4; // LONG
        item.Len = 4;
        item.Value = BitConverter.GetBytes((uint)value);
        bitmap.SetPropertyItem(item);
    }

    private static bool IsGpsTag(int id)
    {
        return id == GpsIfdPointerTag || (id >= 0x0000 && id <= 0x001F);
    }

    public static bool HasGps(string path)
    {
        try
        {
            using var image = Image.FromFile(path);
            return image.PropertyItems.Any(item => IsGpsTag(item.Id));
        }
        catch
        {
            return false;
        }
    }

    // ================= EXIF 读取 =================

    public static ExifInfo ReadExif(string path)
    {
        var info = new ExifInfo();
        try
        {
            using var image = Image.FromFile(path);
            info.Dimensions = image.Width + " × " + image.Height + " 像素";
            PropertyItem[] items = image.PropertyItems;
            var byId = new Dictionary<int, PropertyItem>();
            foreach (var item in items)
            {
                byId[item.Id] = item;
            }
            foreach (var item in items.OrderBy(i => i.Id))
            {
                string value = FormatExifValue(item, byId);
                info.Entries.Add(new ExifEntry
                {
                    Id = item.Id,
                    Tag = TagName(item.Id),
                    Value = value,
                    TypeName = TypeName(item.Type),
                    Length = item.Len
                });
                if (IsGpsTag(item.Id))
                {
                    info.HasGps = true;
                }
            }

            string make = GetAscii(byId, 0x010F);
            string model = GetAscii(byId, 0x0110);
            info.Camera = string.Join(" ", new[] { make, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
            info.DateTaken = GetAscii(byId, 0x9003);
            if (string.IsNullOrWhiteSpace(info.DateTaken))
            {
                info.DateTaken = GetAscii(byId, 0x0132);
            }
            if (byId.TryGetValue(OrientationTag, out var orientationItem))
            {
                info.Orientation = OrientationText(GetUShort(orientationItem));
            }
        }
        catch
        {
            // 无法读取 EXIF 时返回空列表
        }
        return info;
    }

    private static string GetAscii(Dictionary<int, PropertyItem> items, int id)
    {
        return items.TryGetValue(id, out var item) ? DecodeAscii(item.Value) : "";
    }

    private static ushort GetUShort(PropertyItem item)
    {
        return item.Type == 3 && item.Value is { Length: >= 2 } value
            ? BitConverter.ToUInt16(value, 0) : (ushort)1;
    }

    private static string FormatExifValue(PropertyItem item, Dictionary<int, PropertyItem> byId)
    {
        string value;
        try
        {
            value = FormatByType(item);
        }
        catch
        {
            value = "（无法解析）";
        }

        switch (item.Id)
        {
            case 0x0112:
                return OrientationText(GetUShort(item));
            case 0x829A:
                if (TryRational(item, 0, out double seconds))
                {
                    value = seconds > 0 && seconds < 1
                        ? "1/" + Math.Round(1 / seconds).ToString("0", CultureInfo.InvariantCulture) + " 秒"
                        : seconds.ToString("0.##", CultureInfo.InvariantCulture) + " 秒";
                }
                break;
            case 0x829D:
                if (TryRational(item, 0, out double f)) value = "f/" + f.ToString("0.#", CultureInfo.InvariantCulture);
                break;
            case 0x920A:
                if (TryRational(item, 0, out double mm)) value = mm.ToString("0.#", CultureInfo.InvariantCulture) + " mm";
                break;
            case 0x8827:
                value = "ISO " + value;
                break;
            case 0x0128:
            case 0xA210:
                value = value switch
                {
                    "2" => "英寸（2）",
                    "3" => "厘米（3）",
                    _ => value
                };
                break;
            case 0x9209:
                value = FlashText(value);
                break;
            case 0x9207:
                value = MeteringText(value);
                break;
            case 0xA001:
                value = value == "1" ? "sRGB" : value == "65535" ? "未校准" : value;
                break;
            case 0xA402:
                value = value == "1" ? "手动曝光" : value == "2" ? "自动曝光" : value;
                break;
            case 0xA403:
                value = value == "0" ? "自动白平衡" : value == "1" ? "手动白平衡" : value;
                break;
            case 0xA406:
                value = value switch
                {
                    "0" => "标准",
                    "1" => "风景",
                    "2" => "人像",
                    "3" => "夜景",
                    _ => value
                };
                break;
            case 0xA408:
                value = SharpnessText(value, "对比度");
                break;
            case 0xA409:
                value = SharpnessText(value, "饱和度");
                break;
            case 0xA40A:
                value = SharpnessText(value, "锐度");
                break;
            case 0x0001:
                // GPSLatitudeRef（注意与互操作性标签同 ID，按是否含 GPS IFD 判断）
                if (HasGpsContext(byId) && value.Length <= 2) value += "（纬度参考）";
                break;
            case 0x0002:
                if (HasGpsContext(byId))
                {
                    string reference = GetAscii(byId, 0x0001);
                    value = FormatGpsCoordinate(value, reference, "N", "S");
                }
                break;
            case 0x0003:
                if (HasGpsContext(byId) && value.Length <= 2) value += "（经度参考）";
                break;
            case 0x0004:
                if (HasGpsContext(byId))
                {
                    string reference = GetAscii(byId, 0x0003);
                    value = FormatGpsCoordinate(value, reference, "E", "W");
                }
                break;
            case 0x0005:
                if (HasGpsContext(byId))
                {
                    value = value == "0" ? "海平面以上" : value == "1" ? "海平面以下" : value;
                }
                break;
            case 0x0006:
                if (TryRational(item, 0, out double altitude))
                {
                    value = altitude.ToString("0.#", CultureInfo.InvariantCulture) + " 米";
                }
                break;
            case 0x0007:
                value = FormatGpsTime(item);
                break;
            case 0x001D:
                value = value.Replace('/', '-');
                break;
            case 0x9286:
                value = DecodeUserComment(item.Value);
                break;
            case 0x927C:
                value = "（厂商私有数据，" + item.Len + " 字节）";
                break;
        }
        return value;
    }

    private static string FormatByType(PropertyItem item)
    {
        byte[] v = item.Value ?? Array.Empty<byte>();
        switch (item.Type)
        {
            case 1: // BYTE
                return JoinValues(v, 1, b => b[0].ToString(CultureInfo.InvariantCulture));
            case 2: // ASCII
                return DecodeAscii(v);
            case 3: // SHORT
                return JoinValues(v, 2, b => BitConverter.ToUInt16(b, 0).ToString(CultureInfo.InvariantCulture));
            case 4: // LONG
                return JoinValues(v, 4, b => BitConverter.ToUInt32(b, 0).ToString(CultureInfo.InvariantCulture));
            case 5: // RATIONAL
                return JoinRationals(v, false);
            case 6: // SBYTE
                return JoinValues(v, 1, b => ((sbyte)b[0]).ToString(CultureInfo.InvariantCulture));
            case 7: // UNDEFINED
                return FormatUndefined(v);
            case 8: // SSHORT
                return JoinValues(v, 2, b => BitConverter.ToInt16(b, 0).ToString(CultureInfo.InvariantCulture));
            case 9: // SLONG
                return JoinValues(v, 4, b => BitConverter.ToInt32(b, 0).ToString(CultureInfo.InvariantCulture));
            case 10: // SRATIONAL
                return JoinRationals(v, true);
            case 11: // FLOAT
                return JoinValues(v, 4, b => BitConverter.ToSingle(b, 0).ToString("0.###", CultureInfo.InvariantCulture));
            case 12: // DOUBLE
                return JoinValues(v, 8, b => BitConverter.ToDouble(b, 0).ToString("0.###", CultureInfo.InvariantCulture));
            default:
                return Truncate(ToHex(v), 260);
        }
    }

    private static string JoinValues(byte[] value, int size, Func<byte[], string> convert)
    {
        if (size <= 0 || value.Length < size)
        {
            return "";
        }
        int count = Math.Min(value.Length / size, 16);
        var parts = new string[count];
        for (int i = 0; i < count; i++)
        {
            var slice = new byte[size];
            Buffer.BlockCopy(value, i * size, slice, 0, size);
            parts[i] = convert(slice);
        }
        string text = string.Join(", ", parts);
        if (value.Length / size > count)
        {
            text += " …";
        }
        return Truncate(text, 260);
    }

    private static string JoinRationals(byte[] value, bool signed)
    {
        if (value.Length < 8)
        {
            return "";
        }
        int count = Math.Min(value.Length / 8, 8);
        var parts = new List<string>();
        for (int i = 0; i < count; i++)
        {
            if (signed)
            {
                int n = BitConverter.ToInt32(value, i * 8);
                int d = BitConverter.ToInt32(value, i * 8 + 4);
                parts.Add(d == 0 ? "0/0" : d == 1 ? n.ToString(CultureInfo.InvariantCulture)
                    : n + "/" + d + " (" + ((double)n / d).ToString("0.###", CultureInfo.InvariantCulture) + ")");
            }
            else
            {
                uint n = BitConverter.ToUInt32(value, i * 8);
                uint d = BitConverter.ToUInt32(value, i * 8 + 4);
                parts.Add(d == 0 ? "0/0" : d == 1 ? n.ToString(CultureInfo.InvariantCulture)
                    : n + "/" + d + " (" + ((double)n / d).ToString("0.###", CultureInfo.InvariantCulture) + ")");
            }
        }
        return Truncate(string.Join(", ", parts), 260);
    }

    private static bool HasGpsContext(Dictionary<int, PropertyItem> byId)
    {
        return byId.ContainsKey(GpsIfdPointerTag)
            || (byId.ContainsKey(0x0002) && byId.ContainsKey(0x0003) && byId.ContainsKey(0x0004));
    }

    private static bool TryRational(PropertyItem item, int index, out double value)
    {
        value = 0;
        if (item.Value is not { Length: >= 8 } v || index * 8 + 8 > v.Length)
        {
            return false;
        }
        if (item.Type == 10)
        {
            int n = BitConverter.ToInt32(v, index * 8);
            int d = BitConverter.ToInt32(v, index * 8 + 4);
            if (d == 0) return false;
            value = (double)n / d;
            return true;
        }
        uint un = BitConverter.ToUInt32(v, index * 8);
        uint ud = BitConverter.ToUInt32(v, index * 8 + 4);
        if (ud == 0) return false;
        value = (double)un / ud;
        return true;
    }

    private static string FormatGpsCoordinate(string raw, string reference, string positive, string negative)
    {
        var numbers = new List<double>();
        foreach (var part in raw.Split(','))
        {
            string text = part.Trim();
            int open = text.IndexOf('(');
            int close = text.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                text = text[(open + 1)..close];
            }
            else if (text.Contains('/'))
            {
                text = text[..text.IndexOf('/')];
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                numbers.Add(d);
            }
        }
        if (numbers.Count >= 3)
        {
            double deg = numbers[0] + numbers[1] / 60.0 + numbers[2] / 3600.0;
            string dir = reference.ToUpperInvariant() == negative ? negative : positive;
            return deg.ToString("0.000000", CultureInfo.InvariantCulture) + "° " + dir
                + "  (" + numbers[0].ToString("0", CultureInfo.InvariantCulture) + "°"
                + numbers[1].ToString("0", CultureInfo.InvariantCulture) + "'"
                + numbers[2].ToString("0.##", CultureInfo.InvariantCulture) + "\")";
        }
        return raw;
    }

    private static string FormatGpsTime(PropertyItem item)
    {
        if (item.Value is not { Length: >= 24 } v)
        {
            return FormatByType(item);
        }
        uint h = BitConverter.ToUInt32(v, 0);
        uint m = BitConverter.ToUInt32(v, 8);
        uint s = BitConverter.ToUInt32(v, 16);
        return h.ToString("00", CultureInfo.InvariantCulture) + ":"
            + m.ToString("00", CultureInfo.InvariantCulture) + ":"
            + s.ToString("00", CultureInfo.InvariantCulture) + " UTC";
    }

    private static string DecodeAscii(byte[]? value)
    {
        if (value == null || value.Length == 0)
        {
            return "";
        }
        var text = Encoding.ASCII.GetString(value).Trim('\0', ' ');
        text = text.Replace('\0', ' ').Trim();
        return Truncate(text, 260);
    }

    private static string DecodeUserComment(byte[]? value)
    {
        if (value == null || value.Length == 0)
        {
            return "";
        }
        string text;
        if (value.Length >= 8 && value[0] == (byte)'A' && value[1] == (byte)'S'
            && value[2] == (byte)'C' && value[3] == (byte)'I' && value[4] == (byte)'I')
        {
            text = Encoding.ASCII.GetString(value, 8, value.Length - 8);
        }
        else if (value.Length >= 8 && value[0] == (byte)'U' && value[1] == (byte)'N'
            && value[2] == (byte)'I' && value[3] == (byte)'C')
        {
            text = Encoding.Unicode.GetString(value, 8, value.Length - 8);
        }
        else
        {
            text = Encoding.ASCII.GetString(value);
        }
        return Truncate(text.Trim('\0', ' '), 260);
    }

    private static string FormatUndefined(byte[] value)
    {
        if (value.Length == 0)
        {
            return "";
        }
        int printable = value.Count(b => b is >= 32 and <= 126 || b is 9 or 10 or 13 or 0);
        if (printable >= value.Length * 0.9)
        {
            return DecodeAscii(value);
        }
        return Truncate(ToHex(value), 260);
    }

    private static string ToHex(byte[] value)
    {
        int count = Math.Min(value.Length, 40);
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(value[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        if (value.Length > count)
        {
            sb.Append(" …");
        }
        return sb.ToString();
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static string OrientationText(ushort value)
    {
        return value switch
        {
            1 => "正常（1）",
            2 => "水平翻转（2）",
            3 => "旋转 180°（3）",
            4 => "垂直翻转（4）",
            5 => "转置（5）",
            6 => "顺时针旋转 90°（6）",
            7 => "逆时针旋转 90° 后水平翻转（7）",
            8 => "逆时针旋转 90°（8）",
            _ => "未知（" + value + "）"
        };
    }

    private static string FlashText(string value)
    {
        return value switch
        {
            "0" => "未闪光",
            "1" => "已闪光",
            "5" => "闪光，未检测到返回光",
            "7" => "闪光，检测到返回光",
            "9" => "强制闪光",
            "16" => "关闭闪光",
            "24" => "自动，未闪光",
            "25" => "自动，已闪光",
            _ => value
        };
    }

    private static string MeteringText(string value)
    {
        return value switch
        {
            "1" => "平均测光",
            "2" => "中央重点平均测光",
            "3" => "点测光",
            "4" => "多点测光",
            "5" => "矩阵测光",
            "6" => "局部测光",
            _ => value
        };
    }

    private static string SharpnessText(string value, string label)
    {
        return value switch
        {
            "0" => "正常（" + label + "）",
            "1" => "低（" + label + "）",
            "2" => "高（" + label + "）",
            _ => value
        };
    }

    private static string TypeName(int type)
    {
        return type switch
        {
            1 => "BYTE",
            2 => "ASCII",
            3 => "SHORT",
            4 => "LONG",
            5 => "RATIONAL",
            6 => "SBYTE",
            7 => "UNDEFINED",
            8 => "SSHORT",
            9 => "SLONG",
            10 => "SRATIONAL",
            11 => "FLOAT",
            12 => "DOUBLE",
            _ => "TYPE " + type
        };
    }

    private static string TagName(int id)
    {
        return Tags.TryGetValue(id, out string? name) ? name : "0x" + id.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static readonly Dictionary<int, string> Tags = new()
    {
        [0x0000] = "GPS 版本",
        [0x0001] = "GPS 纬度参考",
        [0x0002] = "GPS 纬度",
        [0x0003] = "GPS 经度参考",
        [0x0004] = "GPS 经度",
        [0x0005] = "GPS 海拔参考",
        [0x0006] = "GPS 海拔",
        [0x0007] = "GPS 时间戳",
        [0x0008] = "GPS 卫星",
        [0x0009] = "GPS 状态",
        [0x000A] = "GPS 测量模式",
        [0x000B] = "GPS 精度因子",
        [0x000C] = "GPS 速度参考",
        [0x000D] = "GPS 速度",
        [0x000E] = "GPS 方向参考",
        [0x000F] = "GPS 方向",
        [0x0010] = "GPS 图像方向参考",
        [0x0011] = "GPS 图像方向",
        [0x0012] = "GPS 大地基准",
        [0x0013] = "GPS 目标纬度参考",
        [0x0014] = "GPS 目标纬度",
        [0x0015] = "GPS 目标经度参考",
        [0x0016] = "GPS 目标经度",
        [0x0017] = "GPS 目标方位参考",
        [0x0018] = "GPS 目标方位",
        [0x0019] = "GPS 目标距离参考",
        [0x001A] = "GPS 目标距离",
        [0x001B] = "GPS 处理方法",
        [0x001C] = "GPS 区域信息",
        [0x001D] = "GPS 日期",
        [0x001E] = "GPS 差分校正",
        [0x001F] = "GPS 定位误差",
        [0x0100] = "图像宽度",
        [0x0101] = "图像高度",
        [0x0102] = "每样本位数",
        [0x0103] = "压缩方式",
        [0x0106] = "光度解释",
        [0x010E] = "图像描述",
        [0x010F] = "相机厂商",
        [0x0110] = "相机型号",
        [0x0112] = "方向",
        [0x0115] = "每像素样本数",
        [0x011A] = "水平分辨率",
        [0x011B] = "垂直分辨率",
        [0x011C] = "平面配置",
        [0x0128] = "分辨率单位",
        [0x0131] = "处理软件",
        [0x0132] = "修改时间",
        [0x013B] = "作者",
        [0x013E] = "白点",
        [0x013F] = "主色度",
        [0x0201] = "缩略图偏移",
        [0x0202] = "缩略图长度",
        [0x0211] = "YCbCr 系数",
        [0x0212] = "YCbCr 子采样",
        [0x0213] = "YCbCr 定位",
        [0x0214] = "黑白参考值",
        [0x8298] = "版权",
        [0x829A] = "曝光时间",
        [0x829D] = "光圈值",
        [0x8769] = "Exif 子目录",
        [0x8822] = "曝光程序",
        [0x8824] = "光谱灵敏度",
        [0x8825] = "GPS 子目录",
        [0x8827] = "ISO 感光度",
        [0x8828] = "光电转换函数",
        [0x8830] = "感光度类型",
        [0x8832] = "推荐曝光指数",
        [0x8833] = "ISO 速度",
        [0x9000] = "Exif 版本",
        [0x9003] = "拍摄时间",
        [0x9004] = "数字化时间",
        [0x9010] = "时区偏移",
        [0x9011] = "原始时区偏移",
        [0x9012] = "数字化时区偏移",
        [0x9101] = "分量配置",
        [0x9102] = "压缩位率",
        [0x9201] = "快门速度值",
        [0x9202] = "光圈值（APEX）",
        [0x9203] = "亮度值",
        [0x9204] = "曝光补偿",
        [0x9205] = "最大光圈值",
        [0x9206] = "主体距离",
        [0x9207] = "测光模式",
        [0x9208] = "光源",
        [0x9209] = "闪光灯",
        [0x920A] = "焦距",
        [0x9214] = "主体区域",
        [0x927C] = "厂商注释",
        [0x9286] = "用户注释",
        [0x9290] = "秒以下时间",
        [0x9291] = "原始秒以下时间",
        [0x9292] = "数字化秒以下时间",
        [0x9C9B] = "标题",
        [0x9C9C] = "注释",
        [0x9C9D] = "作者（XP）",
        [0x9C9E] = "关键词",
        [0x9C9F] = "主题",
        [0xA000] = "FlashPix 版本",
        [0xA001] = "色彩空间",
        [0xA002] = "像素宽度",
        [0xA003] = "像素高度",
        [0xA004] = "相关声音文件",
        [0xA005] = "互操作性子目录",
        [0xA20E] = "焦平面水平分辨率",
        [0xA20F] = "焦平面垂直分辨率",
        [0xA210] = "焦平面分辨率单位",
        [0xA214] = "主体位置",
        [0xA215] = "曝光指数",
        [0xA217] = "感光方式",
        [0xA300] = "文件来源",
        [0xA301] = "场景类型",
        [0xA302] = "CFA 图案",
        [0xA401] = "自定义处理",
        [0xA402] = "曝光模式",
        [0xA403] = "白平衡",
        [0xA404] = "数码变焦倍率",
        [0xA405] = "35mm 等效焦距",
        [0xA406] = "场景拍摄类型",
        [0xA407] = "增益控制",
        [0xA408] = "对比度",
        [0xA409] = "饱和度",
        [0xA40A] = "锐度",
        [0xA40B] = "设备设置描述",
        [0xA40C] = "主体距离范围",
        [0xA420] = "图像唯一编号",
        [0xA430] = "相机所有者",
        [0xA431] = "机身序列号",
        [0xA432] = "镜头规格",
        [0xA433] = "镜头厂商",
        [0xA434] = "镜头型号",
        [0xA435] = "镜头序列号"
    };
}
