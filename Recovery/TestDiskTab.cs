using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Recovery;

/// <summary>TestDisk 风格的分区表分析、备份、恢复与丢失分区搜索选项卡。</summary>
public sealed class TestDiskTab : UserControl
{
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _bottomStatus;
    private readonly ComboBox _diskCombo;
    private readonly PillButton _refreshDiskButton;
    private readonly PillButton _analyzeButton;
    private readonly PillButton _searchButton;
    private readonly PillButton _backupButton;
    private readonly PillButton _restoreButton;
    private readonly CheckBox _fullSearch;
    private readonly NumericUpDown _searchGb;
    private readonly ListView _list;
    private readonly TextBox _log;
    private readonly ProgressBar _progress;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _monoFont;

    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _loadedDisks;
    private bool _disposed;
    private List<PartitionEntry> _partitions = new();

    public TestDiskTab()
    {
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);
        _monoFont = new Font("Consolas", 9f);

        _top = new Panel { Dock = DockStyle.Top, Height = 132, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

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
            Text = "TestDisk 复刻（分区表分析 / 备份 / 恢复 / 丢失分区搜索）",
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
        layout.Controls.Add(titleRow, 0, 0);

        var diskBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        diskBar.Controls.Add(NewLabel("物理磁盘：", 72));
        _diskCombo = new ComboBox
        {
            Width = 430,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 1, 8, 0)
        };
        _refreshDiskButton = NewButton("刷新", 64);
        _refreshDiskButton.Click += (_, _) => RefreshDisks();
        _analyzeButton = NewButton("分析分区表", 100, PillButtonStyle.Primary);
        _analyzeButton.Click += (_, _) => _ = AnalyzeAsync();
        diskBar.Controls.AddRange(new Control[]
        {
            _diskCombo, _refreshDiskButton, _analyzeButton
        });
        layout.Controls.Add(diskBar, 0, 1);

