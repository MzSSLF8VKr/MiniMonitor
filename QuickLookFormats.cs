using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MiniMonitor;

internal enum QuickLookKind
{
    Image,
    ImageWeb,
    Text,
    Markdown,
    Audio,
    Video,
    Pdf,
    Html,
    Office,
    Archive,
    Font,
    Folder,
    Other
}

internal sealed record ArchiveEntryInfo(string Name, long Size, long CompressedSize);

/// <summary>QuickLook 复刻的格式识别、插件开关、文本 / Office / 压缩包内容读取。</summary>
internal static class QuickLookFormats
{
    static QuickLookFormats()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // 已注册时忽略
        }
    }

    private static readonly string[] StandardImages =
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico",
        ".psd", ".psb", ".xcf", ".cdr", ".ai", ".eps",
        ".cr2", ".cr3", ".nef", ".arw", ".srf", ".sr2", ".rw2", ".orf", ".dng",
        ".raf", ".raw", ".pef", ".srw", ".3fr", ".erf", ".kdc", ".dcr", ".mef",
        ".mos", ".mrw", ".nrw", ".x3f", ".crw", ".ari", ".bay", ".fff", ".iiq", ".rwl"
    };

    private static readonly string[] WebImages =
    {
        ".webp", ".heic", ".heif", ".avif", ".apng", ".svg"
    };

    private static readonly string[] TextExtensions =
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".properties", ".env",
        ".json", ".jsonc", ".xml", ".xsd", ".xsl", ".yaml", ".yml", ".toml",
        ".csv", ".tsv", ".cs", ".cpp", ".cc", ".c", ".h", ".hpp", ".java",
        ".py", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".css", ".scss", ".less",
        ".sql", ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1", ".psm1",
        ".go", ".rs", ".php", ".rb", ".swift", ".kt", ".kts", ".lua", ".r",
        ".m", ".mm", ".gradle", ".sln", ".csproj", ".vcxproj", ".vbproj",
        ".xaml", ".reg", ".diff", ".patch", ".tex", ".latex", ".makefile",
        ".dockerfile", ".gitignore", ".gitattributes", ".editorconfig",
        ".vue", ".svelte", ".asm", ".vhd", ".vhdl", ".proto", ".graphql"
    };

    private static readonly string[] MarkdownExtensions = { ".md", ".markdown", ".mdown", ".mkd" };

    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".wav", ".flac", ".aac", ".m4a", ".m4b", ".ogg", ".oga",
        ".opus", ".wma", ".aiff", ".aif", ".amr", ".ape", ".ac3", ".mka", ".weba"
    };

    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".3gp", ".ogv", ".vob",
        ".rm", ".rmvb", ".asf", ".f4v"
    };

    private static readonly string[] OfficeExtensions =
    {
        ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm",
        ".doc", ".xls", ".ppt", ".rtf", ".odt", ".ods", ".odp"
    };

    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cab", ".iso"
    };

    private static readonly string[] FontExtensions = { ".ttf", ".otf", ".ttc", ".fon" };

    public static QuickLookKind Detect(string path)
    {
        if (Directory.Exists(path))
        {
            return QuickLookKind.Folder;
        }
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (StandardImages.Contains(extension)) return QuickLookKind.Image;
        if (WebImages.Contains(extension)) return QuickLookKind.ImageWeb;
        if (MarkdownExtensions.Contains(extension)) return QuickLookKind.Markdown;
        if (TextExtensions.Contains(extension)) return QuickLookKind.Text;
        if (extension is ".html" or ".htm" or ".xhtml") return QuickLookKind.Html;
        if (extension == ".pdf") return QuickLookKind.Pdf;
        if (AudioExtensions.Contains(extension)) return QuickLookKind.Audio;
        if (VideoExtensions.Contains(extension)) return QuickLookKind.Video;
        if (OfficeExtensions.Contains(extension)) return QuickLookKind.Office;
        if (ArchiveExtensions.Contains(extension)) return QuickLookKind.Archive;
        if (FontExtensions.Contains(extension)) return QuickLookKind.Font;
        return QuickLookKind.Other;
    }

    public static string KindName(QuickLookKind kind)
    {
        return kind switch
        {
            QuickLookKind.Image or QuickLookKind.ImageWeb => "图片",
            QuickLookKind.Text => "文本 / 代码",
            QuickLookKind.Markdown => "Markdown",
            QuickLookKind.Audio => "音频",
            QuickLookKind.Video => "视频",
            QuickLookKind.Pdf => "PDF",
            QuickLookKind.Html => "网页 / HTML",
            QuickLookKind.Office => "Office 文档",
            QuickLookKind.Archive => "压缩包",
            QuickLookKind.Font => "字体",
            QuickLookKind.Folder => "文件夹",
            _ => "文件"
        };
    }

    public static bool PluginEnabled(AppConfig config, QuickLookKind kind)
    {
        return kind switch
        {
            QuickLookKind.Image or QuickLookKind.ImageWeb => config.QuickLookPluginImage,
            QuickLookKind.Text or QuickLookKind.Markdown => config.QuickLookPluginText,
            QuickLookKind.Audio => config.QuickLookPluginAudio,
            QuickLookKind.Video => config.QuickLookPluginVideo,
            QuickLookKind.Pdf or QuickLookKind.Html => config.QuickLookPluginWeb,
            QuickLookKind.Office => config.QuickLookPluginOffice,
            QuickLookKind.Archive => config.QuickLookPluginArchive,
            QuickLookKind.Font => config.QuickLookPluginFont,
            QuickLookKind.Folder => config.QuickLookPluginFolder,
            _ => true
        };
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
    }

    /// <summary>读取文本预览；超过 2MB 截断。返回 null 表示看起来是二进制文件。</summary>
    public static string? ReadText(string path, out bool truncated)
    {
        truncated = false;
        try
        {
            const long limit = 2 * 1024 * 1024;
            long length = new FileInfo(path).Length;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite);
            int readSize = (int)Math.Min(limit, length);
            var bytes = new byte[readSize];
            int total = 0;
            while (total < readSize)
            {
                int read = stream.Read(bytes, total, readSize - total);
                if (read <= 0) break;
                total += read;
            }
            if (total < readSize)
            {
                Array.Resize(ref bytes, total);
            }
            truncated = length > limit;
            if (bytes.Length == 0)
            {
                return "";
            }
            string? bomText = TryDecodeBom(bytes);
            if (bomText != null)
            {
                return truncated ? bomText + "\r\n\r\n……（文件超过 2MB，仅显示前 2MB）" : bomText;
            }
            if (LooksBinary(bytes))
            {
                return null;
            }
            string text = DecodeText(bytes);
            return truncated ? text + "\r\n\r\n……（文件超过 2MB，仅显示前 2MB）" : text;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksBinary(byte[] bytes)
    {
        int nonPrintable = 0;
        int checkedCount = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < checkedCount; i++)
        {
            byte b = bytes[i];
            if (b == 0)
            {
                return true;
            }
            if (b < 7 || (b > 13 && b < 32))
            {
                nonPrintable++;
            }
        }
        return nonPrintable > checkedCount / 20;
    }

    private static string? TryDecodeBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        return null;
    }

    private static string DecodeText(byte[] bytes)
    {
        string utf8 = Encoding.UTF8.GetString(bytes);
        int replacement = utf8.Count(c => c == '\uFFFD');
        if (replacement > Math.Max(2, utf8.Length / 200))
        {
            try
            {
                return Encoding.GetEncoding(936).GetString(bytes);
            }
            catch
            {
                try
                {
                    return Encoding.Default.GetString(bytes);
                }
                catch
                {
                    return utf8;
                }
            }
        }
        return utf8;
    }

    /// <summary>提取 docx / xlsx / pptx 文本（无 Office 依赖）；旧版二进制格式返回 null。</summary>
    public static string? ExtractOfficeText(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".docx" or ".docm" => ExtractDocx(path),
                ".xlsx" or ".xlsm" => ExtractXlsx(path),
                ".pptx" or ".pptm" => ExtractPptx(path),
                _ => null
            };
        }
        catch (Exception ex)
        {
            return "无法提取文档内容：" + ex.Message;
        }
    }

    private static string? ExtractDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml");
        if (entry == null)
        {
            return null;
        }
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var builder = new StringBuilder();
        foreach (var paragraph in document.Descendants()
            .Where(element => element.Name.LocalName == "p"))
        {
            var texts = paragraph.Descendants()
                .Where(element => element.Name.LocalName == "t")
                .Select(element => element.Value);
            builder.AppendLine(string.Concat(texts));
        }
        return LimitText(builder.ToString());
    }

    private static string? ExtractPptx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var slides = archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => SlideNumber(entry.FullName))
            .ToArray();
        if (slides.Length == 0)
        {
            return null;
        }
        var builder = new StringBuilder();
        int index = 1;
        foreach (var entry in slides)
        {
            using var stream = entry.Open();
            var slide = XDocument.Load(stream);
            var texts = slide.Descendants()
                .Where(element => element.Name.LocalName == "t")
                .Select(element => element.Value);
            builder.AppendLine("【第 " + index + " 页】");
            builder.AppendLine(string.Join(" ", texts));
            builder.AppendLine();
            index++;
        }
        return LimitText(builder.ToString());
    }

    private static int SlideNumber(string name)
    {
        var match = Regex.Match(name, @"slide(\d+)\.xml", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static string? ExtractXlsx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = new List<string>();
        var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry != null)
        {
            using var stream = sharedEntry.Open();
            var shared = XDocument.Load(stream);
            foreach (var item in shared.Descendants()
                .Where(element => element.Name.LocalName == "si"))
            {
                var text = string.Concat(item.Descendants()
                    .Where(element => element.Name.LocalName == "t")
                    .Select(element => element.Value));
                sharedStrings.Add(text);
            }
        }
        var sheet = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName)
            .FirstOrDefault();
        if (sheet == null)
        {
            return null;
        }
        using var sheetStream = sheet.Open();
        var document = XDocument.Load(sheetStream);
        var builder = new StringBuilder();
        int rowCount = 0;
        foreach (var row in document.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            if (rowCount++ > 2000)
            {
                builder.AppendLine("……（仅显示前 2000 行）");
                break;
            }
            var cells = new List<string>();
            foreach (var cell in row.Descendants().Where(element => element.Name.LocalName == "c"))
            {
                string column = CellColumn(cell.Attribute("r")?.Value ?? "");
                int target = ColumnIndex(column);
                while (cells.Count < target && cells.Count < 40)
                {
                    cells.Add("");
                }
                if (cells.Count >= 40)
                {
                    break;
                }
                string type = cell.Attribute("t")?.Value ?? "";
                string value = "";
                var v = cell.Descendants().FirstOrDefault(element => element.Name.LocalName == "v");
                if (v != null)
                {
                    value = v.Value;
                }
                else
                {
                    var inline = cell.Descendants().FirstOrDefault(element => element.Name.LocalName == "t");
                    if (inline != null)
                    {
                        value = inline.Value;
                    }
                }
                if (type == "s" && int.TryParse(value, out int sharedIndex)
                    && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                {
                    value = sharedStrings[sharedIndex];
                }
                cells.Add(value);
            }
            builder.AppendLine(string.Join("\t", cells));
        }
        return LimitText(builder.ToString());
    }

    private static string CellColumn(string reference)
    {
        var letters = new StringBuilder();
        foreach (char ch in reference)
        {
            if (char.IsLetter(ch))
            {
                letters.Append(ch);
            }
            else
            {
                break;
            }
        }
        return letters.ToString();
    }

    private static int ColumnIndex(string column)
    {
        int index = 0;
        foreach (char ch in column.ToUpperInvariant())
        {
            index = index * 26 + (ch - 'A' + 1);
        }
        return Math.Max(1, index) - 1;
    }

    private static string LimitText(string text)
    {
        const int limit = 500_000;
        return text.Length <= limit ? text : text[..limit] + "\r\n\r\n……（内容过长，已截断）";
    }

    /// <summary>列出压缩包内容；不支持 7z / rar（返回 null）。</summary>
    public static List<ArchiveEntryInfo>? ListArchive(string path, out string? error)
    {
        error = null;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension == ".zip")
            {
                using var archive = ZipFile.OpenRead(path);
                // .NET 对未设置 UTF-8 标记的 ZIP 文件名按 UTF-8 解码，中文（GBK）会乱码；
                // 这里直接从中央目录读取原始文件名字节，按正确编码还原。
                List<string>? rawNames = ReadZipEntryNames(path);
                return archive.Entries
                    .Select((entry, index) => new ArchiveEntryInfo(
                        rawNames != null && index < rawNames.Count
                            ? rawNames[index] : entry.FullName,
                        entry.Length, entry.CompressedLength))
                    .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            if (extension is ".tar" or ".tgz" or ".gz" || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var entries = new List<ArchiveEntryInfo>();
                Stream stream = File.OpenRead(path);
                Stream? gzip = null;
                try
                {
                    if (extension is ".tgz" or ".gz"
                        || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        gzip = new GZipStream(stream, CompressionMode.Decompress);
                        stream = gzip;
                    }
                    using var reader = new TarReader(stream);
                    TarEntry? entry;
                    while ((entry = reader.GetNextEntry()) is not null)
                    {
                        entries.Add(new ArchiveEntryInfo(entry.Name, entry.Length, 0));
                    }
                }
                finally
                {
                    gzip?.Dispose();
                    if (gzip == null)
                    {
                        stream.Dispose();
                    }
                }
                return entries;
            }

            var readerOptions = new SharpCompress.Readers.ReaderOptions
            {
                // 兼容中文旧式压缩包（RAR / 7z 等）未使用 Unicode 名称的情况。
                ArchiveEncoding = CreateArchiveEncoding()
            };
            using var sharpArchive = ArchiveFactory.Open(path, readerOptions);
            return sharpArchive.Entries
                .Where(entry => !entry.IsDirectory)
                .Select(entry => new ArchiveEntryInfo(entry.Key ?? "",
                    entry.Size, entry.CompressedSize))
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            error = "读取压缩包失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>读取 ZIP 中央目录里的原始文件名字节，修复中文压缩包的 GBK 乱码。</summary>
    private static List<string>? ReadZipEntryNames(string path)
    {
        const uint EocdSignature = 0x06054b50;
        const uint CentralSignature = 0x02014b50;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 22)
            {
                return null;
            }
            long tailStart = Math.Max(0, stream.Length - (22 + 65535));
            int tailLength = (int)(stream.Length - tailStart);
            var tail = new byte[tailLength];
            stream.Position = tailStart;
            stream.ReadExactly(tail, 0, tailLength);
            int eocd = -1;
            for (int i = tailLength - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) == EocdSignature)
                {
                    eocd = i;
                    break;
                }
            }
            if (eocd < 0)
            {
                return null;
            }
            ushort entryCount = BitConverter.ToUInt16(tail, eocd + 10);
            if (entryCount == ushort.MaxValue)
            {
                return null; // ZIP64：退回系统默认解析
            }
            uint centralSize = BitConverter.ToUInt32(tail, eocd + 12);
            uint centralOffset = BitConverter.ToUInt32(tail, eocd + 16);
            if (centralSize > 64L * 1024 * 1024 || centralOffset >= stream.Length)
            {
                return null;
            }
            stream.Position = centralOffset;
            var names = new List<string>(entryCount);
            var header = new byte[46];
            for (int i = 0; i < entryCount; i++)
            {
                stream.ReadExactly(header, 0, header.Length);
                if (BitConverter.ToUInt32(header, 0) != CentralSignature)
                {
                    return null;
                }
                ushort flags = BitConverter.ToUInt16(header, 8);
                ushort nameLength = BitConverter.ToUInt16(header, 28);
                ushort extraLength = BitConverter.ToUInt16(header, 30);
                ushort commentLength = BitConverter.ToUInt16(header, 32);
                var nameBytes = new byte[nameLength];
                if (nameLength > 0)
                {
                    stream.ReadExactly(nameBytes, 0, nameLength);
                }
                names.Add(DecodeZipName(nameBytes, (flags & 0x0800) != 0));
                int skip = extraLength + commentLength;
                if (skip > 0)
                {
                    stream.Seek(skip, SeekOrigin.Current);
                }
            }
            return names;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ZIP 未设置 UTF-8 标记时，先试 UTF-8，出现替换字符再按 GBK 解码。</summary>
    private static string DecodeZipName(byte[] bytes, bool utf8Flag)
    {
        if (bytes.Length == 0)
        {
            return "";
        }
        if (utf8Flag)
        {
            return Encoding.UTF8.GetString(bytes);
        }
        string utf8 = Encoding.UTF8.GetString(bytes);
        if (!utf8.Contains('\uFFFD'))
        {
            return utf8;
        }
        try
        {
            return Encoding.GetEncoding(936).GetString(bytes);
        }
        catch
        {
            return utf8;
        }
    }

    private static ArchiveEncoding CreateArchiveEncoding()
    {
        try
        {
            return new ArchiveEncoding { Default = Encoding.GetEncoding(936) };
        }
        catch
        {
            return new ArchiveEncoding();
        }
    }

    /// <summary>Markdown 转 HTML：代码块语法高亮 + 每块复制按钮 + 目录（TOC）+ 锚点跳转。</summary>
    public static string MarkdownToHtml(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var body = new StringBuilder();
        var toc = new List<(int Level, string Text, string Id)>();
        int headingIndex = 0;
        bool inCode = false;
        string codeLanguage = "";
        var codeBuffer = new StringBuilder();
        var listItems = new List<string>();
        bool listOrdered = false;
        var paragraph = new List<string>();
        var quoteLines = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }
            body.Append("<p>");
            for (int i = 0; i < paragraph.Count; i++)
            {
                if (i > 0)
                {
                    body.Append("<br/>");
                }
                body.Append(InlineMarkdown(paragraph[i]));
            }
            body.AppendLine("</p>");
            paragraph.Clear();
        }

        void FlushList()
        {
            if (listItems.Count == 0)
            {
                return;
            }
            body.AppendLine(listOrdered ? "<ol>" : "<ul>");
            foreach (string item in listItems)
            {
                body.Append("<li>").Append(InlineMarkdown(item)).AppendLine("</li>");
            }
            body.AppendLine(listOrdered ? "</ol>" : "</ul>");
            listItems.Clear();
        }

        void FlushQuote()
        {
            if (quoteLines.Count == 0)
            {
                return;
            }
            body.AppendLine("<blockquote>");
            foreach (string quote in quoteLines)
            {
                body.Append("<p>").Append(InlineMarkdown(quote)).AppendLine("</p>");
            }
            body.AppendLine("</blockquote>");
            quoteLines.Clear();
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
            FlushQuote();
        }

        foreach (string rawLine in lines)
        {
            string line = rawLine;
            if (inCode)
            {
                string trimmedCode = line.TrimStart();
                if (trimmedCode.StartsWith("```", StringComparison.Ordinal)
                    || trimmedCode.StartsWith("~~~", StringComparison.Ordinal))
                {
                    body.AppendLine(BuildCodeBlock(codeBuffer.ToString(), codeLanguage));
                    inCode = false;
                    codeBuffer.Clear();
                }
                else
                {
                    codeBuffer.AppendLine(line);
                }
                continue;
            }

            string trimmed = line.TrimStart();
            string trimmedEnd = trimmed.TrimEnd();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                FlushAll();
                inCode = true;
                codeLanguage = trimmed.Length > 3 ? trimmed[3..].Trim() : "";
                codeBuffer.Clear();
                continue;
            }
            if (trimmedEnd.Length == 0)
            {
                FlushAll();
                continue;
            }
            if (IsMarkdownRule(trimmedEnd))
            {
                FlushAll();
                body.AppendLine("<hr/>");
                continue;
            }
            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushList();
                quoteLines.Add(trimmed.Length > 1 ? trimmed[1..].TrimStart() : "");
                continue;
            }
            FlushQuote();

            int level = 0;
            while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
            {
                level++;
            }
            if (level > 0 && level < trimmed.Length
                && (trimmed[level] == ' ' || trimmed[level] == '\t'))
            {
                FlushParagraph();
                FlushList();
                string text = trimmed[(level + 1)..].Trim();
                headingIndex++;
                string id = "toc-" + headingIndex;
                body.Append("<h").Append(level).Append(" id=\"").Append(id).Append("\">")
                    .Append(InlineMarkdown(text)).Append("</h").Append(level).AppendLine(">");
                if (level <= 3)
                {
                    toc.Add((level, PlainMarkdownText(text), id));
                }
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal)
                || trimmed.StartsWith("* ", StringComparison.Ordinal)
                || trimmed.StartsWith("+ ", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushQuote();
                if (listOrdered)
                {
                    FlushList();
                }
                listOrdered = false;
                listItems.Add(trimmed[2..]);
                continue;
            }

            int digits = 0;
            while (digits < trimmed.Length && char.IsDigit(trimmed[digits]))
            {
                digits++;
            }
            if (digits > 0 && digits + 1 < trimmed.Length
                && (trimmed[digits] == '.' || trimmed[digits] == ')')
                && trimmed[digits + 1] == ' ')
            {
                FlushParagraph();
                FlushQuote();
                if (!listOrdered)
                {
                    FlushList();
                }
                listOrdered = true;
                listItems.Add(trimmed[(digits + 2)..]);
                continue;
            }

            FlushList();
            paragraph.Add(trimmed);
        }

        if (inCode)
        {
            body.AppendLine(BuildCodeBlock(codeBuffer.ToString(), codeLanguage));
        }
        FlushAll();

        string tocHtml = BuildToc(toc);
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<style>html,body{background:#fff;}html{scroll-behavior:smooth}"
            + "body{font-family:'Microsoft YaHei UI',sans-serif;padding:26px;line-height:1.7;"
            + "max-width:1000px;margin:auto;color:#1e293b}"
            + "h1,h2,h3,h4,h5,h6{color:#0f172a;line-height:1.35;margin:1.35em 0 .6em}"
            + "h1,h2,h3{scroll-margin-top:14px}a{color:#2563eb}"
            + "code{background:#f1f5f9;padding:1px 5px;border-radius:4px}"
            + "pre code{background:transparent;padding:0}"
            + "hr{border:none;border-top:1px solid #e2e8f0;margin:24px 0}"
            + "blockquote{margin:16px 0;padding:10px 16px;border-left:4px solid #93c5fd;"
            + "background:#f8fafc;color:#334155}blockquote p{margin:4px 0}"
            + ".code-block{margin:16px 0;border:1px solid #e2e8f0;border-radius:8px;"
            + "overflow:hidden;background:#f6f8fa}"
            + ".code-head{display:flex;justify-content:space-between;align-items:center;"
            + "padding:6px 10px;background:#eef2f6;border-bottom:1px solid #e2e8f0;"
            + "font-size:12px;color:#64748b}"
            + ".copy-btn{border:1px solid #cbd5e1;background:#fff;color:#334155;border-radius:6px;"
            + "padding:2px 10px;font-size:12px;cursor:pointer}"
            + ".copy-btn:hover{background:#eff6ff;border-color:#2563eb;color:#2563eb}"
            + ".toc{border:1px solid #e2e8f0;background:#f8fafc;border-radius:10px;"
            + "padding:12px 18px;margin:0 0 24px}"
            + ".toc-title{font-weight:700;margin-bottom:6px;color:#0f172a}"
            + ".toc a{display:block;color:#2563eb;text-decoration:none;line-height:1.85;font-size:13.5px}"
            + ".toc a:hover{text-decoration:underline}.toc-l2{padding-left:16px}.toc-l3{padding-left:32px}"
            + SyntaxHighlighter.Css
            + "</style></head><body>" + tocHtml + body
            + CopyCodeScript + "</body></html>";
    }

    /// <summary>代码文件预览页：整页语法高亮（浅色主题）。</summary>
    public static string CodeToHtml(string code, string path)
    {
        string language = SyntaxHighlighter.DetectLanguage(path);
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<style>html,body{background:#fff;}body{margin:0}"
            + SyntaxHighlighter.Css
            + "</style></head><body><pre class=\"code code-file\"><code>"
            + SyntaxHighlighter.Highlight(code, language)
            + "</code></pre></body></html>";
    }

    /// <summary>是否为可高亮的代码文件（用于选择代码预览而不是纯文本预览）。</summary>
    public static bool IsCodeFile(string path)
    {
        return SyntaxHighlighter.DetectLanguage(path) != "plain";
    }

    private static string BuildCodeBlock(string code, string language)
    {
        if (code.EndsWith("\n", StringComparison.Ordinal))
        {
            code = code[..^1];
        }
        string normalized = SyntaxHighlighter.NormalizeLanguage(language);
        string label = string.IsNullOrWhiteSpace(language) || normalized == "plain"
            ? "code" : language.Trim();
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
        return "<div class=\"code-block\"><div class=\"code-head\">"
            + "<span class=\"code-lang\">" + HtmlEscape(label) + "</span>"
            + "<button class=\"copy-btn\" data-code=\"" + encoded + "\">复制</button></div>"
            + "<pre class=\"code\"><code>"
            + SyntaxHighlighter.Highlight(code, normalized)
            + "</code></pre></div>";
    }

    private static string BuildToc(List<(int Level, string Text, string Id)> entries)
    {
        if (entries.Count < 2)
        {
            return "";
        }
        var builder = new StringBuilder();
        builder.AppendLine("<nav class=\"toc\"><div class=\"toc-title\">目录</div>");
        foreach (var entry in entries)
        {
            string cls = entry.Level switch
            {
                1 => "toc-l1",
                2 => "toc-l2",
                _ => "toc-l3"
            };
            builder.Append("<a class=\"").Append(cls).Append("\" href=\"#")
                .Append(entry.Id).Append("\">").Append(HtmlEscape(entry.Text))
                .AppendLine("</a>");
        }
        builder.AppendLine("</nav>");
        return builder.ToString();
    }

    private static string PlainMarkdownText(string text)
    {
        string value = text.Replace("**", "").Replace("__", "").Replace("`", "")
            .Replace("*", "").Replace("_", " ");
        value = Regex.Replace(value, @"\[(.+?)\]\((.+?)\)", "$1");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static bool IsMarkdownRule(string text)
    {
        if (text.Length < 3)
        {
            return false;
        }
        char first = text[0];
        if (first != '-' && first != '*' && first != '_')
        {
            return false;
        }
        foreach (char c in text)
        {
            if (c != first && c != ' ' && c != '\t')
            {
                return false;
            }
        }
        return text.Count(c => c == first) >= 3;
    }

    /// <summary>代码块复制按钮：通过 WebView2 的 postMessage 交给宿主写入剪贴板。</summary>
    private const string CopyCodeScript =
        "<script>"
        + "function decodeB64Utf8(b64){try{var s=atob(b64);var b=new Uint8Array(s.length);"
        + "for(var i=0;i<s.length;i++){b[i]=s.charCodeAt(i);}return new TextDecoder('utf-8').decode(b);}"
        + "catch(e){return '';}}"
        + "document.querySelectorAll('.copy-btn').forEach(function(btn){"
        + "btn.addEventListener('click',function(){"
        + "var text=decodeB64Utf8(btn.getAttribute('data-code'));"
        + "try{if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage({type:'copy',text:text});}"
        + "else if(navigator.clipboard){navigator.clipboard.writeText(text);}}catch(e){}"
        + "var old=btn.textContent;btn.textContent='已复制';"
        + "setTimeout(function(){btn.textContent=old;},1200);});});"
        + "</script>";

    private static string InlineMarkdown(string text)
    {
        string escaped = HtmlEscape(text);
        escaped = Regex.Replace(escaped, @"\*\*(.+?)\*\*", "<b>$1</b>");
        escaped = Regex.Replace(escaped, @"\*(.+?)\*", "<i>$1</i>");
        escaped = Regex.Replace(escaped, "`(.+?)`", "<code>$1</code>");
        escaped = Regex.Replace(escaped, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\">$1</a>");
        return escaped;
    }

    private static string HtmlEscape(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
