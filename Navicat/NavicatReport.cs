using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>简单报表：执行 SQL、预览结果、自动汇总、导出与打印。</summary>
internal sealed class NvReportForm : Form
{
    private readonly DbSession _session;
    private readonly NvSqlEditor _editor;
    private readonly DataGridView _grid;
    private readonly Label _summary;
    private readonly TextBox _title;
    private readonly CheckBox _autoSum;
    private DataTable? _data;
    private int _printRow;
    private string _printTitle = "";

    public NvReportForm(DbSession session)
    {
        _session = session;
        Text = "报表 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(860, 600);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("报表", session.Profile.Name + " · " + session.Kind.ShortName()
            + " · " + session.CurrentDatabase, NvIcon.Report);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = NvTheme.Panel };
        var run = NvTheme.FlatButton("执行查询", NvTheme.Accent, Color.White, 100);
        run.Location = new Point(10, 7);
        run.Click += async (_, _) => await ExecuteAsync();
        var exportHtml = NvTheme.FlatButton("导出 HTML", null, null, 100);
        exportHtml.Location = new Point(118, 7);
        exportHtml.Click += (_, _) => Export("HTML");
        var exportCsv = NvTheme.FlatButton("导出 CSV", null, null, 92);
        exportCsv.Location = new Point(226, 7);
        exportCsv.Click += (_, _) => Export("CSV");
        var exportXlsx = NvTheme.FlatButton("导出 Excel", null, null, 100);
        exportXlsx.Location = new Point(326, 7);
        exportXlsx.Click += (_, _) => Export("XLSX");
        var print = NvTheme.FlatButton("打印预览", null, null, 100);
        print.Location = new Point(434, 7);
        print.Click += (_, _) => PrintReport();
        toolbar.Controls.AddRange(new Control[] { run, exportHtml, exportCsv, exportXlsx, print });
        toolbar.Controls.Add(new Label
        {
            Text = "报表标题",
            Location = new Point(560, 14),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _title = new TextBox
        {
            Location = new Point(628, 10),
            Width = 220,
            Font = NvTheme.Ui(9.2f),
            Text = "数据报表"
        };
        toolbar.Controls.Add(_title);
        _autoSum = new CheckBox
        {
            Text = "自动汇总数值列",
            Location = new Point(862, 14),
            AutoSize = true,
            Checked = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = NvTheme.Text
        };
        toolbar.Controls.Add(_autoSum);
        _autoSum.CheckedChanged += (_, _) => UpdateSummary();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var editorPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 0) };
        editorPanel.Controls.Add(new Label
        {
            Text = "报表 SQL",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _editor = new NvSqlEditor { Dock = DockStyle.Fill, Text = "SELECT * FROM " };
        _editor.InnerBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                await ExecuteAsync();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        editorPanel.Controls.Add(_editor);
        editorPanel.Controls.SetChildIndex(_editor, 0);
        split.Panel1.Controls.Add(editorPanel);

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 8) };
        gridPanel.Controls.Add(new Label
        {
            Text = "报表预览",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoGenerateColumns = true
        };
        NvTheme.StyleGrid(_grid, true);
        gridPanel.Controls.Add(_grid);
        gridPanel.Controls.SetChildIndex(_grid, 0);
        split.Panel2.Controls.Add(gridPanel);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = NvTheme.HeaderBack };
        _summary = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Text = "输入 SQL 后按 F5 或点击“执行查询”。"
        };
        bottom.Controls.Add(_summary);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(header);
        Load += (_, _) =>
        {
            try
            {
                split.SplitterDistance = 210;
            }
            catch
            {
                // ignore
            }
        };
    }

    private async Task ExecuteAsync()
    {
        string sql = _editor.Text.Trim();
        if (string.IsNullOrWhiteSpace(sql))
        {
            NvPrompt.Error(this, "报表", "请输入 SQL 查询语句。");
            return;
        }
        _summary.ForeColor = NvTheme.Muted;
        _summary.Text = "正在执行查询…";
        try
        {
            var result = await Task.Run(() => _session.ExecuteBatch(sql, 600, 50000));
            var set = result.ResultSets.FirstOrDefault(r => r.HasGrid);
            if (set == null)
            {
                _summary.Text = "查询没有返回结果集。";
                return;
            }
            _data = NvDataFile.ToDataTable(set);
            _grid.DataSource = null;
            _grid.DataSource = _data;
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                column.Width = Math.Min(220, Math.Max(90, column.HeaderText.Length * 12));
            }
            _summary.Text = "共 " + _data.Rows.Count + " 行 · 用时 " + result.Elapsed.TotalMilliseconds.ToString("0") + " ms";
            UpdateSummary();
        }
        catch (Exception ex)
        {
            _summary.ForeColor = NvTheme.Red;
            _summary.Text = "执行失败：" + ex.Message;
            NvLog.Error(ex, "工具异常：报表");
            NvPrompt.Error(this, "报表", ex.Message);
        }
    }

    private void UpdateSummary()
    {
        if (_data == null)
        {
            return;
        }
        string text = "共 " + _data.Rows.Count + " 行";
        if (_autoSum.Checked)
        {
            var sums = new List<string>();
            foreach (DataColumn column in _data.Columns)
            {
                if (column.DataType != typeof(byte[])
                    && (column.DataType == typeof(int) || column.DataType == typeof(long)
                        || column.DataType == typeof(decimal) || column.DataType == typeof(double)
                        || column.DataType == typeof(float)))
                {
                    try
                    {
                        decimal sum = 0;
                        foreach (DataRow row in _data.Rows)
                        {
                            if (row[column] != DBNull.Value)
                            {
                                sum += Convert.ToDecimal(row[column], CultureInfo.InvariantCulture);
                            }
                        }
                        sums.Add(column.ColumnName + "=" + sum.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            if (sums.Count > 0)
            {
                text += " · 合计：" + string.Join("，", sums);
            }
        }
        _summary.ForeColor = NvTheme.Muted;
        _summary.Text = text;
    }

    private void Export(string format)
    {
        if (_data == null)
        {
            NvPrompt.Error(this, "报表", "请先执行查询。");
            return;
        }
        if (format == "HTML")
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "HTML 报表 (*.html)|*.html|所有文件 (*.*)|*.*",
                FileName = "report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            try
            {
                File.WriteAllText(dialog.FileName, BuildHtml(), new UTF8Encoding(false));
                NvPrompt.Info(this, "报表", "报表已导出：\n" + dialog.FileName);
            }
            catch (Exception ex)
            {
                NvLog.Error(ex, "工具异常：导出报表");
                NvPrompt.Error(this, "导出报表", ex.Message);
            }
            return;
        }
        var exportFormat = format == "XLSX" ? NvExportFormat.XLSX : NvExportFormat.CSV;
        NvExportService.ExportTableWithDialog(this, _data, "report", _session.Kind, false);
    }

    private string BuildHtml()
    {
        var sb = new StringBuilder();
        string title = _title.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "数据报表";
        }
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>"
            + NvDataFile.XmlEscape(title) + "</title><style>");
        sb.AppendLine("body{font-family:'Microsoft YaHei UI',Arial;background:#f3f6fa;padding:32px;color:#1f2a36}");
        sb.AppendLine("h1{font-size:22px;margin:0 0 6px}.meta{color:#6c7a89;font-size:12px;margin-bottom:16px}");
        sb.AppendLine("table{border-collapse:collapse;background:#fff;box-shadow:0 1px 4px rgba(0,0,0,.12);width:100%}");
        sb.AppendLine("th{background:#2b6cb0;color:#fff;padding:8px 12px;font-size:12px;text-align:left;white-space:nowrap}");
        sb.AppendLine("td{border-bottom:1px solid #e2e8ee;padding:6px 12px;font-size:12px;white-space:pre-wrap}");
        sb.AppendLine("tr:nth-child(even) td{background:#f9fbfd}.sum{margin-top:14px;color:#1f2a36;font-size:13px}");
        sb.AppendLine("</style></head><body>");
        sb.Append("<h1>").Append(NvDataFile.XmlEscape(title)).Append("</h1>");
        sb.Append("<div class=\"meta\">").Append(NvDataFile.XmlEscape(_session.Profile.Name))
            .Append(" · ").Append(NvDataFile.XmlEscape(_session.CurrentDatabase))
            .Append(" · ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("</div>");
        sb.AppendLine("<table><thead><tr>");
        foreach (DataColumn column in _data!.Columns)
        {
            sb.Append("<th>").Append(NvDataFile.XmlEscape(column.ColumnName)).Append("</th>");
        }
        sb.AppendLine("</tr></thead><tbody>");
        foreach (DataRow row in _data.Rows)
        {
            sb.Append("<tr>");
            foreach (DataColumn column in _data.Columns)
            {
                object? value = row[column];
                string text = value == null || value == DBNull.Value ? ""
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                sb.Append("<td>").Append(NvDataFile.XmlEscape(text)).Append("</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");
        sb.Append("<div class=\"sum\">").Append(NvDataFile.XmlEscape(_summary.Text)).Append("</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private void PrintReport()
    {
        if (_data == null || _data.Rows.Count == 0)
        {
            NvPrompt.Error(this, "打印报表", "请先执行返回数据的查询。");
            return;
        }
        _printRow = 0;
        _printTitle = string.IsNullOrWhiteSpace(_title.Text) ? "数据报表" : _title.Text.Trim();
        using var document = new PrintDocument();
        document.DefaultPageSettings.Landscape = _data.Columns.Count > 5;
        document.PrintPage += PrintPage;
        using var preview = new PrintPreviewDialog
        {
            Document = document,
            Width = 1000,
            Height = 720,
            Text = "报表打印预览"
        };
        preview.ShowDialog(this);
    }

    private void PrintPage(object sender, PrintPageEventArgs e)
    {
        if (_data == null)
        {
            return;
        }
        var g = e.Graphics;
        var bounds = e.MarginBounds;
        using var titleFont = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
        using var headerFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        using var cellFont = new Font("Microsoft YaHei UI", 8.2f);
        using var brush = new SolidBrush(Color.Black);
        using var linePen = new Pen(Color.FromArgb(180, 190, 200));
        int y = bounds.Top;
        if (_printRow == 0)
        {
            g.DrawString(_printTitle, titleFont, brush, bounds.Left, y);
            y += 30;
            g.DrawString(_session.Profile.Name + " · " + _session.CurrentDatabase + " · "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), cellFont, Brushes.Gray, bounds.Left, y);
            y += 28;
        }
        int columnCount = _data.Columns.Count;
        int columnWidth = Math.Max(40, bounds.Width / Math.Max(1, columnCount));
        int x = bounds.Left;
        using var headerBrush = new SolidBrush(Color.FromArgb(235, 242, 249));
        for (int c = 0; c < columnCount; c++)
        {
            var rect = new Rectangle(x, y, columnWidth, 22);
            g.FillRectangle(headerBrush, rect);
            g.DrawRectangle(linePen, rect);
            g.DrawString(_data.Columns[c].ColumnName, headerFont, brush, rect, new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            });
            x += columnWidth;
        }
        y += 22;
        while (_printRow < _data.Rows.Count && y < bounds.Bottom - 24)
        {
            x = bounds.Left;
            for (int c = 0; c < columnCount; c++)
            {
                var rect = new Rectangle(x, y, columnWidth, 20);
                g.DrawRectangle(linePen, rect);
                object? value = _data.Rows[_printRow][c];
                string text = value == null || value == DBNull.Value ? ""
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                g.DrawString(text, cellFont, brush, rect, new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                });
                x += columnWidth;
            }
            y += 20;
            _printRow++;
        }
        e.HasMorePages = _printRow < _data.Rows.Count;
    }
}