        var actionBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 4, 0, 0),
            Margin = new Padding(0)
        };
        _searchButton = NewButton("搜索丢失分区", 112);
        _searchButton.Click += (_, _) => _ = SearchLostAsync();
        _backupButton = NewButton("备份分区表", 100);
        _backupButton.Click += (_, _) => BackupTable();
        _restoreButton = NewButton("恢复分区表", 100, PillButtonStyle.Danger);
        _restoreButton.Click += (_, _) => RestoreTable();
        _fullSearch = new CheckBox
        {
            Text = "全盘搜索",
            Width = 90,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 1, 8, 0)
        };
        _searchGb = new NumericUpDown
        {
            Width = 64,
            Height = 24,
            Minimum = 1,
            Maximum = 4096,
            Value = 4,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 1, 4, 0)
        };
        _fullSearch.CheckedChanged += (_, _) => _searchGb.Enabled = !_fullSearch.Checked;
        var hint = new Label
        {
            Text = "恢复分区表会直接写入物理磁盘，请先备份并确认目标磁盘。",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Danger,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(8, 3, 0, 0)
        };
        actionBar.Controls.AddRange(new Control[]
        {
            _searchButton, _backupButton, _restoreButton,
            _fullSearch, _searchGb, NewLabel("GB", 24), hint
        });
        layout.Controls.Add(actionBar, 0, 2);
        _top.Controls.Add(layout);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None
        };
        _list.Columns.Add("编号", 60);
        _list.Columns.Add("分区表", 80);
        _list.Columns.Add("类型", 220);
        _list.Columns.Add("文件系统", 110);
        _list.Columns.Add("起始 LBA", 110);
        _list.Columns.Add("大小", 110);
        _list.Columns.Add("名称 / 位置", 260);
        _list.SelectedIndexChanged += (_, _) => ShowSelectedPartition();
        _list.DoubleClick += (_, _) => ShowSelectedPartition();

        _log = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 180,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = _monoFont,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _log.Text = "TestDisk 复刻：选择物理磁盘后点击“分析分区表”。\r\n"
            + "操作前请先备份分区表；恢复分区表写入后建议重启。\r\n";

        _progress = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 14,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            BackColor = Color.White
        };
        _bottomStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Text = "尚未分析"
        };

        Controls.Add(_list);
        Controls.Add(_progress);
        Controls.Add(_bottomStatus);
        Controls.Add(_log);
        Controls.Add(_top);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_progress, 1);
        Controls.SetChildIndex(_bottomStatus, 2);
        Controls.SetChildIndex(_log, 3);
        Controls.SetChildIndex(_top, 4);

        Disposed += (_, _) =>
        {
            _disposed = true;
            try { _cts?.Cancel(); } catch { }
        };
    }

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

    private Label NewLabel(string text, int width)
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
            Margin = new Padding(0, 2, 4, 0)
        };
    }

    public void SetActive(bool active)
    {
        if (active && !_loadedDisks)
        {
            _loadedDisks = true;
            RefreshDisks();
        }
    }

    private void RefreshDisks()
    {
        string current = (_diskCombo.SelectedItem as RecoveryPhysicalDisk)?.Number.ToString() ?? "";
        var disks = RecoveryDisk.GetPhysicalDisks();
        _diskCombo.BeginUpdate();
        _diskCombo.Items.Clear();
        foreach (var disk in disks)
        {
            _diskCombo.Items.Add(disk);
        }
        _diskCombo.EndUpdate();
        if (_diskCombo.Items.Count > 0)
        {
            int index = 0;
            for (int i = 0; i < _diskCombo.Items.Count; i++)
            {
                if (_diskCombo.Items[i] is RecoveryPhysicalDisk disk
                    && disk.Number.ToString() == current)
                {
                    index = i;
                    break;
                }
            }
            _diskCombo.SelectedIndex = index;
        }
        else
        {
            Log("未找到物理磁盘。请以管理员身份运行。");
        }
    }

    private async Task AnalyzeAsync()
    {
        if (_busy || _disposed)
        {
            return;
        }
        if (_diskCombo.SelectedItem is not RecoveryPhysicalDisk disk)
        {
            MessageBox.Show(FindForm(), "请先选择物理磁盘。",
                "TestDisk 复刻", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _busy = true;
        _analyzeButton.Enabled = false;
        _statusLabel.Text = "● 正在分析分区表…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        try
        {
            var (ok, partitions, summary, error) = await Task.Run(() =>
            {
                bool result = PartitionAnalyzer.Analyze(disk.Number,
                    out var list, out string text, out string err);
                return (result, list, text, err);
            }).ConfigureAwait(true);
            if (_disposed || IsDisposed)
            {
                return;
            }
            if (!ok)
            {
                _statusLabel.Text = "● 分析失败：" + error;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
                Log("分析失败：" + error);
                return;
            }
            _partitions = partitions;
            RefreshList();
            Log("=== " + summary + " ===");
            foreach (var partition in partitions)
            {
                Log(FormatPartition(partition));
            }
            _statusLabel.Text = "● 分析完成 · 找到 " + partitions.Count + " 个分区";
            _statusLabel.ForeColor = ShutdownTheme.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("TestDisk 分析失败：" + ex);
            _statusLabel.Text = "● 分析失败：" + ex.Message;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
            Log("分析失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            if (!_disposed && !IsDisposed)
            {
                _analyzeButton.Enabled = true;
                UpdateBottomStatus();
            }
        }
    }

    private async Task SearchLostAsync()
    {
        if (_busy || _disposed)
        {
            return;
        }
        if (_diskCombo.SelectedItem is not RecoveryPhysicalDisk disk)
        {
            MessageBox.Show(FindForm(), "请先选择物理磁盘。",
                "TestDisk 复刻", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(FindForm(),
            "将扫描磁盘以查找丢失的 NTFS / FAT / ext 分区签名。\n"
            + "全盘扫描可能耗时很长，建议先在空闲时执行。\n\n继续吗？",
            "TestDisk 复刻", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        _busy = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _searchButton.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        _statusLabel.Text = "● 正在搜索丢失分区…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        try
        {
            long maxBytes = _fullSearch.Checked
                ? 0 : (long)_searchGb.Value * 1024 * 1024 * 1024;
            var found = await Task.Run(() => PartitionAnalyzer.SearchLostPartitions(
                disk.Number, maxBytes, token,
                (percent, text) => ReportProgress(percent, text)), token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }
            foreach (var partition in found)
            {
                if (!_partitions.Any(item => item.StartLba == partition.StartLba))
                {
                    _partitions.Add(partition);
                }
            }
            RefreshList();
            foreach (var partition in found)
            {
                Log("搜索到：" + FormatPartition(partition));
            }
            _statusLabel.Text = "● 搜索完成 · 找到 " + found.Count + " 个候选分区";
            _statusLabel.ForeColor = ShutdownTheme.Success;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = "● 搜索已停止";
                _statusLabel.ForeColor = ShutdownTheme.Muted;
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "● 搜索失败：" + ex.Message;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
            Log("搜索失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            if (!_disposed && !IsDisposed)
            {
                _searchButton.Enabled = true;
                _progress.Visible = false;
                UpdateBottomStatus();
            }
        }
    }

    private void ReportProgress(int percent, string text)
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        void Apply()
        {
            if (_disposed || IsDisposed)
            {
                return;
            }
            _progress.Value = Math.Clamp(percent, 0, 100);
            _statusLabel.Text = "● " + text;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(Apply)); } catch { }
            return;
        }
        Apply();
    }

    private void BackupTable()
    {
        if (_diskCombo.SelectedItem is not RecoveryPhysicalDisk disk)
        {
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "备份分区表",
            Filter = "分区表备份|*.bin|所有文件|*.*",
            FileName = "PartitionTable_Disk" + disk.Number + "_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bin"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        if (PartitionAnalyzer.BackupPartitionTable(disk.Number,
            dialog.FileName, out string error))
        {
            Log("分区表已备份：" + dialog.FileName);
            MessageBox.Show(FindForm(), "分区表备份完成：\n" + dialog.FileName,
                "TestDisk 复刻", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(FindForm(), "备份失败：\n" + error,
                "TestDisk 复刻", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreTable()
    {
        if (_diskCombo.SelectedItem is not RecoveryPhysicalDisk disk)
        {
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "选择分区表备份",
            Filter = "分区表备份|*.bin|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        if (MessageBox.Show(FindForm(),
            "即将把备份文件写入物理磁盘 " + disk.Number + " 的引导扇区。\n"
            + "错误的分区表可能导致系统无法启动或数据丢失。\n\n"
            + "请确认已备份当前分区表，并选择正确的磁盘。\n\n继续吗？",
            "TestDisk 复刻", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        if (MessageBox.Show(FindForm(),
            "最后确认：写入磁盘 " + disk.Number + " ？",
            "TestDisk 复刻", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        if (PartitionAnalyzer.RestorePartitionTable(disk.Number,
            dialog.FileName, out string error))
        {
            Log("分区表已从备份恢复：" + dialog.FileName);
            MessageBox.Show(FindForm(),
                "分区表恢复完成。\n请重启计算机使更改生效。",
                "TestDisk 复刻", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(FindForm(), "恢复失败：\n" + error,
                "TestDisk 复刻", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            int row = 0;
            foreach (var partition in _partitions)
            {
                var item = new ListViewItem(partition.Index.ToString())
                {
                    Tag = partition
                };
                item.SubItems.Add(partition.Table);
                item.SubItems.Add(partition.Type);
                item.SubItems.Add(partition.FileSystem);
                item.SubItems.Add(partition.StartLba.ToString());
                item.SubItems.Add(partition.SizeText);
                item.SubItems.Add(partition.Name);
                if (row++ % 2 == 1)
                {
                    item.BackColor = Color.FromArgb(248, 250, 252);
                }
                item.ForeColor = partition.Table == "搜索"
                    ? ShutdownTheme.WarningText : ShutdownTheme.Text;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateBottomStatus();
    }

    private void ShowSelectedPartition()
    {
        if (_list.SelectedItems.Count == 0
            || _list.SelectedItems[0].Tag is not PartitionEntry partition)
        {
            return;
        }
        _bottomStatus.Text = "分区 " + partition.Index + " · "
            + partition.FileSystem + " · 起始 LBA " + partition.StartLba
            + " · 大小 " + partition.SizeText;
    }

    private void UpdateBottomStatus()
    {
        if (_bottomStatus.Text.StartsWith("分区 ", StringComparison.Ordinal))
        {
            return;
        }
        _bottomStatus.Text = "共 " + _partitions.Count + " 个分区 / 候选"
            + " · 选择分区查看详情";
    }

    private static string FormatPartition(PartitionEntry partition) =>
        partition.Index + ". [" + partition.Table + "] " + partition.Type
        + " · " + partition.FileSystem + " · 起始 LBA " + partition.StartLba
        + " · " + partition.SizeText + " · " + partition.Name;

    private void Log(string text)
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text
            + Environment.NewLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _monoFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
