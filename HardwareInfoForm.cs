using System.Reflection;
using System.Text;

namespace MiniMonitor;

/// <summary>
/// 硬件信息查看窗口：Excel 风格的分组表格，支持“精简 / 详细”两个视图切换。
/// 精简视图为鲁大师式一页概览（每类硬件一行关键信息），详细视图为分类色带 + 交替行色的全量表。
/// 顶部工具栏（视图切换 / 搜索 / 一键复制 TSV），支持 Ctrl+F 搜索、Ctrl+C 复制、Esc 关闭；
/// 精简视图顶部的“系统型号 / 系统序列号”支持单击复制到剪贴板，并提示复制成功或失败。
/// </summary>
public sealed class HardwareInfoForm : Form
{
    private readonly DataGridView _grid;
    private readonly TextBox _searchBox;
    private readonly Button _copyBtn;
    private readonly Button _btnSummary;
    private readonly Button _btnDetail;
    private readonly Label _countLabel;
    private readonly Label _toast;
    private readonly List<HardwareCategory> _data;
    private readonly List<HwSummaryItem> _summary;
    private readonly System.Windows.Forms.Timer _copyResetTimer;
    private bool _summaryMode = true;
    private int _totalCount;

    private static readonly Color[] BandColors =
    {
        Color.FromArgb(222, 235, 247), // 蓝
        Color.FromArgb(226, 239, 218), // 绿
        Color.FromArgb(255, 242, 204), // 黄
        Color.FromArgb(252, 228, 214), // 橙
        Color.FromArgb(228, 224, 236), // 紫
        Color.FromArgb(218, 238, 243), // 青
        Color.FromArgb(235, 241, 250), // 浅蓝
        Color.FromArgb(230, 242, 226), // 浅绿
        Color.FromArgb(255, 235, 205), // 杏
        Color.FromArgb(238, 232, 244), // 浅紫
        Color.FromArgb(222, 240, 236)  // 薄荷
    };

    private static readonly Color HeaderColor = Color.FromArgb(31, 78, 121);
    private static readonly Color ToolbarColor = Color.FromArgb(31, 78, 121);

