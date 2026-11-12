using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 证件照处理：常用规格预设、像素/物理尺寸/DPI 联动、纯色背景自动抠图与替换、
/// 24 位 RGB 输出、文件大小区间控制，导出时自动剔除全部 EXIF/GPS。
/// </summary>
internal sealed class ImageIdPhotoTab : UserControl
{
    private sealed record IdPreset(string Name, double MmWidth, double MmHeight, int PixelWidth, int PixelHeight)
    {
        public override string ToString() => Name;
    }

    private static readonly IdPreset[] Presets =
    {
        new("标准一寸（25×35mm / 295×413px）", 25, 35, 295, 413),
        new("小一寸（22×32mm / 260×378px）", 22, 32, 260, 378),
        new("大一寸（33×48mm / 390×567px）", 33, 48, 390, 567),
        new("标准二寸（35×49mm / 413×579px）", 35, 49, 413, 579),
        new("小二寸（33×48mm / 390×567px）", 33, 48, 390, 567),
        new("小二寸（35×45mm / 413×531px）", 35, 45, 413, 531)
    };

    private readonly PhotoPreviewControl _preview;
    private readonly Label _fileLabel;
    private readonly Label _statusLabel;
    private readonly Label _detectedBgLabel;
    private readonly Label _toleranceValue;
    private readonly Label _backgroundHexLabel;
    private readonly Panel _detectedSwatch;
    private readonly Panel _backgroundSwatch;
    private readonly ComboBox _presetCombo;
    private readonly ComboBox _backgroundCombo;
    private readonly ComboBox _formatCombo;
    private readonly ComboBox _colorCombo;
    private readonly NumericUpDown _mmWidth;
    private readonly NumericUpDown _mmHeight;
    private readonly NumericUpDown _pxWidth;
    private readonly NumericUpDown _pxHeight;
    private readonly NumericUpDown _dpi;
    private readonly NumericUpDown _hOffset;
    private readonly NumericUpDown _vOffset;
    private readonly NumericUpDown _minKb;
    private readonly NumericUpDown _maxKb;
    private readonly TrackBar _tolerance;
    private readonly CheckBox _autoBackgroundCheck;
    private readonly CheckBox _fineMattingCheck;
    private readonly CheckBox _complexMattingCheck;
    private readonly CheckBox _limitSizeCheck;
    private readonly PillButton _exportButton;
    private readonly System.Windows.Forms.Timer _previewTimer;
    private readonly Label _cropInfoLabel;

    private Bitmap? _source;
    private Bitmap? _previewBitmap;
    private string? _path;
    private Rectangle? _cropRect;
    private Color _customBackground = Color.FromArgb(255, 0, 0);
    private int _lastBackgroundIndex;
    private bool _updating;
    private bool _disposed;

    public ImageIdPhotoTab()
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

