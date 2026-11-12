using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>单张照片处理：旋转 / 翻转 / 裁剪 / 缩放 / 色彩滤镜 / 导出。</summary>
internal sealed class ImageSingleTab : UserControl
{
    private readonly PhotoPreviewControl _preview;
    private readonly Label _fileLabel;
    private readonly Label _statusLabel;
    private readonly Label _cropLabel;
    private readonly Label _sizeHint;
    private readonly Label _brightnessValue;
    private readonly Label _contrastValue;
    private readonly Label _saturationValue;
    private readonly Label _sizeUnit;
    private readonly Label _sizeX;
    private readonly ComboBox _filterCombo;
    private readonly ComboBox _formatCombo;
    private readonly ComboBox _sizeModeCombo;
    private readonly TrackBar _brightnessBar;
    private readonly TrackBar _contrastBar;
    private readonly TrackBar _saturationBar;
    private readonly NumericUpDown _sizeValue;
    private readonly NumericUpDown _sizeWidth;
    private readonly NumericUpDown _sizeHeight;
    private readonly NumericUpDown _quality;
    private readonly CheckBox _keepExif;
    private readonly PillButton _applySizeButton;
    private readonly PillButton _applyCropButton;
    private readonly PillButton _cancelCropButton;
    private readonly PillButton _exportButton;
    private readonly System.Windows.Forms.Timer _previewTimer;

    private Bitmap? _metadataSource;
    private Bitmap? _working;
    private Bitmap? _previewSource;
    private Bitmap? _previewBitmap;
    private string? _path;
    private bool _syncing;
    private bool _disposed;

    public ImageSingleTab()
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

