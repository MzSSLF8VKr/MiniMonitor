using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>截图插件管理界面：默认不打开，从右键菜单进入。</summary>
public sealed class ScreenshotManagerForm : Form
{
    private readonly AppConfig _config;
    private readonly ScreenshotPluginService _service;
    private readonly Panel _header;
    private readonly CardPanel _screenshotCard;
    private readonly CardPanel _recordingCard;
    private readonly ToggleSwitch _screenshotToggle;
    private readonly ToggleSwitch _recordingToggle;
    private readonly Label _screenshotStatus;
    private readonly Label _recordingStatus;
    private readonly Label _statusLabel;
    private readonly ComboBox _delayCombo;
    private readonly CheckBox _autoCopyCheck;
    private readonly CheckBox _autoEditorCheck;
    private readonly TextBox _watermarkText;
    private readonly ComboBox _watermarkPosition;
    private readonly NumericUpDown _watermarkOpacity;
    private readonly ComboBox _watermarkFont;
    private readonly ComboBox _watermarkFontSize;
    private readonly CheckBox _watermarkBoldCheck;
    private readonly Button _watermarkColorButton;
    private Color _watermarkColor = Color.Purple;
    private readonly ComboBox _formatCombo;
    private readonly ComboBox _fpsCombo;
    private readonly ComboBox _qualityCombo;
    private readonly ComboBox _audioCombo;
    private readonly ComboBox _regionCombo;
    private readonly CheckBox _cursorHighlightCheck;
    private readonly CheckBox _clickEffectsCheck;
    private readonly TextBox _ffmpegPath;
    private readonly Label _ffmpegStatus;
    private readonly Label _hotkeyScreenshot;
    private readonly Label _hotkeyScroll;
    private readonly Label _hotkeyRecordPrint;
    private readonly Label _hotkeyRecordF9;
    private readonly PillButton _captureButton;
    private readonly PillButton _recordButton;
    private bool _updatingUi;
    private bool _closeHover;

