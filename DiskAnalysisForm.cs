using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 磁盘分析管理。默认不打开；从右键菜单进入。
/// 类似 WinDirStat 的核心能力：目录聚合、占用矩形图、最大文件夹/文件定位。
/// </summary>
public sealed class DiskAnalysisForm : Form
{
    private readonly Panel _header;
    private readonly CardPanel _toolbarCard;
    private readonly CardPanel _summaryCard;
    private readonly CardPanel _treemapCard;
    private readonly CardPanel _listCard;
    private readonly ComboBox _driveCombo;
    private readonly PillButton _browseButton;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly Label _pathLabel;
    private readonly Label _statusLabel;
    private readonly Label _treemapTitle;
    private readonly Label _treemapHint;
    private readonly TabControl _tabs;
    private readonly Label[] _statTitles = new Label[8];
    private readonly Label[] _statValues = new Label[8];
    private readonly Label _driveTotalLabel;
    private readonly Label _driveUsedLabel;
    private readonly Label _driveFreeLabel;
    private readonly Label _scanSizeLabel;
    private readonly Label _filesLabel;
    private readonly Label _foldersLabel;
    private readonly Label _skippedLabel;
    private readonly Label _elapsedLabel;
    private readonly TreemapControl _treemap;
    private readonly ListView _folderList;
    private readonly ListView _fileList;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;
    private bool _maxHover;
    private bool _layoutReady;
    private bool _scanning;
    private bool _closing;
    private bool _updatingDrive;
    private string _rootPath = "";
    private CancellationTokenSource? _cts;