        // ---------- 顶部工具栏 ----------
        var toolbar = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            CornerRadius = 14
        };
        var openButton = MakeButton("打开照片", 14, 9, 96, 34, PillButtonStyle.Primary);
        openButton.Click += (_, _) => OpenFile();
        var saveButton = MakeButton("导出图片", 118, 9, 96, 34, PillButtonStyle.Light);
        saveButton.Click += (_, _) => Export();
        var resetButton = MakeButton("重置全部", 222, 9, 96, 34, PillButtonStyle.Light);
        resetButton.Click += (_, _) => ResetAll();
        _fileLabel = MakeLabel("尚未打开照片 · 支持 JPG / PNG / BMP / GIF / TIFF", 334, 9, 600, 34,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _fileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _fileLabel.AutoEllipsis = true;
        _fileLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        toolbar.Controls.AddRange(new Control[]
        {
            openButton, saveButton, resetButton, _fileLabel
        });
        toolbar.Resize += (_, _) =>
        {
            _fileLabel.Width = Math.Max(100, toolbar.ClientSize.Width - _fileLabel.Left - 14);
        };
        root.Controls.Add(toolbar, 0, 0);

        // ---------- 主体：预览 + 设置 ----------
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 342));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        var previewCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(8)
        };
        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White
        };
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        _preview = new PhotoPreviewControl { Dock = DockStyle.Fill };
        _preview.SelectionChanged += (_, _) => UpdateCropState();
        _preview.AllowDrop = true;
        _preview.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        _preview.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                LoadFile(files[0]);
            }
        };
        _statusLabel = MakeLabel("就绪", 4, 0, 300, 24,
            new Font("Microsoft YaHei UI", 8.2f), ShutdownTheme.Muted, Color.White);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        previewLayout.Controls.Add(_preview, 0, 0);
        previewLayout.Controls.Add(_statusLabel, 0, 1);
        previewCard.Controls.Add(previewLayout);
        body.Controls.Add(previewCard, 0, 0);

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
            RowCount = 4,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        settingsScroll.Controls.Add(stack);
        body.Controls.Add(settingsScroll, 1, 0);

        // ---------- 几何变换 ----------
        var geometryCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 184,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var geometryTitle = MakeLabel("几何变换", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var rotateLeft = MakeButton("左转 90°", 16, 40, 88, 32, PillButtonStyle.Light);
        rotateLeft.Click += (_, _) => ApplyTransform(
            bmp => PhotoToolkit.RotateFlip(bmp, RotateFlipType.Rotate270FlipNone), "已向左旋转 90°");
        var rotateRight = MakeButton("右转 90°", 112, 40, 88, 32, PillButtonStyle.Light);
        rotateRight.Click += (_, _) => ApplyTransform(
            bmp => PhotoToolkit.RotateFlip(bmp, RotateFlipType.Rotate90FlipNone), "已向右旋转 90°");
        var rotate180 = MakeButton("旋转 180°", 208, 40, 88, 32, PillButtonStyle.Light);
        rotate180.Click += (_, _) => ApplyTransform(
            bmp => PhotoToolkit.RotateFlip(bmp, RotateFlipType.Rotate180FlipNone), "已旋转 180°");
        var flipH = MakeButton("水平翻转", 16, 78, 88, 32, PillButtonStyle.Light);
        flipH.Click += (_, _) => ApplyTransform(
            bmp => PhotoToolkit.RotateFlip(bmp, RotateFlipType.RotateNoneFlipX), "已水平翻转");
        var flipV = MakeButton("垂直翻转", 112, 78, 88, 32, PillButtonStyle.Light);
        flipV.Click += (_, _) => ApplyTransform(
            bmp => PhotoToolkit.RotateFlip(bmp, RotateFlipType.RotateNoneFlipY), "已垂直翻转");
        _applyCropButton = MakeButton("应用裁剪", 16, 116, 100, 30, PillButtonStyle.Primary);
        _applyCropButton.Enabled = false;
        _applyCropButton.Click += (_, _) => ApplyCrop();
        _cancelCropButton = MakeButton("取消选区", 124, 116, 100, 30, PillButtonStyle.Light);
        _cancelCropButton.Enabled = false;
        _cancelCropButton.Click += (_, _) => _preview.ClearSelection();
        _cropLabel = MakeLabel("未框选", 230, 116, 80, 30,
            new Font("Microsoft YaHei UI", 8.2f), ShutdownTheme.Muted, Color.White);
        _cropLabel.TextAlign = ContentAlignment.MiddleRight;
        var cropHint = MakeLabel("在左侧预览图上按住鼠标拖动，可框选裁剪区域", 16, 152, 294, 20,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        geometryCard.Controls.AddRange(new Control[]
        {
            geometryTitle, rotateLeft, rotateRight, rotate180, flipH, flipV,
            _applyCropButton, _cancelCropButton, _cropLabel, cropHint
        });
        stack.Controls.Add(geometryCard, 0, 0);

        // ---------- 尺寸 ----------
        var sizeCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 196,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var sizeTitle = MakeLabel("调整尺寸", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _sizeModeCombo = new ComboBox
        {
            Location = new Point(16, 40),
            Size = new Size(294, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _sizeModeCombo.Items.AddRange(new object[]
        {
            "保持原始尺寸", "按百分比缩放", "限制最长边", "按宽度缩放", "指定宽高"
        });
        _sizeModeCombo.SelectedIndex = 0;
        _sizeModeCombo.SelectedIndexChanged += (_, _) => OnSizeModeChanged();
        _sizeValue = new NumericUpDown
        {
            Location = new Point(16, 78),
            Size = new Size(110, 28),
            Minimum = 1,
            Maximum = 20000,
            Value = 100,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _sizeValue.ValueChanged += (_, _) => UpdateSizeHint();
        _sizeUnit = MakeLabel("px", 132, 78, 60, 28,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _sizeUnit.TextAlign = ContentAlignment.MiddleLeft;
        _sizeWidth = new NumericUpDown
        {
            Location = new Point(16, 78),
            Size = new Size(110, 28),
            Minimum = 1,
            Maximum = 20000,
            Value = 1000,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Visible = false
        };
        _sizeX = MakeLabel("×", 132, 78, 18, 28,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        _sizeX.TextAlign = ContentAlignment.MiddleCenter;
        _sizeX.Visible = false;
        _sizeHeight = new NumericUpDown
        {
            Location = new Point(154, 78),
            Size = new Size(110, 28),
            Minimum = 1,
            Maximum = 20000,
            Value = 1000,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Visible = false
        };
        _sizeWidth.ValueChanged += (_, _) => UpdateSizeHint();
        _sizeHeight.ValueChanged += (_, _) => UpdateSizeHint();
        _sizeHint = MakeLabel("当前尺寸：—", 16, 116, 294, 20,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _applySizeButton = MakeButton("应用缩放", 16, 144, 120, 32, PillButtonStyle.Primary);
        _applySizeButton.Enabled = false;
        _applySizeButton.Click += (_, _) => ApplyResize();
        sizeCard.Controls.AddRange(new Control[]
        {
            sizeTitle, _sizeModeCombo, _sizeValue, _sizeUnit, _sizeWidth, _sizeX, _sizeHeight,
            _sizeHint, _applySizeButton
        });
        stack.Controls.Add(sizeCard, 0, 1);

        // ---------- 色彩 ----------
        var colorCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 298,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var colorTitle = MakeLabel("色彩调整", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var brightnessTitle = MakeLabel("亮度", 16, 40, 80, 22,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _brightnessValue = MakeLabel("0", 250, 40, 60, 22,
            new Font("Microsoft YaHei UI", 8.2f), ShutdownTheme.Muted, Color.White);
        _brightnessValue.TextAlign = ContentAlignment.MiddleRight;
        _brightnessBar = new TrackBar
        {
            Location = new Point(12, 62),
            Size = new Size(298, 45),
            AutoSize = false,
            Minimum = -50,
            Maximum = 50,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 10,
            Value = 0
        };
        var contrastTitle = MakeLabel("对比度", 16, 112, 80, 22,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _contrastValue = MakeLabel("0", 250, 112, 60, 22,
            new Font("Microsoft YaHei UI", 8.2f), ShutdownTheme.Muted, Color.White);
        _contrastValue.TextAlign = ContentAlignment.MiddleRight;
        _contrastBar = new TrackBar
        {
            Location = new Point(12, 134),
            Size = new Size(298, 45),
            AutoSize = false,
            Minimum = -50,
            Maximum = 50,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 10,
            Value = 0
        };
        var saturationTitle = MakeLabel("饱和度", 16, 184, 80, 22,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _saturationValue = MakeLabel("0", 250, 184, 60, 22,
            new Font("Microsoft YaHei UI", 8.2f), ShutdownTheme.Muted, Color.White);
        _saturationValue.TextAlign = ContentAlignment.MiddleRight;
        _saturationBar = new TrackBar
        {
            Location = new Point(12, 206),
            Size = new Size(298, 45),
            AutoSize = false,
            Minimum = -100,
            Maximum = 100,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 10,
            Value = 0
        };
        var filterTitle = MakeLabel("滤镜", 16, 256, 40, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _filterCombo = new ComboBox
        {
            Location = new Point(60, 254),
            Size = new Size(130, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _filterCombo.Items.AddRange(new object[]
        {
            "原色", "黑白", "怀旧", "冷色调", "暖色调", "反色"
        });
        _filterCombo.SelectedIndex = 0;
        var resetColor = MakeButton("重置调整", 200, 252, 110, 30, PillButtonStyle.Light);
        resetColor.Click += (_, _) => ResetAdjustments();
        colorCard.Controls.AddRange(new Control[]
        {
            colorTitle, brightnessTitle, _brightnessValue, _brightnessBar,
            contrastTitle, _contrastValue, _contrastBar,
            saturationTitle, _saturationValue, _saturationBar,
            filterTitle, _filterCombo, resetColor
        });
        stack.Controls.Add(colorCard, 0, 2);

        // ---------- 导出 ----------
        var exportCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 198,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var exportTitle = MakeLabel("导出设置", 16, 10, 220, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var formatTitle = MakeLabel("格式", 16, 42, 40, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _formatCombo = new ComboBox
        {
            Location = new Point(60, 40),
            Size = new Size(130, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _formatCombo.Items.AddRange(new object[] { "JPEG", "PNG", "BMP", "GIF", "TIFF" });
        _formatCombo.SelectedIndex = 0;
        _formatCombo.SelectedIndexChanged += (_, _) => UpdateQualityState();
        var qualityTitle = MakeLabel("质量", 208, 42, 40, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _quality = new NumericUpDown
        {
            Location = new Point(250, 40),
            Size = new Size(60, 28),
            Minimum = 1,
            Maximum = 100,
            Value = 92,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _keepExif = new CheckBox
        {
            Text = "保留 EXIF 信息（默认关闭）",
            Location = new Point(16, 78),
            Size = new Size(294, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = false
        };
        var exportHint = MakeLabel(
            "默认剔除全部 EXIF/GPS；JPEG 为有损压缩，PNG 无损且支持透明。",
            16, 106, 294, 36,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        _exportButton = MakeButton("导出图片…", 16, 148, 150, 34, PillButtonStyle.Primary);
        _exportButton.Click += (_, _) => Export();
        exportCard.Controls.AddRange(new Control[]
        {
            exportTitle, formatTitle, _formatCombo, qualityTitle, _quality,
            _keepExif, exportHint, _exportButton
        });
        stack.Controls.Add(exportCard, 0, 3);

        // ---------- 事件 ----------
        _previewTimer = new System.Windows.Forms.Timer { Interval = 90 };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreviewBitmap();
        };
        _brightnessBar.ValueChanged += (_, _) => OnAdjustmentChanged();
        _contrastBar.ValueChanged += (_, _) => OnAdjustmentChanged();
        _saturationBar.ValueChanged += (_, _) => OnAdjustmentChanged();
        _filterCombo.SelectedIndexChanged += (_, _) => OnAdjustmentChanged();

        UpdateQualityState();
        OnSizeModeChanged();
        UpdateInfo();
        UpdateCropState();
    }

    // ================= 文件加载 / 导出 =================

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开照片",
            Filter = PhotoToolkit.OpenFileFilter,
            CheckFileExists = true
        };
        if (_path != null)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_path);
        }
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            LoadFile(dialog.FileName);
        }
    }

    private void LoadFile(string path)
    {
        if (!PhotoToolkit.IsSupported(path))
        {
            MessageBox.Show(FindForm(), "不支持的图片格式：\n" + path + "\n\n支持 JPG / PNG / BMP / GIF / TIFF。",
                "图片处理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            var meta = PhotoToolkit.LoadBitmap(path, autoOrient: true);
            var working = (Bitmap)meta.Clone();
            _metadataSource?.Dispose();
            _working?.Dispose();
            _metadataSource = meta;
            _working = working;
            _path = path;
            _preview.ClearSelection();
            ResetAdjustments(silent: true);
            RebuildPreviewSource();
            RefreshPreviewBitmap();
            UpdateInfo();
            UpdateSizeHint();
            _statusLabel.Text = "已打开：" + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "无法打开照片：\n" + ex.Message,
                "图片处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Export()
    {
        if (_working == null)
        {
            MessageBox.Show(FindForm(), "请先打开一张照片。", "图片处理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var format = SelectedFormat();
        string ext = PhotoToolkit.ExtensionFor(format);
        string baseName = _path != null
            ? Path.GetFileNameWithoutExtension(_path)
            : "照片";
        using var dialog = new SaveFileDialog
        {
            Title = "导出图片",
            Filter = PhotoToolkit.FormatName(format) + "|*" + ext + "|所有文件|*.*",
            DefaultExt = ext,
            FileName = baseName + "_处理后" + ext,
            InitialDirectory = _path != null ? Path.GetDirectoryName(_path) : null,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var adjustment = CurrentAdjustment();
            Bitmap? adjusted = null;
            Bitmap source = _working;
            if (!adjustment.IsNeutral)
            {
                adjusted = PhotoToolkit.ApplyAdjustments(_working, adjustment);
                source = adjusted;
            }
            var options = new PhotoSaveOptions
            {
                Format = format,
                Quality = (int)_quality.Value,
                MetadataSource = _keepExif.Checked ? _metadataSource : null,
                IncludeGps = true
            };
            PhotoToolkit.Save(source, dialog.FileName, options);
            adjusted?.Dispose();
            _statusLabel.Text = "已导出：" + Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "导出失败：\n" + ex.Message,
                "图片处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private ImageFormat SelectedFormat()
    {
        return _formatCombo.SelectedIndex switch
        {
            1 => ImageFormat.Png,
            2 => ImageFormat.Bmp,
            3 => ImageFormat.Gif,
            4 => ImageFormat.Tiff,
            _ => ImageFormat.Jpeg
        };
    }

    private void UpdateQualityState()
    {
        bool jpeg = _formatCombo.SelectedIndex == 0;
        _quality.Enabled = jpeg;
        _quality.BackColor = jpeg ? Color.White : Color.FromArgb(241, 245, 249);
    }

    // ================= 图像操作 =================

    private void ApplyTransform(Func<Bitmap, Bitmap> operation, string status)
    {
        if (_working == null)
        {
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            var next = operation(_working);
            ReplaceWorking(next);
            _statusLabel.Text = status;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "处理失败：\n" + ex.Message,
                "图片处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ApplyCrop()
    {
        if (_working == null)
        {
            return;
        }
        var selection = _preview.Selection;
        if (selection == null)
        {
            return;
        }
        try
        {
            var next = PhotoToolkit.Crop(_working, selection.Value);
            ReplaceWorking(next);
            _preview.ClearSelection();
            _statusLabel.Text = "已裁剪为 " + next.Width + " × " + next.Height;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "裁剪失败：\n" + ex.Message,
                "图片处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyResize()
    {
        if (_working == null)
        {
            return;
        }
        var target = ComputeTargetSize();
        if (target == null)
        {
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            var next = PhotoToolkit.Resize(_working, target.Value.Width, target.Value.Height);
            ReplaceWorking(next);
            _statusLabel.Text = "已缩放为 " + next.Width + " × " + next.Height;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "缩放失败：\n" + ex.Message,
                "图片处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private Size? ComputeTargetSize()
    {
        if (_working == null)
        {
            return null;
        }
        int w = _working.Width;
        int h = _working.Height;
        switch (_sizeModeCombo.SelectedIndex)
        {
            case 1:
                int percent = (int)_sizeValue.Value;
                return new Size(Math.Max(1, w * percent / 100), Math.Max(1, h * percent / 100));
            case 2:
                int maxSide = (int)_sizeValue.Value;
                double scale = maxSide / (double)Math.Max(w, h);
                return new Size(Math.Max(1, (int)Math.Round(w * scale)),
                    Math.Max(1, (int)Math.Round(h * scale)));
            case 3:
                int width = (int)_sizeValue.Value;
                return new Size(width, Math.Max(1, (int)Math.Round(h * width / (double)w)));
            case 4:
                return new Size((int)_sizeWidth.Value, (int)_sizeHeight.Value);
            default:
                return new Size(w, h);
        }
    }

    private void ReplaceWorking(Bitmap next)
    {
        var old = _working;
        _working = next;
        old?.Dispose();
        _preview.ClearSelection();
        RebuildPreviewSource();
        RefreshPreviewBitmap();
        UpdateInfo();
        UpdateSizeHint();
    }

    private void ResetAll()
    {
        if (_path == null)
        {
            MessageBox.Show(FindForm(), "请先打开一张照片。", "图片处理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        LoadFile(_path);
        _statusLabel.Text = "已重置为原始照片";
    }

    private void ResetAdjustments(bool silent = false)
    {
        _syncing = true;
        _brightnessBar.Value = 0;
        _contrastBar.Value = 0;
        _saturationBar.Value = 0;
        _filterCombo.SelectedIndex = 0;
        _syncing = false;
        UpdateAdjustmentLabels();
        if (!silent)
        {
            RefreshPreviewBitmap();
            _statusLabel.Text = "已重置色彩调整";
        }
    }

    private void OnAdjustmentChanged()
    {
        UpdateAdjustmentLabels();
        if (_syncing)
        {
            return;
        }
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdateAdjustmentLabels()
    {
        _brightnessValue.Text = Signed(_brightnessBar.Value);
        _contrastValue.Text = Signed(_contrastBar.Value);
        _saturationValue.Text = Signed(_saturationBar.Value);
    }

    private static string Signed(int value)
    {
        return value > 0 ? "+" + value : value.ToString();
    }

    private PhotoAdjustment CurrentAdjustment()
    {
        return new PhotoAdjustment
        {
            Filter = (PhotoFilter)_filterCombo.SelectedIndex,
            Brightness = _brightnessBar.Value,
            Contrast = _contrastBar.Value,
            Saturation = _saturationBar.Value
        };
    }

    private void RebuildPreviewSource()
    {
        _previewSource?.Dispose();
        _previewSource = null;
        if (_working == null)
        {
            return;
        }
        int maxSide = Math.Max(_working.Width, _working.Height);
        const int limit = 1600;
        if (maxSide <= limit)
        {
            _previewSource = (Bitmap)_working.Clone();
            return;
        }
        double scale = limit / (double)maxSide;
        _previewSource = PhotoToolkit.Resize(_working,
            Math.Max(1, (int)Math.Round(_working.Width * scale)),
            Math.Max(1, (int)Math.Round(_working.Height * scale)));
    }

    private void RefreshPreviewBitmap()
    {
        if (_disposed)
        {
            return;
        }
        var adjustment = CurrentAdjustment();
        Bitmap? next = null;
        if (_previewSource != null)
        {
            next = PhotoToolkit.ApplyAdjustments(_previewSource, adjustment);
        }
        var old = _previewBitmap;
        _previewBitmap = next;
        _preview.Photo = next;
        old?.Dispose();
    }

    private void OnSizeModeChanged()
    {
        int mode = _sizeModeCombo.SelectedIndex;
        bool valueMode = mode is 1 or 2 or 3;
        bool sizeMode = mode == 4;
        _sizeValue.Visible = valueMode;
        _sizeUnit.Visible = valueMode;
        _sizeWidth.Visible = sizeMode;
        _sizeHeight.Visible = sizeMode;
        _sizeX.Visible = sizeMode;
        _sizeUnit.Text = mode == 1 ? "%" : "px";
        _sizeValue.Maximum = mode == 1 ? 1000 : 20000;
        _applySizeButton.Enabled = mode != 0 && _working != null;
        UpdateSizeHint();
    }

    private void UpdateSizeHint()
    {
        if (_working == null)
        {
            _sizeHint.Text = "当前尺寸：—";
            _applySizeButton.Enabled = false;
            return;
        }
        _applySizeButton.Enabled = _sizeModeCombo.SelectedIndex != 0;
        var target = ComputeTargetSize();
        _sizeHint.Text = target == null
            ? "当前尺寸：—"
            : "当前 " + _working.Width + " × " + _working.Height
                + "  →  目标 " + target.Value.Width + " × " + target.Value.Height;
    }

    private void UpdateCropState()
    {
        var selection = _preview.Selection;
        _applyCropButton.Enabled = selection != null;
        _cancelCropButton.Enabled = selection != null;
        _cropLabel.Text = selection == null
            ? "未框选"
            : selection.Value.Width + " × " + selection.Value.Height;
    }

    private void UpdateInfo()
    {
        if (_working == null || _path == null)
        {
            _fileLabel.Text = "尚未打开照片 · 支持 JPG / PNG / BMP / GIF / TIFF";
            return;
        }
        long size = 0;
        try
        {
            size = new FileInfo(_path).Length;
        }
        catch
        {
            // 忽略
        }
        string sizeText = size < 1024 * 1024
            ? (size / 1024.0).ToString("0") + " KB"
            : (size / 1024.0 / 1024.0).ToString("0.0") + " MB";
        string original = _metadataSource != null
            ? "  ·  原始 " + _metadataSource.Width + " × " + _metadataSource.Height
            : "";
        _fileLabel.Text = Path.GetFileName(_path) + "  ·  " + _working.Width + " × " + _working.Height
            + "  ·  " + sizeText + original;
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
            _disposed = true;
            _previewTimer.Dispose();
            _preview.Photo = null;
            _previewBitmap?.Dispose();
            _previewSource?.Dispose();
            _working?.Dispose();
            _metadataSource?.Dispose();
        }
        base.Dispose(disposing);
    }
}