        // ---------- 顶部 ----------
        var toolbar = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            CornerRadius = 14
        };
        var openButton = MakeButton("打开照片", 14, 9, 96, 34, PillButtonStyle.Primary);
        openButton.Click += (_, _) => OpenFile();
        var resetButton = MakeButton("重置参数", 118, 9, 96, 34, PillButtonStyle.Light);
        resetButton.Click += (_, _) => ResetSettings();
        _fileLabel = MakeLabel("尚未打开证件照 · 支持 JPG / PNG / BMP / TIFF", 326, 9, 600, 34,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _fileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _fileLabel.AutoEllipsis = true;
        _fileLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        toolbar.Controls.AddRange(new Control[] { openButton, resetButton, _fileLabel });
        toolbar.Resize += (_, _) =>
        {
            _fileLabel.Width = Math.Max(100, toolbar.ClientSize.Width - _fileLabel.Left - 14);
        };
        root.Controls.Add(toolbar, 0, 0);

        // ---------- 预览 + 参数 ----------
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

        var previewCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(8)
        };
        _preview = new PhotoPreviewControl
        {
            Dock = DockStyle.Fill,
            SelectionEnabled = false
        };
        previewCard.Controls.Add(_preview);
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

        // ---------- 规格 ----------
        var sizeCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 252,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var sizeTitle = MakeLabel("证件照规格", 16, 10, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var presetTitle = MakeLabel("预设", 16, 42, 44, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _presetCombo = new ComboBox
        {
            Location = new Point(60, 40),
            Size = new Size(266, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        foreach (var preset in Presets)
        {
            _presetCombo.Items.Add(preset);
        }
        _presetCombo.Items.Add("自定义");
        _presetCombo.SelectedIndexChanged += (_, _) => ApplyPreset(_presetCombo.SelectedIndex);

        var mmTitle = MakeLabel("物理尺寸", 16, 78, 68, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _mmWidth = MakeNumeric(86, 76, 78, 1, 500, 25, 0.5m, 1);
        _mmHeight = MakeNumeric(188, 76, 78, 1, 500, 35, 0.5m, 1);
        var mmX = MakeLabel("×", 168, 76, 16, 26,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        mmX.TextAlign = ContentAlignment.MiddleCenter;
        var mmUnit = MakeLabel("mm", 272, 78, 30, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);

        var pxTitle = MakeLabel("像素尺寸", 16, 114, 68, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _pxWidth = MakeNumeric(86, 112, 78, 10, 4000, 295, 1, 0);
        _pxHeight = MakeNumeric(188, 112, 78, 10, 4000, 413, 1, 0);
        var pxX = MakeLabel("×", 168, 112, 16, 26,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        pxX.TextAlign = ContentAlignment.MiddleCenter;
        var pxUnit = MakeLabel("px", 272, 114, 30, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);

        var dpiTitle = MakeLabel("分辨率", 16, 150, 68, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _dpi = MakeNumeric(86, 148, 78, 72, 600, 300, 1, 0);
        var dpiUnit = MakeLabel("DPI", 170, 150, 40, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var sizeHint = MakeLabel(
            "300 DPI 为常用电子版尺寸；身份证等可按 350 DPI。"
            + "像素、物理尺寸与 DPI 自动联动；报名系统通常要求不小于所选规格的像素尺寸。",
            16, 184, 300, 60,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        sizeCard.Controls.AddRange(new Control[]
        {
            sizeTitle, presetTitle, _presetCombo, mmTitle, _mmWidth, mmX, _mmHeight, mmUnit,
            pxTitle, _pxWidth, pxX, _pxHeight, pxUnit, dpiTitle, _dpi, dpiUnit, sizeHint
        });
        stack.Controls.Add(sizeCard, 0, 0);

        // ---------- 构图 ----------
        var cropCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 176,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var cropTitle = MakeLabel("构图位置", 16, 10, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var hTitle = MakeLabel("水平", 16, 44, 60, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _hOffset = MakeNumeric(78, 42, 76, -100, 100, 0, 1, 0);
        var hUnit = MakeLabel("%", 156, 44, 28, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var vTitle = MakeLabel("垂直", 16, 80, 60, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _vOffset = MakeNumeric(78, 78, 76, -100, 100, 0, 1, 0);
        var vUnit = MakeLabel("%", 156, 80, 28, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var cropButton = MakeButton("裁剪原图", 16, 108, 96, 30, PillButtonStyle.Light);
        cropButton.Click += (_, _) => OpenCropDialog();
        var clearCropButton = MakeButton("清除裁剪", 120, 108, 96, 30, PillButtonStyle.Light);
        clearCropButton.Click += (_, _) => ClearCrop();
        _cropInfoLabel = MakeLabel("未裁剪", 224, 108, 100, 30,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _cropInfoLabel.TextAlign = ContentAlignment.MiddleLeft;
        _cropInfoLabel.AutoEllipsis = true;
        var cropHint = MakeLabel(
            "可先框选保留区域，去掉照片中不需要的元素；裁剪后再做抠图与构图。",
            16, 144, 300, 24,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        cropCard.Controls.AddRange(new Control[]
        {
            cropTitle, hTitle, _hOffset, hUnit, vTitle, _vOffset, vUnit,
            cropButton, clearCropButton, _cropInfoLabel, cropHint
        });
        stack.Controls.Add(cropCard, 0, 1);

        // ---------- 背景 ----------
        var backgroundCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 344,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var backgroundTitle = MakeLabel("背景处理", 16, 10, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _autoBackgroundCheck = new CheckBox
        {
            Text = "自动纯色背景抠图并替换",
            Location = new Point(16, 40),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = true
        };
        _autoBackgroundCheck.CheckedChanged += (_, _) =>
        {
            UpdateBackgroundEnabled();
            UpdateDetectedLabel();
            SchedulePreview();
        };
        var bgTitle = MakeLabel("背景颜色", 16, 72, 64, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _backgroundCombo = new ComboBox
        {
            Location = new Point(82, 70),
            Size = new Size(150, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _backgroundCombo.Items.AddRange(new object[]
        {
            "红底", "蓝底", "白底", "浅蓝底", "灰底", "自定义…"
        });
        _backgroundCombo.SelectedIndex = 0;
        _lastBackgroundIndex = 0;
        _backgroundCombo.SelectedIndexChanged += (_, _) => OnBackgroundChanged();
        _backgroundSwatch = new Panel
        {
            Location = new Point(240, 73),
            Size = new Size(16, 16),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(255, 0, 0)
        };
        _backgroundHexLabel = MakeLabel("#FF0000", 262, 72, 66, 24,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        _backgroundHexLabel.TextAlign = ContentAlignment.MiddleLeft;

        var detectTitle = MakeLabel("识别背景", 16, 104, 64, 24,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _detectedSwatch = new Panel
        {
            Location = new Point(82, 106),
            Size = new Size(16, 16),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };
        _detectedBgLabel = MakeLabel("尚未识别", 106, 104, 220, 24,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _detectedBgLabel.TextAlign = ContentAlignment.MiddleLeft;

        var toleranceTitle = MakeLabel("抠图容差", 16, 136, 64, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _tolerance = new TrackBar
        {
            Location = new Point(80, 132),
            Size = new Size(150, 45),
            AutoSize = false,
            Minimum = 0,
            Maximum = 100,
            Value = 20,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 10
        };
        _toleranceValue = MakeLabel("20", 232, 136, 56, 26,
            new Font("Microsoft YaHei UI", 8.2f), ShutdownTheme.Muted, Color.White);
        _toleranceValue.TextAlign = ContentAlignment.MiddleRight;
        _tolerance.ValueChanged += (_, _) =>
        {
            _toleranceValue.Text = _tolerance.Value.ToString();
            SchedulePreview();
        };
        _fineMattingCheck = new CheckBox
        {
            Text = "发丝级精细抠图（推荐）",
            Location = new Point(16, 184),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = true
        };
        _fineMattingCheck.CheckedChanged += (_, _) =>
        {
            UpdateBackgroundEnabled();
            SchedulePreview();
        };
        _complexMattingCheck = new CheckBox
        {
            Text = "复杂背景抠图（仅保留人像与衣物，较慢）",
            Location = new Point(16, 214),
            Size = new Size(320, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = false
        };
        _complexMattingCheck.CheckedChanged += (_, _) =>
        {
            UpdateBackgroundEnabled();
            UpdateDetectedLabel();
            SchedulePreview();
        };
        var backgroundHint = MakeLabel(
            "普通模式从画面边缘识别纯色/渐变背景；复杂模式会建立背景与人物颜色模型，"
            + "只保留人像和衣物，适合背景有明显纹理、杂物或颜色不均的照片。"
            + "容差越大去除范围越大，过大会误伤同色头发或衣服。",
            16, 248, 300, 88,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        backgroundCard.Controls.AddRange(new Control[]
        {
            backgroundTitle, _autoBackgroundCheck, bgTitle, _backgroundCombo,
            _backgroundSwatch, _backgroundHexLabel, detectTitle, _detectedSwatch,
            _detectedBgLabel, toleranceTitle, _tolerance, _toleranceValue,
            _fineMattingCheck, _complexMattingCheck, backgroundHint
        });
        stack.Controls.Add(backgroundCard, 0, 2);

        // ---------- 输出 ----------
        var outputCard = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 234,
            Margin = new Padding(0, 0, 0, 0),
            CornerRadius = 14
        };
        var outputTitle = MakeLabel("输出要求", 16, 10, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var formatTitle = MakeLabel("格式", 16, 42, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _formatCombo = new ComboBox
        {
            Location = new Point(56, 40),
            Size = new Size(106, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        _formatCombo.Items.AddRange(new object[] { "JPG（.jpg）", "JPEG（.jpeg）" });
        _formatCombo.SelectedIndex = 0;
        var colorTitle = MakeLabel("色彩", 176, 42, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _colorCombo = new ComboBox
        {
            Location = new Point(216, 40),
            Size = new Size(110, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        _colorCombo.Items.AddRange(new object[] { "原色（24 位 RGB）", "黑白（整张灰度）" });
        _colorCombo.SelectedIndex = 0;
        _colorCombo.SelectedIndexChanged += (_, _) => SchedulePreview();

        _limitSizeCheck = new CheckBox
        {
            Text = "限制文件大小（自动调整 JPEG 画质）",
            Location = new Point(16, 76),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            Checked = true
        };
        _limitSizeCheck.CheckedChanged += (_, _) => UpdateSizeLimitEnabled();
        var minTitle = MakeLabel("最小", 16, 110, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _minKb = MakeNumeric(56, 108, 64, 1, 20000, 10, 1, 0);
        var minUnit = MakeLabel("KB", 124, 110, 30, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var maxTitle = MakeLabel("最大", 156, 110, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _maxKb = MakeNumeric(196, 108, 64, 1, 20000, 50, 1, 0);
        var maxUnit = MakeLabel("KB", 264, 110, 30, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var outputHint = MakeLabel(
            "常见要求 10–50 KB（部分省份 10–30 KB 或 10–200 KB）。"
            + "程序会搜索最佳 JPEG 质量尽量落入区间；"
            + "输出固定 24 位 RGB，并自动剔除全部 EXIF/GPS。",
            16, 144, 300, 76,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        outputCard.Controls.AddRange(new Control[]
        {
            outputTitle, formatTitle, _formatCombo, colorTitle, _colorCombo,
            _limitSizeCheck, minTitle, _minKb, minUnit, maxTitle, _maxKb, maxUnit, outputHint
        });
        stack.Controls.Add(outputCard, 0, 3);

        // ---------- 底部 ----------
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        _statusLabel = MakeLabel("打开照片后即可生成证件照", 14, 15, 600, 28,
            new Font("Microsoft YaHei UI", 8.3f), ShutdownTheme.Muted, Color.White);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _exportButton = MakeButton("导出证件照", 0, 14, 150, 34, PillButtonStyle.Primary);
        _exportButton.Enabled = false;
        _exportButton.Click += (_, _) => Export();
        footer.Controls.AddRange(new Control[] { _statusLabel, _exportButton });
        footer.Resize += (_, _) =>
        {
            _exportButton.Location = new Point(Math.Max(0, footer.ClientSize.Width - 164), 14);
            _statusLabel.Width = Math.Max(120, _exportButton.Left - 30);
        };
        root.Controls.Add(footer, 0, 2);

        // ---------- 事件 ----------
        _previewTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };
        _mmWidth.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            MarkCustom();
            _updating = true;
            _pxWidth.Value = PixelsFromMm(_mmWidth.Value, _dpi.Value, _pxWidth);
            _pxHeight.Value = PixelsFromMm(_mmHeight.Value, _dpi.Value, _pxHeight);
            _updating = false;
            UpdateInfoText();
            SchedulePreview();
        };
        _mmHeight.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            MarkCustom();
            _updating = true;
            _pxWidth.Value = PixelsFromMm(_mmWidth.Value, _dpi.Value, _pxWidth);
            _pxHeight.Value = PixelsFromMm(_mmHeight.Value, _dpi.Value, _pxHeight);
            _updating = false;
            UpdateInfoText();
            SchedulePreview();
        };
        _pxWidth.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            MarkCustom();
            _updating = true;
            _mmWidth.Value = MmFromPixels(_pxWidth.Value, _dpi.Value, _mmWidth);
            _mmHeight.Value = MmFromPixels(_pxHeight.Value, _dpi.Value, _mmHeight);
            _updating = false;
            UpdateInfoText();
            SchedulePreview();
        };
        _pxHeight.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            MarkCustom();
            _updating = true;
            _mmWidth.Value = MmFromPixels(_pxWidth.Value, _dpi.Value, _mmWidth);
            _mmHeight.Value = MmFromPixels(_pxHeight.Value, _dpi.Value, _mmHeight);
            _updating = false;
            UpdateInfoText();
            SchedulePreview();
        };
        _dpi.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            MarkCustom();
            _updating = true;
            _pxWidth.Value = PixelsFromMm(_mmWidth.Value, _dpi.Value, _pxWidth);
            _pxHeight.Value = PixelsFromMm(_mmHeight.Value, _dpi.Value, _pxHeight);
            _updating = false;
            UpdateInfoText();
            SchedulePreview();
        };
        _hOffset.ValueChanged += (_, _) => SchedulePreview();
        _vOffset.ValueChanged += (_, _) => SchedulePreview();
        _formatCombo.SelectedIndexChanged += (_, _) => UpdateInfoText();

        UpdateBackgroundEnabled();
        UpdateSizeLimitEnabled();
        UpdateBackgroundLabels();
        UpdateDetectedLabel();
        UpdateCropInfo();
        _updating = true;
        _presetCombo.SelectedIndex = 0;
        _updating = false;
        ApplyPreset(0);
        UpdateInfoText();
    }

    private static NumericUpDown MakeNumeric(int x, int y, int width,
        decimal min, decimal max, decimal value, decimal increment, int decimals)
    {
        return new NumericUpDown
        {
            Location = new Point(x, y),
            Size = new Size(width, 26),
            Minimum = min,
            Maximum = max,
            Value = value,
            Increment = increment,
            DecimalPlaces = decimals,
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
    }

    // ================= 规格联动 =================

    private void ApplyPreset(int index)
    {
        if (_updating || index < 0 || index >= Presets.Length)
        {
            return;
        }
        var preset = Presets[index];
        _updating = true;
        _mmWidth.Value = ClampDecimal(_mmWidth, preset.MmWidth);
        _mmHeight.Value = ClampDecimal(_mmHeight, preset.MmHeight);
        _dpi.Value = 300;
        _pxWidth.Value = Math.Clamp(preset.PixelWidth, (int)_pxWidth.Minimum, (int)_pxWidth.Maximum);
        _pxHeight.Value = Math.Clamp(preset.PixelHeight, (int)_pxHeight.Minimum, (int)_pxHeight.Maximum);
        _updating = false;
        UpdateInfoText();
        SchedulePreview();
    }

    private static decimal ClampDecimal(NumericUpDown control, double value)
    {
        decimal result = (decimal)Math.Round(value, 1);
        return Math.Clamp(result, control.Minimum, control.Maximum);
    }

    private static decimal PixelsFromMm(decimal mm, decimal dpi, NumericUpDown target)
    {
        int pixels = (int)Math.Round(mm / 25.4m * dpi);
        return Math.Clamp(pixels, (int)target.Minimum, (int)target.Maximum);
    }

    private static decimal MmFromPixels(decimal pixels, decimal dpi, NumericUpDown target)
    {
        decimal mm = Math.Round(pixels / dpi * 25.4m, 1);
        return Math.Clamp(mm, target.Minimum, target.Maximum);
    }

    private void MarkCustom()
    {
        if (_presetCombo.SelectedIndex == Presets.Length)
        {
            return;
        }
        _updating = true;
        _presetCombo.SelectedIndex = Presets.Length;
        _updating = false;
    }

    // ================= 背景 =================

    private Color SelectedBackgroundColor()
    {
        return _backgroundCombo.SelectedIndex switch
        {
            0 => Color.FromArgb(255, 0, 0),
            1 => Color.FromArgb(67, 142, 219),
            2 => Color.White,
            3 => Color.FromArgb(214, 230, 245),
            4 => Color.FromArgb(192, 192, 192),
            5 => _customBackground,
            _ => Color.White
        };
    }

    private void OnBackgroundChanged()
    {
        if (_updating)
        {
            return;
        }
        int index = _backgroundCombo.SelectedIndex;
        if (index == 5)
        {
            using var dialog = new ColorDialog
            {
                Color = _customBackground,
                FullOpen = true,
                AnyColor = true
            };
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            {
                _customBackground = dialog.Color;
            }
            else
            {
                _updating = true;
                _backgroundCombo.SelectedIndex = _lastBackgroundIndex;
                _updating = false;
                UpdateBackgroundLabels();
                return;
            }
        }
        else
        {
            _lastBackgroundIndex = index;
        }
        UpdateBackgroundLabels();
        SchedulePreview();
    }

    private void UpdateBackgroundLabels()
    {
        var color = SelectedBackgroundColor();
        _backgroundSwatch.BackColor = color;
        _backgroundHexLabel.Text = ToHex(color);
    }

    private void UpdateBackgroundEnabled()
    {
        bool enabled = _autoBackgroundCheck.Checked;
        _backgroundCombo.Enabled = enabled;
        _backgroundSwatch.Visible = enabled;
        _backgroundHexLabel.Visible = enabled;
        _tolerance.Enabled = enabled;
        _fineMattingCheck.Enabled = enabled;
        _fineMattingCheck.ForeColor = enabled ? ShutdownTheme.Text : Color.FromArgb(148, 163, 184);
        _complexMattingCheck.Enabled = enabled;
        _complexMattingCheck.ForeColor = enabled ? ShutdownTheme.Text : Color.FromArgb(148, 163, 184);
        _toleranceValue.ForeColor = enabled ? ShutdownTheme.Muted : Color.FromArgb(148, 163, 184);
        _detectedSwatch.Visible = enabled && !_complexMattingCheck.Checked;
        _detectedBgLabel.Enabled = enabled;
    }

    private void UpdateDetectedLabel()
    {
        if (!_autoBackgroundCheck.Checked)
        {
            _detectedBgLabel.Text = "未启用自动抠图";
            _detectedBgLabel.ForeColor = ShutdownTheme.Muted;
            return;
        }
        if (_complexMattingCheck.Checked)
        {
            _detectedBgLabel.Text = "复杂背景模型：自动分析纹理与杂物";
            _detectedBgLabel.ForeColor = ShutdownTheme.Text;
            return;
        }
        _detectedBgLabel.Text = _source == null
            ? "打开照片后自动识别"
            : "识别背景：" + ToHex(DetectPreviewBackground());
        _detectedBgLabel.ForeColor = ShutdownTheme.Text;
    }

    private Color DetectPreviewBackground()
    {
        if (_source == null)
        {
            return Color.White;
        }
        try
        {
            using var fitted = PhotoToolkit.FitCover(_source,
                (int)_pxWidth.Value, (int)_pxHeight.Value,
                (int)_hOffset.Value, (int)_vOffset.Value, _cropRect);
            return PhotoToolkit.DetectBackgroundColor(fitted);
        }
        catch
        {
            return Color.White;
        }
    }

    private void OpenCropDialog()
    {
        if (_source == null)
        {
            MessageBox.Show(FindForm(), "请先打开一张证件照原图。", "证件照处理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var result = ImageCropDialog.Show(FindForm(), _source, _cropRect);
        if (result == null)
        {
            return;
        }
        var rect = result.Value;
        _cropRect = rect.Width >= _source.Width && rect.Height >= _source.Height
            ? null : rect;
        UpdateCropInfo();
        UpdateInfoText();
        UpdateDetectedLabel();
        RefreshPreview();
    }

    private void ClearCrop()
    {
        if (_cropRect == null)
        {
            return;
        }
        _cropRect = null;
        UpdateCropInfo();
        UpdateInfoText();
        UpdateDetectedLabel();
        RefreshPreview();
    }

    private void UpdateCropInfo()
    {
        if (_source == null)
        {
            _cropInfoLabel.Text = "未裁剪";
            _cropInfoLabel.ForeColor = ShutdownTheme.Muted;
            return;
        }
        if (_cropRect == null)
        {
            _cropInfoLabel.Text = "整张原图";
            _cropInfoLabel.ForeColor = ShutdownTheme.Muted;
            return;
        }
        _cropInfoLabel.Text = _cropRect.Value.Width + "×" + _cropRect.Value.Height;
        _cropInfoLabel.ForeColor = ShutdownTheme.Accent;
    }

    private static string ToHex(Color color)
    {
        return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
    }

    // ================= 图像构建 / 预览 =================

    private Bitmap BuildResult(out Color detectedBackground)
    {
        if (_source == null)
        {
            throw new InvalidOperationException("尚未打开照片。");
        }
        int width = (int)_pxWidth.Value;
        int height = (int)_pxHeight.Value;
        float dpi = (float)_dpi.Value;
        var current = PhotoToolkit.FitCover(_source, width, height,
            (int)_hOffset.Value, (int)_vOffset.Value, _cropRect);
        detectedBackground = Color.White;
        try
        {
            current.SetResolution(dpi, dpi);
            if (_autoBackgroundCheck.Checked)
            {
                Bitmap replaced;
                if (_complexMattingCheck.Checked)
                {
                    replaced = PhotoToolkit.ReplaceBackgroundComplex(current,
                        SelectedBackgroundColor(), _tolerance.Value);
                }
                else
                {
                    detectedBackground = PhotoToolkit.DetectBackgroundColor(current);
                    replaced = PhotoToolkit.ReplaceBackground(current,
                        detectedBackground, SelectedBackgroundColor(),
                        _tolerance.Value, _fineMattingCheck.Checked);
                }
                current.Dispose();
                current = replaced;
                current.SetResolution(dpi, dpi);
            }
            if (_colorCombo.SelectedIndex == 1)
            {
                var gray = PhotoToolkit.ApplyAdjustments(current,
                    new PhotoAdjustment { Saturation = -100 });
                current.Dispose();
                current = gray;
                current.SetResolution(dpi, dpi);
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private void RefreshPreview()
    {
        if (_disposed)
        {
            return;
        }
        if (_source == null)
        {
            _preview.Photo = null;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            return;
        }
        try
        {
            var result = BuildResult(out var detected);
            var old = _previewBitmap;
            _previewBitmap = result;
            _preview.Photo = result;
            old?.Dispose();
            _detectedSwatch.BackColor = detected;
            if (_autoBackgroundCheck.Checked)
            {
                if (_complexMattingCheck.Checked)
                {
                    UpdateDetectedLabel();
                }
                else
                {
                    _detectedBgLabel.Text = "识别背景：" + ToHex(detected);
                    _detectedBgLabel.ForeColor = ShutdownTheme.Text;
                }
            }
            UpdateInfoText();
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = ShutdownTheme.Danger;
            _statusLabel.Text = "预览失败：" + ex.Message;
        }
    }

    private void SchedulePreview()
    {
        if (_disposed || _source == null)
        {
            return;
        }
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdateInfoText()
    {
        if (_source == null || _path == null)
        {
            _fileLabel.Text = "尚未打开证件照 · 支持 JPG / PNG / BMP / TIFF";
            _exportButton.Enabled = false;
            return;
        }
        _exportButton.Enabled = true;
        _fileLabel.Text = Path.GetFileName(_path)
            + "  ·  " + (int)_pxWidth.Value + " × " + (int)_pxHeight.Value + " px"
            + "  ·  " + (int)_dpi.Value + " DPI"
            + "  ·  " + _mmWidth.Value.ToString("0.#") + " × " + _mmHeight.Value.ToString("0.#") + " mm"
            + (_cropRect != null
                ? "  ·  裁剪 " + _cropRect.Value.Width + "×" + _cropRect.Value.Height
                : "");
    }

    // ================= 文件 / 导出 =================

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开证件照原图",
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
            MessageBox.Show(FindForm(), "不支持的图片格式：\n" + path,
                "证件照处理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            var source = PhotoToolkit.LoadBitmap(path, autoOrient: true);
            _source?.Dispose();
            _source = source;
            _path = path;
            _cropRect = null;
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            _statusLabel.Text = "已打开：" + Path.GetFileName(path) + "，可调整规格和背景后导出";
            UpdateCropInfo();
            UpdateInfoText();
            UpdateDetectedLabel();
            RefreshPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "打开失败：\n" + ex.Message,
                "证件照处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ResetSettings()
    {
        _updating = true;
        _presetCombo.SelectedIndex = 0;
        _hOffset.Value = 0;
        _vOffset.Value = 0;
        _autoBackgroundCheck.Checked = true;
        _fineMattingCheck.Checked = true;
        _complexMattingCheck.Checked = false;
        _backgroundCombo.SelectedIndex = 0;
        _lastBackgroundIndex = 0;
        _tolerance.Value = 20;
        _formatCombo.SelectedIndex = 0;
        _colorCombo.SelectedIndex = 0;
        _limitSizeCheck.Checked = true;
        _minKb.Value = 10;
        _maxKb.Value = 50;
        _cropRect = null;
        _updating = false;
        ApplyPreset(0);
        UpdateBackgroundEnabled();
        UpdateBackgroundLabels();
        UpdateCropInfo();
        UpdateDetectedLabel();
        UpdateInfoText();
        _statusLabel.ForeColor = ShutdownTheme.Muted;
        _statusLabel.Text = "已重置为标准一寸、红底、300 DPI、10–50 KB";
    }

    private void UpdateSizeLimitEnabled()
    {
        bool enabled = _limitSizeCheck.Checked;
        _minKb.Enabled = enabled;
        _maxKb.Enabled = enabled;
    }

    private void Export()
    {
        if (_source == null)
        {
            MessageBox.Show(FindForm(), "请先打开一张证件照原图。", "证件照处理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        bool limitSize = _limitSizeCheck.Checked;
        int minKb = (int)_minKb.Value;
        int maxKb = (int)_maxKb.Value;
        if (limitSize && minKb > maxKb)
        {
            MessageBox.Show(FindForm(), "文件大小下限不能大于上限。", "证件照处理",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string extension = _formatCombo.SelectedIndex == 1 ? ".jpeg" : ".jpg";
        string baseName = _path != null ? Path.GetFileNameWithoutExtension(_path) : "证件照";
        using var dialog = new SaveFileDialog
        {
            Title = "导出证件照",
            Filter = "JPEG 图片|*" + extension,
            DefaultExt = extension,
            FileName = baseName + "_证件照_"
                + (int)_pxWidth.Value + "x" + (int)_pxHeight.Value + extension,
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
            using var result = BuildResult(out _);
            int quality;
            long size;
            string warning;
            if (limitSize)
            {
                (quality, size, warning) = SaveWithSizeLimit(result, dialog.FileName, minKb, maxKb);
            }
            else
            {
                quality = 92;
                PhotoToolkit.Save(result, dialog.FileName, new PhotoSaveOptions
                {
                    Format = ImageFormat.Jpeg,
                    Quality = quality,
                    MetadataSource = null
                });
                size = new FileInfo(dialog.FileName).Length;
                warning = "";
            }

            string sizeText = (size / 1024.0).ToString("0.0") + " KB";
            _statusLabel.ForeColor = warning.Length == 0
                ? ShutdownTheme.Success : ShutdownTheme.WarningText;
            _statusLabel.Text = "已导出：" + Path.GetFileName(dialog.FileName)
                + " · " + (int)_pxWidth.Value + "×" + (int)_pxHeight.Value + " px"
                + " · " + (int)_dpi.Value + " DPI · " + sizeText + " · 质量 " + quality
                + " · EXIF/GPS 已剔除";
            if (warning.Length > 0)
            {
                MessageBox.Show(FindForm(), warning, "证件照文件大小",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "导出失败：\n" + ex.Message,
                "证件照处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private (int Quality, long Size, string Warning) SaveWithSizeLimit(Bitmap image,
        string path, int minKb, int maxKb)
    {
        long maxBytes = maxKb * 1024L;
        long minBytes = minKb * 1024L;
        string temp = Path.Combine(AppPaths.Temp,
            "MiniMonitor_id_" + Guid.NewGuid().ToString("N") + ".jpg");
        int bestQuality = 0;
        try
        {
            int low = 1;
            int high = 100;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                long size = EncodeSize(image, temp, mid);
                if (size <= maxBytes)
                {
                    bestQuality = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            if (bestQuality == 0)
            {
                bestQuality = 1;
                EncodeSize(image, temp, bestQuality);
            }
            PhotoToolkit.Save(image, path, new PhotoSaveOptions
            {
                Format = ImageFormat.Jpeg,
                Quality = bestQuality,
                MetadataSource = null
            });
            long actual = new FileInfo(path).Length;
            string warning = "";
            if (actual > maxBytes)
            {
                warning = "当前像素尺寸下无法压缩到 " + maxKb + " KB 以内，"
                    + "实际约 " + (actual / 1024.0).ToString("0.0") + " KB。\n"
                    + "可适当减小像素尺寸或提高文件大小上限。";
            }
            else if (actual < minBytes)
            {
                warning = "当前像素尺寸下无法达到 " + minKb + " KB 的最小值，"
                    + "实际约 " + (actual / 1024.0).ToString("0.0") + " KB。\n"
                    + "可适当增大像素尺寸或降低文件大小下限。";
            }
            return (bestQuality, actual, warning);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // 临时文件清理失败不影响导出
            }
        }
    }

    private static long EncodeSize(Bitmap image, string tempPath, int quality)
    {
        PhotoToolkit.Save(image, tempPath, new PhotoSaveOptions
        {
            Format = ImageFormat.Jpeg,
            Quality = quality,
            MetadataSource = null
        });
        return new FileInfo(tempPath).Length;
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
            _source?.Dispose();
        }
        base.Dispose(disposing);
    }
}
