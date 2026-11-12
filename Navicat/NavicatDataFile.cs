using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace MiniMonitor.Navicat;

/// <summary>CSV / XLSX 读写（不依赖 Office，使用内置 Zip + XML 实现）。</summary>
internal static class NvDataFile
{
    public static DataTable ToDataTable(QueryResultSet result, int maxRows = int.MaxValue)
    {
        var table = new DataTable();
        foreach (string column in result.Columns)
        {
            table.Columns.Add(column, typeof(object));
        }
        int count = 0;
        foreach (var row in result.Rows)
        {
            if (count++ >= maxRows)
            {
                break;
            }
            var values = new object?[table.Columns.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = i < row.Length ? row[i] ?? DBNull.Value : DBNull.Value;
            }
            table.Rows.Add(values);
        }
        return table;
    }

    public static void WriteCsv(DataTable table, string path, bool includeHeader = true, char delimiter = ',')
    {
        var sb = new StringBuilder();
        if (includeHeader)
        {
            sb.AppendLine(string.Join(delimiter, table.Columns.Cast<DataColumn>().Select(c => CsvCell(c.ColumnName, delimiter))));
        }
        foreach (DataRow row in table.Rows)
        {
            sb.AppendLine(string.Join(delimiter, row.ItemArray.Select(v => CsvCell(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "", delimiter))));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string CsvCell(string text, char delimiter)
    {
        if (text.Contains(delimiter) || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return text;
    }

    public static DataTable ReadCsv(string path, char? delimiter = null, bool firstRowHeader = true)
    {
        string text = ReadTextWithEncoding(path);
        char sep = delimiter ?? DetectDelimiter(text);
        var table = new DataTable();
        var rows = ParseCsv(text, sep);
        if (rows.Count == 0)
        {
            return table;
        }
        int columnCount = rows.Max(r => r.Count);
        int dataStart = firstRowHeader ? 1 : 0;
        for (int i = 0; i < columnCount; i++)
        {
            string name = firstRowHeader && i < rows[0].Count && !string.IsNullOrWhiteSpace(rows[0][i])
                ? rows[0][i]
                : "Column" + (i + 1);
            string unique = name;
            int suffix = 1;
            while (table.Columns.Contains(unique))
            {
                unique = name + "_" + suffix++;
            }
            table.Columns.Add(unique, typeof(string));
        }
        for (int r = dataStart; r < rows.Count; r++)
        {
            var values = new object?[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                values[c] = c < rows[r].Count ? rows[r][c] : "";
            }
            table.Rows.Add(values);
        }
        return table;
    }

    private static string ReadTextWithEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(936).GetString(bytes);
            }
            catch
            {
                return Encoding.Default.GetString(bytes);
            }
        }
    }

    private static char DetectDelimiter(string text)
    {
        string first = text.Split('\n').FirstOrDefault() ?? "";
        int comma = first.Count(c => c == ',');
        int semicolon = first.Count(c => c == ';');
        int tab = first.Count(c => c == '\t');
        if (tab > comma && tab > semicolon)
        {
            return '\t';
        }
        if (semicolon > comma)
        {
            return ';';
        }
        return ',';
    }