    public DiskAnalysisForm()
    {
        Text = "磁盘分析管理 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 720);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;
        MaximizeBox = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(1000, 104),
            BackColor = Color.FromArgb(15, 118, 110)
        };
        _header.Paint += PaintHeader;
        _header.MouseMove += (_, e) =>
        {
            bool close = CloseRect.Contains(e.Location);
            bool max = MaximizeRect.Contains(e.Location);
            if (close != _closeHover || max != _maxHover)
            {
                _closeHover = close;
                _maxHover = max;
                _header.Cursor = close || max ? Cursors.Hand : Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseLeave += (_, _) =>
        {
            if (_closeHover || _maxHover)
            {
                _closeHover = false;
                _maxHover = false;
                _header.Cursor = Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseClick += (_, e) =>
        {
            if (CloseRect.Contains(e.Location))
            {
                Close();
            }
            else if (MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        _header.MouseDoubleClick += (_, e) =>
        {
            if (!CloseRect.Contains(e.Location) && !MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        NativeWindowDrag.Attach(this, _header,
            p => CloseRect.Contains(p) || MaximizeRect.Contains(p)
                || WindowState == FormWindowState.Maximized);
        Controls.Add(_header);

        // ---------- 选择与操作 ----------
        _toolbarCard = new CardPanel
        {
            Location = new Point(24, 116),
            Size = new Size(952, 72)
        };
        var objectLabel = MakeLabel("分析对象", 20, 8, 74, 30,
            new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        objectLabel.TextAlign = ContentAlignment.MiddleLeft;

        _driveCombo = new ComboBox
        {
            Location = new Point(100, 8),
            Size = new Size(460, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 9f),
            IntegralHeight = false,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text
        };
        _browseButton = new PillButton
        {
            Text = "选择文件夹",
            Location = new Point(570, 8),
            Size = new Size(110, 32),
            ButtonStyle = PillButtonStyle.Light
        };
        _stopButton = new PillButton
        {
            Text = "停止",
            Location = new Point(690, 8),
            Size = new Size(100, 32),
            ButtonStyle = PillButtonStyle.Danger,
            Enabled = false
        };
        _startButton = new PillButton
        {
            Text = "开始分析",
            Location = new Point(800, 8),
            Size = new Size(134, 32),
            ButtonStyle = PillButtonStyle.Primary
        };
        _pathLabel = MakeLabel("", 20, 42, 910, 20,
            new Font("Microsoft YaHei UI", 8.3f), ShutdownTheme.Muted, Color.White);
        _pathLabel.AutoEllipsis = true;
        _toolbarCard.Controls.AddRange(new Control[]
        {
            objectLabel, _driveCombo, _browseButton, _stopButton, _startButton, _pathLabel
        });
        Controls.Add(_toolbarCard);

        // ---------- 概览数字 ----------
        _summaryCard = new CardPanel
        {
            Location = new Point(24, 200),
            Size = new Size(952, 84)
        };
        _driveTotalLabel = AddStat(_summaryCard, 0, "磁盘总容量", 8, 24);
        _driveUsedLabel = AddStat(_summaryCard, 1, "磁盘已用", 8, 24);
        _driveFreeLabel = AddStat(_summaryCard, 2, "磁盘可用", 8, 24);
        _scanSizeLabel = AddStat(_summaryCard, 3, "本次扫描大小", 8, 24);
        _filesLabel = AddStat(_summaryCard, 4, "文件数", 46, 60, 9.5f);
        _foldersLabel = AddStat(_summaryCard, 5, "文件夹数", 46, 60, 9.5f);
        _skippedLabel = AddStat(_summaryCard, 6, "跳过（链接/异常）", 46, 60, 9.5f);
        _elapsedLabel = AddStat(_summaryCard, 7, "扫描耗时", 46, 60, 9.5f);
        Controls.Add(_summaryCard);

        // ---------- 占用图 ----------
        _treemapCard = new CardPanel
        {
            Location = new Point(24, 296),
            Size = new Size(584, 404)
        };
        _treemapTitle = MakeLabel("磁盘占用图（面积 = 大小）", 18, 6, 548, 24,
            new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _treemapHint = MakeLabel("双击矩形打开文件夹 · 鼠标悬停查看完整路径", 18, 30, 548, 16,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        _treemap = new TreemapControl
        {
            Location = new Point(18, 48),
            Size = new Size(548, 338)
        };
        _treemapCard.Controls.AddRange(new Control[] { _treemapTitle, _treemapHint, _treemap });
        Controls.Add(_treemapCard);

        // ---------- 排行列表 ----------
        _listCard = new CardPanel
        {
            Location = new Point(620, 296),
            Size = new Size(356, 404)
        };
        _tabs = new TabControl
        {
            Location = new Point(12, 12),
            Size = new Size(332, 380),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        var folderPage = new TabPage("最大文件夹");
        folderPage.BackColor = Color.White;
        folderPage.Padding = new Padding(6);
        _folderList = CreateListView();
        _folderList.Dock = DockStyle.Fill;
        _folderList.Columns.Add("文件夹", 168);
        _folderList.Columns.Add("大小", 88);
        _folderList.Columns.Add("占比", 56);
        folderPage.Controls.Add(_folderList);

        var filePage = new TabPage("最大文件");
        filePage.BackColor = Color.White;
        filePage.Padding = new Padding(6);
        _fileList = CreateListView();
        _fileList.Dock = DockStyle.Fill;
        _fileList.Columns.Add("文件", 140);
        _fileList.Columns.Add("大小", 82);
        _fileList.Columns.Add("所在目录", 150);
        filePage.Controls.Add(_fileList);

        _tabs.TabPages.Add(folderPage);
        _tabs.TabPages.Add(filePage);
        _listCard.Controls.Add(_tabs);
        Controls.Add(_listCard);

        _statusLabel = MakeLabel("请选择磁盘或文件夹，然后点击“开始分析”", 24, 716, 952, 22,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.Transparent);
        _statusLabel.AutoEllipsis = true;
        Controls.Add(_statusLabel);

        // ---------- 事件 ----------
        BuildDriveList();
        _driveCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingDrive || _driveCombo.SelectedItem is not DriveItem item)
            {
                return;
            }
            _rootPath = item.Drive.Root;
            _pathLabel.Text = _rootPath;
            UpdateDriveStats(item.Drive.TotalSize, item.Drive.FreeSpace);
            ClearResult();
        };
        _browseButton.Click += (_, _) => BrowseFolder();
        _startButton.Click += (_, _) => StartScan();
        _stopButton.Click += (_, _) => StopScan();
        _folderList.DoubleClick += (_, _) => OpenSelectedFolder();
        _fileList.DoubleClick += (_, _) => OpenSelectedFile();
        _folderList.MouseDown += (_, e) => SelectItemOnRightClick(_folderList, e);
        _fileList.MouseDown += (_, e) => SelectItemOnRightClick(_fileList, e);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        _layoutReady = true;
        LayoutContent();
    }

    private Label AddStat(CardPanel card, int index, string title, int titleY, int valueY, float valueSize = 10.5f)
    {
        var titleLabel = MakeLabel(title, 20, titleY, 210, 16,
            new Font("Microsoft YaHei UI", 7.5f), ShutdownTheme.Muted, Color.White);
        var valueLabel = MakeLabel("—", 20, valueY, 210, 22,
            new Font("Microsoft YaHei UI", valueSize, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        if (index >= 0 && index < _statTitles.Length)
        {
            _statTitles[index] = titleLabel;
            _statValues[index] = valueLabel;
        }
        card.Controls.Add(titleLabel);
        card.Controls.Add(valueLabel);
        return valueLabel;
    }

    private static ListView CreateListView()
    {
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = false,
            Font = new Font("Microsoft YaHei UI", 8.3f),
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None
        };
        return list;
    }

    private void BuildDriveList()
    {
        var items = new List<DriveItem>();
        try
        {
            foreach (var drive in DriveEnumerator.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }
                    if (drive.Kind is not (DriveEnumerator.DriveKind.Fixed
                        or DriveEnumerator.DriveKind.Removable
                        or DriveEnumerator.DriveKind.Network))
                    {
                        continue;
                    }
                    items.Add(new DriveItem(drive));
                }
                catch
                {
                    // 单个磁盘读取失败时跳过
                }
            }
        }
        catch
        {
            // 磁盘枚举失败
        }

        _updatingDrive = true;
        _driveCombo.Items.Clear();
        foreach (var item in items)
        {
            _driveCombo.Items.Add(item);
        }
        if (_driveCombo.Items.Count > 0)
        {
            _driveCombo.SelectedIndex = 0;
        }
        _updatingDrive = false;

        if (_driveCombo.SelectedItem is DriveItem selected)
        {
            _rootPath = selected.Drive.Root;
            _pathLabel.Text = _rootPath;
            UpdateDriveStats(selected.Drive.TotalSize, selected.Drive.FreeSpace);
        }
        else
        {
            _startButton.Enabled = false;
            _statusLabel.Text = "没有找到可分析的磁盘。";
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
        ClearResult();
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择要分析的文件夹（也可以直接分析整个磁盘）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = _rootPath
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _rootPath = dialog.SelectedPath;
        _pathLabel.Text = _rootPath;
        string? driveRoot = Path.GetPathRoot(_rootPath);
        if (!string.IsNullOrEmpty(driveRoot))
        {
            _updatingDrive = true;
            for (int i = 0; i < _driveCombo.Items.Count; i++)
            {
                if (_driveCombo.Items[i] is DriveItem item
                    && string.Equals(item.Drive.Root.TrimEnd('\\'),
                        driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    _driveCombo.SelectedIndex = i;
                    break;
                }
            }
            _updatingDrive = false;
            UpdateDriveStatsForPath(_rootPath);
        }
        ClearResult();
    }

    private async void StartScan()
    {
        if (_scanning)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
        {
            _statusLabel.Text = "分析对象不存在：" + _rootPath;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
            return;
        }

        _scanning = true;
        _closing = false;
        ClearResult();
        UpdateDriveStatsForPath(_rootPath);
        SetScanningUi(true);
        _statusLabel.Text = "正在扫描：" + _rootPath;
        _statusLabel.ForeColor = ShutdownTheme.Text;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<ScanProgress>(p =>
        {
            if (_closing || !_scanning)
            {
                return;
            }
            ApplyProgress(p);
        });

        try
        {
            var result = await Task.Run(
                () => DiskAnalyzer.Scan(_rootPath, token, progress), token);
            if (token.IsCancellationRequested || _closing)
            {
                return;
            }
            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            if (!_closing)
            {
                _statusLabel.Text = "分析已停止。";
                _statusLabel.ForeColor = ShutdownTheme.WarningText;
            }
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _statusLabel.Text = "分析失败：" + ex.Message;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
                MessageBox.Show(this, ex.Message, "磁盘分析失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _scanning = false;
            _cts?.Dispose();
            _cts = null;
            if (!_closing)
            {
                SetScanningUi(false);
            }
        }
    }

    private void StopScan()
    {
        if (!_scanning || _cts == null)
        {
            return;
        }
        _cts.Cancel();
        _stopButton.Enabled = false;
        _statusLabel.Text = "正在停止分析...";
        _statusLabel.ForeColor = ShutdownTheme.WarningText;
    }

    private void SetScanningUi(bool scanning)
    {
        _driveCombo.Enabled = !scanning;
        _browseButton.Enabled = !scanning;
        _startButton.Enabled = !scanning;
        _stopButton.Enabled = scanning;
        _startButton.Text = scanning ? "分析中..." : "开始分析";
    }

    private void ApplyProgress(ScanProgress progress)
    {
        _scanSizeLabel.Text = FormatBytes(progress.Bytes);
        _filesLabel.Text = progress.Files.ToString("N0", CultureInfo.InvariantCulture);
        _foldersLabel.Text = progress.Folders.ToString("N0", CultureInfo.InvariantCulture);
        _skippedLabel.Text = progress.Skipped.ToString("N0", CultureInfo.InvariantCulture);
        _elapsedLabel.Text = FormatElapsed(progress.Elapsed);
        _statusLabel.Text = "正在扫描：" + progress.CurrentPath;
        _statusLabel.ForeColor = ShutdownTheme.Text;
    }

    private void ApplyResult(DiskScanResult result)
    {
        _scanSizeLabel.Text = FormatBytes(result.TotalBytes);
        _filesLabel.Text = result.FileCount.ToString("N0", CultureInfo.InvariantCulture);
        _foldersLabel.Text = result.FolderCount.ToString("N0", CultureInfo.InvariantCulture);
        _skippedLabel.Text = result.SkippedCount.ToString("N0", CultureInfo.InvariantCulture);
        _elapsedLabel.Text = FormatElapsed(result.Elapsed);
        _treemap.SetData(result);

        _folderList.BeginUpdate();
        _folderList.Items.Clear();
        foreach (var node in result.LargestFolders)
        {
            double percent = result.TotalBytes > 0 ? node.Size * 100.0 / result.TotalBytes : 0;
            var item = new ListViewItem(node.Name) { Tag = node };
            item.SubItems.Add(FormatBytes(node.Size));
            item.SubItems.Add(percent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            item.ToolTipText = node.FullPath;
            _folderList.Items.Add(item);
        }
        _folderList.EndUpdate();

        _fileList.BeginUpdate();
        _fileList.Items.Clear();
        foreach (var file in result.LargestFiles)
        {
            var item = new ListViewItem(file.Name) { Tag = file };
            item.SubItems.Add(FormatBytes(file.Size));
            item.SubItems.Add(Path.GetDirectoryName(file.FullPath) ?? "");
            item.ToolTipText = file.FullPath;
            _fileList.Items.Add(item);
        }
        _fileList.EndUpdate();
        _fileList.ShowItemToolTips = true;
        _folderList.ShowItemToolTips = true;

        _statusLabel.Text = "分析完成：" + FormatBytes(result.TotalBytes)
            + " · " + result.FileCount.ToString("N0", CultureInfo.InvariantCulture) + " 个文件 · "
            + result.FolderCount.ToString("N0", CultureInfo.InvariantCulture) + " 个文件夹 · 用时 "
            + FormatElapsed(result.Elapsed);
        _statusLabel.ForeColor = ShutdownTheme.Success;
    }

    private void ClearResult()
    {
        _treemap.SetData(null);
        _folderList.Items.Clear();
        _fileList.Items.Clear();
        _scanSizeLabel.Text = "—";
        _filesLabel.Text = "—";
        _foldersLabel.Text = "—";
        _skippedLabel.Text = "—";
        _elapsedLabel.Text = "—";
        if (!_scanning)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(_rootPath)
                ? "请选择磁盘或文件夹，然后点击“开始分析”"
                : "已选择 " + _rootPath + "，点击“开始分析”";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
        }
    }

    private void UpdateDriveStatsForPath(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root)
                && DriveEnumerator.TryGetSpace(root, out long total, out long free))
            {
                UpdateDriveStats(total, free);
            }
        }
        catch
        {
            _driveTotalLabel.Text = "—";
            _driveUsedLabel.Text = "—";
            _driveFreeLabel.Text = "—";
        }
    }

    private void UpdateDriveStats(long total, long free)
    {
        try
        {
            _driveTotalLabel.Text = FormatBytes(total);
            _driveUsedLabel.Text = FormatBytes(total - free);
            _driveFreeLabel.Text = FormatBytes(free);
        }
        catch
        {
            _driveTotalLabel.Text = "—";
            _driveUsedLabel.Text = "—";
            _driveFreeLabel.Text = "—";
        }
    }

    private void OpenSelectedFolder()
    {
        if (_folderList.SelectedItems.Count > 0
            && _folderList.SelectedItems[0].Tag is DirNode node)
        {
            OpenFolder(node.FullPath);
        }
    }

    private void OpenSelectedFile()
    {
        if (_fileList.SelectedItems.Count > 0
            && _fileList.SelectedItems[0].Tag is FileEntry file)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file.FullPath + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法打开文件位置",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法打开文件夹",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void SelectItemOnRightClick(ListView list, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }
        var item = list.GetItemAt(e.X, e.Y);
        if (item != null)
        {
            item.Selected = true;
            list.Focus();
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? bytes.ToString("N0", CultureInfo.InvariantCulture) + " B"
            : value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return ((int)elapsed.TotalHours) + ":" + elapsed.ToString("mm\\:ss", CultureInfo.InvariantCulture);
        }
        return elapsed.ToString("m\\:ss", CultureInfo.InvariantCulture);
    }

    private static Label MakeLabel(string text, int x, int y, int width, int height,
        Font font, Color color, Color backColor)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = font,
            ForeColor = color,
            BackColor = backColor,
            AutoSize = false
        };
    }

    private Rectangle CloseRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 52f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

    private Rectangle MaximizeRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 96f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        _header.Invalidate();
    }

    private void LayoutContent()
    {
        if (!_layoutReady)
        {
            return;
        }

        int width = ClientSize.Width;
        int height = ClientSize.Height;
        _header.Size = new Size(width, 104);
        int contentWidth = Math.Max(600, width - 48);

        _toolbarCard.Location = new Point(24, 116);
        _toolbarCard.Size = new Size(contentWidth, 72);
        int startWidth = _startButton.Width;
        int stopWidth = _stopButton.Width;
        int browseWidth = _browseButton.Width;
        const int gap = 10;
        const int rightPadding = 18;
        _startButton.Location = new Point(contentWidth - rightPadding - startWidth, 8);
        _stopButton.Location = new Point(_startButton.Left - gap - stopWidth, 8);
        _browseButton.Location = new Point(_stopButton.Left - gap - browseWidth, 8);
        _driveCombo.Location = new Point(100, 8);
        _driveCombo.Width = Math.Max(220, _browseButton.Left - gap - 100);
        _pathLabel.Location = new Point(20, 42);
        _pathLabel.Width = contentWidth - 40;

        _summaryCard.Location = new Point(24, 200);
        _summaryCard.Size = new Size(contentWidth, 84);
        int columnWidth = Math.Max(120, (contentWidth - 40) / 4);
        for (int i = 0; i < _statTitles.Length; i++)
        {
            int x = 20 + (i % 4) * columnWidth;
            int titleY = i < 4 ? 8 : 46;
            int valueY = i < 4 ? 24 : 60;
            _statTitles[i].Location = new Point(x, titleY);
            _statTitles[i].Width = Math.Max(100, columnWidth - 20);
            _statValues[i].Location = new Point(x, valueY);
            _statValues[i].Width = Math.Max(100, columnWidth - 20);
        }

        int footerY = height - 44;
        int mainTop = 296;
        int mainHeight = Math.Max(240, footerY - 12 - mainTop);
        int rightWidth = Math.Max(356, (int)(contentWidth * 0.30));
        int treemapWidth = Math.Max(300, contentWidth - 12 - rightWidth);

        _treemapCard.Location = new Point(24, mainTop);
        _treemapCard.Size = new Size(treemapWidth, mainHeight);
        _treemapTitle.Location = new Point(18, 6);
        _treemapTitle.Width = treemapWidth - 36;
        _treemapHint.Location = new Point(18, 30);
        _treemapHint.Width = treemapWidth - 36;
        _treemap.Location = new Point(18, 48);
        _treemap.Size = new Size(treemapWidth - 36, Math.Max(100, mainHeight - 66));

        _listCard.Location = new Point(24 + contentWidth - rightWidth, mainTop);
        _listCard.Size = new Size(rightWidth, mainHeight);
        _tabs.Location = new Point(12, 12);
        _tabs.Size = new Size(rightWidth - 24, mainHeight - 24);

        _statusLabel.Location = new Point(24, footerY);
        _statusLabel.Width = contentWidth;

        int listWidth = Math.Max(180, _tabs.Width - 24);
        int folderName = Math.Max(100, (int)(listWidth * 0.52));
        int folderSize = Math.Max(64, (int)(listWidth * 0.27));
        _folderList.Columns[0].Width = folderName;
        _folderList.Columns[1].Width = folderSize;
        _folderList.Columns[2].Width = Math.Max(48, listWidth - folderName - folderSize);
        int fileName = Math.Max(90, (int)(listWidth * 0.38));
        int fileSize = Math.Max(58, (int)(listWidth * 0.22));
        _fileList.Columns[0].Width = fileName;
        _fileList.Columns[1].Width = fileSize;
        _fileList.Columns[2].Width = Math.Max(80, listWidth - fileName - fileSize);

        _header.Invalidate();
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 104f;

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(15, 118, 110), Color.FromArgb(14, 116, 144),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 250, -110, 330, 330);
            g.FillEllipse(glow, _header.Width - 100, 30, 140, 140);
        }

        TextRenderer.DrawText(g, "磁盘分析管理", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s), (int)(_header.Width - 140f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "磁盘空间扫描 · 文件夹占用图 · 大文件定位", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s), (int)(_header.Width - 160f * s), (int)(26f * s)),
            Color.FromArgb(224, 255, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f * s))
        {
            int inset = (int)(12f * s);
            g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
        }

        var maximize = MaximizeRect;
        if (_maxHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, maximize);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.8f * s))
        {
            int cx = maximize.Left + maximize.Width / 2;
            int cy = maximize.Top + maximize.Height / 2;
            if (WindowState == FormWindowState.Maximized)
            {
                g.DrawRectangle(pen, cx - 8, cy - 6, 11, 9);
                g.DrawRectangle(pen, cx - 3, cy - 1, 11, 9);
            }
            else
            {
                g.DrawRectangle(pen, cx - 7, cy - 6, 15, 12);
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _cts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutContent();
        float radius = WindowState == FormWindowState.Maximized
            ? 0f
            : 18f * (Height / 720f);
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        if (_layoutReady)
        {
            _header.Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing = true;
            _cts?.Cancel();
            _headerTitleFont.Dispose();
            _headerSubFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }

    private sealed class DriveItem
    {
        public DriveEnumerator.DriveEntry Drive { get; }
        private readonly string _display;

        public DriveItem(DriveEnumerator.DriveEntry drive)
        {
            Drive = drive;
            string type = drive.Kind switch
            {
                DriveEnumerator.DriveKind.Fixed => "本机磁盘",
                DriveEnumerator.DriveKind.Removable => "可移动磁盘",
                DriveEnumerator.DriveKind.Network => "网络磁盘",
                _ => "磁盘"
            };
            try
            {
                _display = drive.Root + "  " + type + "  ("
                    + FormatBytes(drive.FreeSpace) + " 可用 / "
                    + FormatBytes(drive.TotalSize) + ")";
            }
            catch
            {
                _display = drive.Root + "  " + type;
            }
        }

        public override string ToString()
        {
            return _display;
        }
    }
}

/// <summary>
/// 文件夹矩形占用图：按大小铺满矩形，递归细分到子文件夹；
/// 顶部一级文件夹带标题条，叶子块显示名称与大小。
/// </summary>
internal sealed class TreemapControl : Control
{
    private const int MaxDepth = 4;
    private const float MinSubdivideArea = 2400f;
    private static readonly Color[] TopColors =
    {
        Color.FromArgb(14, 116, 144),
        Color.FromArgb(13, 148, 136),
        Color.FromArgb(37, 99, 235),
        Color.FromArgb(79, 70, 229),
        Color.FromArgb(124, 58, 237),
        Color.FromArgb(190, 24, 93),
        Color.FromArgb(180, 83, 9),
        Color.FromArgb(21, 128, 61)
    };
    private static readonly Color FilesColor = Color.FromArgb(100, 116, 139);

    private readonly Font _headerFont = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
    private readonly Font _labelFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold);
    private readonly Font _smallFont = new Font("Microsoft YaHei UI", 7.5f);
    private readonly ToolTip _toolTip = new ToolTip
    {
        InitialDelay = 400,
        ReshowDelay = 100,
        AutoPopDelay = 6000,
        ShowAlways = true
    };
    private readonly List<MapItem> _hitItems = new();
    private readonly List<MapItem> _rootItems = new();
    private DiskScanResult? _result;
    private MapItem? _hover;

    public TreemapControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        Cursor = Cursors.Default;
    }

