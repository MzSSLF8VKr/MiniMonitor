using System.Text;
using System.Text.RegularExpressions;

namespace MiniMonitor.Music;

public sealed class LrcLine
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = "";
    public string Translation { get; set; } = "";
}

/// <summary>LRC 歌词解析与定位。只读取本地 .lrc 或标签内嵌歌词，不访问网络。</summary>
public sealed class LrcDocument
{
    private static readonly Regex TimeTag = new(
        @"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled);

    private static readonly Regex OffsetTag = new(
        @"\[offset:\s*([+-]?\d+)\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool _encodingRegistered;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string By { get; private set; } = "";
    public string RawText { get; private set; } = "";
    public List<LrcLine> Lines { get; } = new();

    public bool HasTimestamps => Lines.Count > 0;

    public static string? FindLyricsFile(string audioPath)
    {
        try
        {
            string directory = Path.GetDirectoryName(audioPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(audioPath);
            foreach (string candidate in new[]
                     {
                         Path.Combine(directory, name + ".lrc"),
                         Path.Combine(directory, name + ".LRC")
                     })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // 忽略路径异常
        }
        return null;
    }

    public static LrcDocument? LoadForTrack(MusicTrack track)
    {
        string? path = FindLyricsFile(track.Path);
        if (path != null)
        {
            try
            {
                string text = ReadText(path);
                return Parse(text);
            }
            catch
            {
                // 继续尝试内嵌歌词
            }
        }
        try
        {
            using var file = TagLib.File.Create(track.Path);
            string lyrics = file.Tag?.Lyrics ?? "";
            if (!string.IsNullOrWhiteSpace(lyrics))
            {
                return Parse(lyrics);
            }
        }
        catch
        {
            // 无内嵌歌词
        }
        return null;
    }

    private static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
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
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch
        {
            // 中文 LRC 常见 GB18030 编码
        }
        try
        {
            if (!_encodingRegistered)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _encodingRegistered = true;
            }
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
        catch
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public static LrcDocument Parse(string content)
    {
        var document = new LrcDocument { RawText = content ?? "" };
        if (string.IsNullOrWhiteSpace(content))
        {
            return document;
        }
        int offsetMs = 0;
        var match = OffsetTag.Match(content);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int parsedOffset))
        {
            offsetMs = parsedOffset;
        }

        var entries = new List<(double Seconds, string Text)>();
        foreach (string rawLine in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (line.StartsWith("[ti:", StringComparison.OrdinalIgnoreCase))
            {
                document.Title = ExtractTagValue(line);
                continue;
            }
            if (line.StartsWith("[ar:", StringComparison.OrdinalIgnoreCase))
            {
                document.Artist = ExtractTagValue(line);
                continue;
            }
            if (line.StartsWith("[al:", StringComparison.OrdinalIgnoreCase))
            {
                document.Album = ExtractTagValue(line);
                continue;
            }
            if (line.StartsWith("[by:", StringComparison.OrdinalIgnoreCase))
            {
                document.By = ExtractTagValue(line);
                continue;
            }
            var tags = TimeTag.Matches(line);
            if (tags.Count == 0)
            {
                continue;
            }
            string text = TimeTag.Replace(line, "").Trim();
            foreach (Match tag in tags)
            {
                int minutes = int.Parse(tag.Groups[1].Value);
                int seconds = int.Parse(tag.Groups[2].Value);
                int milliseconds = 0;
                if (tag.Groups[3].Success)
                {
                    string value = tag.Groups[3].Value;
                    milliseconds = value.Length switch
                    {
                        1 => int.Parse(value) * 100,
                        2 => int.Parse(value) * 10,
                        _ => int.Parse(value[..3])
                    };
                }
                double total = minutes * 60 + seconds + milliseconds / 1000.0 + offsetMs / 1000.0;
                if (total >= 0)
                {
                    entries.Add((total, text));
                }
            }
        }

        foreach (var group in entries.GroupBy(e => Math.Round(e.Seconds, 2)).OrderBy(g => g.Key))
        {
            var texts = group.Select(g => g.Text).Where(t => t.Length > 0).ToList();
            if (texts.Count == 0)
            {
                continue;
            }
            var line = new LrcLine
            {
                Time = TimeSpan.FromSeconds(group.Key),
                Text = texts[0]
            };
            if (texts.Count > 1)
            {
                line.Translation = texts[1];
            }
            document.Lines.Add(line);
        }
        return document;
    }

    private static string ExtractTagValue(string line)
    {
        int close = line.IndexOf(']');
        return close > 4 ? line[4..close].Trim() : "";
    }

    /// <summary>返回当前时间对应的歌词行索引；-1 表示尚未开始。</summary>
    public int IndexAt(TimeSpan position)
    {
        if (Lines.Count == 0)
        {
            return -1;
        }
        int low = 0;
        int high = Lines.Count - 1;
        int result = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (Lines[middle].Time <= position)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return result;
    }
}
