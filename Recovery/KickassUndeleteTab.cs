using System.Diagnostics;
using System.Windows.Forms;

namespace MiniMonitor.Recovery;

/// <summary>Kickass Undelete v1.5.5 风格的已删除文件恢复选项卡。</summary>
public sealed class KickassUndeleteTab : UserControl
{
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _bottomStatus;
    private readonly ComboBox _volumeCombo;
    private readonly PillButton _refreshVolumesButton;
    private readonly PillButton _scanButton;
    private readonly PillButton _stopButton;
    private readonly TextBox _outputBox;
    private readonly PillButton _browseButton;
    private readonly PillButton _recoverButton;
    private readonly PillButton _selectAllButton;
    private readonly TextBox _filterBox;
    private readonly CheckBox _onlyGood;
    private readonly ListView _list;
    private readonly ProgressBar _progress;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly ContextMenuStrip _menu;

    private CancellationTokenSource? _cts;
    private List<RecoverableFile> _files = new();
    private bool _scanning;
    private bool _recovering;
    private bool _loadedVolumes;
    private bool _disposed;

    public KickassUndeleteTab()
    {
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);

        _top = new Panel { Dock = DockStyle.Top, Height = 158, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

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
            Text = "Kickass Undelete复刻（NTFS / FAT 已删除文件恢复）",
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

        var scanBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        scanBar.Controls.Add(NewLabel("扫描卷：", 62));
        _volumeCombo = new ComboBox
        {
            Width = 360,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 1, 8, 0)
        };
        _refreshVolumesButton = NewButton("刷新卷", 76);
        _refreshVolumesButton.Click += (_, _) => RefreshVolumes();
        _scanButton = NewButton("扫描已删除文件", 124, PillButtonStyle.Primary);
        _scanButton.Click += (_, _) => _ = ScanAsync();
        _stopButton = NewButton("停止", 68);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopScan();
        scanBar.Controls.AddRange(new Control[]
        {
            _volumeCombo, _refreshVolumesButton, _scanButton, _stopButton
        });
        layout.Controls.Add(scanBar, 0, 1);