    private static List<List<string>> ParseCsv(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }
                continue;
            }
            if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\r')
            {
                // skip
            }
            else if (c == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(x => x.Length > 0) || row.Count > 1)
                {
                    rows.Add(row);
                }
                row = new List<string>();
            }
            else
            {
                cell.Append(c);
            }
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }

    // ================= XLSX 写入 =================

    public static void WriteXlsx(DataTable table, string path, string sheetName = "Sheet1")
    {
        string safeSheet = SanitizeSheetName(sheetName);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        AddEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>");

        AddEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        AddEntry(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"" + XmlEscape(safeSheet) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

        AddEntry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>");

        AddEntry(zip, "xl/styles.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"1\"><font><sz val=\"10\"/><name val=\"Microsoft YaHei UI\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
            "</styleSheet>");

        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        int rowIndex = 1;
        sheet.Append("<row r=\"").Append(rowIndex).Append("\">");
        for (int c = 0; c < table.Columns.Count; c++)
        {
            AppendCell(sheet, rowIndex, c, table.Columns[c].ColumnName);
        }
        sheet.Append("</row>");

        foreach (DataRow row in table.Rows)
        {
            rowIndex++;
            sheet.Append("<row r=\"").Append(rowIndex).Append("\">");
            for (int c = 0; c < table.Columns.Count; c++)
            {
                object? value = row[c];
                if (value == null || value == DBNull.Value)
                {
                    continue;
                }
                if (value is byte[] bytes)
                {
                    AppendCell(sheet, rowIndex, c, "0x" + Convert.ToHexString(bytes));
                }
                else if (value is sbyte or byte or short or ushort or int or uint or long or ulong
                    or float or double or decimal)
                {
                    AppendNumberCell(sheet, rowIndex, c, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0");
                }
                else if (value is bool b)
                {
                    AppendCell(sheet, rowIndex, c, b ? "TRUE" : "FALSE");
                }
                else if (value is DateTime dt)
                {
                    AppendCell(sheet, rowIndex, c, dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                }
                else
                {
                    AppendCell(sheet, rowIndex, c, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                }
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");
        AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
    }

    private static void AppendCell(StringBuilder sb, int row, int column, string text)
    {
        sb.Append("<c r=\"").Append(CellRef(row, column)).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
            .Append(XmlEscape(text)).Append("</t></is></c>");
    }

    private static void AppendNumberCell(StringBuilder sb, int row, int column, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            AppendCell(sb, row, column, text);
            return;
        }
        sb.Append("<c r=\"").Append(CellRef(row, column)).Append("\"><v>").Append(text).Append("</v></c>");
    }

    public static string CellRef(int row, int column)
    {
        var sb = new StringBuilder();
        int value = column;
        do
        {
            sb.Insert(0, (char)('A' + value % 26));
            value = value / 26 - 1;
        }
        while (value >= 0);
        return sb.ToString() + row;
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string SanitizeSheetName(string name)
    {
        foreach (char c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
        {
            name = name.Replace(c, '_');
        }
        name = name.Trim('\'');
        if (name.Length > 31)
        {
            name = name[..31];
        }
        return string.IsNullOrWhiteSpace(name) ? "Sheet1" : name;
    }

    // ================= XLSX 读取 =================

    public static DataTable ReadXlsx(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var shared = LoadSharedStrings(zip);
        var sheetEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("不是有效的 XLSX 文件：找不到工作表。");

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var sheetStream = sheetEntry.Open();
        var doc = XDocument.Load(sheetStream);
        var table = new DataTable();
        var rows = doc.Descendants(ns + "row").ToList();
        if (rows.Count == 0)
        {
            return table;
        }

        int maxColumn = 0;
        var parsed = new List<(int Row, SortedDictionary<int, string> Cells)>();
        int rowIndex = 0;
        foreach (var row in rows)
        {
            rowIndex++;
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                string reference = (string?)cell.Attribute("r") ?? "";
                int column = ColumnIndex(reference);
                string type = (string?)cell.Attribute("t") ?? "";
                string value;
                if (type == "inlineStr")
                {
                    value = cell.Element(ns + "is")?.Element(ns + "t")?.Value ?? "";
                }
                else
                {
                    string raw = cell.Element(ns + "v")?.Value ?? "";
                    value = type == "s" && int.TryParse(raw, out int index)
                        ? (index >= 0 && index < shared.Count ? shared[index] : "")
                        : raw;
                }
                cells[column] = value;
                maxColumn = Math.Max(maxColumn, column);
            }
            parsed.Add((rowIndex, cells));
        }

        for (int c = 0; c <= maxColumn; c++)
        {
            table.Columns.Add("Column" + (c + 1), typeof(string));
        }
        foreach (var item in parsed)
        {
            var values = new object?[maxColumn + 1];
            for (int c = 0; c <= maxColumn; c++)
            {
                values[c] = item.Cells.TryGetValue(c, out string? value) ? value : "";
            }
            table.Rows.Add(values);
        }
        return table;
    }

    private static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            return list;
        }
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        foreach (var item in doc.Descendants(ns + "si"))
        {
            list.Add(string.Concat(item.Descendants(ns + "t").Select(t => t.Value)));
        }
        return list;
    }

    private static int ColumnIndex(string reference)
    {
        int result = 0;
        foreach (char c in reference)
        {
            if (char.IsLetter(c))
            {
                result = result * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            }
            else
            {
                break;
            }
        }
        return Math.Max(0, result - 1);
    }

    public static string XmlEscape(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}
