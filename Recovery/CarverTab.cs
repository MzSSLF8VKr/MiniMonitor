using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace MiniMonitor.Recovery;

/// <summary>FreeRecover / PhotoRec 共用的签名雕刻选项卡。</summary>
public sealed class CarverTab : UserControl
{
    private readonly string _title;
    private readonly string _description;
    private readonly bool _includePhysical;
    private readonly bool _defaultAllCategories;
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _bottomStatus;
    private readonly ComboBox _sourceCombo;
    private readonly TextBox _outputBox;
    private readonly PillButton _browseButton;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly CheckBox _fullScan;
    private readonly NumericUpDown _limitGb;
    private readonly CheckBox _images;
    private readonly CheckBox _videos;
    private readonly CheckBox _audios;
    private readonly CheckBox _documents;
    private readonly CheckBox _archives;
    private readonly ListView _list;
    private readonly ProgressBar _progress;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _smallBoldFont;

    private CancellationTokenSource? _cts;
    private bool _scanning;
    private bool _loadedSources;
    private bool _active;
    private bool _disposed;
    private List<CarvedFile> _results = new();

    public CarverTab(string title, string description, bool includePhysical,
        bool defaultAllCategories)
    {
        _title = title;
        _description = description;
        _includePhysical = includePhysical;
        _defaultAllCategories = defaultAllCategories;
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);
        _smallBoldFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);

        _top = new Panel { Dock = DockStyle.Top, Height = 162, BackColor = Color.White };
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
            Text = _title + "（" + _description + "）",
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

        var sourceBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        sourceBar.Controls.Add(NewLabel("扫描源：", 62));
        _sourceCombo = new ComboBox
        {
            Width = 430,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 1, 8, 0)
        };
        _startButton = NewButton("开始扫描", 92, PillButtonStyle.Primary);
        _startButton.Click += (_, _) => _ = ScanAsync();
        _stopButton = NewButton("停止", 68);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopScan();
        sourceBar.Controls.AddRange(new Control[]
        {
            _sourceCombo, _startButton, _stopButton
        });
        layout.Controls.Add(sourceBar, 0, 1);

        var outputBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 4, 0, 0),
            Margin = new Padding(0)
        };
        outputBar.Controls.Add(NewLabel("恢复目录：", 72));
        _outputBox = new TextBox
        {
            Width = 340,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "请选择另一块磁盘上的文件夹",
            Margin = new Padding(0, 1, 8, 0)
        };
        _browseButton = NewButton("浏览…", 72);
        _browseButton.Click += (_, _) => BrowseOutput();
        _fullScan = NewCheck("全盘扫描（较慢）", 132);
        _limitGb = new NumericUpDown
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
        _fullScan.CheckedChanged += (_, _) =>
        {
            _limitGb.Enabled = !_fullScan.Checked;
        };
        outputBar.Controls.AddRange(new Control[]
        {
            _outputBox, _browseButton, _fullScan, _limitGb, NewLabel("GB", 24)
        });
        layout.Controls.Add(outputBar, 0, 2);

        var typeBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 5, 0, 0),
            Margin = new Padding(0)
        };
        _images = NewCheck("图片", 62);
        _videos = NewCheck("视频", 62);
        _audios = NewCheck("音频", 62);
        _documents = NewCheck("文档", 62);
        _archives = NewCheck("压缩包", 76);
        bool all = _defaultAllCategories;
        _images.Checked = all;
        _videos.Checked = all;
        _audios.Checked = all;
        _documents.Checked = all;
        _archives.Checked = all;
        var hint = new Label
        {
            Text = "按文件签名雕刻；恢复文件请务必保存到另一块磁盘，避免覆盖待恢复数据。",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(8, 3, 0, 0)
        };
        typeBar.Controls.AddRange(new Control[]
        {
            NewLabel("文件类型：", 72), _images, _videos, _audios,
            _documents, _archives, hint
        });
        layout.Controls.Add(typeBar, 0, 3);
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
        _list.Columns.Add("类型", 200);
        _list.Columns.Add("扩展名", 80);
        _list.Columns.Add("磁盘偏移", 150);
        _list.Columns.Add("大小", 100);
        _list.Columns.Add("输出文件", 520);
        _list.DoubleClick += (_, _) => OpenSelectedOutput();

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

    private CheckBox NewCheck(string text, int width)
    {
        return new CheckBox
        {
            Text = text,
            Width = width,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 1, 8, 0)
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
        _active = active;
        if (active && !_loadedSources)
        {
            _loadedSources = true;
            RefreshSources();
        }
        if (!active)
        {
            StopScan();
        }
    }

    private void RefreshSources()
    {
        string current = _sourceCombo.SelectedItem?.ToString() ?? "";
        try
        {
            var sources = RecoveryDisk.GetSources(_includePhysical);
            _sourceCombo.BeginUpdate();
            _sourceCombo.Items.Clear();
            foreach (var source in sources)
            {
                _sourceCombo.Items.Add(source);
            }
            _sourceCombo.EndUpdate();
            if (_sourceCombo.Items.Count > 0)
            {
                int index = 0;
                for (int i = 0; i < _sourceCombo.Items.Count; i++)
                {
                    if (_sourceCombo.Items[i]?.ToString() == current)
                    {
                        index = i;
                        break;
                    }
                }
                _sourceCombo.SelectedIndex = index;
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "● 枚举磁盘失败：" + ex.Message;
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
        if (_sourceCombo.SelectedItem is not RecoverySource source)
        {
            MessageBox.Show(FindForm(), "请先选择扫描源。",
                _title, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                _title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (!source.IsPhysical && source.Volume != null
            && output.StartsWith(source.Volume.RootPath,
                StringComparison.OrdinalIgnoreCase)
            && MessageBox.Show(FindForm(),
                "恢复目录位于待扫描的同一磁盘卷上，继续写入可能覆盖尚未恢复的数据。\n\n"
                + "仍要继续吗？", _title, MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        var types = FileCarver.CreateTypes();
        foreach (var type in types)
        {
            type.Enabled = type.Category switch
            {
                "图片" => _images.Checked,
                "视频" => _videos.Checked,
                "音频" => _audios.Checked,
                "文档" => _documents.Checked,
                "压缩包" => _archives.Checked,
                _ => false
            };
        }
        if (!types.Any(type => type.Enabled))
        {
            MessageBox.Show(FindForm(), "请至少勾选一种文件类型。",
                _title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        long maxBytes = _fullScan.Checked
            ? 0 : (long)_limitGb.Value * 1024 * 1024 * 1024;
        _scanning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _progress.Visible = true;
        _progress.Value = 0;
        _statusLabel.Text = "● 正在按签名扫描…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        try
        {
            using SafeFileHandle? handle = RecoveryDisk.OpenSource(source);
            if (handle == null)
            {
                _statusLabel.Text = "● 无法打开扫描源（请以管理员身份运行）";
                _statusLabel.ForeColor = ShutdownTheme.Danger;
                return;
            }
            long length = source.Length > 0
                ? source.Length : RecoveryDisk.GetLength(handle);
            var results = await Task.Run(() => FileCarver.Carve(handle, length,
                types, output, maxBytes, token,
                (percent, text) => ReportProgress(percent, text)), token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }
            _results = results;
            RefreshList();
            _statusLabel.Text = "● 扫描完成 · 恢复 " + results.Count + " 个文件";
            _statusLabel.ForeColor = ShutdownTheme.Success;
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
            Debug.WriteLine(_title + " 扫描失败：" + ex);
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
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
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
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (_disposed || IsDisposed)
                    {
                        return;
                    }
                    _progress.Value = Math.Clamp(percent, 0, 100);
                    _statusLabel.Text = "● " + text;
                }));
            }
            catch
            {
            }
            return;
        }
        _progress.Value = Math.Clamp(percent, 0, 100);
        _statusLabel.Text = "● " + text;
    }

    private void RefreshList()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            int row = 0;
            foreach (var file in _results)
            {
                var item = new ListViewItem(file.TypeName) { Tag = file };
                item.SubItems.Add(file.Extension);
                item.SubItems.Add("0x" + file.Offset.ToString("X"));
                item.SubItems.Add(RecoveryDisk.FormatSize(file.Size));
                item.SubItems.Add(file.OutputPath);
                if (row++ % 2 == 1)
                {
                    item.BackColor = Color.FromArgb(248, 250, 252);
                }
                item.ForeColor = Color.FromArgb(6, 95, 70);
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateBottomStatus();
    }

    private void UpdateBottomStatus()
    {
        _bottomStatus.Text = "共恢复 " + _results.Count + " 个文件"
            + (_results.Count == 0 ? "" : " · 请到恢复目录查看")
            + " · 输出：" + (_outputBox.Text.Length > 0
                ? _outputBox.Text : "（未选择）");
    }

    private void OpenSelectedOutput()
    {
        if (_list.SelectedItems.Count == 0
            || _list.SelectedItems[0].Tag is not CarvedFile file)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + file.OutputPath + "\"",
                UseShellExecute = true
            });
        }
        catch
        {
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
            _smallBoldFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