    public ScreenshotManagerForm(ScreenshotPluginService service, AppConfig config)
    {
        _service = service;
        _config = config;

        Text = "截图插件 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 720);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(1000, 104),
            BackColor = Color.FromArgb(15, 118, 110)
        };
        _header.Paint += PaintHeader;
        _header.MouseMove += (_, e) =>
        {
            bool hover = CloseRect.Contains(e.Location);
            if (hover != _closeHover)
            {
                _closeHover = hover;
                _header.Cursor = hover ? Cursors.Hand : Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseLeave += (_, _) =>
        {
            _closeHover = false;
            _header.Cursor = Cursors.Default;
            _header.Invalidate();
        };
        _header.MouseClick += (_, e) =>
        {
            if (CloseRect.Contains(e.Location))
            {
                Close();
            }
        };
        NativeWindowDrag.Attach(this, _header, p => CloseRect.Contains(p));
        Controls.Add(_header);

        _screenshotCard = new CardPanel
        {
            Location = new Point(24, 116),
            Size = new Size(464, 74)
        };
        _screenshotToggle = new ToggleSwitch
        {
            Location = new Point(20, 22),
            Size = new Size(56, 30),
            OnColor = Color.FromArgb(13, 148, 136)
        };
        var ssTitle = MakeLabel("启用截图功能", 90, 12, 260, 26,
            new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var ssHint = MakeLabel("默认开启 · Ctrl+Shift+C 矩形截图", 92, 40, 300, 22,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _screenshotStatus = MakeLabel("已开启", 360, 22, 84, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Success, Color.White);
        _screenshotStatus.TextAlign = ContentAlignment.MiddleRight;
        _screenshotCard.Controls.AddRange(new Control[]
        {
            _screenshotToggle, ssTitle, ssHint, _screenshotStatus
        });
        Controls.Add(_screenshotCard);

        _recordingCard = new CardPanel
        {
            Location = new Point(512, 116),
            Size = new Size(464, 74)
        };
        _recordingToggle = new ToggleSwitch
        {
            Location = new Point(20, 22),
            Size = new Size(56, 30),
            OnColor = Color.FromArgb(13, 148, 136)
        };
        var recTitle = MakeLabel("启用屏幕录制", 90, 12, 260, 26,
            new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var recHint = MakeLabel("默认开启 · Shift+PrintScreen / Shift+F9", 92, 40, 310, 22,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _recordingStatus = MakeLabel("已开启", 360, 22, 84, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Success, Color.White);
        _recordingStatus.TextAlign = ContentAlignment.MiddleRight;
        _recordingCard.Controls.AddRange(new Control[]
        {
            _recordingToggle, recTitle, recHint, _recordingStatus
        });
        Controls.Add(_recordingCard);

        var tabs = new TabControl
        {
            Location = new Point(24, 202),
            Size = new Size(952, 434),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        Controls.Add(tabs);

        // ---------- 截图设置 ----------
        var capturePage = new TabPage("截图设置") { BackColor = Color.White, Padding = new Padding(10) };
        var captureTitle = MakeLabel("截图模式（点击立即执行）", 6, 4, 400, 24,
            new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        capturePage.Controls.Add(captureTitle);
        var modes = new[]
        {
            ("全屏所有显示器", ScreenshotMode.FullScreen),
            ("当前活动窗口", ScreenshotMode.ActiveWindow),
            ("矩形区域", ScreenshotMode.Rectangle),
            ("圆角矩形", ScreenshotMode.RoundedRectangle),
            ("椭圆区域", ScreenshotMode.Ellipse),
            ("自由手绘", ScreenshotMode.Freehand),
            ("多边形区域", ScreenshotMode.Polygon),
            ("滚动窗口（长网页）", ScreenshotMode.ScrollingWindow)
        };
        for (int i = 0; i < modes.Length; i++)
        {
            var button = new PillButton
            {
                Text = modes[i].Item1,
                Location = new Point(6 + (i % 4) * 222, 36 + (i / 4) * 42),
                Size = new Size(214, 34),
                ButtonStyle = PillButtonStyle.Light
            };
            var mode = modes[i].Item2;
            button.Click += (_, _) => CaptureFromManager(mode);
            capturePage.Controls.Add(button);
        }

        var delayLabel = MakeLabel("截图延迟", 8, 134, 70, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        delayLabel.TextAlign = ContentAlignment.MiddleLeft;
        _delayCombo = new ComboBox
        {
            Location = new Point(82, 134),
            Size = new Size(100, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _delayCombo.Items.AddRange(new object[] { "0 秒", "1 秒", "3 秒", "5 秒", "10 秒" });
        _autoCopyCheck = new CheckBox
        {
            Text = "截图后自动复制到剪贴板",
            Location = new Point(210, 136),
            Size = new Size(210, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _autoEditorCheck = new CheckBox
        {
            Text = "截图后自动打开编辑器",
            Location = new Point(430, 136),
            Size = new Size(200, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        capturePage.Controls.AddRange(new Control[]
        {
            delayLabel, _delayCombo, _autoCopyCheck, _autoEditorCheck
        });

        var hotkeyHint = MakeLabel(
            "Ctrl+Shift+C 矩形区域截图；Ctrl+Shift+W 滚动截图；支持多显示器、延迟、圆角/椭圆/多边形/自由手绘。",
            8, 174, 880, 22,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        capturePage.Controls.Add(hotkeyHint);
        var shortcutCard = new CardPanel
        {
            Location = new Point(8, 206),
            Size = new Size(880, 124),
            ShowShadow = false
        };
        var shortcutTitle = MakeLabel("全局快捷键", 16, 10, 300, 22,
            new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _hotkeyScreenshot = MakeLabel("", 16, 36, 430, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Text, Color.White);
        _hotkeyScroll = MakeLabel("", 16, 58, 430, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Text, Color.White);
        _hotkeyRecordPrint = MakeLabel("", 16, 80, 430, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Text, Color.White);
        _hotkeyRecordF9 = MakeLabel("", 16, 102, 430, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Text, Color.White);
        var reRegister = new PillButton
        {
            Text = "重新注册",
            Location = new Point(742, 12),
            Size = new Size(120, 30),
            ButtonStyle = PillButtonStyle.Light
        };
        reRegister.Click += (_, _) => _service.ApplyHotkeys();
        shortcutCard.Controls.AddRange(new Control[]
        {
            shortcutTitle, _hotkeyScreenshot, _hotkeyScroll,
            _hotkeyRecordPrint, _hotkeyRecordF9, reRegister
        });
        capturePage.Controls.Add(shortcutCard);
        tabs.TabPages.Add(capturePage);

        // ---------- 图像编辑 ----------
        var editPage = new TabPage("图像编辑") { BackColor = Color.White, Padding = new Padding(10) };
        var editTitle = MakeLabel("水印与编辑默认值", 6, 4, 400, 24,
            new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        editPage.Controls.Add(editTitle);
        var watermarkLabel = MakeLabel("水印文字", 8, 42, 64, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        watermarkLabel.TextAlign = ContentAlignment.MiddleLeft;
        _watermarkText = new TextBox
        {
            Location = new Point(76, 42),
            Size = new Size(300, 28),
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        var posLabel = MakeLabel("水印位置", 396, 42, 64, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        posLabel.TextAlign = ContentAlignment.MiddleLeft;
        _watermarkPosition = new ComboBox
        {
            Location = new Point(464, 42),
            Size = new Size(120, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _watermarkPosition.Items.AddRange(new object[]
        {
            "右下角", "左下角", "右上角", "左上角", "居中"
        });
        var colorLabel = MakeLabel("水印颜色", 596, 42, 64, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        colorLabel.TextAlign = ContentAlignment.MiddleLeft;
        _watermarkColorButton = new Button
        {
            Location = new Point(664, 42),
            Size = new Size(110, 28),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Text = WatermarkStyle.DefaultColorHex,
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold)
        };
        _watermarkColorButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;
        _watermarkColorButton.FlatAppearance.BorderSize = 1;

        var fontLabel = MakeLabel("字体", 8, 84, 40, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        fontLabel.TextAlign = ContentAlignment.MiddleLeft;
        _watermarkFont = new ComboBox
        {
            Location = new Point(52, 84),
            Size = new Size(230, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownWidth = 290,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        PopulateWatermarkFonts();
        var fontSizeLabel = MakeLabel("字号", 294, 84, 40, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        fontSizeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _watermarkFontSize = new ComboBox
        {
            Location = new Point(338, 84),
            Size = new Size(110, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _watermarkFontSize.Items.AddRange(new object[]
        {
            "自动（当前）", "10", "12", "14", "16", "18", "20", "22", "24", "28",
            "32", "36", "40", "48", "56", "64", "72", "96"
        });
        _watermarkBoldCheck = new CheckBox
        {
            Text = "加粗",
            Location = new Point(460, 86),
            Size = new Size(64, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Checked = true
        };
        var opacityLabel = MakeLabel("透明度", 542, 84, 56, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        opacityLabel.TextAlign = ContentAlignment.MiddleLeft;
        _watermarkOpacity = new NumericUpDown
        {
            Location = new Point(600, 84),
            Size = new Size(80, 28),
            DecimalPlaces = 2,
            Increment = 0.05m,
            Minimum = 0m,
            Maximum = 1m,
            Value = 0.65m,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        var editorHint = MakeLabel(
            "编辑器内置：裁剪、箭头、矩形、椭圆、文字、高亮、模糊、马赛克、智能擦除、水印、边框、圆角、撤销/重做。\n" +
            "水印文字/字体/字号/颜色/加粗/位置/透明度会作为默认模板；字号“自动”沿用原有按图片宽度计算（单位 pt）。",
            8, 126, 880, 60,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        editPage.Controls.AddRange(new Control[]
        {
            watermarkLabel, _watermarkText, posLabel, _watermarkPosition,
            colorLabel, _watermarkColorButton, fontLabel, _watermarkFont,
            fontSizeLabel, _watermarkFontSize, _watermarkBoldCheck,
            opacityLabel, _watermarkOpacity, editorHint
        });
        tabs.TabPages.Add(editPage);

        // ---------- 屏幕录制 ----------
        var recordPage = new TabPage("屏幕录制") { BackColor = Color.White, Padding = new Padding(10) };
        var recordTitle = MakeLabel("录制参数", 6, 4, 400, 24,
            new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        recordPage.Controls.Add(recordTitle);

        _formatCombo = AddCombo(recordPage, "格式", 8, 42, 110, new[] { "MP4", "GIF" });
        _fpsCombo = AddCombo(recordPage, "帧率", 340, 42, 100, new[] { "10", "15", "24", "30", "60" });
        _qualityCombo = AddCombo(recordPage, "质量", 550, 42, 100, new[] { "低", "中", "高" });
        _regionCombo = AddCombo(recordPage, "区域", 8, 84, 170, new[] { "全屏", "区域选择" });
        _audioCombo = AddCombo(recordPage, "音频", 340, 84, 170, new[] { "无", "系统声音", "麦克风" });
        _cursorHighlightCheck = new CheckBox
        {
            Text = "光标高亮",
            Location = new Point(550, 86),
            Size = new Size(110, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _clickEffectsCheck = new CheckBox
        {
            Text = "点击涟漪效果",
            Location = new Point(670, 86),
            Size = new Size(140, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        recordPage.Controls.AddRange(new Control[] { _cursorHighlightCheck, _clickEffectsCheck });

        var ffmpegLabel = MakeLabel("FFmpeg 路径", 8, 126, 86, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        ffmpegLabel.TextAlign = ContentAlignment.MiddleLeft;
        _ffmpegPath = new TextBox
        {
            Location = new Point(96, 126),
            Size = new Size(540, 28),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        var browse = new PillButton
        {
            Text = "浏览",
            Location = new Point(644, 126),
            Size = new Size(86, 28),
            ButtonStyle = PillButtonStyle.Light
        };
        browse.Click += (_, _) => BrowseFfmpeg();
        _ffmpegStatus = MakeLabel("", 96, 158, 740, 22,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        recordPage.Controls.AddRange(new Control[] { ffmpegLabel, _ffmpegPath, browse, _ffmpegStatus });

        var recordHint = MakeLabel(
            "MP4 需要 ffmpeg.exe；GIF 使用内置编码器。GIF 受内存限制约 20 秒，MP4/GIF 均可选区域或全屏；" +
            "音频仅在 MP4 且检测到 FFmpeg 时混流。",
            8, 190, 880, 46,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        recordPage.Controls.Add(recordHint);
        tabs.TabPages.Add(recordPage);

        // ---------- 快捷键 ----------
        var hotkeyPage = new TabPage("快捷键与状态") { BackColor = Color.White, Padding = new Padding(10) };
        var hotkeyTitle = MakeLabel("默认快捷键", 6, 4, 400, 24,
            new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var hotkeyInfo = MakeLabel(
            "Ctrl+Shift+C\n    矩形区域截图（完成后自动复制到剪贴板，并打开编辑器）\n\n" +
            "Ctrl+Shift+W\n    滚动截图（长网页 / 长文档，自动滚动拼接）\n\n" +
            "Shift+PrintScreen 或 Shift+F9\n    开始 / 停止屏幕录制\n\n" +
            "快捷键全局生效；若被其它软件占用，状态会显示注册失败。",
            8, 40, 860, 190,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Text, Color.White);
        hotkeyPage.Controls.AddRange(new Control[] { hotkeyTitle, hotkeyInfo });
        tabs.TabPages.Add(hotkeyPage);

        // ---------- 底部操作 ----------
        _captureButton = new PillButton
        {
            Text = "立即矩形截图",
            Location = new Point(24, 652),
            Size = new Size(180, 34),
            ButtonStyle = PillButtonStyle.Primary
        };
        _captureButton.Click += (_, _) => CaptureFromManager(ScreenshotMode.Rectangle);
        _recordButton = new PillButton
        {
            Text = "开始录屏",
            Location = new Point(216, 652),
            Size = new Size(140, 34),
            ButtonStyle = PillButtonStyle.Light
        };
        _recordButton.Click += (_, _) => ToggleRecordingFromManager();
        _statusLabel = MakeLabel("就绪", 376, 652, 600, 34,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.Transparent);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        Controls.AddRange(new Control[] { _captureButton, _recordButton, _statusLabel });

        // ---------- 事件与初始值 ----------
        _updatingUi = true;
        _screenshotToggle.Checked = _config.ScreenshotEnabled;
        _recordingToggle.Checked = _config.ScreenRecordingEnabled;
        _autoCopyCheck.Checked = _config.ScreenshotAutoCopy;
        _autoEditorCheck.Checked = _config.ScreenshotAutoEditor;
        _delayCombo.SelectedIndex = _config.ScreenshotDelaySeconds switch
        {
            1 => 1,
            3 => 2,
            5 => 3,
            10 => 4,
            _ => 0
        };
        _watermarkText.Text = _config.WatermarkText;
        _watermarkPosition.SelectedIndex = PositionIndex(_config.WatermarkPosition);
        _watermarkOpacity.Value = (decimal)Math.Clamp(_config.WatermarkOpacity, 0, 1);
        SelectWatermarkFont(_config.WatermarkFontFamily);
        SelectWatermarkFontSize(_config.WatermarkFontSize);
        _watermarkBoldCheck.Checked = _config.WatermarkBold;
        _watermarkColor = WatermarkStyle.ParseColor(_config.WatermarkColor);
        UpdateWatermarkColorButton();
        _formatCombo.SelectedIndex = ScreenshotSettingParser.ParseFormat(_config.RecordingFormat) == RecordingFormat.Gif ? 1 : 0;
        _fpsCombo.SelectedItem = _config.RecordingFps.ToString();
        if (_fpsCombo.SelectedIndex < 0)
        {
            _fpsCombo.SelectedItem = "30";
        }
        _qualityCombo.SelectedIndex = _config.RecordingQuality switch
        {
            "Low" => 0,
            "Medium" => 1,
            _ => 2
        };
        _audioCombo.SelectedIndex = ScreenshotSettingParser.ParseAudio(_config.RecordingAudio) switch
        {
            RecordingAudioSource.System => 1,
            RecordingAudioSource.Microphone => 2,
            _ => 0
        };
        _regionCombo.SelectedIndex = ScreenshotSettingParser.ParseRegion(_config.RecordingMode) == RecordingRegionMode.Region ? 1 : 0;
        _cursorHighlightCheck.Checked = _config.RecordingCursorHighlight;
        _clickEffectsCheck.Checked = _config.RecordingClickEffects;
        _ffmpegPath.Text = _config.FfmpegPath;
        _updatingUi = false;

        _screenshotToggle.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.ScreenshotEnabled = _screenshotToggle.Checked;
            _config.Save();
            _service.ApplyHotkeys();
            UpdateState();
        };
        _recordingToggle.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.ScreenRecordingEnabled = _recordingToggle.Checked;
            _config.Save();
            _service.ApplyHotkeys();
            UpdateState();
        };
        _delayCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.ScreenshotDelaySeconds = _delayCombo.SelectedIndex switch
            {
                1 => 1,
                2 => 3,
                3 => 5,
                4 => 10,
                _ => 0
            };
            _config.Save();
        };
        _autoCopyCheck.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.ScreenshotAutoCopy = _autoCopyCheck.Checked;
            _config.Save();
        };
        _autoEditorCheck.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.ScreenshotAutoEditor = _autoEditorCheck.Checked;
            _config.Save();
        };
        _watermarkText.TextChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.WatermarkText = _watermarkText.Text;
            _config.Save();
        };
        _watermarkPosition.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.WatermarkPosition = PositionValue(_watermarkPosition.SelectedIndex);
            _config.Save();
        };
        _watermarkOpacity.ValueChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.WatermarkOpacity = (double)_watermarkOpacity.Value;
            _config.Save();
        };
        _watermarkFont.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            string? family = _watermarkFont.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(family)) return;
            _config.WatermarkFontFamily = family;
            _config.Save();
        };
        _watermarkFontSize.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.WatermarkFontSize = ParseWatermarkFontSize(_watermarkFontSize.SelectedItem?.ToString());
            _config.Save();
        };
        _watermarkBoldCheck.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.WatermarkBold = _watermarkBoldCheck.Checked;
            _config.Save();
        };
        _watermarkColorButton.Click += (_, _) => PickWatermarkColor();
        _formatCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.RecordingFormat = _formatCombo.SelectedIndex == 1 ? "GIF" : "MP4";
            _config.Save();
            UpdateFfmpegStatus();
        };
        _fpsCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.RecordingFps = int.TryParse(_fpsCombo.SelectedItem?.ToString(), out int fps) ? fps : 30;
            _config.Save();
        };
        _qualityCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.RecordingQuality = _qualityCombo.SelectedIndex switch
            {
                0 => "Low",
                1 => "Medium",
                _ => "High"
            };
            _config.Save();
        };
        _audioCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.RecordingAudio = _audioCombo.SelectedIndex switch
            {
                1 => "System",
                2 => "Microphone",
                _ => "None"
            };
            _config.Save();
        };
        _regionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.RecordingMode = _regionCombo.SelectedIndex == 1 ? "Region" : "FullScreen";
            _config.Save();
        };
        _cursorHighlightCheck.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.RecordingCursorHighlight = _cursorHighlightCheck.Checked;
            _config.Save();
        };
        _clickEffectsCheck.CheckedChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.RecordingClickEffects = _clickEffectsCheck.Checked;
            _config.Save();
        };
        _ffmpegPath.TextChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.FfmpegPath = _ffmpegPath.Text.Trim();
            _config.Save();
            UpdateFfmpegStatus();
        };

        _service.StateChanged += OnServiceStateChanged;
        _service.StatusChanged += OnStatusChanged;
        FormClosed += (_, _) =>
        {
            _service.StateChanged -= OnServiceStateChanged;
            _service.StatusChanged -= OnStatusChanged;
        };

        UpdateState();
        UpdateFfmpegStatus();
    }

    private static ComboBox AddCombo(Control parent, string label, int labelX, int y, int comboWidth, string[] items)
    {
        var text = MakeLabel(label, labelX, y, 58, 28,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        text.TextAlign = ContentAlignment.MiddleLeft;
        var combo = new ComboBox
        {
            Location = new Point(labelX + 60, y),
            Size = new Size(comboWidth, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        combo.Items.AddRange(Array.ConvertAll(items, item => (object)item));
        parent.Controls.Add(text);
        parent.Controls.Add(combo);
        return combo;
    }

    private static int PositionIndex(string? position)
    {
        return position switch
        {
            "BottomLeft" => 1,
            "TopRight" => 2,
            "TopLeft" => 3,
            "Center" => 4,
            _ => 0
        };
    }

    private static string PositionValue(int index)
    {
        return index switch
        {
            1 => "BottomLeft",
            2 => "TopRight",
            3 => "TopLeft",
            4 => "Center",
            _ => "BottomRight"
        };
    }

    private void PopulateWatermarkFonts()
    {
        var names = new List<string>();
        try
        {
            names.AddRange(FontFamily.Families
                .Select(family => family.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name)
                    && !name.StartsWith("@", StringComparison.Ordinal)));
        }
        catch
        {
            // 字体枚举失败时使用下面的兜底字体
        }
        names.Add(WatermarkStyle.DefaultFontFamily);
        names.Add("Microsoft YaHei UI");
        names.Add("宋体");

        _watermarkFont.Items.Clear();
        foreach (string name in names
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            _watermarkFont.Items.Add(name);
        }
    }

    private void SelectWatermarkFont(string? family)
    {
        int index = FindWatermarkFontIndex(family);
        if (index < 0) index = FindWatermarkFontIndex(WatermarkStyle.DefaultFontFamily);
        if (index < 0) index = FindWatermarkFontIndex("Microsoft YaHei UI");
        if (index < 0) index = FindWatermarkFontIndex("微软雅黑");
        if (index < 0 && _watermarkFont.Items.Count > 0) index = 0;
        _watermarkFont.SelectedIndex = index;
    }

    private int FindWatermarkFontIndex(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return -1;
        }
        string target = family.Trim();
        for (int i = 0; i < _watermarkFont.Items.Count; i++)
        {
            if (string.Equals(_watermarkFont.Items[i]?.ToString(), target,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private void SelectWatermarkFontSize(int size)
    {
        if (size <= 0)
        {
            _watermarkFontSize.SelectedIndex = 0;
            return;
        }
        string text = Math.Clamp(size, 1, 200).ToString();
        int index = _watermarkFontSize.Items.IndexOf(text);
        if (index < 0)
        {
            _watermarkFontSize.Items.Add(text);
            index = _watermarkFontSize.Items.Count - 1;
        }
        _watermarkFontSize.SelectedIndex = index;
    }

    private static int ParseWatermarkFontSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || text.StartsWith("自动", StringComparison.Ordinal))
        {
            return 0;
        }
        return int.TryParse(text, out int size) ? Math.Clamp(size, 1, 200) : 0;
    }

    private void UpdateWatermarkColorButton()
    {
        _watermarkColorButton.BackColor = _watermarkColor;
        int luminance = _watermarkColor.R * 299
            + _watermarkColor.G * 587
            + _watermarkColor.B * 114;
        _watermarkColorButton.ForeColor = luminance >= 160000 ? Color.Black : Color.White;
        _watermarkColorButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;
        _watermarkColorButton.FlatAppearance.MouseOverBackColor = _watermarkColor;
        _watermarkColorButton.FlatAppearance.MouseDownBackColor = _watermarkColor;
        _watermarkColorButton.Text = WatermarkStyle.ToHex(_watermarkColor);
    }

    private void PickWatermarkColor()
    {
        using var dialog = new ColorDialog
        {
            Color = _watermarkColor,
            FullOpen = true,
            AnyColor = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _watermarkColor = dialog.Color;
            UpdateWatermarkColorButton();
            _config.WatermarkColor = WatermarkStyle.ToHex(_watermarkColor);
            _config.Save();
        }
    }

    private async void CaptureFromManager(ScreenshotMode mode)
    {
        if (_service.IsCapturing)
        {
            return;
        }
        Hide();
        await Task.Delay(160);
        try
        {
            await _service.TakeScreenshotAsync(mode);
        }
        finally
        {
            if (!_config.ScreenshotAutoEditor && !IsDisposed)
            {
                Show();
            }
        }
    }

    private void ToggleRecordingFromManager()
    {
        if (_service.IsRecording)
        {
            _service.StopRecording();
        }
        else
        {
            Hide();
            try
            {
                _service.StartRecording();
            }
            finally
            {
                if (!IsDisposed)
                {
                    Show();
                }
            }
        }
        UpdateState();
    }

    private void BrowseFfmpeg()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ffmpegPath.Text = dialog.FileName;
        }
    }

    private void OnServiceStateChanged()
    {
        if (IsDisposed)
        {
            return;
        }
        UpdateState();
    }

    private void OnStatusChanged(string status)
    {
        if (IsDisposed)
        {
            return;
        }
        _statusLabel.Text = status;
    }

    private void UpdateState()
    {
        _updatingUi = true;
        _screenshotToggle.Checked = _config.ScreenshotEnabled;
        _recordingToggle.Checked = _config.ScreenRecordingEnabled;
        _updatingUi = false;
        _screenshotStatus.Text = _config.ScreenshotEnabled ? "已开启" : "已关闭";
        _screenshotStatus.ForeColor = _config.ScreenshotEnabled
            ? ShutdownTheme.Success : ShutdownTheme.Muted;
        _recordingStatus.Text = _config.ScreenRecordingEnabled ? "已开启" : "已关闭";
        _recordingStatus.ForeColor = _config.ScreenRecordingEnabled
            ? ShutdownTheme.Success : ShutdownTheme.Muted;
        _screenshotCard.BorderColor = _config.ScreenshotEnabled
            ? Color.FromArgb(13, 148, 136) : ShutdownTheme.CardBorder;
        _recordingCard.BorderColor = _config.ScreenRecordingEnabled
            ? Color.FromArgb(13, 148, 136) : ShutdownTheme.CardBorder;
        _screenshotCard.Invalidate();
        _recordingCard.Invalidate();
        _captureButton.Enabled = _config.ScreenshotEnabled;
        _recordButton.Enabled = _config.ScreenRecordingEnabled;
        _recordButton.Text = _service.IsRecording ? "停止录屏" : "开始录屏";
        _recordButton.ButtonStyle = _service.IsRecording
            ? PillButtonStyle.Danger : PillButtonStyle.Light;
        _recordButton.Invalidate();
        _hotkeyScreenshot.Text = "Ctrl+Shift+C："
            + (_service.ScreenshotHotkeyRegistered ? "已注册" : "未注册（可能被占用）");
        _hotkeyScreenshot.ForeColor = _service.ScreenshotHotkeyRegistered
            ? ShutdownTheme.Success : ShutdownTheme.Danger;
        _hotkeyScroll.Text = "Ctrl+Shift+W："
            + (_service.ScrollHotkeyRegistered ? "已注册（滚动截图）" : "未注册（可能被占用）");
        _hotkeyScroll.ForeColor = _service.ScrollHotkeyRegistered
            ? ShutdownTheme.Success : ShutdownTheme.Danger;
        _hotkeyRecordPrint.Text = "Shift+PrintScreen："
            + (_service.RecordPrintHotkeyRegistered ? "已注册" : "未注册");
        _hotkeyRecordPrint.ForeColor = _service.RecordPrintHotkeyRegistered
            ? ShutdownTheme.Success : ShutdownTheme.Danger;
        _hotkeyRecordF9.Text = "Shift+F9："
            + (_service.RecordF9HotkeyRegistered ? "已注册" : "未注册");
        _hotkeyRecordF9.ForeColor = _service.RecordF9HotkeyRegistered
            ? ShutdownTheme.Success : ShutdownTheme.Danger;
        if (!string.IsNullOrEmpty(_service.HotkeyError))
        {
            _statusLabel.Text = _service.HotkeyError;
        }
        UpdateFfmpegStatus();
    }

    private void UpdateFfmpegStatus()
    {
        string? ffmpeg = FfmpegLocator.Find(_config.FfmpegPath);
        if (ffmpeg != null)
        {
            _ffmpegStatus.Text = "已找到：" + ffmpeg;
            _ffmpegStatus.ForeColor = ShutdownTheme.Success;
        }
        else
        {
            _ffmpegStatus.Text = "未找到 ffmpeg.exe；MP4 录制不可用，GIF 可用。";
            _ffmpegStatus.ForeColor = ShutdownTheme.Danger;
        }
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
            return new Rectangle((int)(_header.Width - 52f * s), (int)(14f * s),
                (int)(34f * s), (int)(34f * s));
        }
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = _header.Height / 104f;
        using var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(15, 118, 110), Color.FromArgb(13, 148, 136),
            LinearGradientMode.Horizontal);
        g.FillRectangle(brush, _header.ClientRectangle);
        using var titleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        TextRenderer.DrawText(g, "截图插件", titleFont,
            new Rectangle((int)(28f * s), (int)(12f * s),
                (int)(_header.Width - 140f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        using var subFont = new Font("Microsoft YaHei UI", 9.5f);
        TextRenderer.DrawText(g, "截图 · 图像编辑 · 屏幕录制", subFont,
            new Rectangle((int)(30f * s), (int)(54f * s),
                (int)(_header.Width - 160f * s), (int)(26f * s)),
            Color.FromArgb(224, 255, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f * s);
        int inset = (int)(12f * s);
        g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
        g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float scale = Height / 720f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), 18f * scale);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _service.StateChanged -= OnServiceStateChanged;
            _service.StatusChanged -= OnStatusChanged;
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
}
