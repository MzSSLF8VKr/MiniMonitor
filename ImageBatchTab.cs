using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>批量处理：批量缩放 / 格式转换 / 压缩质量 / 命名规则 / 保留 EXIF。</summary>
internal sealed class ImageBatchTab : UserControl
{
    private enum BatchResizeMode
    {
        Keep,
        Percent,
        LongEdge,
        Width,
        Explicit
    }

    private sealed class BatchOptions
    {
        public bool SameFolder;
        public string OutputDir = "";
        public int FormatIndex;
        public int Quality = 90;
        public BatchResizeMode Resize;
        public int ResizeValue;
        public int Width;
        public int Height;
        public bool NoUpscale;
        public int Naming;
        public bool Overwrite;
        public bool KeepExif;
    }

    private readonly ListView _list;
    private readonly Label _countLabel;
    private readonly Label _statusLabel;
    private readonly Label _resizeHint;
    private readonly Label _resizeUnit;
    private readonly Label _resizeX;
    private readonly TextBox _outputDir;
    private readonly ComboBox _formatCombo;
    private readonly ComboBox _resizeModeCombo;
    private readonly ComboBox _namingCombo;
    private readonly NumericUpDown _quality;
    private readonly NumericUpDown _resizeValue;
    private readonly NumericUpDown _resizeWidth;
    private readonly NumericUpDown _resizeHeight;
    private readonly CheckBox _sameFolderCheck;
    private readonly CheckBox _keepExifCheck;
    private readonly CheckBox _noUpscaleCheck;
    private readonly CheckBox _overwriteCheck;
    private readonly ProgressBar _progress;
    private readonly PillButton _addFilesButton;
    private readonly PillButton _addFolderButton;
    private readonly PillButton _removeButton;
    private readonly PillButton _clearButton;
    private readonly PillButton _startButton;
    private readonly PillButton _cancelButton;
    private readonly PillButton _openFolderButton;
    private readonly Panel _footer;
    private bool _running;
    private bool _cancelRequested;
    private bool _updatingResizeUi;

