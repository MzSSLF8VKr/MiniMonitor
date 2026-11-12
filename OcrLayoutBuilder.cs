using System.Text;

namespace MiniMonitor;

public sealed record OcrTextBlock(
    string Text, double X, double Y, double Width, double Height);

/// <summary>
/// 根据 OCR 单词边界重建接近原图的文本排版：
/// 合并同一视觉行的文字块，用等宽字符 + 空格还原缩进、分栏和表格列。
/// 独立于 WinRT，便于单元测试。
/// </summary>
internal static class OcrLayoutBuilder
{
    public static string Build(IReadOnlyList<IReadOnlyList<OcrTextBlock>> lines)
    {
        var rows = new List<VisualRow>();
        double allTop = double.MaxValue;
        double allBottom = double.MinValue;
        double allLeft = double.MaxValue;

        foreach (var line in lines)
        {
            var words = new List<WordBox>();
            foreach (var block in line)
            {
                string text = block.Text?.Trim() ?? "";
                if (text.Length == 0 || block.Width <= 0 || block.Height <= 0)
                {
                    continue;
                }
                words.Add(new WordBox
                {
                    Text = text,
                    X = block.X,
                    Y = block.Y,
                    Width = block.Width,
                    Height = block.Height
                });
            }
            if (words.Count == 0)
            {
                continue;
            }

            double top = words.Min(word => word.Y);
            double bottom = words.Max(word => word.Y + word.Height);
            double left = words.Min(word => word.X);
            allTop = Math.Min(allTop, top);
            allBottom = Math.Max(allBottom, bottom);
            allLeft = Math.Min(allLeft, left);

            VisualRow? bestRow = null;
            double bestScore = 0;
            foreach (var row in rows)
            {
                double overlap = Math.Min(bottom, row.Bottom) - Math.Max(top, row.Top);
                double minHeight = Math.Min(bottom - top, row.Height);
                if (minHeight <= 0)
                {
                    continue;
                }
                double overlapRatio = Math.Max(0, overlap) / minHeight;
                double centerDistance = Math.Abs(
                    (top + bottom) / 2.0 - (row.Top + row.Bottom) / 2.0);
                bool sameLine = overlapRatio >= 0.3
                    || centerDistance <= Math.Max(bottom - top, row.Height) * 0.45;
                if (sameLine && (bestRow == null || overlapRatio > bestScore))
                {
                    bestScore = overlapRatio;
                    bestRow = row;
                }
            }

            if (bestRow == null)
            {
                bestRow = new VisualRow { Top = top, Bottom = bottom };
                rows.Add(bestRow);
            }
            bestRow.Words.AddRange(words);
            bestRow.Top = Math.Min(bestRow.Top, top);
            bestRow.Bottom = Math.Max(bestRow.Bottom, bottom);
        }

        if (rows.Count == 0)
        {
            return "";
        }

        rows.Sort((a, b) => a.Top.CompareTo(b.Top));
        var allWords = rows.SelectMany(row => row.Words).ToList();
        double medianHeight = Median(allWords.Select(word => word.Height).ToList());
        if (medianHeight <= 0)
        {
            medianHeight = Math.Max(1, allBottom - allTop);
        }

        var widthRatios = new List<double>();
        foreach (var word in allWords)
        {
            int cells = DisplayCells(word.Text);
            if (cells >= 2)
            {
                widthRatios.Add(word.Width / cells);
            }
        }
        double cellWidth = widthRatios.Count >= 2
            ? Median(widthRatios)
            : medianHeight * 0.55;
        if (cellWidth < 3)
        {
            cellWidth = Math.Max(3, medianHeight * 0.35);
        }
        cellWidth = Math.Min(cellWidth, medianHeight * 1.3);
        if (cellWidth <= 0)
        {
            cellWidth = 8;
        }

        double leftMargin = allLeft;
        var columnAnchors = new List<ColumnAnchor>();

        int ResolveTargetColumn(double x)
        {
            double tolerance = Math.Max(3.0, cellWidth * 0.65);
            // 同一列的单词起始坐标通常有 1~几个像素误差；吸附到已有列锚点，
            // 避免相近的 x 被四舍五入到不同列而破坏表格对齐。
            foreach (var anchor in columnAnchors)
            {
                if (Math.Abs(x - anchor.X) <= tolerance)
                {
                    return anchor.Column;
                }
            }
            int column = Math.Max(0, (int)Math.Round((x - leftMargin) / cellWidth));
            columnAnchors.Add(new ColumnAnchor { X = x, Column = column });
            return column;
        }

        var builder = new StringBuilder();
        double previousBottom = double.MinValue;
        foreach (var row in rows)
        {
            if (previousBottom != double.MinValue)
            {
                double gap = row.Top - previousBottom;
                if (gap > medianHeight * 1.6)
                {
                    builder.AppendLine();
                    if (gap > medianHeight * 3.2)
                    {
                        builder.AppendLine();
                    }
                }
            }
            previousBottom = row.Bottom;

            var words = row.Words.OrderBy(word => word.X).ToList();
            var line = new StringBuilder();
            int currentColumn = 0;
            WordBox? previous = null;
            foreach (var word in words)
            {
                int targetColumn = ResolveTargetColumn(word.X);
                if (previous == null)
                {
                    if (targetColumn > currentColumn)
                    {
                        line.Append(' ', targetColumn - currentColumn);
                        currentColumn = targetColumn;
                    }
                }
                else
                {
                    double gapPixels = word.X - (previous.X + previous.Width);
                    int minColumn = currentColumn + (gapPixels > cellWidth * 0.15 ? 1 : 0);
                    targetColumn = Math.Max(targetColumn, minColumn);
                    if (targetColumn > currentColumn)
                    {
                        line.Append(' ', targetColumn - currentColumn);
                        currentColumn = targetColumn;
                    }
                }

                line.Append(word.Text);
                currentColumn += DisplayCells(word.Text);
                previous = word;
            }
            builder.AppendLine(line.ToString().TrimEnd());
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2.0
            : values[middle];
    }

    /// <summary>等宽字体下的显示宽度：中日韩全角字符按 2 格计算。</summary>
    private static int DisplayCells(string text)
    {
        int cells = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length
                && char.IsLowSurrogate(text[i + 1]))
            {
                cells += 2;
                i++;
                continue;
            }
            cells += IsWideChar(c) ? 2 : 1;
        }
        return Math.Max(1, cells);
    }

    private static bool IsWideChar(char c)
    {
        return c is >= '\u1100' and <= '\u115F'
            or >= '\u2E80' and <= '\uA4CF'
            or >= '\uAC00' and <= '\uD7A3'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\uFE10' and <= '\uFE19'
            or >= '\uFE30' and <= '\uFE6F'
            or >= '\uFF00' and <= '\uFF60'
            or >= '\uFFE0' and <= '\uFFE6';
    }

    private sealed class WordBox
    {
        public string Text { get; init; } = "";
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
    }

    private sealed class VisualRow
    {
        public List<WordBox> Words { get; } = new();
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Height => Bottom - Top;
    }

    private sealed class ColumnAnchor
    {
        public double X { get; init; }
        public int Column { get; init; }
    }
}
