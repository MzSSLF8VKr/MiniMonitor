using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>数据库文档：把表、列、索引、外键导出为 HTML 数据字典。</summary>
internal sealed class NvDbDocForm : Form
{
    private readonly DbSession _session;
    private readonly TextBox _path;
    private readonly CheckBox _includeIndexes;
    private readonly CheckBox _includeForeignKeys;
    private readonly CheckBox _includeViews;
    private readonly CheckBox _openAfter;
    private readonly RichTextBox _log;
    private readonly ProgressBar _progress;
    private readonly Button _generate;
    private readonly Label _status;

    public NvDbDocForm(DbSession session)
    {
        _session = session;
        Text = "数据库文档 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(660, 480);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("数据库文档", session.Profile.Name + " · " + session.Kind.ShortName()
            + " · " + session.CurrentDatabase, NvIcon.Report);

        var top = new Panel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(16, 12, 16, 0) };
        top.Controls.Add(new Label
        {
            Text = "输出文件",
            Location = new Point(4, 18),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _path = new TextBox
        {
            Location = new Point(80, 14),
            Width = 520,
            Font = NvTheme.Ui(9.2f),
            Text = Path.Combine(NvSettingsStore.Current.ExportFolder,
                "db_doc_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html")
        };
        top.Controls.Add(_path);
        var browse = NvTheme.FlatButton("浏览…", null, null, 76);
        browse.Location = new Point(610, 13);
        browse.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "HTML 文档 (*.html)|*.html|所有文件 (*.*)|*.*",
                FileName = Path.GetFileName(_path.Text)
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _path.Text = dialog.FileName;
            }
        };
        top.Controls.Add(browse);

        _includeIndexes = new CheckBox
        {
            Text = "包含索引",
            Location = new Point(80, 52),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _includeForeignKeys = new CheckBox
        {
            Text = "包含外键",
            Location = new Point(200, 52),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _includeViews = new CheckBox
        {
            Text = "包含视图",
            Location = new Point(320, 52),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _openAfter = new CheckBox
        {
            Text = "生成后打开",
            Location = new Point(440, 52),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        top.Controls.AddRange(new Control[] { _includeIndexes, _includeForeignKeys, _includeViews, _openAfter });
        top.Controls.Add(new Label
        {
            Text = "生成的 HTML 数据字典包含表清单、列类型 / 非空 / 主键 / 默认值 / 注释、索引和外键关系，可直接打印或归档。",
            Location = new Point(4, 90),
            Size = new Size(700, 40),
            ForeColor = NvTheme.Muted
        });
        _generate = NvTheme.FlatButton("生成文档", NvTheme.Accent, Color.White, 110);
        _generate.Location = new Point(4, 132);
        _generate.Click += async (_, _) => await GenerateAsync();
        top.Controls.Add(_generate);
        _progress = new ProgressBar
        {
            Location = new Point(124, 136),
            Width = 320,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false
        };
        top.Controls.Add(_progress);

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = NvTheme.Mono(9f),
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            WordWrap = false
        };
        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 6, 16, 12) };
        logHost.Controls.Add(_log);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Text = "就绪"
        };
        bottom.Controls.Add(_status);

        var close = NvTheme.FlatButton("关闭", null, null, 88);
        close.Location = new Point(0, 0);
        close.Visible = false;
        Controls.Add(logHost);
        Controls.Add(top);
        Controls.Add(bottom);
        Controls.Add(header);
    }

    private void Append(string text)
    {
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => Append(text)));
            }
            catch
            {
                // 窗口已关闭
            }
            return;
        }
        _log.AppendText(text + Environment.NewLine);
    }

    private async Task GenerateAsync()
    {
        string path = _path.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            NvPrompt.Error(this, "数据库文档", "请填写输出文件路径。");
            return;
        }
        _generate.Enabled = false;
        _progress.Visible = true;
        _log.Clear();
        _status.Text = "正在读取元数据…";
        try
        {
            bool indexes = _includeIndexes.Checked;
            bool fks = _includeForeignKeys.Checked;
            bool views = _includeViews.Checked;
            string html = await Task.Run(() => BuildHtml(indexes, fks, views));
            string folder = Path.GetDirectoryName(path) ?? NvSettingsStore.Current.ExportFolder;
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, html, new UTF8Encoding(false));
            _status.Text = "文档已生成：" + path;
            Append("完成：" + path + "（" + new FileInfo(path).Length / 1024 + " KB）");
            NvPrompt.Info(this, "数据库文档", "文档已生成：\n" + path);
            if (_openAfter.Checked)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Append("无法打开文档：" + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _status.Text = "生成失败：" + ex.Message;
            NvLog.Error(ex, "工具异常：数据库文档");
            NvPrompt.Error(this, "数据库文档", ex.Message);
        }
        finally
        {
            _generate.Enabled = true;
            _progress.Visible = false;
        }
    }

    private string BuildHtml(bool includeIndexes, bool includeForeignKeys, bool includeViews)
    {
        var tables = _session.GetTables(_session.CurrentDatabase);
        var viewList = includeViews ? _session.GetViews(_session.CurrentDatabase) : new List<TableInfo>();
        var sb = new StringBuilder();
        string title = _session.Profile.Name + " / " + _session.CurrentDatabase + " 数据库文档";
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>"
            + NvDataFile.XmlEscape(title) + "</title><style>");
        sb.AppendLine("body{font-family:'Microsoft YaHei UI',Arial;background:#f3f6fa;color:#1f2a36;margin:0}");
        sb.AppendLine(".page{max-width:1100px;margin:0 auto;padding:28px 36px 60px}");
        sb.AppendLine("h1{font-size:26px;margin:0 0 6px}h2{font-size:18px;margin:28px 0 10px;border-left:4px solid #2b6cb0;padding-left:10px}");
        sb.AppendLine("h3{font-size:15px;margin:24px 0 8px}.meta{color:#6c7a89;font-size:12px;margin-bottom:20px}");
        sb.AppendLine(".card{background:#fff;border:1px solid #dbe4ed;border-radius:6px;padding:14px 16px;margin:12px 0;box-shadow:0 1px 3px rgba(0,0,0,.05)}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin:8px 0 4px}th{background:#eef4fa;color:#33475b;font-size:12px;text-align:left;padding:6px 10px;border:1px solid #dbe4ed}");
        sb.AppendLine("td{font-size:12px;padding:5px 10px;border:1px solid #e2e8ee;vertical-align:top}tr:nth-child(even) td{background:#fafcfe}");
        sb.AppendLine(".muted{color:#8a97a5}.tag{display:inline-block;background:#e8f1f9;color:#23679e;border-radius:3px;padding:1px 5px;font-size:11px;margin-right:4px}");
        sb.AppendLine("a{color:#2b6cb0;text-decoration:none}code{font-family:Consolas,monospace;font-size:11px;background:#f4f6f8;padding:1px 4px;border-radius:3px}");
        sb.AppendLine("</style></head><body><div class=\"page\">");
        sb.Append("<h1>").Append(NvDataFile.XmlEscape(title)).Append("</h1>");
        sb.Append("<div class=\"meta\">").Append(_session.Profile.Kind.ShortName())
            .Append(" · ").Append(NvDataFile.XmlEscape(_session.Profile.ServerLabel))
            .Append(" · 生成时间 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(" · 表 ").Append(tables.Count).Append(" · 视图 ").Append(viewList.Count).Append("</div>");

        sb.AppendLine("<h2>目录</h2><div class=\"card\"><ol>");
        foreach (var table in tables)
        {
            sb.Append("<li><a href=\"#t_").Append(NvDataFile.XmlEscape(table.Name)).Append("\">")
                .Append(NvDataFile.XmlEscape(table.DisplayName)).Append("</a></li>");
        }
        foreach (var view in viewList)
        {
            sb.Append("<li><a href=\"#v_").Append(NvDataFile.XmlEscape(view.Name)).Append("\">")
                .Append(NvDataFile.XmlEscape(view.DisplayName)).Append("（视图）</a></li>");
        }
        sb.AppendLine("</ol></div>");

        sb.AppendLine("<h2>表结构</h2>");
        int index = 0;
        foreach (var table in tables)
        {
            index++;
            Append("读取 " + table.DisplayName + "…");
            var columns = _session.GetColumns(table);
            sb.Append("<div class=\"card\" id=\"t_").Append(NvDataFile.XmlEscape(table.Name)).Append("\">");
            sb.Append("<h3>").Append(index).Append(". ").Append(NvDataFile.XmlEscape(table.DisplayName))
                .Append(" <span class=\"muted\">").Append(table.Rows?.ToString() ?? "").Append(" 行</span></h3>");
            if (!string.IsNullOrWhiteSpace(table.Comment))
            {
                sb.Append("<div class=\"muted\">").Append(NvDataFile.XmlEscape(table.Comment)).Append("</div>");
            }
            sb.AppendLine("<table><tr><th>#</th><th>列名</th><th>类型</th><th>非空</th><th>默认值</th><th>主键</th><th>注释</th></tr>");
            foreach (var column in columns)
            {
                sb.Append("<tr><td>").Append(column.Ordinal).Append("</td><td><code>")
                    .Append(NvDataFile.XmlEscape(column.Name)).Append("</code></td><td>")
                    .Append(NvDataFile.XmlEscape(column.DisplayType)).Append("</td><td>")
                    .Append(column.Nullable ? "否" : "是").Append("</td><td>")
                    .Append(string.IsNullOrWhiteSpace(column.DefaultValue) ? "" : NvDataFile.XmlEscape(column.DefaultValue!))
                    .Append("</td><td>").Append(column.PrimaryKey ? "PK" : "").Append("</td><td>")
                    .Append(NvDataFile.XmlEscape(column.Comment ?? "")).Append("</td></tr>");
            }
            sb.AppendLine("</table>");

            if (includeIndexes)
            {
                var indexes = _session.GetIndexes(table).Where(i => !i.Primary).ToList();
                if (indexes.Count > 0)
                {
                    sb.AppendLine("<div class=\"muted\">索引</div><table><tr><th>名称</th><th>唯一</th><th>列</th></tr>");
                    foreach (var item in indexes)
                    {
                        sb.Append("<tr><td><code>").Append(NvDataFile.XmlEscape(item.Name)).Append("</code></td><td>")
                            .Append(item.Unique ? "是" : "否").Append("</td><td>")
                            .Append(NvDataFile.XmlEscape(item.ColumnText)).Append("</td></tr>");
                    }
                    sb.AppendLine("</table>");
                }
            }
            if (includeForeignKeys)
            {
                var fkList = _session.GetForeignKeys(table);
                if (fkList.Count > 0)
                {
                    sb.AppendLine("<div class=\"muted\">外键</div><table><tr><th>名称</th><th>列</th><th>引用表</th><th>引用列</th></tr>");
                    foreach (var fk in fkList)
                    {
                        sb.Append("<tr><td><code>").Append(NvDataFile.XmlEscape(fk.Name)).Append("</code></td><td>")
                            .Append(NvDataFile.XmlEscape(fk.ColumnText)).Append("</td><td>")
                            .Append(NvDataFile.XmlEscape(fk.RefTable)).Append("</td><td>")
                            .Append(NvDataFile.XmlEscape(fk.RefColumnText)).Append("</td></tr>");
                    }
                    sb.AppendLine("</table>");
                }
            }
            sb.AppendLine("</div>");
        }

        if (viewList.Count > 0)
        {
            sb.AppendLine("<h2>视图</h2>");
            foreach (var view in viewList)
            {
                Append("读取视图 " + view.DisplayName + "…");
                var columns = _session.GetColumns(view);
                sb.Append("<div class=\"card\" id=\"v_").Append(NvDataFile.XmlEscape(view.Name)).Append("\">");
                sb.Append("<h3>").Append(NvDataFile.XmlEscape(view.DisplayName)).Append("</h3>");
                sb.AppendLine("<table><tr><th>#</th><th>列名</th><th>类型</th><th>可空</th></tr>");
                foreach (var column in columns)
                {
                    sb.Append("<tr><td>").Append(column.Ordinal).Append("</td><td><code>")
                        .Append(NvDataFile.XmlEscape(column.Name)).Append("</code></td><td>")
                        .Append(NvDataFile.XmlEscape(column.DisplayType)).Append("</td><td>")
                        .Append(column.Nullable ? "是" : "否").Append("</td></tr>");
                }
                sb.AppendLine("</table></div>");
            }
        }
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }
}
