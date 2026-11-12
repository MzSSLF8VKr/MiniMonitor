using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MiniMonitor;

public sealed record OcrRecognitionResult(
    string Text, string PlainText, string Language, TimeSpan Elapsed);

/// <summary>
/// Windows 本地 OCR：调用系统内置 Windows.Media.Ocr，离线识别，不上传图片。
/// 自动优先使用简体中文 OCR 语言包，其次中文/英文/系统默认语言。
/// </summary>
public static class WindowsOcrService
{
    public static async Task<OcrRecognitionResult> RecognizeAsync(Bitmap source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var stopwatch = Stopwatch.StartNew();
        Bitmap prepared = source;
        bool ownsPrepared = false;
        try
        {
            prepared = PrepareForOcr(source);
            ownsPrepared = !ReferenceEquals(prepared, source);
            byte[] png = EncodePng(prepared);

            OcrEngine? engine = CreateEngine(out string languageTag);
            if (engine == null)
            {
                throw new InvalidOperationException(
                    "未找到可用的 Windows 本地 OCR 语言包。\n\n" +
                    "请打开 Windows 设置 → 时间和语言 → 语言，进入中文(简体) → 选项，" +
                    "安装“光学字符识别”组件后重试。整个过程不需要联网上传图片。");
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            OcrResult result = await engine.RecognizeAsync(softwareBitmap);

            string plainText = NormalizeText(result.Text);
            string layoutText = BuildLayoutText(result);
            if (string.IsNullOrWhiteSpace(layoutText))
            {
                layoutText = plainText;
            }
            stopwatch.Stop();
            return new OcrRecognitionResult(layoutText, plainText, languageTag, stopwatch.Elapsed);
        }
        finally
        {
            if (ownsPrepared)
            {
                prepared.Dispose();
            }
        }
    }

    private static string BuildLayoutText(OcrResult result)
    {
        var lines = new List<IReadOnlyList<OcrTextBlock>>();
        foreach (OcrLine line in result.Lines)
        {
            var blocks = new List<OcrTextBlock>();
            foreach (OcrWord word in line.Words)
            {
                var rect = word.BoundingRect;
                blocks.Add(new OcrTextBlock(
                    word.Text ?? "", rect.X, rect.Y, rect.Width, rect.Height));
            }
            if (blocks.Count > 0)
            {
                lines.Add(blocks);
            }
        }
        return OcrLayoutBuilder.Build(lines);
    }

    private static string NormalizeText(string? text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
    }

    private static OcrEngine? CreateEngine(out string languageTag)
    {
        languageTag = "";
        var languages = OcrEngine.AvailableRecognizerLanguages;
        if (languages == null || languages.Count == 0)
        {
            return null;
        }

        var ordered = new List<Language>();
        Language? simplified = null;
        Language? chinese = null;
        Language? english = null;
        foreach (var language in languages)
        {
            string tag = language.LanguageTag ?? "";
            if (simplified == null && tag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
            {
                simplified = language;
            }
            if (chinese == null && tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                chinese = language;
            }
            if (english == null && tag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                english = language;
            }
        }
        if (simplified != null)
        {
            ordered.Add(simplified);
        }
        if (chinese != null && !ordered.Contains(chinese))
        {
            ordered.Add(chinese);
        }
        if (english != null && !ordered.Contains(english))
        {
            ordered.Add(english);
        }
        foreach (var language in languages)
        {
            if (!ordered.Contains(language))
            {
                ordered.Add(language);
            }
        }

        foreach (var language in ordered)
        {
            OcrEngine? engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine != null)
            {
                languageTag = language.LanguageTag ?? "";
                return engine;
            }
        }

        OcrEngine? profileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (profileEngine != null)
        {
            languageTag = profileEngine.RecognizerLanguage.LanguageTag ?? "";
            return profileEngine;
        }
        return null;
    }

    private static Bitmap PrepareForOcr(Bitmap source)
    {
        float maxDimension = OcrEngine.MaxImageDimension;
        if (maxDimension <= 0)
        {
            maxDimension = 10000f;
        }

        float scale = 1f;
        if (source.Width > maxDimension || source.Height > maxDimension)
        {
            scale = Math.Min(maxDimension / source.Width, maxDimension / source.Height);
        }
        else if (source.Width < 900 || source.Height < 500)
        {
            // 小截图放大后再识别，可明显提升小字号文字识别率。
            scale = 2f;
        }
        scale = Math.Min(scale, Math.Min(maxDimension / source.Width, maxDimension / source.Height));
        if (scale <= 0 || Math.Abs(scale - 1f) < 0.01f)
        {
            return source;
        }

        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        return result;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
