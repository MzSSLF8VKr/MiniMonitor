using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 单个媒体类别的格式转换面板：文件列表、输出格式/质量设置、批量转换进度。
/// 图片优先使用 GDI+；音频、视频以及 WebP / HEIC / AVIF 使用 FFmpeg。
/// </summary>
internal sealed class FormatConverterPanel : UserControl
{
    private sealed class ConversionOptions
    {
        public int Quality = 90;
        public int BitrateIndex = 2;
        public int VideoQualityIndex = 1;
        public int ResolutionIndex;
        public bool KeepExif;
        public bool Overwrite;
        public bool SameFolder;
        public string OutputDir = "";
    }

    private static readonly int[] AudioBitrates = { 96, 128, 192, 256, 320 };

    private readonly AppConfig _config;
    private readonly MediaCategory _category;
    private readonly ListView _list;
    private readonly Label _countLabel;
    private readonly Label _statusLabel;
    private readonly Label _hintLabel;
    private readonly ComboBox _formatCombo;
    private readonly ComboBox _bitrateCombo;
    private readonly ComboBox _videoQualityCombo;
    private readonly ComboBox _resolutionCombo;
    private readonly NumericUpDown _quality;
    private readonly TextBox _outputDir;
    private readonly CheckBox _sameFolderCheck;
    private readonly CheckBox _keepExifCheck;
    private readonly CheckBox _overwriteCheck;
    private readonly ProgressBar _progress;
    private readonly PillButton _addFileButton;
    private readonly PillButton _addFolderButton;
    private readonly PillButton _removeButton;
    private readonly PillButton _clearButton;
    private readonly PillButton _startButton;
    private readonly PillButton _cancelButton;
    private readonly PillButton _openFolderButton;
    private readonly Panel _footer;
    private Process? _process;
    private bool _running;
    private bool _cancelRequested;

