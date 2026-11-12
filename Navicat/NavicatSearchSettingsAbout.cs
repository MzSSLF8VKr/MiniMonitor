using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>数据库对象搜索：表、视图、函数、列名。</summary>
internal sealed class NvDbSearchForm : Form
{
    private readonly DbSession _session;
    private readonly TextBox _keyword;
    private readonly CheckBox _searchObjects;
    private readonly CheckBox _searchColumns;
    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly Button _search;
    private readonly List<DbSearchHit> _hits = new();

    public DbSearchHit? SelectedHit { get; private set; }

    public NvDbSearchForm(DbSession session)
    {
        _session = session;
        Text = "数据库搜索 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 600);
        MinimumSize = new Size(680, 500);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("数据库搜索", "按名称搜索表 / 视图 / 函数 / 列", NvIcon.Search);

        var top = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(14, 10, 14, 0) };
        top.Controls.Add(new Label
        {
            Text = "关键词",
            Location = new Point(4, 18),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _keyword = new TextBox
        {
            Location = new Point(68, 14),
            Width = 500,
            Font = NvTheme.Ui(9.4f)
        };
        top.Controls.Add(_keyword);
        _search = NvTheme.FlatButton("搜索", NvTheme.Accent, Color.White, 88);
        _search.Location = new Point(580, 13);
        _search.Click += async (_, _) => await SearchAsync();
        top.Controls.Add(_search);
        _searchObjects = new CheckBox
        {
            Text = "搜索对象名",
            Location = new Point(70, 46),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _searchColumns = new CheckBox
        {
            Text = "搜索列名",
            Location = new Point(190, 46),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        top.Controls.AddRange(new Control[] { _searchObjects, _searchColumns });
        _keyword.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                await SearchAsync();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            MultiSelect = false
        };
        NvTheme.StyleGrid(_grid, true);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "类型", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Schema", HeaderText = "Schema", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 260 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Detail",
            HeaderText = "详情",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _hits.Count)
            {
                SelectedHit = _hits[e.RowIndex];
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Text = "输入关键词后按 Enter 搜索。"
        };
        bottom.Controls.Add(_status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 0) };
        body.Controls.Add(_grid);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(header);
        Load += (_, _) => _keyword.Focus();
    }

    private async Task SearchAsync()
    {
        string keyword = _keyword.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }
        _search.Enabled = false;
        _status.Text = "正在搜索…";
        _grid.Rows.Clear();
        _hits.Clear();
        try
        {
            var hits = await Task.Run(() => Search(keyword));
            _hits.AddRange(hits);
            foreach (var hit in _hits)
            {
                _grid.Rows.Add(hit.ObjectType, hit.Schema, hit.Name, hit.Detail);
            }
            _status.Text = hits.Count == 0 ? "没有找到匹配项。" : "找到 " + hits.Count + " 个匹配项，双击打开。";
        }
        catch (Exception ex)
        {
            _status.Text = "搜索失败：" + ex.Message;
        }
        finally
        {
            _search.Enabled = true;
        }
    }

    private List<DbSearchHit> Search(string keyword)
    {
        var hits = new List<DbSearchHit>();
        string db = _session.CurrentDatabase;
        var tables = _session.GetTables(db);
        var views = _session.GetViews(db);
        var routines = _session.GetRoutines(db);
        if (_searchObjects.Checked)
        {
            foreach (var table in tables)
            {
                if (table.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new DbSearchHit
                    {
                        Database = db,
                        Schema = table.Schema,
                        Name = table.Name,
                        ObjectType = "表",
                        Detail = (table.Rows?.ToString() ?? "?") + " 行"
                    });
                }
            }
            foreach (var view in views)
            {
                if (view.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new DbSearchHit
                    {
                        Database = db,
                        Schema = view.Schema,
                        Name = view.Name,
                        ObjectType = "视图"
                    });
                }
            }
            foreach (var routine in routines)
            {
                if (routine.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new DbSearchHit
                    {
                        Database = db,
                        Schema = routine.Schema,
                        Name = routine.Name,
                        ObjectType = routine.Type
                    });
                }
            }
        }
        if (_searchColumns.Checked)
        {
            int scanned = 0;
            foreach (var table in tables.Take(300))
            {
                scanned++;
                foreach (var column in _session.GetColumns(table))
                {
                    if (column.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new DbSearchHit
                        {
                            Database = db,
                            Schema = table.Schema,
                            Name = table.Name + "." + column.Name,
                            ObjectType = "列",
                            Detail = column.DisplayType
                        });
                    }
                }
            }
            _ = scanned;
        }
        return hits;
    }
}

/// <summary>复刻版选项。</summary>
internal sealed class NvSettingsForm : Form
{
    private readonly NumericUpDown _pageSize;
    private readonly NumericUpDown _maxRows;
    private readonly NumericUpDown _fontSize;
    private readonly CheckBox _wordWrap;
    private readonly CheckBox _confirm;
    private readonly CheckBox _welcome;
    private readonly TextBox _exportFolder;
    private readonly TextBox _backupFolder;