        var recoverBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 4, 0, 0),
            Margin = new Padding(0)
        };
        recoverBar.Controls.Add(NewLabel("恢复目录：", 72));
        _outputBox = new TextBox
        {
            Width = 330,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "请选择另一块磁盘上的文件夹",
            Margin = new Padding(0, 1, 8, 0)
        };
        _browseButton = NewButton("浏览…", 72);
        _browseButton.Click += (_, _) => BrowseOutput();
        _recoverButton = NewButton("恢复选中", 92);
        _recoverButton.Click += (_, _) => _ = RecoverSelectedAsync();
        _selectAllButton = NewButton("全选", 68);
        _selectAllButton.Click += (_, _) => SelectAll();
        recoverBar.Controls.AddRange(new Control[]
        {
            _outputBox, _browseButton, _recoverButton, _selectAllButton
        });
        layout.Controls.Add(recoverBar, 0, 2);

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 4, 0, 0),
            Margin = new Padding(0)
        };
        filterBar.Controls.Add(NewLabel("筛选：", 42));
        _filterBox = new TextBox
        {
            Width = 280,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "文件名 / 路径 / 扩展名…",
            Margin = new Padding(0, 1, 10, 0)
        };
        _onlyGood = new CheckBox
        {
            Text = "仅显示恢复几率良好",
            Width = 150,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 1, 10, 0)
        };
        var hint = new Label
        {
            Text = "NTFS 读取 $MFT/$Bitmap；FAT 读取目录项。恢复文件请保存到另一块磁盘。",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(6, 3, 0, 0)
        };
        filterBar.Controls.AddRange(new Control[]
        {
            _filterBox, _onlyGood, hint
        });
        layout.Controls.Add(filterBar, 0, 3);
        _top.Controls.Add(layout);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = true,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None
        };
        _list.Columns.Add("文件名", 220);
        _list.Columns.Add("原路径", 380);
        _list.Columns.Add("大小", 100);
        _list.Columns.Add("修改时间", 140);
        _list.Columns.Add("恢复几率", 150);
        _list.Columns.Add("文件系统", 80);
        _list.Columns.Add("MFT 记录", 90);
        _list.Columns.Add("状态", 80);
        _list.ColumnClick += (_, e) =>
        {
            if (e.Column < 0)
            {
                return;
            }
            SortBy(e.Column);
        };

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
            Text = "尚未扫描"
        };

        Controls.Add(_list);
        Controls.Add(_progress);
        Controls.Add(_bottomStatus);
        Controls.Add(_top);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_progress, 1);
        Controls.SetChildIndex(_bottomStatus, 2);
        Controls.SetChildIndex(_top, 3);

        _menu = new ContextMenuStrip();
        _menu.Items.Add("恢复选中", null, (_, _) => _ = RecoverSelectedAsync());
        _menu.Items.Add("全选", null, (_, _) => SelectAll());
        _menu.Items.Add("复制原路径", null, (_, _) => CopySelectedPath());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("打开恢复目录", null, (_, _) => OpenOutputFolder());
        _list.ContextMenuStrip = _menu;
        _list.DoubleClick += (_, _) => _ = RecoverSelectedAsync();

        _filterBox.TextChanged += (_, _) => RefreshList();
        _onlyGood.CheckedChanged += (_, _) => RefreshList();
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
        if (active && !_loadedVolumes)
        {
            _loadedVolumes = true;
            RefreshVolumes();
        }
        if (!active)
        {
            StopScan();
        }
    }

    private void RefreshVolumes()
    {
        string current = (_volumeCombo.SelectedItem as RecoveryVolume)?.RootPath ?? "";
        var volumes = RecoveryDisk.GetVolumes();
        _volumeCombo.BeginUpdate();
        _volumeCombo.Items.Clear();
        foreach (var volume in volumes)
        {
            _volumeCombo.Items.Add(volume);
        }
        _volumeCombo.EndUpdate();
        if (_volumeCombo.Items.Count > 0)
        {
            int index = 0;
            for (int i = 0; i < _volumeCombo.Items.Count; i++)
            {
                if (_volumeCombo.Items[i] is RecoveryVolume volume
                    && volume.RootPath == current)
                {
                    index = i;
                    break;
                }
            }
            _volumeCombo.SelectedIndex = index;
        }
        else
        {
            _statusLabel.Text = "● 未找到可扫描的固定 / 可移动卷";
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择恢复文件保存目录（建议使用另一块磁盘）",
            UseDescriptionForTitle = true
        };
        if (_outputBox.Text.Length > 0 && Directory.Exists(_outputBox.Text))
        {
            dialog.SelectedPath = _outputBox.Text;
        }
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _outputBox.Text = dialog.SelectedPath;
        }
    }

    private void StopScan()
    {
        try { _cts?.Cancel(); } catch { }
    }

    private async Task ScanAsync()
    {
        if (_scanning || _disposed)
        {
            return;
        }
        if (_volumeCombo.SelectedItem is not RecoveryVolume volume)
        {
            MessageBox.Show(FindForm(), "请先选择要扫描的卷。",
                "Kickass Undelete 复刻", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        _scanning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _scanButton.Enabled = false;
        _stopButton.Enabled = true;
        _refreshingUi(true);
        _statusLabel.Text = "● 正在扫描 " + volume.RootPath + " …";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        try
        {
            var result = await Task.Run(() => DeletedFileScanner.Scan(volume,
                token, (percent, text) => ReportProgress(percent, text)), token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }
            _files = result.Files;
            RefreshList();
            if (result.Error.Length > 0)
            {
                _statusLabel.Text = "● " + result.Error;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
            else
            {
                _statusLabel.Text = "● 扫描完成 · 发现 " + result.Files.Count
                    + " 个已删除文件（" + result.FileSystem + "）";
                _statusLabel.ForeColor = ShutdownTheme.Success;
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = "● 已停止";
                _statusLabel.ForeColor = ShutdownTheme.Muted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Kickass Undelete 扫描失败：" + ex);
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
                _scanButton.Enabled = true;
                _stopButton.Enabled = false;
                _refreshingUi(false);
                UpdateBottomStatus();
            }
        }
    }

    private void _refreshingUi(bool scanning)
    {
        _progress.Visible = scanning;
        if (scanning)
        {
            _progress.Value = 0;
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

    private void RefreshList()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        string filter = _filterBox.Text.Trim();
        var files = _files.AsEnumerable();
        if (filter.Length > 0)
        {
            files = files.Where(file =>
                file.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || file.OriginalPath.Contains(filter,
                    StringComparison.OrdinalIgnoreCase)
                || file.Extension.Contains(filter,
                    StringComparison.OrdinalIgnoreCase));
        }
        if (_onlyGood.Checked)
        {
            files = files.Where(file => file.Status.StartsWith("良好",
                StringComparison.Ordinal));
        }
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            int row = 0;
            foreach (var file in files)
            {
                var item = new ListViewItem(file.Name) { Tag = file };
                item.SubItems.Add(file.OriginalPath);
                item.SubItems.Add(RecoveryDisk.FormatSize(file.Size));
                item.SubItems.Add(file.Modified == DateTime.MinValue ? ""
                    : file.Modified.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(file.Status);
                item.SubItems.Add(file.FileSystem);
                item.SubItems.Add(file.FileSystem == "NTFS"
                    ? file.RecordNumber.ToString() : "");
                item.SubItems.Add(file.Resident ? "驻留" : "非驻留");
                if (row++ % 2 == 1)
                {
                    item.BackColor = Color.FromArgb(248, 250, 252);
                }
                item.ForeColor = file.Status.StartsWith("良好",
                    StringComparison.Ordinal)
                    ? Color.FromArgb(6, 95, 70)
                    : file.Status.StartsWith("已覆盖", StringComparison.Ordinal)
                        ? Color.FromArgb(148, 163, 184)
                        : ShutdownTheme.WarningText;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateBottomStatus();
    }

    private void SortBy(int column)
    {
        _files = column switch
        {
            0 => _files.OrderBy(file => file.Name,
                StringComparer.CurrentCultureIgnoreCase).ToList(),
            1 => _files.OrderBy(file => file.OriginalPath,
                StringComparer.CurrentCultureIgnoreCase).ToList(),
            2 => _files.OrderBy(file => file.Size).ToList(),
            3 => _files.OrderBy(file => file.Modified).ToList(),
            4 => _files.OrderBy(file => file.Status,
                StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => _files
        };
        RefreshList();
    }

    private void UpdateBottomStatus()
    {
        _bottomStatus.Text = "共 " + _files.Count + " 个已删除文件"
            + (_files.Count == 0 ? "" : " · 选择文件后点击“恢复选中”")
            + " · 恢复目录：" + (_outputBox.Text.Length > 0
                ? _outputBox.Text : "（未选择）");
    }

    private void SelectAll()
    {
        foreach (ListViewItem item in _list.Items)
        {
            item.Selected = true;
        }
    }

    private void CopySelectedPath()
    {
        if (_list.SelectedItems.Count == 0
            || _list.SelectedItems[0].Tag is not RecoverableFile file)
        {
            return;
        }
        try { Clipboard.SetText(file.OriginalPath); } catch { }
    }

    private void OpenOutputFolder()
    {
        string output = _outputBox.Text.Trim();
        if (output.Length == 0 || !Directory.Exists(output))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = output,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private async Task RecoverSelectedAsync()
    {
        if (_recovering)
        {
            return;
        }
        if (_volumeCombo.SelectedItem is not RecoveryVolume volume)
        {
            return;
        }
        var files = _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as RecoverableFile)
            .Where(file => file != null)
            .Select(file => file!)
            .ToList();
        if (files.Count == 0)
        {
            MessageBox.Show(FindForm(), "请先选择要恢复的文件。",
                "Kickass Undelete 复刻", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        string output = _outputBox.Text.Trim();
        if (output.Length == 0)
        {
            BrowseOutput();
            output = _outputBox.Text.Trim();
        }
        if (output.Length == 0)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(output);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "无法创建恢复目录：\n" + ex.Message,
                "Kickass Undelete 复刻", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        if (output.StartsWith(volume.RootPath,
            StringComparison.OrdinalIgnoreCase)
            && MessageBox.Show(FindForm(),
                "恢复目录位于待恢复数据的同一磁盘卷上，继续写入可能覆盖尚未恢复的文件。\n\n"
                + "仍要继续吗？", "Kickass Undelete 复刻",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        _recovering = true;
        _recoverButton.Enabled = false;
        _recoverButton.Text = "恢复中…";
        _statusLabel.Text = "● 正在恢复 " + files.Count + " 个文件…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        try
        {
            var (success, failed, errors) = await Task.Run(() =>
            {
                int ok = 0;
                int bad = 0;
                var messages = new List<string>();
                foreach (var file in files)
                {
                    if (DeletedFileScanner.Recover(file, volume, output,
                        out _, out string error))
                    {
                        ok++;
                    }
                    else
                    {
                        bad++;
                        if (messages.Count < 5 && error.Length > 0)
                        {
                            messages.Add(file.Name + "：" + error);
                        }
                    }
                }
                return (ok, bad, messages);
            }).ConfigureAwait(true);
            if (_disposed || IsDisposed)
            {
                return;
            }
            _statusLabel.Text = "● 恢复完成 · 成功 " + success
                + " 个 · 失败 " + failed + " 个";
            _statusLabel.ForeColor = failed == 0
                ? ShutdownTheme.Success : ShutdownTheme.WarningText;
            MessageBox.Show(FindForm(),
                "恢复完成。\n\n成功：" + success + " 个\n失败：" + failed + " 个"
                + (errors.Count > 0
                    ? "\n\n前几个错误：\n" + string.Join("\n", errors) : ""),
                "Kickass Undelete 复刻", MessageBoxButtons.OK,
                failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (success > 0 && MessageBox.Show(FindForm(), "是否打开恢复目录？",
                "Kickass Undelete 复刻", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                OpenOutputFolder();
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "● 恢复失败：" + ex.Message;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
        finally
        {
            _recovering = false;
            if (!_disposed && !IsDisposed)
            {
                _recoverButton.Enabled = true;
                _recoverButton.Text = "恢复选中";
            }
        }
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
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
