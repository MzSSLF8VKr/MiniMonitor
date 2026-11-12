using System.Diagnostics;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 蓝屏查看器选项卡：复刻 NirSoft BlueScreenView 的核心能力。
/// 自动检测当前 Windows 版本，扫描系统蓝屏转储文件，解析蓝屏代码与参数，
/// 并用简体中文给出发生缘由、解决方法；分隔线下方显示 Microsoft 官方英文原文。
/// </summary>
public sealed class BlueScreenViewTab : UserControl
{
    private const int ColTime = 0;
    private const int ColFile = 1;
    private const int ColCode = 2;
    private const int ColName = 3;
    private const int ColChinese = 4;
    private const int ColParam1 = 5;
    private const int ColParam2 = 6;
    private const int ColParam3 = 7;
    private const int ColParam4 = 8;
    private const int ColDriver = 9;
    private const int ColDumpType = 10;
    private const int ColSize = 11;

    private readonly AppConfig _config;
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _osLabel;
    private readonly Label _bottomStatus;
    private readonly ListView _list;
    private readonly TextBox _detail;
    private readonly SplitContainer _split;
    private readonly PillButton _refreshButton;
    private readonly PillButton _folderButton;
    private readonly PillButton _openFolderButton;
    private readonly PillButton _officialButton;
    private readonly PillButton _fetchButton;
    private readonly PillButton _exportButton;
    private readonly TextBox _filterBox;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _smallBoldFont;
    private readonly Font _detailFont;
    private readonly List<CrashDumpEntry> _dumps = new();
    private readonly int[] _columnWidths = new int[12];

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _docCts;
    private WindowsOsInfo _osInfo = new();
    private string _dumpFolder = "";
    private bool _scanning;
    private bool _isActive;
    private bool _disposed;
    private DateTime _lastScan;
    private int _sortColumn = ColTime;
    private bool _sortAscending = false;
    private bool _markOddEven;

    public BlueScreenViewTab(AppConfig config)
    {
        _config = config;
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);
        _smallBoldFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        _detailFont = new Font("Microsoft YaHei UI", 9.5f);

