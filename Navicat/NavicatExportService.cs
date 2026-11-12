using System.Data;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

internal enum NvExportFormat
{
    CSV,
    TXT,
    JSON,
    XML,
    HTML,
    SQL,
    XLSX
}

internal static class NvExportService
{
    public static bool ExportTableWithDialog(IWin32Window? owner, DataTable table, string suggestedName,
        DbKind kind, bool includeCreate)
    {
        using var dialog = new NvExportDialog(table, suggestedName, kind, includeCreate);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    public static int Export(DataTable table, string path, NvExportFormat format, bool includeHeader = true,
        bool includeCreate = false, DbKind kind = DbKind.MySQL, string tableName = "exported_table")
    {
        switch (format)
        {
            case NvExportFormat.CSV:
                NvDataFile.WriteCsv(table, path, includeHeader);
                break;
            case NvExportFormat.TXT:
                WriteDelimited(table, path, includeHeader, '\t');
                break;
            case NvExportFormat.JSON:
                WriteJson(table, path);
                break;
            case NvExportFormat.XML:
                WriteXml(table, path);
                break;
            case NvExportFormat.HTML:
                WriteHtml(table, path);
                break;
            case NvExportFormat.SQL:
                WriteSql(table, path, includeCreate, kind, tableName);
                break;
            case NvExportFormat.XLSX:
                NvDataFile.WriteXlsx(table, path, tableName);
                break;
        }
        return table.Rows.Count;
    }

    private static void WriteDelimited(DataTable table, string path, bool includeHeader, char delimiter)
    {
        var sb = new StringBuilder();
        if (includeHeader)
        {
            sb.AppendLine(string.Join(delimiter, table.Columns.Cast<DataColumn>()
                .Select(c => Csv(c.ColumnName, delimiter))));
        }
        foreach (DataRow row in table.Rows)
        {
            sb.AppendLine(string.Join(delimiter, row.ItemArray
                .Select(v => Csv(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "", delimiter))));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Csv(string text, char delimiter)
    {
        if (text.Contains(delimiter) || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return text;
    }

    private static void WriteJson(DataTable table, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (int r = 0; r < table.Rows.Count; r++)
        {
            sb.Append("  {");
            for (int c = 0; c < table.Columns.Count; c++)
            {
                if (c > 0)
                {
                    sb.Append(", ");
                }
                object? value = table.Rows[r][c];
                sb.Append('"').Append(JsonEscape(table.Columns[c].ColumnName)).Append("\": ");
                AppendJsonValue(sb, value);
            }
            sb.Append('}');
            sb.AppendLine(r == table.Rows.Count - 1 ? "" : ",");
        }
        sb.AppendLine("]");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void AppendJsonValue(StringBuilder sb, object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            sb.Append("null");
            return;
        }
        switch (value)
        {
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                sb.Append('"').Append(dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('"');
                break;
            case byte[] bytes:
                sb.Append('"').Append(Convert.ToBase64String(bytes)).Append('"');
                break;
            default:
                sb.Append('"').Append(JsonEscape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""))
                    .Append('"');
                break;
        }
    }

    private static string JsonEscape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    private static void WriteXml(DataTable table, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<rows>");
        foreach (DataRow row in table.Rows)
        {
            sb.AppendLine("  <row>");
            foreach (DataColumn column in table.Columns)
            {
                object? value = row[column];
                sb.Append("    <col name=\"").Append(NvDataFile.XmlEscape(column.ColumnName)).Append("\">");
                if (value != null && value != DBNull.Value)
                {
                    string text = value is byte[] bytes
                        ? Convert.ToBase64String(bytes)
                        : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    sb.Append(NvDataFile.XmlEscape(text));
                }
                sb.AppendLine("</col>");
            }
            sb.AppendLine("  </row>");
        }
        sb.AppendLine("</rows>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteHtml(DataTable table, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>查询结果</title><style>");
        sb.AppendLine("body{font-family:'Microsoft YaHei UI',Arial;background:#f3f6fa;padding:24px;color:#1f2a36}");
        sb.AppendLine("h2{font-size:18px;margin:0 0 12px}table{border-collapse:collapse;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.12)}");
        sb.AppendLine("th{background:#eef4fa;border:1px solid #ccd6e0;padding:6px 12px;font-size:12px;text-align:left;white-space:nowrap}");
        sb.AppendLine("td{border:1px solid #e2e8ee;padding:5px 12px;font-size:12px;white-space:pre-wrap}");
        sb.AppendLine("tr:nth-child(even) td{background:#f9fbfd}.meta{color:#6c7a89;font-size:12px;margin-bottom:10px}");
        sb.AppendLine("</style></head><body>");
        sb.Append("<h2>查询结果</h2><div class=\"meta\">").Append(table.Rows.Count).Append(" 行 × ")
            .Append(table.Columns.Count).Append(" 列 · 导出时间 ")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("</div>");
        sb.AppendLine("<table><thead><tr>");
        foreach (DataColumn column in table.Columns)
        {
            sb.Append("<th>").Append(NvDataFile.XmlEscape(column.ColumnName)).Append("</th>");
        }
        sb.AppendLine("</tr></thead><tbody>");
        foreach (DataRow row in table.Rows)
        {
            sb.Append("<tr>");
            foreach (DataColumn column in table.Columns)
            {
                object? value = row[column];
                string text = value == null || value == DBNull.Value
                    ? "<span style=\"color:#a0aab5\">NULL</span>"
                    : NvDataFile.XmlEscape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                sb.Append("<td>").Append(text).Append("</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteSql(DataTable table, string path, bool includeCreate, DbKind kind, string tableName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- 由 MiniMonitor Navicat 复刻版导出");
        sb.AppendLine("-- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        string safeTable = tableName;
        if (includeCreate)
        {
            sb.Append("CREATE TABLE ").Append(QuoteName(safeTable, kind)).Append(" (");
            sb.AppendLine();
            var cols = new List<string>();
            foreach (DataColumn column in table.Columns)
            {
                cols.Add("  " + QuoteName(column.ColumnName, kind) + " " + GuessType(column, kind));
            }
            sb.AppendLine(string.Join("," + Environment.NewLine, cols));
            sb.AppendLine(");");
            sb.AppendLine();
        }
        foreach (DataRow row in table.Rows)
        {
            sb.Append("INSERT INTO ").Append(QuoteName(safeTable, kind)).Append(" (")
                .Append(string.Join(", ", table.Columns.Cast<DataColumn>().Select(c => QuoteName(c.ColumnName, kind))))
                .Append(") VALUES (")
                .Append(string.Join(", ", row.ItemArray.Select(v => NvSqlUtil.Literal(v, kind))))
                .AppendLine(");");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string QuoteName(string name, DbKind kind)
    {
        return kind switch
        {
            DbKind.MySQL => "`" + name.Replace("`", "``") + "`",
            DbKind.SqlServer => "[" + name.Replace("]", "]]") + "]",
            _ => "\"" + name.Replace("\"", "\"\"") + "\""
        };
    }

    private static string GuessType(DataColumn column, DbKind kind)
    {
        Type type = column.DataType;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            return kind == DbKind.Oracle ? "NUMBER(19)" : "BIGINT";
        }
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
        {
            return kind switch
            {
                DbKind.Oracle => "NUMBER(18,4)",
                DbKind.SqlServer => "DECIMAL(18,4)",
                DbKind.PostgreSQL => "NUMERIC(18,4)",
                _ => "DECIMAL(18,4)"
            };
        }
        if (type == typeof(bool))
        {
            return kind == DbKind.SqlServer ? "BIT" : kind == DbKind.PostgreSQL ? "BOOLEAN" : "TINYINT(1)";
        }
        if (type == typeof(DateTime))
        {
            return kind == DbKind.Oracle ? "TIMESTAMP" : "DATETIME";
        }
        if (type == typeof(byte[]))
        {
            return kind switch
            {
                DbKind.PostgreSQL => "BYTEA",
                DbKind.SqlServer => "VARBINARY(MAX)",
                DbKind.Oracle => "BLOB",
                _ => "LONGBLOB"
            };
        }
        return kind switch
        {
            DbKind.PostgreSQL => "TEXT",
            DbKind.SqlServer => "NVARCHAR(MAX)",
            DbKind.Oracle => "CLOB",
            _ => "LONGTEXT"
        };
    }
}

/// <summary>导出向导。</summary>
internal sealed class NvExportDialog : Form
{
    private readonly DataTable _table;
    private readonly DbKind _kind;
    private readonly ComboBox _format;
    private readonly TextBox _path;
    private readonly CheckBox _header;
    private readonly CheckBox _create;
    private readonly CheckBox _openFolder;
    private readonly Label _summary;
    private readonly Button _export;
    private string _suggested;

    public NvExportDialog(DataTable table, string suggestedName, DbKind kind, bool includeCreate)
    {
        _table = table;
        _kind = kind;
        _suggested = MakeSafeName(suggestedName);

        Text = "导出向导";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 430);
        MinimumSize = new Size(560, 400);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = NvTheme.HeaderBack };
        header.Controls.Add(new Label
        {
            Text = "导出数据",
            Location = new Point(20, 10),
            AutoSize = true,
            Font = NvTheme.Ui(13f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        header.Controls.Add(new Label
        {
            Text = "选择导出格式与目标文件，支持 CSV / Excel / JSON / XML / HTML / SQL。",
            Location = new Point(22, 38),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        header.Controls.Add(new PictureBox
        {
            Image = NvIcons.Get(NvIcon.Export, 30),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(560, 16),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 0) };
        _summary = new Label
        {
            Text = $"待导出：{_table.Rows.Count} 行 × {_table.Columns.Count} 列",
            Location = new Point(20, 16),
            AutoSize = true,
            ForeColor = NvTheme.Text,
            Font = NvTheme.Ui(9.5f, FontStyle.Bold)
        };
        body.Controls.Add(_summary);
        body.Controls.Add(new Label
        {
            Text = "导出格式",
            Location = new Point(20, 58),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _format = new ComboBox
        {
            Location = new Point(110, 54),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        _format.Items.AddRange(new object[]
        {
            "CSV (逗号分隔)", "TXT (制表符分隔)", "Excel 工作簿 (*.xlsx)", "JSON", "XML", "HTML", "SQL 脚本"
        });
        _format.SelectedIndex = 0;
        body.Controls.Add(_format);

        body.Controls.Add(new Label
        {
            Text = "目标文件",
            Location = new Point(20, 102),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _path = new TextBox
        {
            Location = new Point(110, 98),
            Width = 380,
            Font = NvTheme.Ui(9.2f),
            Text = BuildDefaultPath(NvSettingsStore.Current.ExportFolder, _suggested, "csv")
        };
        body.Controls.Add(_path);
        var browse = NvTheme.FlatButton("浏览…", null, null, 76);
        browse.Location = new Point(500, 97);
        browse.Click += (_, _) => Browse();
        body.Controls.Add(browse);

        _header = new CheckBox
        {
            Text = "包含列标题",
            Location = new Point(110, 138),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _create = new CheckBox
        {
            Text = "包含 CREATE TABLE 语句（SQL）",
            Location = new Point(260, 138),
            AutoSize = true,
            Checked = includeCreate,
            ForeColor = NvTheme.Text
        };
        _openFolder = new CheckBox
        {
            Text = "导出完成后打开所在文件夹",
            Location = new Point(110, 168),
            AutoSize = true,
            Checked = false,
            ForeColor = NvTheme.Text
        };
        body.Controls.AddRange(new Control[] { _header, _create, _openFolder });
        body.Controls.Add(new Label
        {
            Text = "提示：Excel 导出使用标准 XLSX 格式，可直接用 Excel / WPS 打开；SQL 导出会按当前数据库类型生成转义后的 INSERT 语句。",
            Location = new Point(20, 206),
            Size = new Size(560, 44),
            ForeColor = NvTheme.Muted
        });
        _format.SelectedIndexChanged += (_, _) => UpdateExtension();
        _path.TextChanged += (_, _) => UpdateExtension(false);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = NvTheme.Background };
        _export = NvTheme.FlatButton("开始导出", NvTheme.Accent, Color.White, 104);
        _export.Location = new Point(400, 12);
        _export.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _export.Click += (_, _) => DoExport();
        var cancel = NvTheme.FlatButton("取消", null, null, 88);
        cancel.Location = new Point(512, 12);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cancel.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _export, cancel });

        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(header);
        AcceptButton = _export;
        CancelButton = cancel;
    }

    private static string MakeSafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "export" : name;
    }

    private static string BuildDefaultPath(string folder, string name, string ext)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        return Path.Combine(folder, name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + ext);
    }

    private NvExportFormat SelectedFormat => _format.SelectedIndex switch
    {
        0 => NvExportFormat.CSV,
        1 => NvExportFormat.TXT,
        2 => NvExportFormat.XLSX,
        3 => NvExportFormat.JSON,
        4 => NvExportFormat.XML,
        5 => NvExportFormat.HTML,
        _ => NvExportFormat.SQL
    };

    private string Extension => SelectedFormat switch
    {
        NvExportFormat.CSV => "csv",
        NvExportFormat.TXT => "txt",
        NvExportFormat.XLSX => "xlsx",
        NvExportFormat.JSON => "json",
        NvExportFormat.XML => "xml",
        NvExportFormat.HTML => "html",
        _ => "sql"
    };

    private void UpdateExtension(bool resetPath = true)
    {
        if (resetPath)
        {
            _path.Text = BuildDefaultPath(NvSettingsStore.Current.ExportFolder, _suggested, Extension);
        }
        _create.Enabled = SelectedFormat == NvExportFormat.SQL;
        _header.Enabled = SelectedFormat is NvExportFormat.CSV or NvExportFormat.TXT;
    }

    private void Browse()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出到",
            Filter = SelectedFormat switch
            {
                NvExportFormat.CSV => "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                NvExportFormat.TXT => "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                NvExportFormat.XLSX => "Excel 工作簿 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
                NvExportFormat.JSON => "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                NvExportFormat.XML => "XML 文件 (*.xml)|*.xml|所有文件 (*.*)|*.*",
                NvExportFormat.HTML => "HTML 文件 (*.html)|*.html|所有文件 (*.*)|*.*",
                _ => "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*"
            },
            FileName = Path.GetFileName(_path.Text)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _path.Text = dialog.FileName;
        }
    }

    private async void DoExport()
    {
        string path = _path.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            NvPrompt.Error(this, "导出", "请填写目标文件路径。");
            return;
        }
        try
        {
            string folder = Path.GetDirectoryName(path) ?? "";
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _export.Enabled = false;
            _export.Text = "导出中…";
            var format = SelectedFormat;
            bool header = _header.Checked;
            bool create = _create.Checked;
            int rows = await Task.Run(() =>
                NvExportService.Export(_table, path, format, header, create, _kind, _suggested));
            _export.Enabled = true;
            _export.Text = "开始导出";
            NvSettingsStore.Current.ExportFolder = Path.GetDirectoryName(path) ?? NvSettingsStore.Current.ExportFolder;
            NvSettingsStore.Save();
            NvPrompt.Info(this, "导出完成", $"已导出 {rows} 行到：\n{path}");
            if (_openFolder.Checked)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                catch
                {
                    // ignore
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _export.Enabled = true;
            _export.Text = "开始导出";
            NvLog.Error(ex, "工具异常：导出失败");
            NvPrompt.Error(this, "导出失败", ex.Message);
        }
    }
}