    public ImageBatchTab()
    {
        BackColor = Color.White;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(root);

        // ---------- 顶部操作 ----------
        var toolbar = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            CornerRadius = 14
        };
        _addFilesButton = MakeButton("添加照片", 14, 9, 96, 34, PillButtonStyle.Primary);
        _addFilesButton.Click += (_, _) => AddFilesDialog();
        _addFolderButton = MakeButton("添加文件夹", 118, 9, 106, 34, PillButtonStyle.Light);
        _addFolderButton.Click += (_, _) => AddFolderDialog();
        _removeButton = MakeButton("移除选中", 232, 9, 96, 34, PillButtonStyle.Light);
        _removeButton.Click += (_, _) => RemoveSelected();
        _clearButton = MakeButton("清空列表", 336, 9, 96, 34, PillButtonStyle.Light);
        _clearButton.Click += (_, _) => ClearList();
        _countLabel = MakeLabel("共 0 张照片", 444, 9, 500, 34,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _countLabel.TextAlign = ContentAlignment.MiddleLeft;
        _countLabel.AutoEllipsis = true;
        _countLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        toolbar.Controls.AddRange(new Control[]
        {
            _addFilesButton, _addFolderButton, _removeButton, _clearButton, _countLabel
        });
        toolbar.Resize += (_, _) =>
        {
            _countLabel.Width = Math.Max(100, toolbar.ClientSize.Width - _countLabel.Left - 14);
        };
        root.Controls.Add(toolbar, 0, 0);

        // ---------- 列表 + 设置 ----------
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 342));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        var listCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(10)
        };
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            AllowDrop = true
        };
        _list.Columns.Add("文件名", 260);
        _list.Columns.Add("大小", 80);
        _list.Columns.Add("尺寸", 110);
        _list.Columns.Add("状态", 100);
        _list.Resize += (_, _) => LayoutColumns();
        _list.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                AddDropped(paths);
            }
        };
        _list.SelectedIndexChanged += (_, _) => UpdateCount();
        listCard.Controls.Add(_list);
        body.Controls.Add(listCard, 0, 0);

        var settingsScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        settingsScroll.Controls.Add(stack);
        body.Controls.Add(settingsScroll, 1, 0);

        // ---------- 输出设置 ----------
        var outputCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 236,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var outputTitle = MakeLabel("输出设置", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var dirTitle = MakeLabel("输出目录", 16, 40, 80, 22,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _outputDir = new TextBox
        {
            Location = new Point(16, 62),
            Size = new Size(234, 28),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Text = DefaultOutputDir()
        };
        var browseButton = MakeButton("浏览", 258, 62, 74, 30, PillButtonStyle.Light);
        browseButton.Click += (_, _) => BrowseOutputDir();
        _sameFolderCheck = new CheckBox
        {
            Text = "输出到源文件所在文件夹（不覆盖原文件）",
            Location = new Point(16, 98),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        _sameFolderCheck.CheckedChanged += (_, _) =>
        {
            bool same = _sameFolderCheck.Checked;
            _outputDir.Enabled = !same;
            browseButton.Enabled = !same;
        };
        var formatTitle = MakeLabel("格式", 16, 132, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _formatCombo = new ComboBox
        {
            Location = new Point(60, 130),
            Size = new Size(118, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _formatCombo.Items.AddRange(new object[]
        {
            "保持原格式", "JPEG", "PNG", "BMP", "GIF", "TIFF"
        });
        _formatCombo.SelectedIndex = 0;
        _formatCombo.SelectedIndexChanged += (_, _) => UpdateQualityState();
        var qualityTitle = MakeLabel("质量", 196, 132, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _quality = new NumericUpDown
        {
            Location = new Point(238, 130),
            Size = new Size(60, 28),
            Minimum = 1,
            Maximum = 100,
            Value = 90,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _keepExifCheck = new CheckBox
        {
            Text = "保留 EXIF 信息（默认关闭）",
            Location = new Point(16, 166),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = false
        };
        var outputHint = MakeLabel(
            "默认剔除全部 EXIF/GPS；输出不会覆盖原文件，GIF 仅处理第一帧。",
            16, 196, 300, 34,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        outputCard.Controls.AddRange(new Control[]
        {
            outputTitle, dirTitle, _outputDir, browseButton, _sameFolderCheck,
            formatTitle, _formatCombo, qualityTitle, _quality, _keepExifCheck, outputHint
        });
        stack.Controls.Add(outputCard, 0, 0);

        // ---------- 处理参数 ----------
        var paramCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 250,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var paramTitle = MakeLabel("处理参数", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var resizeTitle = MakeLabel("尺寸", 16, 40, 60, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _resizeModeCombo = new ComboBox
        {
            Location = new Point(60, 38),
            Size = new Size(256, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _resizeModeCombo.Items.AddRange(new object[]
        {
            "保持原始尺寸", "按百分比缩放", "限制最长边", "按宽度缩放", "指定宽高"
        });
        _resizeModeCombo.SelectedIndex = 0;
        _resizeModeCombo.SelectedIndexChanged += (_, _) => OnResizeModeChanged();
        _resizeValue = new NumericUpDown
        {
            Location = new Point(16, 76),
            Size = new Size(110, 28),
            Minimum = 1,
            Maximum = 20000,
            Value = 80,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Visible = false
        };
        _resizeValue.ValueChanged += (_, _) => UpdateResizeHint();
        _resizeUnit = MakeLabel("%", 132, 76, 60, 28,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _resizeUnit.TextAlign = ContentAlignment.MiddleLeft;
        _resizeUnit.Visible = false;
        _resizeWidth = new NumericUpDown
        {
            Location = new Point(16, 76),
            Size = new Size(110, 28),
            Minimum = 1,
            Maximum = 20000,
            Value = 1920,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Visible = false
        };
        _resizeX = MakeLabel("×", 132, 76, 18, 28,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        _resizeX.TextAlign = ContentAlignment.MiddleCenter;
        _resizeX.Visible = false;
        _resizeHeight = new NumericUpDown
        {
            Location = new Point(154, 76),
            Size = new Size(110, 28),
            Minimum = 1,
            Maximum = 20000,
            Value = 1080,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Visible = false
        };
        _resizeWidth.ValueChanged += (_, _) => UpdateResizeHint();
        _resizeHeight.ValueChanged += (_, _) => UpdateResizeHint();
        _resizeHint = MakeLabel("每张照片按自身尺寸计算目标大小", 16, 112, 300, 20,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        _noUpscaleCheck = new CheckBox
        {
            Text = "不放大图片（仅缩小或保持原尺寸）",
            Location = new Point(16, 138),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = true
        };
        var namingTitle = MakeLabel("命名", 16, 174, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _namingCombo = new ComboBox
        {
            Location = new Point(60, 172),
            Size = new Size(256, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _namingCombo.Items.AddRange(new object[]
        {
            "保留原文件名", "原名_已处理", "序号_原名"
        });
        _namingCombo.SelectedIndex = 1;
        _overwriteCheck = new CheckBox
        {
            Text = "覆盖同名输出文件（不会覆盖源文件）",
            Location = new Point(16, 208),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        paramCard.Controls.AddRange(new Control[]
        {
            paramTitle, resizeTitle, _resizeModeCombo, _resizeValue, _resizeUnit,
            _resizeWidth, _resizeX, _resizeHeight, _resizeHint, _noUpscaleCheck,
            namingTitle, _namingCombo, _overwriteCheck
        });
        stack.Controls.Add(paramCard, 0, 1);

        // ---------- 使用说明 ----------
        var hintCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 140,
            Margin = new Padding(0, 0, 0, 0),
            CornerRadius = 14
        };
        var hintTitle = MakeLabel("批量处理说明", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var hintText = MakeLabel(
            "1. 添加照片或文件夹，可把文件直接拖进左侧列表。\n"
            + "2. 支持 JPG / PNG / BMP / GIF / TIFF 相互转换。\n"
            + "3. 处理在后台执行，可随时取消；每张照片状态独立显示。\n"
            + "4. 保留 EXIF 时会同步更新宽高；输出文件绝不覆盖源文件。",
            16, 40, 300, 92,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        hintCard.Controls.AddRange(new Control[] { hintTitle, hintText });
        stack.Controls.Add(hintCard, 0, 2);

        // ---------- 底部进度 ----------
        _footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        _progress = new ProgressBar
        {
            Location = new Point(14, 15),
            Size = new Size(250, 22),
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        _statusLabel = MakeLabel("等待添加照片", 278, 15, 300, 28,
            new Font("Microsoft YaHei UI", 8.3f), ShutdownTheme.Muted, Color.White);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _startButton = MakeButton("开始处理", 0, 14, 120, 34, PillButtonStyle.Primary);
        _startButton.Click += (_, _) => StartBatch();
        _cancelButton = MakeButton("取消", 0, 14, 100, 34, PillButtonStyle.Danger);
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => CancelBatch();
        _openFolderButton = MakeButton("打开输出目录", 0, 14, 120, 34, PillButtonStyle.Light);
        _openFolderButton.Click += (_, _) => OpenOutputFolder();
        _footer.Controls.AddRange(new Control[]
        {
            _progress, _statusLabel, _startButton, _cancelButton, _openFolderButton
        });
        _footer.Resize += (_, _) => LayoutFooter();
        root.Controls.Add(_footer, 0, 2);

        UpdateQualityState();
        OnResizeModeChanged();
        LayoutFooter();
        LayoutColumns();
        UpdateCount();
        _statusLabel.Text = "等待添加照片 · 支持拖放";
    }

    // ================= 布局 =================

    private void LayoutFooter()
    {
        int right = _footer.ClientSize.Width - 14;
        _startButton.Location = new Point(right - _startButton.Width, 14);
        _cancelButton.Location = new Point(_startButton.Left - 10 - _cancelButton.Width, 14);
        _openFolderButton.Location = new Point(_cancelButton.Left - 10 - _openFolderButton.Width, 14);
        _statusLabel.Location = new Point(278, 15);
        _statusLabel.Width = Math.Max(120, _openFolderButton.Left - 16 - _statusLabel.Left);
    }

    private void LayoutColumns()
    {
        if (_list.Columns.Count < 4)
        {
            return;
        }
        int width = Math.Max(300, _list.ClientSize.Width - 4);
        _list.Columns[0].Width = Math.Max(140, (int)(width * 0.44));
        _list.Columns[1].Width = Math.Max(64, (int)(width * 0.14));
        _list.Columns[2].Width = Math.Max(90, (int)(width * 0.22));
        _list.Columns[3].Width = Math.Max(80, width - _list.Columns[0].Width
            - _list.Columns[1].Width - _list.Columns[2].Width);
    }

    // ================= 列表 =================

    private void AddFilesDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "添加照片",
            Filter = PhotoToolkit.OpenFileFilter,
            Multiselect = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void AddFolderDialog()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含照片的文件夹（会包含子文件夹）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            AddFolder(dialog.SelectedPath);
        }
    }

    private void AddDropped(string[] paths)
    {
        var files = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                AddFolder(path);
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }
        }
        if (files.Count > 0)
        {
            AddPaths(files);
        }
    }

    private void AddFolder(string folder)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _statusLabel.Text = "正在扫描文件夹…";
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(PhotoToolkit.IsSupported)
                .Take(5000)
                .ToArray();
            AddPaths(files);
            _statusLabel.Text = "已从文件夹添加 " + files.Length + " 张照片";
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "读取文件夹失败：\n" + ex.Message,
                "批量处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is string path)
            {
                existing.Add(path);
            }
        }

        _list.BeginUpdate();
        try
        {
            foreach (string path in paths)
            {
                if (!PhotoToolkit.IsSupported(path) || !File.Exists(path) || !existing.Add(path))
                {
                    continue;
                }
                var item = new ListViewItem(Path.GetFileName(path)) { Tag = path };
                try
                {
                    var info = PhotoToolkit.ReadFileInfo(path);
                    item.SubItems.Add(FormatSize(info.FileSize));
                    item.SubItems.Add(info.Width + " × " + info.Height);
                }
                catch
                {
                    item.SubItems.Add("—");
                    item.SubItems.Add("—");
                }
                item.SubItems.Add("待处理");
                item.ToolTipText = path;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateCount();
        _statusLabel.Text = "共 " + _list.Items.Count + " 张照片待处理";
    }

    private void RemoveSelected()
    {
        if (_running)
        {
            return;
        }
        foreach (ListViewItem item in _list.SelectedItems.Cast<ListViewItem>().ToArray())
        {
            _list.Items.Remove(item);
        }
        UpdateCount();
    }

    private void ClearList()
    {
        if (_running)
        {
            return;
        }
        _list.Items.Clear();
        _progress.Value = 0;
        UpdateCount();
        _statusLabel.Text = "列表已清空";
    }

    private void UpdateCount()
    {
        _countLabel.Text = "共 " + _list.Items.Count + " 张照片"
            + (_list.SelectedItems.Count > 0 ? " · 已选中 " + _list.SelectedItems.Count + " 张" : "");
        bool has = _list.Items.Count > 0;
        _startButton.Enabled = has && !_running;
        _removeButton.Enabled = !_running && _list.SelectedItems.Count > 0;
        _clearButton.Enabled = !_running && has;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0") + " KB";
        }
        return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
    }

    // ================= 参数 =================

    private static string DefaultOutputDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "MiniMonitor 图片处理");
    }

    private void BrowseOutputDir()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择输出文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(_outputDir.Text))
        {
            dialog.SelectedPath = _outputDir.Text;
        }
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _outputDir.Text = dialog.SelectedPath;
        }
    }

    private void UpdateQualityState()
    {
        bool jpeg = _formatCombo.SelectedIndex is 0 or 1;
        _quality.Enabled = jpeg;
        _quality.BackColor = jpeg ? Color.White : Color.FromArgb(241, 245, 249);
    }

    private void OnResizeModeChanged()
    {
        int mode = _resizeModeCombo.SelectedIndex;
        bool valueMode = mode is 1 or 2 or 3;
        bool explicitMode = mode == 4;
        _resizeValue.Visible = valueMode;
        _resizeUnit.Visible = valueMode;
        _resizeWidth.Visible = explicitMode;
        _resizeHeight.Visible = explicitMode;
        _resizeX.Visible = explicitMode;
        _resizeUnit.Text = mode == 1 ? "%" : "px";
        if (mode == 1)
        {
            _resizeValue.Maximum = 1000;
        }
        else
        {
            _resizeValue.Maximum = 20000;
        }
        if (!_updatingResizeUi)
        {
            _updatingResizeUi = true;
            _resizeValue.Value = mode switch
            {
                1 => 80,
                2 => 1920,
                3 => 1280,
                _ => _resizeValue.Value
            };
            _updatingResizeUi = false;
        }
        UpdateResizeHint();
    }

    private void UpdateResizeHint()
    {
        switch (_resizeModeCombo.SelectedIndex)
        {
            case 1:
                _resizeHint.Text = "每张照片按 " + (int)_resizeValue.Value + "% 缩放";
                break;
            case 2:
                _resizeHint.Text = "长边不超过 " + (int)_resizeValue.Value + " 像素";
                break;
            case 3:
                _resizeHint.Text = "统一缩放到宽 " + (int)_resizeValue.Value + " 像素";
                break;
            case 4:
                _resizeHint.Text = "统一缩放到 " + (int)_resizeWidth.Value + " × " + (int)_resizeHeight.Value;
                break;
            default:
                _resizeHint.Text = "不改变照片像素尺寸";
                break;
        }
    }

    // ================= 执行 =================

    private void CancelBatch()
    {
        if (!_running)
        {
            return;
        }
        _cancelRequested = true;
        _cancelButton.Enabled = false;
        _cancelButton.Text = "正在取消…";
        _statusLabel.Text = "正在取消，等待当前照片处理完成…";
    }

    private async void StartBatch()
    {
        if (_running || _list.Items.Count == 0)
        {
            return;
        }

        var options = new BatchOptions
        {
            SameFolder = _sameFolderCheck.Checked,
            OutputDir = _outputDir.Text.Trim(),
            FormatIndex = _formatCombo.SelectedIndex,
            Quality = (int)_quality.Value,
            Resize = (BatchResizeMode)_resizeModeCombo.SelectedIndex,
            ResizeValue = (int)_resizeValue.Value,
            Width = (int)_resizeWidth.Value,
            Height = (int)_resizeHeight.Value,
            NoUpscale = _noUpscaleCheck.Checked,
            Naming = _namingCombo.SelectedIndex,
            Overwrite = _overwriteCheck.Checked,
            KeepExif = _keepExifCheck.Checked
        };
        if (!options.SameFolder)
        {
            if (string.IsNullOrWhiteSpace(options.OutputDir))
            {
                MessageBox.Show(FindForm(), "请选择输出目录。", "批量处理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Directory.CreateDirectory(options.OutputDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "无法创建输出目录：\n" + ex.Message,
                    "批量处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        _running = true;
        _cancelRequested = false;
        _cancelButton.Text = "取消";
        _cancelButton.Enabled = true;
        _startButton.Enabled = false;
        _addFilesButton.Enabled = false;
        _addFolderButton.Enabled = false;
        _removeButton.Enabled = false;
        _clearButton.Enabled = false;
        _progress.Minimum = 0;
        _progress.Maximum = _list.Items.Count;
        _progress.Value = 0;

        int completed = 0;
        int failed = 0;
        int total = _list.Items.Count;
        int index = 0;
        try
        {
            foreach (ListViewItem item in _list.Items.Cast<ListViewItem>().ToArray())
            {
                if (IsDisposed || Disposing || _cancelRequested)
                {
                    break;
                }
                string path = item.Tag as string ?? "";
                item.SubItems[3].Text = "处理中…";
                _statusLabel.Text = "正在处理 " + (index + 1) + " / " + total + "：" + item.Text;
                _list.EnsureVisible(item.Index);
                int currentIndex = index;
                var result = await Task.Run(() => ProcessOne(path, currentIndex, options));
                if (IsDisposed || Disposing)
                {
                    break;
                }
                if (result.Success)
                {
                    item.SubItems[3].Text = "完成";
                    item.ToolTipText = "输出：" + result.Output;
                    completed++;
                }
                else
                {
                    item.SubItems[3].Text = "失败";
                    item.ToolTipText = result.Error;
                    failed++;
                }
                index++;
                _progress.Value = Math.Min(index, _progress.Maximum);
                _countLabel.Text = "共 " + total + " 张照片 · 完成 " + completed
                    + " · 失败 " + failed + " · 剩余 " + (total - index);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "批量处理中断：\n" + ex.Message,
                "批量处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _running = false;
                bool cancelled = _cancelRequested;
                _cancelButton.Enabled = false;
                _cancelButton.Text = "取消";
                _addFilesButton.Enabled = true;
                _addFolderButton.Enabled = true;
                UpdateCount();
                _statusLabel.Text = cancelled
                    ? "已取消：完成 " + completed + " 张，失败 " + failed + " 张"
                    : "处理完成：成功 " + completed + " 张，失败 " + failed + " 张"
                        + (_list.Items.Count > completed + failed
                            ? "，未处理 " + (_list.Items.Count - completed - failed) + " 张" : "");
            }
        }
    }

    private static BatchResult ProcessOne(string path, int index, BatchOptions options)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new BatchResult(false, "", "源文件不存在");
            }
            var format = options.FormatIndex switch
            {
                1 => ImageFormat.Jpeg,
                2 => ImageFormat.Png,
                3 => ImageFormat.Bmp,
                4 => ImageFormat.Gif,
                5 => ImageFormat.Tiff,
                _ => PhotoToolkit.FormatFromPath(path) ?? ImageFormat.Jpeg
            };
            string ext = options.FormatIndex == 0
                ? Path.GetExtension(path).ToLowerInvariant()
                : PhotoToolkit.ExtensionFor(format);
            if (ext == ".jpeg")
            {
                ext = ".jpg";
            }

            string directory = options.SameFolder
                ? Path.GetDirectoryName(path) ?? ""
                : options.OutputDir;
            if (string.IsNullOrEmpty(directory))
            {
                return new BatchResult(false, "", "无法确定输出目录");
            }
            Directory.CreateDirectory(directory);

            string baseName = Path.GetFileNameWithoutExtension(path);
            string name = options.Naming switch
            {
                1 => baseName + "_已处理",
                2 => (index + 1).ToString("000") + "_" + baseName,
                _ => baseName
            };
            string output = Path.Combine(directory, name + ext);
            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase)
                || (!options.Overwrite && File.Exists(output)))
            {
                output = UniquePath(directory, name, ext);
            }

            using var source = PhotoToolkit.LoadBitmap(path, autoOrient: true);
            var target = CalcTarget(source.Width, source.Height, options);
            Bitmap? resized = null;
            Bitmap pixels = source;
            if (!target.Equals(source.Size))
            {
                resized = PhotoToolkit.Resize(source, target.Width, target.Height);
                pixels = resized;
            }
            var saveOptions = new PhotoSaveOptions
            {
                Format = format,
                Quality = options.Quality,
                MetadataSource = options.KeepExif ? source : null,
                IncludeGps = true
            };
            PhotoToolkit.Save(pixels, output, saveOptions);
            resized?.Dispose();
            return new BatchResult(true, output, "");
        }
        catch (Exception ex)
        {
            return new BatchResult(false, "", ex.Message);
        }
    }

    private static Size CalcTarget(int width, int height, BatchOptions options)
    {
        double scale = 1.0;
        switch (options.Resize)
        {
            case BatchResizeMode.Percent:
                scale = options.ResizeValue / 100.0;
                break;
            case BatchResizeMode.LongEdge:
                scale = options.ResizeValue / (double)Math.Max(width, height);
                break;
            case BatchResizeMode.Width:
                scale = options.ResizeValue / (double)width;
                break;
            case BatchResizeMode.Explicit:
                var explicitSize = new Size(options.Width, options.Height);
                if (options.NoUpscale && (explicitSize.Width > width || explicitSize.Height > height))
                {
                    return new Size(width, height);
                }
                return explicitSize;
            default:
                return new Size(width, height);
        }
        if (options.NoUpscale && scale > 1.0)
        {
            scale = 1.0;
        }
        return new Size(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static string UniquePath(string directory, string baseName, string extension)
    {
        for (int i = 1; i < 9999; i++)
        {
            string candidate = Path.Combine(directory, baseName + "_" + i + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(directory, baseName + "_" + Guid.NewGuid().ToString("N")[..6] + extension);
    }

    private void OpenOutputFolder()
    {
        string folder = _sameFolderCheck.Checked
            ? (_list.Items.Count > 0 && _list.Items[0].Tag is string first
                ? Path.GetDirectoryName(first) ?? "" : "")
            : _outputDir.Text.Trim();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(FindForm(), "输出目录尚不存在，请先执行一次批量处理。", "批量处理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "无法打开文件夹：\n" + ex.Message, "批量处理",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed record BatchResult(bool Success, string Output, string Error);

    // ================= 工具 =================

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

    private static PillButton MakeButton(string text, int x, int y, int width, int height,
        PillButtonStyle style)
    {
        return new PillButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            ButtonStyle = style
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancelRequested = true;
        }
        base.Dispose(disposing);
    }
}