    public NvSettingsForm()
    {
        Text = "选项";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 520);
        MinimumSize = new Size(560, 480);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("选项", "Navicat 复刻版全局设置", NvIcon.Server);
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 14, 20, 0) };

        AddLabel(panel, "默认每页行数", 14);
        _pageSize = new NumericUpDown
        {
            Location = new Point(190, 10),
            Width = 100,
            Minimum = 50,
            Maximum = 10000,
            Increment = 50,
            Value = Math.Clamp(NvSettingsStore.Current.PageSize, 50, 10000),
            Font = NvTheme.Ui(9.2f)
        };
        panel.Controls.Add(_pageSize);

        AddLabel(panel, "查询最大结果行数", 54);
        _maxRows = new NumericUpDown
        {
            Location = new Point(190, 50),
            Width = 120,
            Minimum = 100,
            Maximum = 1000000,
            Increment = 1000,
            Value = Math.Clamp(NvSettingsStore.Current.MaxResultRows, 100, 1000000),
            Font = NvTheme.Ui(9.2f)
        };
        panel.Controls.Add(_maxRows);

        AddLabel(panel, "SQL 编辑器字号", 94);
        _fontSize = new NumericUpDown
        {
            Location = new Point(190, 90),
            Width = 80,
            Minimum = 7,
            Maximum = 24,
            DecimalPlaces = 1,
            Increment = 0.5m,
            Value = (decimal)Math.Clamp(NvSettingsStore.Current.EditorFontSize, 7f, 24f),
            Font = NvTheme.Ui(9.2f)
        };
        panel.Controls.Add(_fontSize);

        _wordWrap = new CheckBox
        {
            Text = "SQL 编辑器自动换行",
            Location = new Point(20, 132),
            AutoSize = true,
            Checked = NvSettingsStore.Current.WordWrap,
            ForeColor = NvTheme.Text
        };
        _confirm = new CheckBox
        {
            Text = "执行危险操作前确认（删除表 / 清空表 / 还原）",
            Location = new Point(20, 160),
            AutoSize = true,
            Checked = NvSettingsStore.Current.ConfirmDangerous,
            ForeColor = NvTheme.Text
        };
        _welcome = new CheckBox
        {
            Text = "启动时显示欢迎页",
            Location = new Point(20, 188),
            AutoSize = true,
            Checked = NvSettingsStore.Current.ShowWelcomePage,
            ForeColor = NvTheme.Text
        };
        panel.Controls.AddRange(new Control[] { _wordWrap, _confirm, _welcome });

        AddLabel(panel, "默认导出目录", 228);
        _exportFolder = new TextBox
        {
            Location = new Point(190, 224),
            Width = 320,
            Font = NvTheme.Ui(9f),
            Text = NvSettingsStore.Current.ExportFolder
        };
        panel.Controls.Add(_exportFolder);
        var browseExport = NvTheme.FlatButton("浏览…", null, null, 70);
        browseExport.Location = new Point(516, 223);
        browseExport.Click += (_, _) => BrowseFolder(_exportFolder);
        panel.Controls.Add(browseExport);

        AddLabel(panel, "默认备份目录", 268);
        _backupFolder = new TextBox
        {
            Location = new Point(190, 264),
            Width = 320,
            Font = NvTheme.Ui(9f),
            Text = NvSettingsStore.Current.BackupFolder
        };
        panel.Controls.Add(_backupFolder);
        var browseBackup = NvTheme.FlatButton("浏览…", null, null, 70);
        browseBackup.Location = new Point(516, 263);
        browseBackup.Click += (_, _) => BrowseFolder(_backupFolder);
        panel.Controls.Add(browseBackup);

        panel.Controls.Add(new Label
        {
            Text = "数据目录：%APPDATA%\\MiniMonitor\\navicat\n连接密码使用 Windows DPAPI 当前用户密钥加密保存。",
            Location = new Point(20, 310),
            Size = new Size(540, 44),
            ForeColor = NvTheme.Muted
        });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = NvTheme.Background };
        var ok = NvTheme.FlatButton("保存", NvTheme.Accent, Color.White, 92);
        ok.Location = new Point(400, 10);
        ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        ok.Click += (_, _) => Save();
        var cancel = NvTheme.FlatButton("取消", null, null, 88);
        cancel.Location = new Point(500, 10);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cancel.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { ok, cancel });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 0) };
        body.Controls.Add(panel);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(header);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static void AddLabel(Control parent, string text, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(20, y + 4),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
    }

    private void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = target.Text
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private void Save()
    {
        var settings = NvSettingsStore.Current;
        settings.PageSize = (int)_pageSize.Value;
        settings.MaxResultRows = (int)_maxRows.Value;
        settings.EditorFontSize = (float)_fontSize.Value;
        settings.WordWrap = _wordWrap.Checked;
        settings.ConfirmDangerous = _confirm.Checked;
        settings.ShowWelcomePage = _welcome.Checked;
        settings.ExportFolder = _exportFolder.Text.Trim();
        settings.BackupFolder = _backupFolder.Text.Trim();
        NvSettingsStore.Save();
        NvPrompt.Info(this, "选项", "设置已保存。部分设置将在新打开的标签页中生效。");
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>关于 Navicat 复刻版。</summary>
internal sealed class NavicatAboutForm : Form
{
    public NavicatAboutForm()
    {
        Text = "关于 Navicat 复刻版";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(680, 500);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.Font;

        var header = new Panel { Dock = DockStyle.Top, Height = 150, BackColor = Color.FromArgb(25, 55, 82) };
        header.Paint += (_, e) =>
        {
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                header.ClientRectangle, Color.FromArgb(20, 48, 74), Color.FromArgb(38, 96, 132),
                System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, header.ClientRectangle);
            e.Graphics.DrawImage(NvIcons.Get(NvIcon.Logo, 64), new Rectangle(30, 40, 64, 64));
        };
        header.Controls.Add(new Label
        {
            Text = "Navicat 复刻版",
            Location = new Point(112, 34),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 22f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text = "MiniMonitor v" + Application.ProductVersion
                + "  ·  功能对标 DBeaver 26.2.4  ·  界面复刻 Navicat Premium",
            Location = new Point(116, 84),
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 233, 244),
            BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text = "MiniMonitor 插件 · 独立实现，与 PremiumSoft 无关",
            Location = new Point(116, 108),
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 210, 226),
            BackColor = Color.Transparent
        });

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24, 16, 24, 0) };
        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = NvTheme.Background,
            ForeColor = NvTheme.Text,
            Font = NvTheme.Ui(9.4f),
            DetectUrls = false
        };
        text.Text =
            "功能概览\r\n" +
            "• 支持 MySQL / MariaDB、PostgreSQL、SQL Server、Oracle、SQLite，全部驱动内嵌，无需安装客户端。\r\n" +
            "• Navicat 风格连接树、多文档标签、SQL 编辑器、结果网格、消息面板与状态栏。\r\n" +
            "• 表数据浏览与编辑：分页、排序、WHERE 筛选、增删改、批量粘贴、自动提交或手动事务。\r\n" +
            "• 表设计器：列、类型、主键、自增、默认值、注释、索引、外键，生成并执行 ALTER 脚本。\r\n" +
            "• SQL 编辑器：语法高亮、行号、Ctrl+Space 补全、F5 执行、Ctrl+Enter 执行选中、事务、保存脚本。\r\n" +
            "• 导入 / 导出：CSV、TXT、Excel XLSX、JSON、XML、HTML、SQL，自定义列映射与批量导入。\r\n" +
            "• 备份 / 还原：完整 SQL 备份（结构 + 数据 + 视图），支持还原与计划任务定时备份。\r\n" +
            "• 数据传输 / 数据同步 / 结构同步：跨连接复制表与数据、按主键同步、结构差异脚本。\r\n" +
            "• 用户管理、服务器监控（会话终止 / 变量查看）、ER 图、可视化查询构建器、报表与打印。\r\n" +
            "• SSH 隧道、SSL、连接超时、字符集、连接导入导出、密码 DPAPI 加密保存。\r\n" +
            "• 计划任务：按间隔或每天固定时间执行查询 / 备份 / 导出，日志可查看。\r\n\r\n" +
            "排错日志\r\n" +
            "• 日志目录：%APPDATA%\\MiniMonitor\\navicat\\logs\r\n" +
            "• 连接、SQL、导入导出、备份还原、传输同步等操作与异常堆栈都会写入按天日志；\r\n" +
            "  菜单「帮助 → 查看日志」可查看、刷新、复制、清空日志或打开日志目录。\r\n\r\n" +
            "说明\r\n" +
            "• Navicat 是 PremiumSoft CyberTech Ltd. 的商标；本功能为独立实现的界面兼容版本，不包含任何 Navicat 代码。\r\n" +
            "• Oracle / SQL Server 需要服务器允许 TCP 连接并具备相应账号权限。\r\n" +
            "• UI 与交互尽量向 Navicat Premium 对齐，但个别厂商专有功能（如云同步、协同、模型转换）以本地等价实现代替。\r\n\r\n" +
            "数据目录：%APPDATA%\\MiniMonitor\\navicat";
        body.Controls.Add(text);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = NvTheme.Background };
        var copy = NvTheme.FlatButton("复制信息", null, null, 96);
        copy.Location = new Point(24, 10);
        copy.Click += (_, _) =>
        {
            Clipboard.SetText("Navicat 复刻版 · MiniMonitor v" + Application.ProductVersion
                + "\r\n功能对标 DBeaver 26.2.4\r\n界面复刻 Navicat Premium");
            NvPrompt.Info(this, "关于", "版本信息已复制到剪贴板。");
        };
        var close = NvTheme.FlatButton("关闭", NvTheme.Accent, Color.White, 88);
        close.Location = new Point(ClientSize.Width - 112, 10);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.OK;
        bottom.Controls.AddRange(new Control[] { copy, close });

        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(header);
        AcceptButton = close;
        CancelButton = close;
    }
}