    public HardwareInfoForm()
    {
        Text = "硬件信息查看 - MiniMonitor";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(860, 640);
        MinimumSize = new Size(660, 440);
        BackColor = Color.FromArgb(240, 242, 245);
        KeyPreview = true;
        ShowInTaskbar = true;
        Icon = LoadAppIcon();

        UseWaitCursor = true;
        try
        {
            _data = HardwareInfo.Collect();
            _summary = HardwareInfo.BuildSummary(_data);
        }
        catch (Exception ex)
        {
            _data = new List<HardwareCategory>
            {
                new("硬件信息")
                {
                    Entries = { new HwEntry("采集失败", ex.Message) }
                }
            };
            _summary = new List<HwSummaryItem>();
        }
        finally
        {
            UseWaitCursor = false;
        }

        // ---------- 顶部工具栏 ----------
        // 布局规则：标题 / “搜索”文字 / “复制全部”按钮为 AutoSize 列，视图切换为固定宽列，任何窗口宽度下都完整可见；
        // 弹性空间 / 条数 / 搜索框为 Percent 列，窗口变窄时三者按比例收缩，不会把右侧控件挤出窗体。
        var topHost = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = ToolbarColor };
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = ToolbarColor,
            Padding = new Padding(14, 8, 14, 8)
        };
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));       // 单行占满高度，避免固定行高裁切
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 0 标题
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));// 1 视图切换（含左右外边距，避免按钮被裁切）
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));  // 2 弹性空间
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));  // 3 条数
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 4 “搜索”
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));  // 5 搜索框（可伸缩）
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 6 复制按钮

        var title = new Label
        {
            Text = "硬件信息查看",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            AutoSize = false,
            Margin = new Padding(0)
        };

        // 视图切换（精简 / 详细）
        _btnSummary = MakeViewButton("精简");
        _btnDetail = MakeViewButton("详细");
        _btnSummary.Click += (_, _) => SwitchView(summary: true);
        _btnDetail.Click += (_, _) => SwitchView(summary: false);
        var switchHost = new Panel
        {
            BackColor = ToolbarColor,
            Margin = new Padding(12, 6, 12, 6),
            Size = new Size(116, 34)
        };
        _btnSummary.Location = new Point(0, 2);
        _btnDetail.Location = new Point(60, 2);
        switchHost.Controls.Add(_btnSummary);
        switchHost.Controls.Add(_btnDetail);

        _countLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(214, 228, 244),
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = false,
            Margin = new Padding(0, 0, 10, 0),
            // 文本固定为短格式（“共 N 项”/“M / N 项”），窄窗口下也不挤占
            Text = "共 0 项"
        };

        var searchLabel = new Label
        {
            Text = "搜索",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = false,
            Margin = new Padding(0, 0, 6, 0)
        };

        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "搜索",
            Margin = new Padding(0, 7, 10, 7)
        };

        _copyBtn = new Button
        {
            Text = "复制全部",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = HeaderColor,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            Size = new Size(92, 30),
            Margin = new Padding(0, 6, 0, 6),
            Cursor = Cursors.Hand
        };
        _copyBtn.FlatAppearance.BorderColor = Color.FromArgb(120, 150, 190);
        _copyBtn.FlatAppearance.BorderSize = 1;

        top.Controls.Add(title, 0, 0);
        top.Controls.Add(switchHost, 1, 0);
        top.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = ToolbarColor, Margin = new Padding(0) }, 2, 0);
        top.Controls.Add(_countLabel, 3, 0);
        top.Controls.Add(searchLabel, 4, 0);
        top.Controls.Add(_searchBox, 5, 0);
        top.Controls.Add(_copyBtn, 6, 0);
        topHost.Controls.Add(top);

        // ---------- 表格主体 ----------
        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(240, 242, 245),
            Padding = new Padding(12)
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Color.White,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(226, 230, 234),
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            ColumnHeadersHeight = 34,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 26 },
            DefaultCellStyle =
            {
                Font = new Font("Microsoft YaHei UI", 9.5f),
                ForeColor = Color.FromArgb(40, 46, 58),
                SelectionBackColor = Color.FromArgb(189, 215, 238),
                SelectionForeColor = Color.FromArgb(20, 30, 50),
                Padding = new Padding(6, 0, 6, 0)
            },
            AlternatingRowsDefaultCellStyle =
            {
                BackColor = Color.FromArgb(247, 249, 252)
            },
            ColumnHeadersDefaultCellStyle =
            {
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                SelectionBackColor = HeaderColor,
                Padding = new Padding(6, 0, 0, 0)
            }
        };
        EnableDoubleBuffer(_grid);
        ConfigureColumns();

        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                e.ToolTipText = _grid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? "";
            }
        };
        _grid.CellClick += (_, e) => TryCopySummaryValue(e.RowIndex, e.ColumnIndex);

        body.Controls.Add(_grid);

        _toast = new Label
        {
            AutoSize = true,
            Padding = new Padding(14, 6, 14, 6),
            BackColor = Color.FromArgb(46, 125, 50),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            Visible = false
        };
        body.Controls.Add(_toast);
        _toast.BringToFront();

        // ---------- 事件 ----------
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _copyBtn.Click += (_, _) => CopyAll();
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopyAll();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        _copyResetTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _copyResetTimer.Tick += (_, _) =>
        {
            _copyResetTimer.Stop();
            _copyBtn.Text = "复制全部";
            _toast.Visible = false;
        };

        Controls.Add(body);
        Controls.Add(topHost);
        UpdateViewButtons();
        PopulateSummary(); // 默认打开精简视图（鲁大师式一页概览）
    }

    private static void EnableDoubleBuffer(DataGridView grid)
    {
        typeof(DataGridView)
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(grid, true);
    }

    // ================= 视图切换（精简 / 详细） =================

    private static Button MakeViewButton(string text)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(56, 30),
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            TabStop = false
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(106, 147, 192);
        b.FlatAppearance.BorderSize = 1;
        return b;
    }

    private void UpdateViewButtons()
    {
        _btnSummary.BackColor = _summaryMode ? Color.White : Color.FromArgb(43, 97, 148);
        _btnSummary.ForeColor = _summaryMode ? HeaderColor : Color.White;
        _btnDetail.BackColor = !_summaryMode ? Color.White : Color.FromArgb(43, 97, 148);
        _btnDetail.ForeColor = !_summaryMode ? HeaderColor : Color.White;
    }

    private void SwitchView(bool summary)
    {
        if (_summaryMode == summary)
        {
            return;
        }
        _summaryMode = summary;
        UpdateViewButtons();
        ConfigureColumns();
        if (summary)
        {
            PopulateSummary();
        }
        else
        {
            Populate();
        }
        ApplyFilter();
    }

    /// <summary>按当前视图配置表格列：精简=两列（项目/信息），详细=三列（分类/项目/值）。
    /// 精简视图信息列开启自动换行 + 行高自适应（内容过长内部换行、完整可见）。</summary>
    private void ConfigureColumns()
    {
        _grid.Columns.Clear();
        if (_summaryMode)
        {
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colItem",
                HeaderText = "项目",
                Width = 150,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Frozen = true
            });
            var colInfo = new DataGridViewTextBoxColumn
            {
                Name = "colInfo",
                HeaderText = "信息",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 400,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colInfo.DefaultCellStyle.WrapMode = DataGridViewTriState.True; // 单元格内部自动换行
            _grid.Columns.Add(colInfo);
        }
        else
        {
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colCat",
                HeaderText = "分类",
                Width = 120,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Frozen = true
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colName",
                HeaderText = "项目",
                Width = 250,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colValue",
                HeaderText = "值",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 320,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
    }

    /// <summary>精简视图：每类硬件一行概览，一页看完（鲁大师风格）。
    /// 行高自适应（自动换行模式下内容多高行就多高），保底 36px。</summary>
    private void PopulateSummary()
    {
        _grid.Rows.Clear();
        foreach (var item in _summary)
        {
            int idx = _grid.Rows.Add(item.Title, item.Detail);
            var row = _grid.Rows[idx];
            row.Tag = new RowInfo
            {
                IsBand = false,
                Text = item.Title + " " + item.Detail,
                CopyValue = item.Title is "系统型号" or "系统序列号" ? item.Detail : null
            };
            row.MinimumHeight = 36;
            row.Cells[0].Style.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            row.Cells[0].Style.ForeColor = HeaderColor;
            row.Cells[0].Style.BackColor = Color.FromArgb(242, 246, 250);
        }
        _totalCount = _summary.Count;
        _countLabel.Text = "共 " + _totalCount + " 项";
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(HardwareInfoForm).Assembly
                .GetManifestResourceStream("MiniMonitor.app.ico");
            return stream != null ? new Icon(stream) : null;
        }
        catch
        {
            return null;
        }
    }

    // ================= 数据填充 =================

    private void Populate()
    {
        _grid.Rows.Clear();
        int catIndex = 0;
        foreach (var cat in _data)
        {
            Color bandColor = BandColors[catIndex % BandColors.Length];
            var children = new List<int>();

            int bandIdx = _grid.Rows.Add(cat.Name, "", "");
            var bandRow = _grid.Rows[bandIdx];
            bandRow.Tag = new RowInfo { IsBand = true, Children = children };
            bandRow.DefaultCellStyle.BackColor = bandColor;
            bandRow.DefaultCellStyle.ForeColor = Color.FromArgb(31, 78, 121);
            bandRow.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            bandRow.Height = 30;

            foreach (var e in cat.Entries)
            {
                int idx = _grid.Rows.Add("", e.Name, e.Value);
                var row = _grid.Rows[idx];
                row.Tag = new RowInfo
                {
                    IsBand = false,
                    Text = cat.Name + " " + e.Name + " " + e.Value
                };
                children.Add(idx);
            }
            catIndex++;
        }
        _totalCount = _data.Sum(c => c.Entries.Count);
        _countLabel.Text = "共 " + _totalCount + " 项";
    }

    private sealed class RowInfo
    {
        public bool IsBand;
        public string? Text;
        public string? CopyValue;
        public List<int>? Children;
    }

    // ================= 搜索过滤 =================

    private void ApplyFilter()
    {
        string q = _searchBox.Text.Trim();
        int shown = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not RowInfo info)
            {
                row.Visible = true;
                continue;
            }
            if (info.IsBand)
            {
                bool any = info.Children!.Any(i => _grid.Rows[i].Visible);
                row.Visible = any;
                if (any)
                {
                    shown++;
                }
            }
            else
            {
                bool match = string.IsNullOrEmpty(q)
                    || info.Text!.Contains(q, StringComparison.OrdinalIgnoreCase);
                row.Visible = match;
                if (match)
                {
                    shown++;
                }
            }
        }
        _countLabel.Text = string.IsNullOrEmpty(q)
            ? "共 " + _totalCount + " 项"
            : shown + " / " + _totalCount + " 项";
    }

    // ================= 精简视图点击复制（系统型号 / 系统序列号） =================

    private void TryCopySummaryValue(int rowIndex, int columnIndex)
    {
        if (!_summaryMode || rowIndex < 0 || rowIndex >= _grid.Rows.Count)
        {
            return;
        }
        if (_grid.Rows[rowIndex].Tag is not RowInfo info)
        {
            return;
        }
        string value = info.CopyValue ?? "";
        if (value.Length == 0)
        {
            return;
        }
        try
        {
            Clipboard.SetText(value);
            ShowToast("复制成功", success: true);
        }
        catch
        {
            ShowToast("复制失败", success: false);
        }
    }

    private void ShowToast(string message, bool success)
    {
        _toast.Text = message;
        _toast.BackColor = success ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40);
        _toast.Visible = true;
        _toast.BringToFront();
        _toast.Location = new Point(
            _grid.Left + Math.Max(0, (_grid.Width - _toast.Width) / 2),
            _grid.Top + Math.Max(0, _grid.Height - _toast.Height - 18));
        _copyResetTimer.Stop();
        _copyResetTimer.Start();
    }

    // ================= 一键复制（TSV，可直接粘贴进 Excel） =================

    private void CopyAll()
    {
        var sb = new StringBuilder();
        if (_summaryMode)
        {
            sb.AppendLine("项目\t信息");
            foreach (var item in _summary)
            {
                sb.Append(item.Title).Append('\t');
                AppendTsvField(sb, item.Detail);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("分类\t项目\t值");
            foreach (var cat in _data)
            {
                foreach (var e in cat.Entries)
                {
                    sb.Append(cat.Name).Append('\t').Append(e.Name).Append('\t').AppendLine(e.Value);
                }
            }
        }
        try
        {
            Clipboard.SetText(sb.ToString());
            _copyBtn.Text = "已复制 ✓";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        catch
        {
            // 剪贴板被占用时静默失败
        }
    }

    /// <summary>TSV 字段转义：含制表符/换行/引号时用双引号包裹，保证多行信息粘贴进 Excel 不串行。</summary>
    private static void AppendTsvField(StringBuilder sb, string value)
    {
        if (value.IndexOfAny(new[] { '\t', '\r', '\n', '"' }) >= 0)
        {
            sb.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        }
        else
        {
            sb.Append(value);
        }
    }
}
