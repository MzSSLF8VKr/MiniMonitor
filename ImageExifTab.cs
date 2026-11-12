using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>EXIF 信息查看与剔除：查看全部元数据、GPS 检测、剔除全部 EXIF 或仅移除 GPS。</summary>
internal sealed class ImageExifTab : UserControl
{
    private readonly PhotoPreviewControl _preview;
    private readonly Label _fileLabel;
    private readonly Label _nameValue;
    private readonly Label _dimensionsValue;
    private readonly Label _sizeValue;
    private readonly Label _formatValue;
    private readonly Label _cameraValue;
    private readonly Label _dateValue;
    private readonly Label _orientationValue;
    private readonly Label _gpsValue;
    private readonly Label _metadataValue;
    private readonly Label _removeStatus;
    private readonly ListView _list;
    private readonly TextBox _detail;
    private readonly TextBox _search;
    private readonly NumericUpDown _quality;
    private readonly PillButton _removeAllButton;
    private readonly PillButton _removeGpsButton;
    private readonly PillButton _copyButton;
    private readonly Label _entryCountLabel;
    private Bitmap? _previewBitmap;
    private ExifInfo? _info;
    private string? _path;

    public ImageExifTab()
    {
        BackColor = Color.White;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // ---------- 顶部 ----------
        var toolbar = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            CornerRadius = 14
        };
        var openButton = MakeButton("打开照片", 14, 9, 96, 34, PillButtonStyle.Primary);
        openButton.Click += (_, _) => OpenFile();
        var reloadButton = MakeButton("重新读取", 118, 9, 96, 34, PillButtonStyle.Light);
        reloadButton.Click += (_, _) =>
        {
            if (_path != null)
            {
                LoadFile(_path);
            }
        };
        _copyButton = MakeButton("复制信息", 222, 9, 96, 34, PillButtonStyle.Light);
        _copyButton.Enabled = false;
        _copyButton.Click += (_, _) => CopyInfo();
        _fileLabel = MakeLabel("尚未打开照片", 332, 9, 600, 34,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _fileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _fileLabel.AutoEllipsis = true;
        _fileLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        toolbar.Controls.AddRange(new Control[]
        {
            openButton, reloadButton, _copyButton, _fileLabel
        });
        toolbar.Resize += (_, _) =>
        {
            _fileLabel.Width = Math.Max(100, toolbar.ClientSize.Width - _fileLabel.Left - 14);
        };
        root.Controls.Add(toolbar, 0, 0);

        // ---------- 主体 ----------
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 342));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        // 左侧：预览 + 信息 + 剔除
        var leftScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 10, 0)
        };
        var leftStack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        leftStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
        {
            leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        leftScroll.Controls.Add(leftStack);
        body.Controls.Add(leftScroll, 0, 0);

        var previewCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 190,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(8),
            CornerRadius = 14
        };
        _preview = new PhotoPreviewControl
        {
            Dock = DockStyle.Fill,
            SelectionEnabled = false
        };
        previewCard.Controls.Add(_preview);
        leftStack.Controls.Add(previewCard, 0, 0);

        var infoCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 246,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var infoTitle = MakeLabel("文件信息", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _nameValue = AddInfoRow(infoCard, 38, "文件名", out _);
        _dimensionsValue = AddInfoRow(infoCard, 62, "尺寸", out _);
        _sizeValue = AddInfoRow(infoCard, 86, "文件大小", out _);
        _formatValue = AddInfoRow(infoCard, 110, "格式", out _);
        _cameraValue = AddInfoRow(infoCard, 134, "相机", out _);
        _dateValue = AddInfoRow(infoCard, 158, "拍摄时间", out _);
        _orientationValue = AddInfoRow(infoCard, 182, "方向", out _);
        _metadataValue = AddInfoRow(infoCard, 206, "元数据", out _);
        _gpsValue = MakeLabel("无 GPS 定位信息", 16, 226, 294, 18,
            new Font("Microsoft YaHei UI", 8f, FontStyle.Bold), ShutdownTheme.Success, Color.White);
        infoCard.Controls.Add(_gpsValue);
        infoCard.Controls.Add(infoTitle);
        leftStack.Controls.Add(infoCard, 0, 1);

        var removeCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 218,
            Margin = new Padding(0, 0, 0, 0),
            CornerRadius = 14
        };
        var removeTitle = MakeLabel("EXIF 剔除", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var removeHint = MakeLabel(
            "剔除全部会同时移除相机型号、拍摄时间、GPS 等全部拍摄信息；"
            + "仅移除 GPS 可保留其它拍摄信息。输出文件不会覆盖原图。",
            16, 38, 294, 62,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        _removeAllButton = MakeButton("剔除全部 EXIF…", 16, 104, 140, 34, PillButtonStyle.Primary);
        _removeAllButton.Enabled = false;
        _removeAllButton.Click += (_, _) => RemoveExif(all: true);
        _removeGpsButton = MakeButton("仅移除 GPS…", 164, 104, 140, 34, PillButtonStyle.Light);
        _removeGpsButton.Enabled = false;
        _removeGpsButton.Click += (_, _) => RemoveExif(all: false);
        var qualityTitle = MakeLabel("JPEG 质量", 16, 150, 66, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _quality = new NumericUpDown
        {
            Location = new Point(86, 148),
            Size = new Size(60, 26),
            Minimum = 1,
            Maximum = 100,
            Value = 95,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        var qualityHint = MakeLabel("重新编码时使用", 154, 150, 150, 26,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        _removeStatus = MakeLabel("剔除时会按 EXIF 方向自动摆正照片", 16, 180, 294, 30,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        removeCard.Controls.AddRange(new Control[]
        {
            removeTitle, removeHint, _removeAllButton, _removeGpsButton,
            qualityTitle, _quality, qualityHint, _removeStatus
        });
        leftStack.Controls.Add(removeCard, 0, 2);

        // 右侧：EXIF 列表
        var listCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0)
        };
        var listLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White
        };
        listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        var searchPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var searchTitle = MakeLabel("搜索", 2, 9, 42, 24,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _search = new TextBox
        {
            Location = new Point(46, 6),
            Size = new Size(300, 26),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            PlaceholderText = "按标签或值过滤，例如 GPS / 相机 / 时间"
        };
        _search.TextChanged += (_, _) => PopulateList();
        var searchHint = MakeLabel("共 0 项", 356, 9, 200, 24,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _entryCountLabel = searchHint;
        searchHint.TextAlign = ContentAlignment.MiddleRight;
        searchHint.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        searchPanel.Resize += (_, _) =>
        {
            searchHint.Width = Math.Max(80, searchPanel.ClientSize.Width - searchHint.Left);
        };
        searchPanel.Controls.AddRange(new Control[] { searchTitle, _search, searchHint });
        listLayout.Controls.Add(searchPanel, 0, 0);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text
        };
        _list.Columns.Add("标签", 190);
        _list.Columns.Add("值", 300);
        _list.Columns.Add("类型", 80);
        _list.Resize += (_, _) =>
        {
            if (_list.Columns.Count >= 3)
            {
                int width = Math.Max(300, _list.ClientSize.Width - 4);
                _list.Columns[0].Width = Math.Min(210, (int)(width * 0.30));
                _list.Columns[2].Width = 80;
                _list.Columns[1].Width = Math.Max(120, width - _list.Columns[0].Width - 80);
            }
        };
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        listLayout.Controls.Add(_list, 0, 1);

        _detail = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Microsoft YaHei UI", 8f),
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "选中一条 EXIF 记录可查看完整内容。"
        };
        var detailHolder = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = Color.White
        };
        detailHolder.Controls.Add(_detail);
        listLayout.Controls.Add(detailHolder, 0, 2);

        listCard.Controls.Add(listLayout);
        body.Controls.Add(listCard, 1, 0);

        UpdateInfo(null);
    }

    private static Label AddInfoRow(CardPanel card, int y, string title, out Label titleLabel)
    {
        titleLabel = MakeLabel(title, 16, y, 64, 20,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var value = MakeLabel("—", 88, y, 232, 20,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Text, Color.White);
        value.AutoEllipsis = true;
        card.Controls.Add(titleLabel);
        card.Controls.Add(value);
        return value;
    }

    // ================= 读取 =================

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开照片查看 EXIF",
            Filter = PhotoToolkit.OpenFileFilter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            LoadFile(dialog.FileName);
        }
    }

    private void LoadFile(string path)
    {
        if (!PhotoToolkit.IsSupported(path))
        {
            MessageBox.Show(FindForm(), "不支持的图片格式：\n" + path,
                "EXIF 信息", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            var info = PhotoToolkit.ReadExif(path);
            var fileInfo = PhotoToolkit.ReadFileInfo(path);
            _info = info;
            _path = path;

            _fileLabel.Text = path;
            _nameValue.Text = Path.GetFileName(path);
            _dimensionsValue.Text = fileInfo.Width + " × " + fileInfo.Height + " 像素";
            _sizeValue.Text = FormatSize(fileInfo.FileSize);
            _formatValue.Text = fileInfo.FormatName;
            _cameraValue.Text = string.IsNullOrWhiteSpace(info.Camera) ? "—" : info.Camera;
            _dateValue.Text = string.IsNullOrWhiteSpace(info.DateTaken) ? "—" : info.DateTaken;
            _orientationValue.Text = string.IsNullOrWhiteSpace(info.Orientation)
                ? "无方向标记" : info.Orientation;
            _metadataValue.Text = info.Entries.Count + " 项";
            _gpsValue.Text = info.HasGps ? "包含 GPS 定位信息（建议剔除）" : "无 GPS 定位信息";
            _gpsValue.ForeColor = info.HasGps ? ShutdownTheme.Danger : ShutdownTheme.Success;

            _copyButton.Enabled = true;
            _removeAllButton.Enabled = true;
            _removeGpsButton.Enabled = info.HasGps;
            _removeStatus.ForeColor = ShutdownTheme.Muted;
            _removeStatus.Text = info.HasGps
                ? "检测到 GPS 定位，可用“仅移除 GPS”保留其它信息。"
                : "剔除时会按 EXIF 方向自动摆正照片。";

            LoadPreview(path);
            PopulateList();
            _detail.Text = info.Entries.Count == 0
                ? "该图片不包含 EXIF / 元数据。"
                : "选中一条 EXIF 记录可查看完整内容。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "读取失败：\n" + ex.Message,
                "EXIF 信息", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void LoadPreview(string path)
    {
        var old = _previewBitmap;
        _previewBitmap = null;
        _preview.Photo = null;
        old?.Dispose();
        try
        {
            using var source = PhotoToolkit.LoadBitmap(path, autoOrient: true);
            int maxSide = Math.Max(source.Width, source.Height);
            const int limit = 640;
            if (maxSide > limit)
            {
                double scale = limit / (double)maxSide;
                _previewBitmap = PhotoToolkit.Resize(source,
                    Math.Max(1, (int)Math.Round(source.Width * scale)),
                    Math.Max(1, (int)Math.Round(source.Height * scale)));
            }
            else
            {
                _previewBitmap = (Bitmap)source.Clone();
            }
            _preview.Photo = _previewBitmap;
        }
        catch
        {
            // 预览失败不影响 EXIF 查看
        }
    }

    private void PopulateList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        string filter = _search.Text.Trim();
        int total = 0;
        if (_info != null)
        {
            foreach (var entry in _info.Entries)
            {
                if (!string.IsNullOrEmpty(filter)
                    && !entry.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !entry.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var item = new ListViewItem(entry.Tag) { Tag = entry };
                item.SubItems.Add(entry.Value);
                item.SubItems.Add(entry.TypeName);
                _list.Items.Add(item);
                total++;
            }
        }
        _list.EndUpdate();
        _entryCountLabel.Text = "共 " + total + " 项";
        ShowDetail();
    }

    private void ShowDetail()
    {
        if (_list.SelectedItems.Count == 0)
        {
            _detail.Text = _info?.Entries.Count == 0
                ? "该图片不包含 EXIF / 元数据。"
                : "选中一条 EXIF 记录可查看完整内容。";
            return;
        }
        if (_list.SelectedItems[0].Tag is not ExifEntry entry)
        {
            return;
        }
        _detail.Text = entry.Tag + "\r\n"
            + "标签 ID：0x" + entry.Id.ToString("X4") + "    类型：" + entry.TypeName
            + "    长度：" + entry.Length + " 字节\r\n"
            + "值：" + entry.Value;
    }

    private void UpdateInfo(ExifInfo? info)
    {
        if (info == null)
        {
            _nameValue.Text = "—";
            _dimensionsValue.Text = "—";
            _sizeValue.Text = "—";
            _formatValue.Text = "—";
            _cameraValue.Text = "—";
            _dateValue.Text = "—";
            _orientationValue.Text = "—";
            _metadataValue.Text = "0 项";
            _gpsValue.Text = "无 GPS 定位信息";
            _gpsValue.ForeColor = ShutdownTheme.Success;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0.0") + " KB";
        }
        return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
    }

    private void CopyInfo()
    {
        if (_info == null || _path == null)
        {
            return;
        }
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("文件\t" + _path);
            sb.AppendLine("标签\t值\t类型");
            foreach (var entry in _info.Entries)
            {
                sb.AppendLine(entry.Tag + "\t" + entry.Value.Replace('\t', ' ') + "\t" + entry.TypeName);
            }
            Clipboard.SetText(sb.ToString());
            _removeStatus.ForeColor = ShutdownTheme.Success;
            _removeStatus.Text = "已复制 " + _info.Entries.Count + " 项 EXIF 信息到剪贴板。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "复制失败：\n" + ex.Message,
                "EXIF 信息", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ================= 剔除 =================

    private void RemoveExif(bool all)
    {
        if (_path == null || _info == null)
        {
            return;
        }
        var format = PhotoToolkit.FormatFromPath(_path) ?? ImageFormat.Jpeg;
        string ext = Path.GetExtension(_path);
        string suffix = all ? "_已清除EXIF" : "_已移除GPS";
        string baseName = Path.GetFileNameWithoutExtension(_path);
        using var dialog = new SaveFileDialog
        {
            Title = all ? "剔除全部 EXIF 并另存" : "仅移除 GPS 信息并另存",
            Filter = PhotoToolkit.FormatName(format) + "|*" + ext + "|所有文件|*.*",
            DefaultExt = ext,
            FileName = baseName + suffix + ext,
            InitialDirectory = Path.GetDirectoryName(_path),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            int remaining;
            if (all)
            {
                using var raw = PhotoToolkit.LoadBitmap(_path, autoOrient: false);
                using var oriented = PhotoToolkit.AutoOrient(raw);
                PhotoToolkit.Save(oriented, dialog.FileName, new PhotoSaveOptions
                {
                    Format = format,
                    Quality = (int)_quality.Value,
                    MetadataSource = null
                });
                remaining = CountProperties(dialog.FileName);
            }
            else
            {
                using var raw = PhotoToolkit.LoadBitmap(_path, autoOrient: false);
                PhotoToolkit.Save(raw, dialog.FileName, new PhotoSaveOptions
                {
                    Format = format,
                    Quality = (int)_quality.Value,
                    MetadataSource = raw,
                    IncludeGps = false
                });
                remaining = CountProperties(dialog.FileName);
            }

            int removed = Math.Max(0, _info.Entries.Count - remaining);
            _removeStatus.ForeColor = ShutdownTheme.Success;
            _removeStatus.Text = "已保存：" + Path.GetFileName(dialog.FileName)
                + "，移除约 " + removed + " 项元数据。";
            MessageBox.Show(FindForm(),
                (all ? "已剔除全部 EXIF 元数据。\n\n" : "已移除 GPS 定位信息，其它 EXIF 保留。\n\n")
                + "输出文件：" + dialog.FileName + "\n"
                + "原元数据 " + _info.Entries.Count + " 项，输出后剩余 " + remaining + " 项。",
                "EXIF 信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "剔除失败：\n" + ex.Message,
                "EXIF 信息", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static int CountProperties(string path)
    {
        try
        {
            using var image = Image.FromFile(path);
            return image.PropertyItems.Length;
        }
        catch
        {
            return 0;
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
            _preview.Photo = null;
            _previewBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