    public FormatConverterPanel(AppConfig config, MediaCategory category)
    {
        _config = config;
        _category = category;
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
        _addFileButton = MakeButton("添加文件", 14, 9, 96, 34, PillButtonStyle.Primary);
        _addFileButton.Click += (_, _) => AddFilesDialog();
        _addFolderButton = MakeButton("添加文件夹", 118, 9, 106, 34, PillButtonStyle.Light);
        _addFolderButton.Click += (_, _) => AddFolderDialog();
        _removeButton = MakeButton("移除选中", 232, 9, 96, 34, PillButtonStyle.Light);
        _removeButton.Click += (_, _) => RemoveSelected();
        _clearButton = MakeButton("清空列表", 336, 9, 96, 34, PillButtonStyle.Light);
        _clearButton.Click += (_, _) => ClearList();
        _countLabel = MakeLabel("共 0 个文件", 444, 9, 500, 34,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _countLabel.TextAlign = ContentAlignment.MiddleLeft;
        _countLabel.AutoEllipsis = true;
        _countLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        toolbar.Controls.AddRange(new Control[]
        {
            _addFileButton, _addFolderButton, _removeButton, _clearButton, _countLabel
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
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
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
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            AllowDrop = true
        };
        _list.Columns.Add("文件名", 280);
        _list.Columns.Add("大小", 86);
        _list.Columns.Add("状态", 96);
        _list.Resize += (_, _) => LayoutColumns();
        _list.SelectedIndexChanged += (_, _) => UpdateCount();
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
        int settingsHeight = _category switch
        {
            MediaCategory.Audio => 215,
            MediaCategory.Video => 236,
            _ => 214
        };
        var settingsCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = settingsHeight,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var settingsTitle = MakeLabel("输出设置", 16, 10, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var formatTitle = MakeLabel("输出格式", 16, 42, 64, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _formatCombo = new ComboBox
        {
            Location = new Point(82, 40),
            Size = new Size(244, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        foreach (var format in ConverterFormats.FormatsFor(_category))
        {
            _formatCombo.Items.Add(format);
        }
        _formatCombo.SelectedIndexChanged += (_, _) => UpdateOptionStates();

        _quality = new NumericUpDown
        {
            Location = new Point(82, 76),
            Size = new Size(80, 26),
            Minimum = 1,
            Maximum = 100,
            Value = 90,
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        _bitrateCombo = new ComboBox
        {
            Location = new Point(82, 76),
            Size = new Size(110, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _bitrateCombo.Items.AddRange(new object[] { "96 kbps", "128 kbps", "192 kbps", "256 kbps", "320 kbps" });
        _bitrateCombo.SelectedIndex = 2;
        _videoQualityCombo = new ComboBox
        {
            Location = new Point(82, 76),
            Size = new Size(150, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _videoQualityCombo.Items.AddRange(new object[] { "高质量", "标准", "小体积" });
        _videoQualityCombo.SelectedIndex = 1;
        _resolutionCombo = new ComboBox
        {
            Location = new Point(82, 112),
            Size = new Size(150, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _resolutionCombo.Items.AddRange(new object[] { "保持原始分辨率", "1080p", "720p", "480p" });
        _resolutionCombo.SelectedIndex = 0;
        _keepExifCheck = new CheckBox
        {
            Text = "保留 EXIF 信息（默认关闭）",
            Location = new Point(16, 108),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = false
        };
        _hintLabel = MakeLabel("", 16, 140, 320, 64,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);

        settingsCard.Controls.AddRange(new Control[]
        {
            settingsTitle, formatTitle, _formatCombo, _hintLabel
        });
        switch (_category)
        {
            case MediaCategory.Image:
                var qualityTitle = MakeLabel("质量", 16, 78, 60, 26,
                    new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
                var qualityUnit = MakeLabel("（JPG / WebP / AVIF）", 166, 78, 160, 26,
                    new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
                settingsCard.Controls.AddRange(new Control[]
                {
                    qualityTitle, _quality, qualityUnit, _keepExifCheck
                });
                _hintLabel.Text =
                    "输入：JPG / PNG / APNG / BMP / GIF / TIFF / ICO / WebP / HEIC / AVIF，\n"
                    + "以及 PSD / PSB / XCF / CDR / RAW(CR2/NEF/ARW 等) / AI / EPS。\n"
                    + "输出：JPG、PNG、BMP、GIF、TIFF、ICO、WebP、AVIF、APNG。\n"
                    + "PSD / RAW 等内置解码；WebP / HEIC / AVIF 需要 FFmpeg。";
                break;
            case MediaCategory.Audio:
                var bitrateTitle = MakeLabel("比特率", 16, 78, 60, 26,
                    new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
                settingsCard.Controls.AddRange(new Control[] { bitrateTitle, _bitrateCombo });
                _hintLabel.Text =
                    "输入：MP3 / WAV / FLAC / AAC / M4A / OGG / Opus / WMA / AIFF / APE 等，\n"
                    + "也可直接从 MP4 / MKV 等视频提取音轨。\n"
                    + "输出：MP3、WAV、FLAC、M4A、OGG、Opus、WMA、AIFF。\n"
                    + "音频转换需要 FFmpeg，可在窗口底部设置路径。";
                break;
            case MediaCategory.Video:
                var videoQualityTitle = MakeLabel("质量", 16, 78, 60, 26,
                    new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
                var resolutionTitle = MakeLabel("分辨率", 16, 114, 60, 26,
                    new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
                settingsCard.Controls.AddRange(new Control[]
                {
                    videoQualityTitle, _videoQualityCombo, resolutionTitle, _resolutionCombo
                });
                _hintLabel.Text =
                    "输入：MP4 / MKV / AVI / MOV / WMV / FLV / WebM / TS / M2TS / 3GP / OGV / GIF 等。\n"
                    + "输出：MP4、MKV、MOV、WebM、AVI、GIF、WMV、FLV、TS。\n"
                    + "视频转换需要 FFmpeg；文件越大耗时越久。";
                break;
        }
        _hintLabel.Location = new Point(16, _category switch
        {
            MediaCategory.Audio => 112,
            MediaCategory.Video => 150,
            _ => 140
        });
        _hintLabel.Height = _category == MediaCategory.Audio ? 78 : 64;
        stack.Controls.Add(settingsCard, 0, 0);

        // ---------- 保存位置 ----------
        var outputCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 204,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var outputTitle = MakeLabel("保存位置", 16, 10, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var dirTitle = MakeLabel("输出目录", 16, 42, 80, 24,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _outputDir = new TextBox
        {
            Location = new Point(16, 64),
            Size = new Size(244, 28),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Text = DefaultOutputDir()
        };
        var browseButton = MakeButton("浏览", 268, 64, 66, 30, PillButtonStyle.Light);
        browseButton.Click += (_, _) => BrowseOutputDir();
        _sameFolderCheck = new CheckBox
        {
            Text = "输出到源文件所在文件夹（不覆盖原文件）",
            Location = new Point(16, 100),
            Size = new Size(320, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        _sameFolderCheck.CheckedChanged += (_, _) =>
        {
            bool same = _sameFolderCheck.Checked;
            _outputDir.Enabled = !same;
            browseButton.Enabled = !same;
        };
        _overwriteCheck = new CheckBox
        {
            Text = "覆盖同名输出文件（不会覆盖源文件）",
            Location = new Point(16, 130),
            Size = new Size(320, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        var outputHint = MakeLabel(
            "输出文件名沿用原名，只修改扩展名；重名时自动追加序号。",
            16, 162, 320, 34,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        outputCard.Controls.AddRange(new Control[]
        {
            outputTitle, dirTitle, _outputDir, browseButton, _sameFolderCheck,
            _overwriteCheck, outputHint
        });
        stack.Controls.Add(outputCard, 0, 1);

        // ---------- 格式说明 ----------
        var infoCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 138,
            Margin = new Padding(0, 0, 0, 0),
            CornerRadius = 14
        };
        var infoTitle = MakeLabel("格式与说明", 16, 10, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var infoText = MakeLabel(
            "1. 支持批量添加文件或文件夹，也可直接拖入左侧列表。\n"
            + "2. 转换在后台执行，可随时取消；失败项会保留错误信息。\n"
            + "3. 输出不会覆盖源文件；图片默认剔除 EXIF/GPS。\n"
            + "4. 音频、视频以及 WebP / AVIF / APNG 转换依赖 ffmpeg.exe。",
            16, 40, 320, 92,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        infoCard.Controls.AddRange(new Control[] { infoTitle, infoText });
        stack.Controls.Add(infoCard, 0, 2);

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
        _statusLabel = MakeLabel("等待添加文件", 278, 15, 300, 28,
            new Font("Microsoft YaHei UI", 8.3f), ShutdownTheme.Muted, Color.White);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _startButton = MakeButton("开始转换", 0, 14, 120, 34, PillButtonStyle.Primary);
        _startButton.Click += (_, _) => StartConversion();
        _cancelButton = MakeButton("取消", 0, 14, 100, 34, PillButtonStyle.Danger);
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => CancelConversion();
        _openFolderButton = MakeButton("打开输出目录", 0, 14, 120, 34, PillButtonStyle.Light);
        _openFolderButton.Click += (_, _) => OpenOutputFolder();
        _footer.Controls.AddRange(new Control[]
        {
            _progress, _statusLabel, _startButton, _cancelButton, _openFolderButton
        });
        _footer.Resize += (_, _) => LayoutFooter();
        root.Controls.Add(_footer, 0, 2);

        _formatCombo.SelectedIndex = 0;
        UpdateOptionStates();
        LayoutFooter();
        LayoutColumns();
        UpdateCount();
        _statusLabel.Text = "等待添加" + ConverterFormats.CategoryName(_category) + "文件 · 支持拖放";
    }

    public MediaCategory Category => _category;

    public bool AddExternalFile(string path)
    {
        if (!File.Exists(path) || ConverterFormats.DetectCategory(path) != _category)
        {
            return false;
        }
        AddPaths(new[] { path });
        return true;
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
        if (_list.Columns.Count < 3)
        {
            return;
        }
        int width = Math.Max(300, _list.ClientSize.Width - 4);
        _list.Columns[0].Width = Math.Max(160, (int)(width * 0.56));
        _list.Columns[1].Width = Math.Max(70, (int)(width * 0.18));
        _list.Columns[2].Width = Math.Max(80, width - _list.Columns[0].Width - _list.Columns[1].Width);
    }

    // ================= 文件列表 =================

    private void AddFilesDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "添加" + ConverterFormats.CategoryName(_category) + "文件",
            Filter = ConverterFormats.OpenFileFilter(_category),
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
            Description = "选择包含" + ConverterFormats.CategoryName(_category)
                + "文件的文件夹（包含子文件夹）",
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
                .Where(path => ConverterFormats.AcceptsFile(_category, path))
                .Take(5000)
                .ToArray();
            AddPaths(files);
            _statusLabel.Text = "已从文件夹添加 " + files.Length + " 个文件";
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "读取文件夹失败：\n" + ex.Message,
                "通用格式转换", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        if (_running)
        {
            return;
        }
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
                if (!File.Exists(path)
                    || !ConverterFormats.AcceptsFile(_category, path)
                    || !existing.Add(path))
                {
                    continue;
                }
                var item = new ListViewItem(Path.GetFileName(path)) { Tag = path };
                try
                {
                    long size = new FileInfo(path).Length;
                    item.SubItems.Add(FormatSize(size));
                }
                catch
                {
                    item.SubItems.Add("—");
                }
                item.SubItems.Add("待转换");
                item.ToolTipText = path;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateCount();
        _statusLabel.Text = "共 " + _list.Items.Count + " 个文件待转换";
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
        _countLabel.Text = "共 " + _list.Items.Count + " 个文件"
            + (_list.SelectedItems.Count > 0 ? " · 已选中 " + _list.SelectedItems.Count + " 个" : "");
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
        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
    }

    // ================= 选项 =================

    private string DefaultOutputDir()
    {
        string baseDir = _category switch
        {
            MediaCategory.Audio => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            MediaCategory.Video => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        return Path.Combine(baseDir, "MiniMonitor 格式转换");
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

    private void UpdateOptionStates()
    {
        if (_formatCombo.SelectedItem is not OutputFormat format)
        {
            return;
        }
        switch (_category)
        {
            case MediaCategory.Image:
                bool lossy = format.Extension is "jpg" or "webp" or "avif";
                _quality.Enabled = lossy;
                _quality.BackColor = lossy ? Color.White : Color.FromArgb(241, 245, 249);
                bool canKeepExif = format.DirectImage && format.Extension != "ico";
                _keepExifCheck.Enabled = canKeepExif;
                if (!canKeepExif)
                {
                    _keepExifCheck.Checked = false;
                }
                break;
            case MediaCategory.Audio:
                bool lossyAudio = format.Extension is "mp3" or "m4a" or "ogg" or "opus" or "wma";
                _bitrateCombo.Enabled = lossyAudio;
                _bitrateCombo.BackColor = lossyAudio ? Color.White : Color.FromArgb(241, 245, 249);
                break;
        }
    }

    // ================= 转换 =================

    private void CancelConversion()
    {
        if (!_running)
        {
            return;
        }
        _cancelRequested = true;
        _cancelButton.Enabled = false;
        _cancelButton.Text = "正在取消…";
        _statusLabel.Text = "正在取消，等待当前转换结束…";
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch
        {
            // 进程已经退出
        }
    }

    private async void StartConversion()
    {
        if (_running || _list.Items.Count == 0)
        {
            return;
        }
        if (_formatCombo.SelectedItem is not OutputFormat format)
        {
            return;
        }

        var options = new ConversionOptions
        {
            Quality = (int)_quality.Value,
            BitrateIndex = Math.Max(0, _bitrateCombo.SelectedIndex),
            VideoQualityIndex = Math.Max(0, _videoQualityCombo.SelectedIndex),
            ResolutionIndex = Math.Max(0, _resolutionCombo.SelectedIndex),
            KeepExif = _keepExifCheck.Checked,
            Overwrite = _overwriteCheck.Checked,
            SameFolder = _sameFolderCheck.Checked,
            OutputDir = _outputDir.Text.Trim()
        };
        if (!options.SameFolder)
        {
            if (string.IsNullOrWhiteSpace(options.OutputDir))
            {
                MessageBox.Show(FindForm(), "请选择输出目录。", "通用格式转换",
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
                    "通用格式转换", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        if (NeedsFfmpeg(format) && FfmpegLocator.Find(_config.FfmpegPath) == null)
        {
            MessageBox.Show(FindForm(),
                "当前转换需要 ffmpeg.exe，但未找到。\n\n"
                + "请把 ffmpeg.exe 放到程序目录或系统 PATH 中，"
                + "或在窗口底部点击“设置 FFmpeg…”指定路径。\n"
                + "图片转换为 JPG / PNG / BMP / GIF / TIFF / ICO 不需要 FFmpeg；\n"
                + "PSD / PSB / XCF / CDR / RAW / AI / EPS 也使用内置解码。",
                "缺少 FFmpeg", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _running = true;
        _cancelRequested = false;
        _cancelButton.Text = "取消";
        _cancelButton.Enabled = true;
        _startButton.Enabled = false;
        _addFileButton.Enabled = false;
        _addFolderButton.Enabled = false;
        _removeButton.Enabled = false;
        _clearButton.Enabled = false;
        _progress.Minimum = 0;
        _progress.Maximum = _list.Items.Count;
        _progress.Value = 0;

        int total = _list.Items.Count;
        int completed = 0;
        int failed = 0;
        int index = 0;
        string firstError = "";
        try
        {
            foreach (ListViewItem item in _list.Items.Cast<ListViewItem>().ToArray())
            {
                if (IsDisposed || Disposing || _cancelRequested)
                {
                    break;
                }
                string input = item.Tag as string ?? "";
                item.SubItems[2].Text = "转换中…";
                _statusLabel.Text = "正在转换 " + (index + 1) + " / " + total
                    + "：" + item.Text;
                _list.EnsureVisible(item.Index);
                string output = "";
                try
                {
                    output = BuildOutputPath(input, format, options);
                    if (_category == MediaCategory.Image && format.Extension == "ico")
                    {
                        await ConvertImageIcoAsync(input, output, options);
                    }
                    else if (_category == MediaCategory.Image && AdvancedImageSupport.IsAdvanced(input))
                    {
                        await ConvertAdvancedImageAsync(input, output, format, options);
                    }
                    else if (_category == MediaCategory.Image && format.DirectImage
                        && ConverterFormats.IsDirectImageInput(input))
                    {
                        string localOutput = output;
                        await Task.Run(() => ConvertImageDirect(input, localOutput, format, options));
                    }
                    else
                    {
                        await ConvertWithFfmpegAsync(input, output, format, options);
                    }
                    if (IsDisposed || Disposing)
                    {
                        break;
                    }
                    item.SubItems[2].Text = "完成";
                    item.ToolTipText = "输出：" + output;
                    completed++;
                }
                catch (OperationCanceledException)
                {
                    if (!IsDisposed && !Disposing)
                    {
                        item.SubItems[2].Text = "已取消";
                    }
                    if (!string.IsNullOrEmpty(output))
                    {
                        TryDelete(output);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (IsDisposed || Disposing)
                    {
                        break;
                    }
                    item.SubItems[2].Text = "失败";
                    item.ToolTipText = ex.Message;
                    if (!string.IsNullOrEmpty(output))
                    {
                        TryDelete(output);
                    }
                    if (firstError.Length == 0)
                    {
                        firstError = ex.Message;
                    }
                    failed++;
                }
                index++;
                _progress.Value = Math.Min(index, _progress.Maximum);
                _countLabel.Text = "共 " + total + " 个文件 · 完成 " + completed
                    + " · 失败 " + failed + " · 剩余 " + (total - index);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "转换中断：\n" + ex.Message,
                "通用格式转换", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _process = null;
            if (!IsDisposed && !Disposing)
            {
                _running = false;
                bool cancelled = _cancelRequested;
                _cancelButton.Enabled = false;
                _cancelButton.Text = "取消";
                _addFileButton.Enabled = true;
                _addFolderButton.Enabled = true;
                UpdateCount();
                _statusLabel.Text = cancelled
                    ? "已取消：完成 " + completed + " 个，失败 " + failed + " 个"
                    : "转换完成：成功 " + completed + " 个，失败 " + failed + " 个"
                        + (firstError.Length > 0 ? "；首个错误：" + firstError : "");
            }
        }
    }

    private bool NeedsFfmpeg(OutputFormat format)
    {
        if (_category != MediaCategory.Image)
        {
            return true;
        }
        if (!format.DirectImage)
        {
            return true;
        }
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is string path
                && !ConverterFormats.IsDirectImageInput(path)
                && !AdvancedImageSupport.IsAdvanced(path))
            {
                return true;
            }
        }
        return false;
    }

    private string BuildOutputPath(string input, OutputFormat format, ConversionOptions options)
    {
        string directory = options.SameFolder
            ? Path.GetDirectoryName(input) ?? ""
            : options.OutputDir;
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("无法确定输出目录。");
        }
        Directory.CreateDirectory(directory);
        string name = Path.GetFileNameWithoutExtension(input);
        string extension = "." + format.Extension;
        string candidate = Path.Combine(directory, name + extension);
        bool sameAsSource = string.Equals(Path.GetFullPath(candidate),
            Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase);
        if (sameAsSource || (!options.Overwrite && File.Exists(candidate)))
        {
            candidate = UniquePath(directory, name, extension);
        }
        return candidate;
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

    private static void ConvertImageDirect(string input, string output, OutputFormat format,
        ConversionOptions options)
    {
        var imageFormat = ConverterFormats.DirectImageFormat(format.Extension)
            ?? throw new InvalidOperationException("不支持直接输出该图片格式。");
        using var source = PhotoToolkit.LoadBitmap(input, autoOrient: true);
        PhotoToolkit.Save(source, output, new PhotoSaveOptions
        {
            Format = imageFormat,
            Quality = options.Quality,
            MetadataSource = options.KeepExif ? source : null
        });
    }

    private async Task ConvertAdvancedImageAsync(string input, string output, OutputFormat format,
        ConversionOptions options)
    {
        if (format.Extension == "ico")
        {
            string localOutput = output;
            await Task.Run(() =>
            {
                using var source = AdvancedImageSupport.LoadBitmap(input);
                PhotoToolkit.SaveIco(source, localOutput);
            });
            return;
        }
        if (format.DirectImage)
        {
            var imageFormat = ConverterFormats.DirectImageFormat(format.Extension)
                ?? throw new InvalidOperationException("不支持直接输出该图片格式。");
            await Task.Run(() =>
            {
                using var source = AdvancedImageSupport.LoadBitmap(input);
                PhotoToolkit.Save(source, output, new PhotoSaveOptions
                {
                    Format = imageFormat,
                    Quality = options.Quality,
                    MetadataSource = options.KeepExif ? source : null
                });
            });
            return;
        }
        // 输出 WebP / AVIF / APNG 等需要 FFmpeg：先解码为临时 PNG，再交给 FFmpeg。
        string temp = Path.Combine(AppPaths.Temp,
            "MiniMonitor_adv_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await Task.Run(() =>
            {
                using var source = AdvancedImageSupport.LoadBitmap(input);
                PhotoToolkit.Save(source, temp, new PhotoSaveOptions
                {
                    Format = ImageFormat.Png,
                    Quality = 90
                });
            });
            await ConvertWithFfmpegAsync(temp, output, format, options);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task ConvertImageIcoAsync(string input, string output, ConversionOptions options)
    {
        if (AdvancedImageSupport.IsAdvanced(input))
        {
            string localOutput = output;
            await Task.Run(() =>
            {
                using var source = AdvancedImageSupport.LoadBitmap(input);
                PhotoToolkit.SaveIco(source, localOutput);
            });
            return;
        }
        if (ConverterFormats.IsDirectImageInput(input))
        {
            string localOutput = output;
            await Task.Run(() =>
            {
                using var source = PhotoToolkit.LoadBitmap(input, autoOrient: true);
                PhotoToolkit.SaveIco(source, localOutput);
            });
            return;
        }

        string temp = Path.Combine(AppPaths.Temp,
            "MiniMonitor_ico_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var tempFormat = new OutputFormat("PNG", "png", true);
            await ConvertWithFfmpegAsync(input, temp, tempFormat, options);
            string localOutput = output;
            await Task.Run(() =>
            {
                using var source = PhotoToolkit.LoadBitmap(temp, autoOrient: true);
                PhotoToolkit.SaveIco(source, localOutput);
            });
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task ConvertWithFfmpegAsync(string input, string output,
        OutputFormat format, ConversionOptions options)
    {
        string? ffmpeg = FfmpegLocator.Find(_config.FfmpegPath);
        if (ffmpeg == null)
        {
            throw new InvalidOperationException("未找到 ffmpeg.exe。");
        }
        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var arguments = BuildFfmpegArguments(input, output, format, options);
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = startInfo };
        _process = process;
        try
        {
            process.Start();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string error = await errorTask;
            if (_cancelRequested)
            {
                TryDelete(output);
                throw new OperationCanceledException();
            }
            if (process.ExitCode != 0)
            {
                TryDelete(output);
                throw new InvalidOperationException("FFmpeg 转换失败："
                    + Tail(error, 420));
            }
            if (!File.Exists(output))
            {
                throw new InvalidOperationException("FFmpeg 未生成输出文件。");
            }
        }
        finally
        {
            _process = null;
        }
    }

    private List<string> BuildFfmpegArguments(string input, string output,
        OutputFormat format, ConversionOptions options)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", input };
        string extension = format.Extension;
        switch (_category)
        {
            case MediaCategory.Image:
                if (extension != "apng")
                {
                    args.Add("-frames:v");
                    args.Add("1");
                }
                if (!options.KeepExif)
                {
                    args.Add("-map_metadata");
                    args.Add("-1");
                }
                if (extension == "jpg")
                {
                    args.Add("-q:v");
                    args.Add(((int)Math.Round(1 + (100 - options.Quality) / 100.0 * 29)).ToString());
                    args.Add("-pix_fmt");
                    args.Add("yuvj420p");
                }
                else if (extension == "webp")
                {
                    args.Add("-c:v");
                    args.Add("libwebp");
                    args.Add("-quality");
                    args.Add(options.Quality.ToString());
                    args.Add("-compression_level");
                    args.Add("6");
                }
                else if (extension == "avif")
                {
                    args.Add("-c:v");
                    args.Add("libaom-av1");
                    args.Add("-still-picture");
                    args.Add("1");
                    args.Add("-crf");
                    args.Add(((int)Math.Round(51 - options.Quality / 100.0 * 51)).ToString());
                    args.Add("-cpu-used");
                    args.Add("6");
                }
                else if (extension == "apng")
                {
                    args.Add("-c:v");
                    args.Add("apng");
                    args.Add("-plays");
                    args.Add("0");
                    args.Add("-pix_fmt");
                    args.Add("rgba");
                }
                break;
            case MediaCategory.Audio:
                args.Add("-vn");
                int audioBitrate = AudioBitrates[Math.Clamp(options.BitrateIndex,
                    0, AudioBitrates.Length - 1)];
                switch (extension)
                {
                    case "mp3":
                        args.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", audioBitrate + "k" });
                        break;
                    case "wav":
                        args.AddRange(new[] { "-c:a", "pcm_s16le" });
                        break;
                    case "flac":
                        args.AddRange(new[] { "-c:a", "flac" });
                        break;
                    case "m4a":
                        args.AddRange(new[] { "-c:a", "aac", "-b:a", audioBitrate + "k",
                            "-movflags", "+faststart" });
                        break;
                    case "ogg":
                        args.AddRange(new[] { "-c:a", "libvorbis", "-b:a", audioBitrate + "k" });
                        break;
                    case "opus":
                        args.AddRange(new[] { "-c:a", "libopus", "-b:a", audioBitrate + "k" });
                        break;
                    case "wma":
                        args.AddRange(new[] { "-c:a", "wmav2", "-b:a", audioBitrate + "k" });
                        break;
                    case "aiff":
                        args.AddRange(new[] { "-c:a", "pcm_s16be" });
                        break;
                }
                break;
            case MediaCategory.Video:
                int videoQuality = Math.Clamp(options.VideoQualityIndex, 0, 2);
                int crf = videoQuality switch { 0 => 18, 1 => 23, _ => 28 };
                string preset = videoQuality switch { 0 => "slow", 1 => "medium", _ => "fast" };
                int videoAudioBitrate = videoQuality switch { 0 => 192, 1 => 128, _ => 96 };
                int targetHeight = options.ResolutionIndex switch
                {
                    1 => 1080,
                    2 => 720,
                    3 => 480,
                    _ => 0
                };
                string? scale = targetHeight > 0
                    ? "scale=-2:" + targetHeight + ":flags=lanczos"
                    : null;
                switch (extension)
                {
                    case "mp4":
                        args.AddRange(new[] { "-c:v", "libx264", "-preset", preset,
                            "-crf", crf.ToString(), "-pix_fmt", "yuv420p",
                            "-c:a", "aac", "-b:a", videoAudioBitrate + "k",
                            "-movflags", "+faststart" });
                        break;
                    case "mkv":
                        args.AddRange(new[] { "-c:v", "libx264", "-preset", preset,
                            "-crf", crf.ToString(), "-pix_fmt", "yuv420p",
                            "-c:a", "aac", "-b:a", videoAudioBitrate + "k" });
                        break;
                    case "mov":
                        args.AddRange(new[] { "-c:v", "libx264", "-preset", preset,
                            "-crf", crf.ToString(), "-pix_fmt", "yuv420p",
                            "-c:a", "aac", "-b:a", videoAudioBitrate + "k",
                            "-movflags", "+faststart" });
                        break;
                    case "webm":
                        args.AddRange(new[] { "-c:v", "libvpx-vp9",
                            "-crf", (videoQuality switch { 0 => 30, 1 => 35, _ => 40 }).ToString(),
                            "-b:v", "0", "-c:a", "libopus",
                            "-b:a", videoAudioBitrate + "k" });
                        break;
                    case "avi":
                        args.AddRange(new[] { "-c:v", "mpeg4",
                            "-q:v", (videoQuality switch { 0 => 2, 1 => 4, _ => 7 }).ToString(),
                            "-c:a", "libmp3lame", "-b:a", videoAudioBitrate + "k" });
                        break;
                    case "gif":
                        args.Add("-an");
                        int fps = videoQuality switch { 0 => 15, 1 => 12, _ => 8 };
                        string gifFilter = scale == null ? "fps=" + fps : "fps=" + fps + "," + scale;
                        args.Add("-vf");
                        args.Add(gifFilter);
                        args.Add("-loop");
                        args.Add("0");
                        break;
                    case "wmv":
                        args.AddRange(new[] { "-c:v", "wmv2",
                            "-b:v", (videoQuality switch { 0 => "4M", 1 => "2M", _ => "1M" }),
                            "-c:a", "wmav2", "-b:a", videoAudioBitrate + "k" });
                        break;
                    case "flv":
                        args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast",
                            "-crf", crf.ToString(), "-c:a", "aac",
                            "-b:a", videoAudioBitrate + "k", "-ar", "44100" });
                        break;
                    case "ts":
                        args.AddRange(new[] { "-c:v", "libx264", "-preset", preset,
                            "-crf", crf.ToString(), "-c:a", "aac",
                            "-b:a", videoAudioBitrate + "k", "-f", "mpegts" });
                        break;
                }
                if (scale != null && extension != "gif")
                {
                    args.Add("-vf");
                    args.Add(scale);
                }
                break;
        }
        args.Add(output);
        return args;
    }

    private static string Tail(string text, int length)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "未知错误";
        }
        text = text.Trim();
        return text.Length <= length ? text : text[^length..];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 删除失败不影响主流程
        }
    }

    private void OpenOutputFolder()
    {
        string folder = _sameFolderCheck.Checked
            ? (_list.Items.Count > 0 && _list.Items[0].Tag is string first
                ? Path.GetDirectoryName(first) ?? "" : "")
            : _outputDir.Text.Trim();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(FindForm(), "输出目录尚不存在，请先执行一次转换。", "通用格式转换",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "无法打开文件夹：\n" + ex.Message, "通用格式转换",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

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
            try
            {
                _process?.Kill(entireProcessTree: true);
            }
            catch
            {
                // 忽略
            }
        }
        base.Dispose(disposing);
    }
}