        _top = new Panel { Dock = DockStyle.Top, Height = 148, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };

        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _titleLabel = new Label
        {
            Text = "蓝屏查看器（BlueScreenView）",
            Font = _titleFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 14, 0),
            Anchor = AnchorStyles.Left
        };
        _statusLabel = new Label
        {
            Text = "● 未启用",
            Font = _statusFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        titleRow.Controls.Add(_titleLabel, 0, 0);
        titleRow.Controls.Add(_statusLabel, 1, 0);
        topLayout.Controls.Add(titleRow, 0, 0);

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _refreshButton = NewButton("刷新", 72, PillButtonStyle.Primary);
        _refreshButton.Click += (_, _) => _ = ScanDumpsAsync();
        _folderButton = NewButton("选择转储目录", 106);
        _folderButton.Click += (_, _) => ChooseDumpFolder();
        _openFolderButton = NewButton("打开转储目录", 106);
        _openFolderButton.Click += (_, _) => OpenDumpFolder();
        _officialButton = NewButton("微软官方代码库", 116);
        _officialButton.Click += (_, _) => OpenMicrosoftIndex();
        _fetchButton = NewButton("获取微软原文", 106);
        _fetchButton.Click += (_, _) => _ = FetchSelectedDocAsync(force: true);
        _exportButton = NewButton("导出", 72);
        _exportButton.Click += (_, _) => ShowExportMenu();
        buttonBar.Controls.AddRange(new Control[]
        {
            _refreshButton, _folderButton, _openFolderButton,
            _officialButton, _fetchButton, _exportButton
        });
        topLayout.Controls.Add(buttonBar, 0, 1);

        _osLabel = new Label
        {
            Text = "当前系统：检测中…",
            Font = _smallFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        topLayout.Controls.Add(_osLabel, 0, 2);

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0, 2, 0, 0),
            Margin = new Padding(0)
        };
        filterBar.Controls.Add(NewOptionLabel("筛选：", 42));
        _filterBox = new TextBox
        {
            Width = 280,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "蓝屏代码 / 名称 / 文件名 / 驱动…",
            Margin = new Padding(0, 1, 10, 0)
        };
        var hint = new Label
        {
            Text = "仅在进入本页时扫描；默认读取 C:\\Windows\\Minidump 与 MEMORY.DMP",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(4, 3, 0, 0)
        };
        filterBar.Controls.AddRange(new Control[] { _filterBox, hint });
        topLayout.Controls.Add(filterBar, 0, 3);
        _top.Controls.Add(topLayout);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true,
            ShowItemToolTips = true,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None
        };
        _list.Columns.Add("崩溃时间", 140);
        _list.Columns.Add("转储文件", 200);
        _list.Columns.Add("蓝屏代码", 100);
        _list.Columns.Add("蓝屏名称", 230);
        _list.Columns.Add("中文名称", 220);
        _list.Columns.Add("参数 1", 130);
        _list.Columns.Add("参数 2", 130);
        _list.Columns.Add("参数 3", 130);
        _list.Columns.Add("参数 4", 130);
        _list.Columns.Add("可能相关驱动", 170);
        _list.Columns.Add("转储类型", 130);
        _list.Columns.Add("大小", 80);
        for (int i = 0; i < _list.Columns.Count && i < _columnWidths.Length; i++)
        {
            _columnWidths[i] = _list.Columns[i].Width;
        }
        _list.ColumnClick += (_, e) =>
        {
            if (e.Column < 0)
            {
                return;
            }
            if (_sortColumn == e.Column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = e.Column;
                _sortAscending = true;
            }
            RefreshList();
        };
        _list.SelectedIndexChanged += (_, _) => UpdateSelectedDetail();
        _list.ContextMenuStrip = BuildMenu();

        _detail = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Font = _detailFont,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None,
            Text = "请选择一条蓝屏记录查看详情。"
        };

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(226, 232, 240)
        };
        _split.Panel1.Controls.Add(_list);
        _split.Panel2.Controls.Add(_detail);
        _split.Panel1.BackColor = Color.White;
        _split.Panel2.BackColor = Color.FromArgb(248, 250, 252);
        _split.Panel2.Padding = new Padding(10, 8, 10, 8);

        _bottomStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Text = "尚未扫描"
        };

        Controls.Add(_split);
        Controls.Add(_bottomStatus);
        Controls.Add(_top);
        Controls.SetChildIndex(_split, 0);
        Controls.SetChildIndex(_bottomStatus, 1);
        Controls.SetChildIndex(_top, 2);

        _filterBox.TextChanged += (_, _) => RefreshList();
        _osInfo = BugCheckDatabase.DetectOperatingSystem();
        _osLabel.Text = "当前系统：" + _osInfo.FullName
            + "  ·  内置微软蓝屏代码库（覆盖 Windows 10 / 11 / Server 2016-2025）" + BugCheckDatabase.CodeCount + " 条";
        Disposed += (_, _) =>
        {
            _disposed = true;
            try { _scanCts?.Cancel(); } catch { }
            try { _docCts?.Cancel(); } catch { }
        };
    }

    // ================= UI =================

    private PillButton NewButton(string text, int width,
        PillButtonStyle style = PillButtonStyle.Light)
    {
        return new PillButton
        {
            Text = text,
            Width = width,
            Height = 30,
            ButtonStyle = style,
            Font = _smallFont,
            Margin = new Padding(0, 3, 6, 3)
        };
    }

    private Label NewOptionLabel(string text, int width = 22)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(0, 2, 8, 0)
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Renderer = CreateRenderer() };
        menu.Items.Add("打开转储文件位置", null, (_, _) => OpenSelectedDumpFolder());
        menu.Items.Add("打开微软官方页面", null, (_, _) => OpenSelectedMicrosoftPage());
        menu.Items.Add("获取微软原文", null, (_, _) => _ = FetchSelectedDocAsync(force: true));
        menu.Items.Add("复制详情", null, (_, _) => CopyDetail());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导出 CSV…", null, (_, _) => Export("csv"));
        menu.Items.Add("导出 TXT…", null, (_, _) => Export("txt"));
        menu.Items.Add("导出 HTML…", null, (_, _) => Export("html"));
        menu.Items.Add("导出 XML…", null, (_, _) => Export("xml"));
        menu.Items.Add(new ToolStripSeparator());
        var columns = new ToolStripMenuItem("显示列");
        for (int i = 0; i < _list.Columns.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(_list.Columns[i].Text)
            {
                CheckOnClick = true,
                Checked = true
            };
            item.CheckedChanged += (_, _) =>
            {
                int width = _columnWidths[index];
                _list.Columns[index].Width = item.Checked ? Math.Max(50, width) : 0;
            };
            columns.DropDownItems.Add(item);
        }
        menu.Items.Add(columns);
        var grid = new ToolStripMenuItem("显示网格线")
        {
            CheckOnClick = true,
            Checked = _list.GridLines
        };
        grid.CheckedChanged += (_, _) => _list.GridLines = grid.Checked;
        menu.Items.Add(grid);
        var markRows = new ToolStripMenuItem("奇偶行交替底色")
        {
            CheckOnClick = true,
            Checked = _markOddEven
        };
        markRows.CheckedChanged += (_, _) =>
        {
            _markOddEven = markRows.Checked;
            RefreshList();
        };
        menu.Items.Add(markRows);
        menu.Items.Add("自动调整列宽", null, (_, _) =>
        {
            for (int i = 0; i < _list.Columns.Count; i++)
            {
                _list.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize);
                int headerWidth = _list.Columns[i].Width;
                _list.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
                _list.Columns[i].Width = Math.Max(headerWidth, _list.Columns[i].Width);
                _columnWidths[i] = _list.Columns[i].Width;
            }
        });
        menu.Opening += (_, _) =>
        {
            bool has = _list.SelectedItems.Count > 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem entry
                    && (entry.Text?.StartsWith("打开转储") == true
                        || entry.Text?.StartsWith("打开微软") == true
                        || entry.Text?.StartsWith("获取微软") == true
                        || entry.Text?.StartsWith("复制详情") == true))
                {
                    entry.Enabled = has;
                }
            }
        };
        return menu;
    }

    private static ToolStripRenderer CreateRenderer() =>
        new ToolStripProfessionalRenderer(new MoniColorTable()) { RoundedEdges = false };

    private sealed class MoniColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuItemSelected => Color.FromArgb(224, 239, 254);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(224, 239, 254);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(224, 239, 254);
        public override Color MenuItemBorder => Color.FromArgb(191, 219, 254);
        public override Color MenuBorder => Color.FromArgb(203, 213, 225);
        public override Color SeparatorDark => Color.FromArgb(226, 232, 240);
        public override Color SeparatorLight => Color.White;
        public override Color CheckBackground => Color.FromArgb(219, 234, 254);
        public override Color CheckSelectedBackground => Color.FromArgb(219, 234, 254);
    }

    // ================= 扫描 =================

    /// <summary>由网络测试窗口在选择 / 离开该选项卡时调用。</summary>
    public void SetActive(bool active)
    {
        if (_isActive == active || _disposed)
        {
            return;
        }
        _isActive = active;
        if (active)
        {
            _statusLabel.Text = "● 已启用";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            _osInfo = BugCheckDatabase.DetectOperatingSystem();
            _osLabel.Text = "当前系统：" + _osInfo.FullName
                + "  ·  内置微软蓝屏代码库（覆盖 Windows 10 / 11 / Server 2016-2025）" + BugCheckDatabase.CodeCount + " 条";
            _ = ScanDumpsAsync();
        }
        else
        {
            try { _scanCts?.Cancel(); } catch { }
            try { _docCts?.Cancel(); } catch { }
            _statusLabel.Text = "● 未启用（切换到该选项卡后自动扫描）";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
        }
    }

    private async Task ScanDumpsAsync()
    {
        if (_scanning || _disposed || !_isActive)
        {
            return;
        }
        _scanning = true;
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        _refreshButton.Enabled = false;
        _statusLabel.Text = "● 正在扫描蓝屏转储…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        try
        {
            string folder = _dumpFolder;
            var entries = await Task.Run(
                () => CrashDumpParser.Scan(folder, token), token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }
            _dumps.Clear();
            _dumps.AddRange(entries);
            _lastScan = DateTime.Now;
            RefreshList();
            _statusLabel.Text = _dumps.Count == 0
                ? "● 未找到蓝屏转储文件（请确认以管理员身份运行）"
                : "● 扫描完成 · 找到 " + _dumps.Count + " 个蓝屏转储";
            _statusLabel.ForeColor = _dumps.Count == 0
                ? ShutdownTheme.Danger : ShutdownTheme.Success;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = _isActive ? "● 已停止" : "● 未启用";
                _statusLabel.ForeColor = ShutdownTheme.Muted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("BlueScreenView 扫描失败：" + ex);
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = "● 扫描失败：" + ex.Message;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
        }
        finally
        {
            _scanning = false;
            if (!_disposed && !IsDisposed)
            {
                _refreshButton.Enabled = true;
                UpdateBottomStatus();
            }
        }
    }


    // ================= 列表 / 详情 =================

    private void RefreshList()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        string selectedFile = _list.SelectedItems.Count > 0
            ? (_list.SelectedItems[0].Tag as CrashDumpEntry)?.FilePath ?? ""
            : "";
        List<CrashDumpEntry> entries = _dumps.ToList();
        string filter = _filterBox.Text.Trim();
        if (filter.Length > 0)
        {
            entries = entries.Where(entry =>
                entry.BugCheckCode.ToString("X8").Contains(filter,
                    StringComparison.OrdinalIgnoreCase)
                || ("0x" + entry.BugCheckCode.ToString("X8")).Contains(filter,
                    StringComparison.OrdinalIgnoreCase)
                || (entry.Info?.Name ?? "").Contains(filter,
                    StringComparison.OrdinalIgnoreCase)
                || (entry.Info?.ChineseName ?? "").Contains(filter,
                    StringComparison.OrdinalIgnoreCase)
                || entry.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.SuspectDriver.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.DumpType.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        entries.Sort(CompareEntries);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            int rowIndex = 0;
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.CrashTime == DateTime.MinValue
                    ? "" : entry.CrashTime.ToString("yyyy-MM-dd HH:mm:ss"))
                {
                    Tag = entry
                };
                item.SubItems.Add(entry.FileName);
                item.SubItems.Add("0x" + entry.BugCheckCode.ToString("X8"));
                item.SubItems.Add(entry.Info?.Name ?? "");
                item.SubItems.Add(entry.Info?.ChineseName ?? "");
                item.SubItems.Add("0x" + entry.Parameters[0].ToString("X16"));
                item.SubItems.Add("0x" + entry.Parameters[1].ToString("X16"));
                item.SubItems.Add("0x" + entry.Parameters[2].ToString("X16"));
                item.SubItems.Add("0x" + entry.Parameters[3].ToString("X16"));
                item.SubItems.Add(entry.SuspectDriver);
                item.SubItems.Add(entry.DumpType);
                item.SubItems.Add(FormatSize(entry.FileSize));
                if (_markOddEven && rowIndex % 2 == 1)
                {
                    item.BackColor = Color.FromArgb(248, 250, 252);
                }
                if (entry.ParseError.Length > 0)
                {
                    item.ForeColor = Color.FromArgb(148, 163, 184);
                }
                else if (entry.SuspectDriver.Length > 0)
                {
                    item.ForeColor = Color.FromArgb(6, 95, 70);
                }
                else
                {
                    item.ForeColor = ShutdownTheme.Text;
                }
                item.ToolTipText = entry.FileName
                    + "\n蓝屏代码：0x" + entry.BugCheckCode.ToString("X8")
                    + "\n名称：" + (entry.Info?.Name ?? "未知")
                    + "\n中文：" + (entry.Info?.ChineseName ?? "未知")
                    + (entry.ParseError.Length > 0
                        ? "\n解析：" + entry.ParseError : "")
                    + "\n路径：" + entry.FilePath;
                if (selectedFile.Length > 0
                    && string.Equals(entry.FilePath, selectedFile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                }
                _list.Items.Add(item);
                rowIndex++;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateBottomStatus();
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }
    }

    private int CompareEntries(CrashDumpEntry left, CrashDumpEntry right)
    {
        int result = _sortColumn switch
        {
            ColTime => left.CrashTime.CompareTo(right.CrashTime),
            ColFile => string.Compare(left.FileName, right.FileName,
                StringComparison.CurrentCultureIgnoreCase),
            ColCode => left.BugCheckCode.CompareTo(right.BugCheckCode),
            ColName => string.Compare(left.Info?.Name ?? "", right.Info?.Name ?? "",
                StringComparison.CurrentCultureIgnoreCase),
            ColChinese => string.Compare(left.Info?.ChineseName ?? "",
                right.Info?.ChineseName ?? "", StringComparison.CurrentCulture),
            ColParam1 => left.Parameters[0].CompareTo(right.Parameters[0]),
            ColParam2 => left.Parameters[1].CompareTo(right.Parameters[1]),
            ColParam3 => left.Parameters[2].CompareTo(right.Parameters[2]),
            ColParam4 => left.Parameters[3].CompareTo(right.Parameters[3]),
            ColDriver => string.Compare(left.SuspectDriver, right.SuspectDriver,
                StringComparison.CurrentCultureIgnoreCase),
            ColDumpType => string.Compare(left.DumpType, right.DumpType,
                StringComparison.CurrentCultureIgnoreCase),
            ColSize => left.FileSize.CompareTo(right.FileSize),
            _ => 0
        };
        if (result == 0)
        {
            result = right.CrashTime.CompareTo(left.CrashTime);
        }
        return _sortAscending ? result : -result;
    }

    private void UpdateSelectedDetail()
    {
        if (_list.SelectedItems.Count == 0)
        {
            _detail.Text = _dumps.Count == 0
                ? "未找到蓝屏转储文件。\r\n\r\n请确认：\r\n1. 以管理员身份运行 MiniMonitor；\r\n"
                  + "2. C:\\Windows\\Minidump 存在 .dmp 文件；\r\n"
                  + "3. 或点击“选择转储目录”指定其他转储文件夹。"
                : "请选择一条蓝屏记录查看详情。";
            return;
        }
        if (_list.SelectedItems[0].Tag is not CrashDumpEntry entry)
        {
            return;
        }
        var (cachedCause, cachedResolution) = BugCheckDatabase.ReadCachedDoc(
            entry.BugCheckCode);
        string englishCause = cachedCause.Length > 0
            ? cachedCause : entry.Info?.EnglishCause ?? "";
        string englishResolution = cachedResolution.Length > 0
            ? cachedResolution : entry.Info?.EnglishResolution ?? "";
        _detail.Text = BuildDetailText(entry, englishCause, englishResolution,
            fetching: cachedCause.Length == 0 && cachedResolution.Length == 0);
        if (entry.Info != null && cachedCause.Length == 0
            && cachedResolution.Length == 0)
        {
            _ = LoadDocForEntryAsync(entry);
        }
    }

    private string BuildDetailText(CrashDumpEntry entry, string englishCause,
        string englishResolution, bool fetching)
    {
        var info = entry.Info ?? BugCheckDatabase.Lookup(entry.BugCheckCode);
        var builder = new StringBuilder();
        builder.AppendLine("【当前系统】" + _osInfo.FullName);
        builder.AppendLine("【蓝屏代码】0x" + entry.BugCheckCode.ToString("X8")
            + "    " + info.Name);
        builder.AppendLine("【中文名称】" + info.ChineseName);
        builder.AppendLine("【适用系统】" + _osInfo.Family
            + "  ·  内置代码库覆盖 Windows 10 / 11 / Server 2016-2025");
        builder.AppendLine("【转储文件】" + entry.FilePath);
        builder.AppendLine("【崩溃时间】" + (entry.CrashTime == DateTime.MinValue
            ? "—" : entry.CrashTime.ToString("yyyy-MM-dd HH:mm:ss")));
        builder.AppendLine("【转储类型】" + entry.DumpType
            + " · " + FormatSize(entry.FileSize));
        builder.AppendLine("【蓝屏参数】");
        for (int i = 0; i < 4; i++)
        {
            builder.AppendLine("  参数 " + (i + 1) + "：0x"
                + entry.Parameters[i].ToString("X16"));
        }
        if (entry.SuspectDriver.Length > 0)
        {
            builder.AppendLine("【可能相关驱动】" + entry.SuspectDriver
                + "（" + entry.SuspectPath + "）");
            builder.AppendLine("提示：该驱动由蓝屏参数地址与转储模块列表匹配推断，仅供参考。");
        }
        if (entry.ParseError.Length > 0)
        {
            builder.AppendLine("【解析提示】" + entry.ParseError);
        }
        builder.AppendLine();
        builder.AppendLine("【蓝屏发生缘由（简体中文）】");
        builder.AppendLine(info.Cause);
        builder.AppendLine();
        builder.AppendLine("【解决方法（简体中文）】");
        builder.AppendLine(info.Solution);
        builder.AppendLine();
        builder.AppendLine(new string('─', 72));
        builder.AppendLine("【Microsoft 官方原文解决方案（英文）】");
        if (englishCause.Length > 0)
        {
            builder.AppendLine("Cause:");
            builder.AppendLine(englishCause);
        }
        if (englishResolution.Length > 0)
        {
            builder.AppendLine("Resolution:");
            builder.AppendLine(englishResolution);
        }
        if (englishCause.Length == 0 && englishResolution.Length == 0)
        {
            builder.AppendLine(fetching
                ? "正在从 Microsoft Learn 获取原文…"
                : "尚未缓存微软原文。请点击“获取微软原文”联网下载，"
                  + "或点击“打开微软官方页面”查看。");
        }
        builder.AppendLine();
        builder.AppendLine("官方页面：" + info.Url);
        if (info.Known)
        {
            builder.AppendLine("代码库：Microsoft Learn Bug Check Code Reference（内置 "
                + BugCheckDatabase.CodeCount + " 条）");
        }
        else
        {
            builder.AppendLine("代码库：该代码未收录于内置索引，请打开官方页面查询。");
        }
        return builder.ToString();
    }

    private async Task LoadDocForEntryAsync(CrashDumpEntry entry)
    {
        if (entry.Info == null)
        {
            return;
        }
        _docCts?.Cancel();
        _docCts?.Dispose();
        _docCts = new CancellationTokenSource();
        var token = _docCts.Token;
        try
        {
            var (cause, resolution) = await BugCheckDatabase.FetchMicrosoftDocAsync(
                entry.Info, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }
            if (_list.SelectedItems.Count == 0
                || _list.SelectedItems[0].Tag is not CrashDumpEntry current
                || current.BugCheckCode != entry.BugCheckCode)
            {
                return;
            }
            string finalCause = cause.Length > 0
                ? cause : entry.Info.EnglishCause;
            string finalResolution = resolution.Length > 0
                ? resolution : entry.Info.EnglishResolution;
            _detail.Text = BuildDetailText(entry, finalCause, finalResolution, false);
            if (cause.Length > 0 || resolution.Length > 0)
            {
                _statusLabel.Text = "● 已获取 Microsoft 官方原文：0x"
                    + entry.BugCheckCode.ToString("X8");
                _statusLabel.ForeColor = ShutdownTheme.Success;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!_disposed && !IsDisposed
                && _list.SelectedItems.Count > 0
                && _list.SelectedItems[0].Tag is CrashDumpEntry current
                && current.BugCheckCode == entry.BugCheckCode)
            {
                _detail.Text = BuildDetailText(entry, entry.Info.EnglishCause,
                    entry.Info.EnglishResolution, false);
            }
        }
    }

    private async Task FetchSelectedDocAsync(bool force)
    {
        if (_list.SelectedItems.Count == 0
            || _list.SelectedItems[0].Tag is not CrashDumpEntry entry
            || entry.Info == null)
        {
            return;
        }
        if (force)
        {
            try { File.Delete(BugCheckDatabase.DocCachePath(entry.BugCheckCode)); } catch { }
        }
        _fetchButton.Enabled = false;
        _statusLabel.Text = "● 正在获取 Microsoft 官方原文…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        await LoadDocForEntryAsync(entry);
        if (!_disposed && !IsDisposed)
        {
            _fetchButton.Enabled = true;
        }
    }

    private void UpdateBottomStatus()
    {
        _bottomStatus.Text = "共 " + _dumps.Count + " 个蓝屏转储"
            + (_dumps.Count == 0 ? ""
                : " · 最近崩溃 " + _dumps.Max(entry => entry.CrashTime)
                    .ToString("yyyy-MM-dd HH:mm:ss"))
            + (_lastScan == DateTime.MinValue ? ""
                : "  ·  上次扫描 " + _lastScan.ToString("HH:mm:ss"))
            + (_list.Items.Count != _dumps.Count
                ? "（当前显示 " + _list.Items.Count + " 条）" : "")
            + "  ·  转储目录：" + (_dumpFolder.Length > 0
                ? _dumpFolder : @"C:\Windows\Minidump");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
        }
        if (bytes >= 1024L * 1024)
        {
            return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
        }
        return (bytes / 1024.0).ToString("0.0") + " KB";
    }

    // ================= 操作 =================

    private void ChooseDumpFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 .dmp 蓝屏转储文件的文件夹",
            UseDescriptionForTitle = true,
            SelectedPath = _dumpFolder.Length > 0 ? _dumpFolder
                : Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows), "Minidump")
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        _dumpFolder = dialog.SelectedPath;
        _ = ScanDumpsAsync();
    }

    private void OpenDumpFolder()
    {
        string folder = _dumpFolder.Length > 0 ? _dumpFolder
            : Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.Windows), "Minidump");
        try
        {
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
        }
    }

    private void OpenSelectedDumpFolder()
    {
        if (_list.SelectedItems.Count == 0
            || _list.SelectedItems[0].Tag is not CrashDumpEntry entry)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + entry.FilePath + "\"",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void OpenMicrosoftIndex()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = BugCheckDatabase.MicrosoftIndexUrl,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void OpenSelectedMicrosoftPage()
    {
        if (_list.SelectedItems.Count == 0
            || _list.SelectedItems[0].Tag is not CrashDumpEntry entry)
        {
            return;
        }
        string url = entry.Info?.Url ?? BugCheckDatabase.MicrosoftIndexUrl;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void CopyDetail()
    {
        if (_detail.Text.Length == 0)
        {
            return;
        }
        try { Clipboard.SetText(_detail.Text); } catch { }
    }

    // ================= 导出 =================

    private void ShowExportMenu()
    {
        var menu = new ContextMenuStrip { Renderer = CreateRenderer() };
        menu.Items.Add("CSV 文件（.csv）", null, (_, _) => Export("csv"));
        menu.Items.Add("制表符文本（.txt）", null, (_, _) => Export("txt"));
        menu.Items.Add("HTML 文件（.html）", null, (_, _) => Export("html"));
        menu.Items.Add("XML 文件（.xml）", null, (_, _) => Export("xml"));
        menu.Show(_exportButton, new Point(0, _exportButton.Height));
    }

    private void Export(string format)
    {
        string extension = format switch
        {
            "csv" => "csv",
            "html" => "html",
            "xml" => "xml",
            _ => "txt"
        };
        string filter = format switch
        {
            "csv" => "CSV 文件|*.csv",
            "html" => "HTML 文件|*.html",
            "xml" => "XML 文件|*.xml",
            _ => "文本文件|*.txt"
        };
        using var dialog = new SaveFileDialog
        {
            Title = "导出蓝屏转储列表",
            Filter = filter,
            FileName = "BlueScreenView_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + extension
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
            List<CrashDumpEntry> entries = _dumps.ToList();
            entries.Sort(CompareEntries);
            string content = format switch
            {
                "csv" => BuildCsv(entries),
                "html" => BuildHtml(entries),
                "xml" => BuildXml(entries),
                _ => BuildText(entries)
            };
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            _bottomStatus.Text = "已导出：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "导出失败：\n" + ex.Message,
                "BlueScreenView", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string[] ExportHeaders() => new[]
    {
        "崩溃时间", "转储文件", "蓝屏代码", "蓝屏名称", "中文名称",
        "参数1", "参数2", "参数3", "参数4", "可能相关驱动",
        "转储类型", "文件大小"
    };

    private static string[] ExportRow(CrashDumpEntry entry) => new[]
    {
        entry.CrashTime == DateTime.MinValue ? ""
            : entry.CrashTime.ToString("yyyy-MM-dd HH:mm:ss"),
        entry.FilePath,
        "0x" + entry.BugCheckCode.ToString("X8"),
        entry.Info?.Name ?? "",
        entry.Info?.ChineseName ?? "",
        "0x" + entry.Parameters[0].ToString("X16"),
        "0x" + entry.Parameters[1].ToString("X16"),
        "0x" + entry.Parameters[2].ToString("X16"),
        "0x" + entry.Parameters[3].ToString("X16"),
        entry.SuspectDriver,
        entry.DumpType,
        FormatSize(entry.FileSize)
    };

    private static string BuildText(List<CrashDumpEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", ExportHeaders()));
        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join("\t", ExportRow(entry)));
        }
        return builder.ToString();
    }

    private static string BuildCsv(List<CrashDumpEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", ExportHeaders().Select(QuoteCsv)));
        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join(",", ExportRow(entry).Select(QuoteCsv)));
        }
        return builder.ToString();
    }

    private static string QuoteCsv(string value) =>
        "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static string BuildHtml(List<CrashDumpEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>BlueScreenView</title><style>");
        builder.AppendLine("body{font-family:'Microsoft YaHei UI',Arial,sans-serif;background:#f8fafc;color:#1e293b;padding:20px}");
        builder.AppendLine("h1{font-size:20px}table{border-collapse:collapse;width:100%;background:#fff}");
        builder.AppendLine("th,td{border:1px solid #cbd5e1;padding:6px 9px;font-size:13px;text-align:left}");
        builder.AppendLine("th{background:#e2e8f0}tr:nth-child(even) td{background:#f8fafc}");
        builder.AppendLine("</style></head><body><h1>BlueScreenView</h1>");
        builder.AppendLine("<p>导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + " · 共 " + entries.Count + " 个转储</p><table><thead><tr>");
        foreach (string header in ExportHeaders())
        {
            builder.Append("<th>").Append(WebUtility.HtmlEncode(header)).AppendLine("</th>");
        }
        builder.AppendLine("</tr></thead><tbody>");
        foreach (var entry in entries)
        {
            builder.AppendLine("<tr>");
            foreach (string value in ExportRow(entry))
            {
                builder.Append("<td>").Append(WebUtility.HtmlEncode(value)).AppendLine("</td>");
            }
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody></table></body></html>");
        return builder.ToString();
    }

    private static string BuildXml(List<CrashDumpEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<blueScreenView exported=\""
            + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\">");
        foreach (var entry in entries)
        {
            builder.AppendLine("  <crash>");
            AppendXml(builder, "crashTime", entry.CrashTime == DateTime.MinValue
                ? "" : entry.CrashTime.ToString("yyyy-MM-ddTHH:mm:ss"));
            AppendXml(builder, "dumpFile", entry.FilePath);
            AppendXml(builder, "bugCheckCode", "0x" + entry.BugCheckCode.ToString("X8"));
            AppendXml(builder, "bugCheckName", entry.Info?.Name ?? "");
            AppendXml(builder, "chineseName", entry.Info?.ChineseName ?? "");
            AppendXml(builder, "parameter1", "0x" + entry.Parameters[0].ToString("X16"));
            AppendXml(builder, "parameter2", "0x" + entry.Parameters[1].ToString("X16"));
            AppendXml(builder, "parameter3", "0x" + entry.Parameters[2].ToString("X16"));
            AppendXml(builder, "parameter4", "0x" + entry.Parameters[3].ToString("X16"));
            AppendXml(builder, "suspectDriver", entry.SuspectDriver);
            AppendXml(builder, "dumpType", entry.DumpType);
            AppendXml(builder, "fileSize", entry.FileSize.ToString());
            builder.AppendLine("  </crash>");
        }
        builder.AppendLine("</blueScreenView>");
        return builder.ToString();
    }

    private static void AppendXml(StringBuilder builder, string name, string value)
    {
        builder.Append("    <").Append(name).Append('>')
            .Append(WebUtility.HtmlEncode(value ?? ""))
            .Append("</").Append(name).AppendLine(">");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            try { _scanCts?.Cancel(); } catch { }
            try { _docCts?.Cancel(); } catch { }
            _scanCts?.Dispose();
            _docCts?.Dispose();
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _smallBoldFont.Dispose();
            _detailFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