    public void SetData(DiskScanResult? result)
    {
        _result = result;
        RebuildLayout();
        Invalidate();
    }

    private void RebuildLayout()
    {
        _hitItems.Clear();
        _rootItems.Clear();
        _hover = null;
        _toolTip.Hide(this);

        if (_result == null || _result.TotalBytes <= 0)
        {
            return;
        }

        var root = _result.Root;
        var items = new List<MapItem>();
        int colorIndex = 0;
        foreach (var child in root.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size))
        {
            items.Add(new MapItem(child, child.Name, child.FullPath, child.Size, false,
                TopColors[colorIndex % TopColors.Length]));
            colorIndex++;
        }
        if (root.DirectFileSize > 0)
        {
            items.Add(new MapItem(null, "根目录文件", root.FullPath, root.DirectFileSize, true, FilesColor));
        }
        if (items.Count == 0)
        {
            return;
        }

        var bounds = new RectangleF(8, 8, Math.Max(1, Width - 16), Math.Max(1, Height - 16));
        Squarify(items, bounds);
        _rootItems.AddRange(items);
        LayoutChildren(_rootItems, 0);
    }

    private void LayoutChildren(List<MapItem> items, int depth)
    {
        foreach (var item in items)
        {
            _hitItems.Add(item);
            if (item.Folder == null || depth >= MaxDepth)
            {
                continue;
            }

            var rect = item.Rect;
            if (rect.Width < 28 || rect.Height < 26
                || rect.Width * rect.Height < MinSubdivideArea)
            {
                continue;
            }

            float header = depth == 0 ? 22f : 0f;
            RectangleF inner;
            if (header > 0 && rect.Height > header + 18)
            {
                inner = new RectangleF(rect.X + 2, rect.Y + header, rect.Width - 4, rect.Height - header - 2);
            }
            else
            {
                inner = new RectangleF(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            }
            if (inner.Width <= 4 || inner.Height <= 4)
            {
                continue;
            }

            var children = BuildChildren(item);
            if (children.Count == 0)
            {
                continue;
            }
            Squarify(children, inner);
            item.Children = children;
            LayoutChildren(children, depth + 1);
        }
    }

    private static List<MapItem> BuildChildren(MapItem parent)
    {
        var items = new List<MapItem>();
        var folder = parent.Folder;
        if (folder == null)
        {
            return items;
        }
        Color childColor = Blend(parent.Color, Color.White, 0.10f);
        foreach (var child in folder.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size))
        {
            items.Add(new MapItem(child, child.Name, child.FullPath, child.Size, false, childColor));
        }
        if (folder.DirectFileSize > 0)
        {
            items.Add(new MapItem(null, "直接文件", folder.FullPath, folder.DirectFileSize, true,
                Blend(parent.Color, FilesColor, 0.55f)));
        }
        return items;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.White);

        if (_result == null)
        {
            DrawEmpty(g);
            return;
        }
        DrawItems(g, _rootItems, 0);
    }

    private void DrawItems(Graphics g, List<MapItem> items, int depth)
    {
        foreach (var item in items)
        {
            if (item.Rect.Width < 1 || item.Rect.Height < 1)
            {
                continue;
            }

            using (var fill = new SolidBrush(item.Color))
            {
                g.FillRectangle(fill, item.Rect);
            }
            using (var border = new Pen(depth == 0
                ? Color.FromArgb(245, 255, 255, 255)
                : Color.FromArgb(215, 255, 255, 255), depth == 0 ? 2f : 1f))
            {
                g.DrawRectangle(border, item.Rect.X, item.Rect.Y, item.Rect.Width, item.Rect.Height);
            }

            if (depth == 0 && item.Folder != null)
            {
                float headerHeight = Math.Min(22f, item.Rect.Height);
                var headerRect = new RectangleF(item.Rect.X, item.Rect.Y, item.Rect.Width, headerHeight);
                using (var headerBrush = new SolidBrush(Blend(item.Color, Color.Black, 0.22f)))
                {
                    g.FillRectangle(headerBrush, headerRect);
                }
                DrawHeaderLabel(g, item, headerRect);
            }

            if (item.Children != null && item.Children.Count > 0)
            {
                DrawItems(g, item.Children, depth + 1);
            }
            else if (depth > 0 || item.Folder == null)
            {
                DrawInnerLabel(g, item, depth);
            }
        }
    }

    private void DrawHeaderLabel(Graphics g, MapItem item, RectangleF headerRect)
    {
        if (headerRect.Width < 24 || headerRect.Height < 12)
        {
            return;
        }
        string text = headerRect.Width >= 130
            ? item.Name + "   " + FormatBytes(item.Size)
            : item.Name;
        TextRenderer.DrawText(g, text, _headerFont,
            new Rectangle((int)headerRect.X + 6, (int)headerRect.Y,
                Math.Max(0, (int)headerRect.Width - 10), (int)headerRect.Height),
            Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawInnerLabel(Graphics g, MapItem item, int depth)
    {
        var rect = item.Rect;
        if (rect.Width < 62 || rect.Height < 20)
        {
            return;
        }
        Color textColor = IsDark(item.Color)
            ? Color.White
            : Color.FromArgb(30, 41, 59);
        string name = item.Name;
        TextRenderer.DrawText(g, name, _labelFont,
            new Rectangle((int)rect.X + 5, (int)rect.Y + 3,
                Math.Max(0, (int)rect.Width - 10), 18),
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (rect.Height >= 36)
        {
            TextRenderer.DrawText(g, FormatBytes(item.Size), _smallFont,
                new Rectangle((int)rect.X + 5, (int)rect.Y + 19,
                    Math.Max(0, (int)rect.Width - 10), 16),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawEmpty(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var centerX = Width / 2f;
        var centerY = Height / 2f - 26f;
        var disk = new RectangleF(centerX - 48, centerY - 34, 96, 68);
        ShutdownTheme.FillRounded(g, disk, 16f, Color.FromArgb(236, 254, 255));
        ShutdownTheme.DrawRoundedBorder(g, disk, 16f, Color.FromArgb(153, 246, 228));
        using (var pen = new Pen(Color.FromArgb(13, 148, 136), 2f))
        {
            g.DrawEllipse(pen, disk.Left + 30, disk.Top + 16, 36, 12);
            g.DrawLine(pen, disk.Left + 30, disk.Top + 22, disk.Left + 30, disk.Top + 46);
            g.DrawLine(pen, disk.Right - 30, disk.Top + 22, disk.Right - 30, disk.Top + 46);
            g.DrawArc(pen, disk.Left + 30, disk.Top + 36, 36, 22, 0, 180);
        }
        TextRenderer.DrawText(g, "选择磁盘或文件夹后开始分析", _headerFont,
            new Rectangle(0, (int)(centerY + 52), Width, 26),
            ShutdownTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "扫描结果会在这里显示文件夹占用矩形图", _smallFont,
            new Rectangle(0, (int)(centerY + 78), Width, 20),
            ShutdownTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit == _hover)
        {
            return;
        }
        _hover = hit;
        _toolTip.Hide(this);
        Cursor = hit == null ? Cursors.Default : Cursors.Hand;
        if (hit != null)
        {
            _toolTip.Show(BuildTooltip(hit), this, e.X + 14, e.Y + 18, 6000);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        _toolTip.Hide(this);
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        var hit = HitTest(e.Location);
        if (hit == null)
        {
            return;
        }
        string path = hit.IsFilesBlock ? hit.FullPath : hit.FullPath;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 忽略资源管理器启动失败
        }
    }

    private MapItem? HitTest(Point point)
    {
        MapItem? best = null;
        float bestArea = float.MaxValue;
        foreach (var item in _hitItems)
        {
            if (!item.Rect.Contains(point))
            {
                continue;
            }
            float area = item.Rect.Width * item.Rect.Height;
            if (area < bestArea)
            {
                bestArea = area;
                best = item;
            }
        }
        return best;
    }

    private string BuildTooltip(MapItem item)
    {
        double percent = _result != null && _result.TotalBytes > 0
            ? item.Size * 100.0 / _result.TotalBytes
            : 0;
        string kind = item.IsFilesBlock ? "直接文件" : "文件夹";
        return item.FullPath + "\n" + kind + "：" + FormatBytes(item.Size)
            + "（" + percent.ToString("0.0", CultureInfo.InvariantCulture) + "%）";
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildLayout();
        Invalidate();
    }

    private static void Squarify(List<MapItem> items, RectangleF bounds)
    {
        var sorted = items.Where(item => item.Size > 0)
            .OrderByDescending(item => item.Size).ToList();
        if (sorted.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        double total = sorted.Sum(item => (double)item.Size);
        if (total <= 0)
        {
            return;
        }
        double scale = bounds.Width * (double)bounds.Height / total;
        var rect = bounds;
        var row = new List<MapItem>();
        int index = 0;

        while (index < sorted.Count)
        {
            var next = sorted[index];
            if (row.Count == 0)
            {
                row.Add(next);
                index++;
                continue;
            }
            if (WorstAspect(row, next, scale, rect) <= WorstAspect(row, null, scale, rect))
            {
                row.Add(next);
                index++;
            }
            else
            {
                LayoutRow(row, rect, ref rect, scale);
                row.Clear();
            }
        }
        if (row.Count > 0)
        {
            LayoutRow(row, rect, ref rect, scale);
        }
    }

    private static double WorstAspect(List<MapItem> row, MapItem? extra, double scale, RectangleF rect)
    {
        double sum = 0;
        double min = double.MaxValue;
        double max = 0;
        foreach (var item in row)
        {
            double area = item.Size * scale;
            sum += area;
            min = Math.Min(min, area);
            max = Math.Max(max, area);
        }
        if (extra != null)
        {
            double area = extra.Size * scale;
            sum += area;
            min = Math.Min(min, area);
            max = Math.Max(max, area);
        }
        if (sum <= 0 || min <= 0 || max <= 0)
        {
            return double.MaxValue;
        }
        double side = Math.Min(rect.Width, rect.Height);
        if (side <= 0)
        {
            return double.MaxValue;
        }
        return Math.Max(side * side * max / (sum * sum), sum * sum / (side * side * min));
    }

    private static void LayoutRow(List<MapItem> row, RectangleF current, ref RectangleF remaining, double scale)
    {
        double rowArea = row.Sum(item => (double)item.Size * scale);
        if (rowArea <= 0)
        {
            return;
        }

        if (current.Width >= current.Height)
        {
            double stripWidth = rowArea / current.Height;
            stripWidth = Math.Min(stripWidth, current.Width);
            if (stripWidth <= 0)
            {
                return;
            }
            float y = current.Y;
            for (int i = 0; i < row.Count; i++)
            {
                double area = row[i].Size * scale;
                float height = (float)(area / stripWidth);
                if (i == row.Count - 1)
                {
                    height = Math.Max(0, current.Bottom - y);
                }
                row[i].Rect = new RectangleF(current.X, y, (float)stripWidth, height);
                y += height;
            }
            remaining = new RectangleF(current.X + (float)stripWidth, current.Y,
                current.Width - (float)stripWidth, current.Height);
        }
        else
        {
            double stripHeight = rowArea / current.Width;
            stripHeight = Math.Min(stripHeight, current.Height);
            if (stripHeight <= 0)
            {
                return;
            }
            float x = current.X;
            for (int i = 0; i < row.Count; i++)
            {
                double area = row[i].Size * scale;
                float width = (float)(area / stripHeight);
                if (i == row.Count - 1)
                {
                    width = Math.Max(0, current.Right - x);
                }
                row[i].Rect = new RectangleF(x, current.Y, width, (float)stripHeight);
                x += width;
            }
            remaining = new RectangleF(current.X, current.Y + (float)stripHeight,
                current.Width, current.Height - (float)stripHeight);
        }
    }

    private static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }

    private static bool IsDark(Color color)
    {
        return 0.299 * color.R + 0.587 * color.G + 0.114 * color.B < 150;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? bytes.ToString("N0", CultureInfo.InvariantCulture) + " B"
            : value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _headerFont.Dispose();
            _labelFont.Dispose();
            _smallFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class MapItem
    {
        public DirNode? Folder { get; }
        public string Name { get; }
        public string FullPath { get; }
        public long Size { get; }
        public bool IsFilesBlock { get; }
        public Color Color { get; }
        public RectangleF Rect { get; set; }
        public List<MapItem>? Children { get; set; }

        public MapItem(DirNode? folder, string name, string fullPath, long size, bool isFilesBlock, Color color)
        {
            Folder = folder;
            Name = name;
            FullPath = fullPath;
            Size = size;
            IsFilesBlock = isFilesBlock;
            Color = color;
        }
    }
}
