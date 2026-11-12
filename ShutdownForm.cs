using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 自定义关机设置窗口：默认关闭，需要手动开启后才能使用。
/// 提供快速预设、指定日期时间（日历 + 时钟 + 文本输入）、自定义时长三种方式；
/// 所有关机操作均为强制关机（shutdown /s /f）。
/// </summary>
public sealed class ShutdownForm : Form
{
    public DateTime? Deadline { get; private set; }

    private static readonly (string Text, TimeSpan Span)[] Presets =
    {
        ("15分钟", TimeSpan.FromMinutes(15)),
        ("30分钟", TimeSpan.FromMinutes(30)),
        ("1小时", TimeSpan.FromHours(1)),
        ("1.5小时", TimeSpan.FromMinutes(90)),
        ("2小时", TimeSpan.FromHours(2)),
        ("3小时", TimeSpan.FromHours(3)),
        ("6小时", TimeSpan.FromHours(6)),
        ("12小时", TimeSpan.FromHours(12)),
        ("24小时", TimeSpan.FromHours(24)),
        ("2天后", TimeSpan.FromDays(2)),
        ("3天后", TimeSpan.FromDays(3)),
        ("4天后", TimeSpan.FromDays(4)),
        ("5天后", TimeSpan.FromDays(5)),
        ("6天后", TimeSpan.FromDays(6)),
        ("7天后", TimeSpan.FromDays(7))
    };

    private static readonly string[] InputFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm",
        "yyyy年MM月dd日 HH:mm:ss",
        "yyyy年MM月dd日 HH:mm"
    };

    private readonly Panel _header;
    private readonly ToggleSwitch _enableToggle;
    private readonly Panel _bodyHost;
    private readonly Label _enableStatus;
    private readonly DateTimePicker _datePicker;
    private readonly DateTimePicker _timePicker;
    private readonly TextBox _dateTimeInput;
    private readonly Label _dateTimeStatus;
    private NumericUpDown _durationValue = null!;
    private ComboBox _durationUnit = null!;
    private Label _durationPreview = null!;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;
    private bool _syncing;

    public ShutdownForm()
    {
        Text = "自定义关机 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 640);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        // ---------- 顶部渐变标题 ----------
        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ClientSize.Width, 104),
            BackColor = ShutdownTheme.AccentDark
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
            if (_closeHover)
            {
                _closeHover = false;
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
        };
        NativeWindowDrag.Attach(this, _header, p => CloseRect.Contains(p));
        Controls.Add(_header);

        // ---------- 启用开关卡片 ----------
        var enableCard = new CardPanel
        {
            Location = new Point(24, 116),
            Size = new Size(772, 74)
        };
        _enableToggle = new ToggleSwitch
        {
            Location = new Point(20, 22),
            Size = new Size(56, 30),
            Checked = false
        };
        var enableTitle = new Label
        {
            Text = "启用自定义关机功能",
            Location = new Point(90, 12),
            Size = new Size(420, 26),
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        var enableHint = new Label
        {
            Text = "默认关闭，需要手动开启；关闭状态下下方所有关机操作不可用",
            Location = new Point(92, 40),
            Size = new Size(520, 22),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        _enableStatus = new Label
        {
            Text = "未启用",
            Location = new Point(enableCard.Width - 112, 22),
            Size = new Size(92, 28),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        enableCard.Controls.Add(_enableToggle);
        enableCard.Controls.Add(enableTitle);
        enableCard.Controls.Add(enableHint);
        enableCard.Controls.Add(_enableStatus);
        Controls.Add(enableCard);

        // ---------- 内容区（默认禁用） ----------
        _bodyHost = new Panel
        {
            Location = new Point(24, 202),
            Size = new Size(772, 336),
            BackColor = ShutdownTheme.Background,
            Enabled = false
        };

        var presetCard = new CardPanel
        {
            Location = new Point(0, 0),
            Size = new Size(490, 200)
        };
        BuildPresetCard(presetCard);
        _bodyHost.Controls.Add(presetCard);

        var durationCard = new CardPanel
        {
            Location = new Point(0, 212),
            Size = new Size(490, 124)
        };
        BuildDurationCard(durationCard);
        _bodyHost.Controls.Add(durationCard);

        var dateCard = new CardPanel
        {
            Location = new Point(502, 0),
            Size = new Size(270, 336)
        };

        var dateTitle = new Label
        {
            Text = "指定日期时间",
            Location = new Point(18, 14),
            Size = new Size(234, 24),
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        var dateHint = new Label
        {
            Text = "选择日历与时钟，或直接输入时间",
            Location = new Point(18, 38),
            Size = new Size(234, 20),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        var dateLabel = new Label
        {
            Text = "日期",
            Location = new Point(18, 64),
            Size = new Size(234, 18),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        _datePicker = new DateTimePicker
        {
            Location = new Point(18, 84),
            Size = new Size(234, 28),
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy年MM月dd日",
            Font = new Font("Microsoft YaHei UI", 9.5f)
        };
        var timeLabel = new Label
        {
            Text = "时间",
            Location = new Point(18, 120),
            Size = new Size(130, 18),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        _timePicker = new DateTimePicker
        {
            Location = new Point(18, 140),
            Size = new Size(130, 28),
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm:ss",
            ShowUpDown = true,
            Font = new Font("Microsoft YaHei UI", 9.5f)
        };
        var timeUnit = new Label
        {
            Text = "时 : 分 : 秒",
            Location = new Point(156, 144),
            Size = new Size(96, 20),
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        var inputLabel = new Label
        {
            Text = "或直接输入日期时间",
            Location = new Point(18, 178),
            Size = new Size(234, 18),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        _dateTimeInput = new TextBox
        {
            Location = new Point(18, 198),
            Size = new Size(234, 27),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };
        var inputFormat = new Label
        {
            Text = "格式：yyyy-MM-dd HH:mm:ss",
            Location = new Point(18, 228),
            Size = new Size(234, 18),
            Font = new Font("Microsoft YaHei UI", 7.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        var dateButton = new PillButton
        {
            Text = "按指定时间关机",
            ButtonStyle = PillButtonStyle.Primary,
            Location = new Point(18, 252),
            Size = new Size(234, 36),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            BackColor = Color.White
        };
        dateButton.Click += (_, _) =>
        {
            if (!_enableToggle.Checked || !TryGetDateTarget(out DateTime target))
            {
                return;
            }
            Commit(target);
        };
        _dateTimeStatus = new Label
        {
            Text = "",
            Location = new Point(18, 294),
            Size = new Size(234, 24),
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };

        dateCard.Controls.Add(dateTitle);
        dateCard.Controls.Add(dateHint);
        dateCard.Controls.Add(dateLabel);
        dateCard.Controls.Add(_datePicker);
        dateCard.Controls.Add(timeLabel);
        dateCard.Controls.Add(_timePicker);
        dateCard.Controls.Add(timeUnit);
        dateCard.Controls.Add(inputLabel);
        dateCard.Controls.Add(_dateTimeInput);
        dateCard.Controls.Add(inputFormat);
        dateCard.Controls.Add(dateButton);
        dateCard.Controls.Add(_dateTimeStatus);
        _bodyHost.Controls.Add(dateCard);
        Controls.Add(_bodyHost);

        _enableToggle.CheckedChanged += (_, _) =>
        {
            bool enabled = _enableToggle.Checked;
            _bodyHost.Enabled = enabled;
            _enableStatus.Text = enabled ? "已启用" : "未启用";
            _enableStatus.ForeColor = enabled ? ShutdownTheme.Success : ShutdownTheme.Muted;
            enableHint.Text = enabled
                ? "已开启；请选择下方任一方式设置强制关机"
                : "默认关闭，需要手动开启；关闭状态下下方所有关机操作不可用";
            enableCard.BorderColor = enabled ? ShutdownTheme.Accent : ShutdownTheme.CardBorder;
            enableCard.Invalidate();
            if (enabled)
            {
                UpdateDurationPreview();
                UpdateDateStatus(_datePicker.Value.Date + _timePicker.Value.TimeOfDay);
            }
        };

        // ---------- 安全提示 ----------
        var notice = new CardPanel
        {
            Location = new Point(24, 550),
            Size = new Size(772, 58),
            FillColor = ShutdownTheme.WarningBg,
            BorderColor = ShutdownTheme.WarningBorder,
            ShowShadow = false,
            CornerRadius = 12
        };
        var noticeText = new Label
        {
            Text = "安全提示：所有关机均为强制关机（/f）。设置后会弹出置顶倒计时窗口，倒计时结束前可随时点击“取消定时关机”。",
            Location = new Point(18, 11),
            Size = new Size(736, 36),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.WarningText,
            BackColor = ShutdownTheme.WarningBg,
            AutoSize = false
        };
        notice.Controls.Add(noticeText);
        Controls.Add(notice);

        // ---------- 日期时间联动 ----------
        var initial = DateTime.Now.AddMinutes(30);
        _syncing = true;
        _datePicker.MinDate = DateTime.Today;
        _datePicker.MaxDate = DateTime.Today.AddYears(10);
        _datePicker.Value = initial.Date;
        _timePicker.Value = DateTime.Today.Add(initial.TimeOfDay);
        _syncing = false;
        _datePicker.ValueChanged += (_, _) => SyncTextFromPickers();
        _timePicker.ValueChanged += (_, _) => SyncTextFromPickers();
        _dateTimeInput.Leave += (_, _) => ParseTextIntoPickers();
        _dateTimeInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ParseTextIntoPickers();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        SyncTextFromPickers();

        _durationUnit.SelectedIndexChanged += (_, _) => UpdateDurationPreview();
        _durationValue.ValueChanged += (_, _) => UpdateDurationPreview();
        UpdateDurationPreview();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
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

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 104f;

        using (var brush = new LinearGradientBrush(
            _header.ClientRectangle,
            Color.FromArgb(30, 64, 175),
            Color.FromArgb(37, 99, 235),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 220, -100, 300, 300);
            g.FillEllipse(glow, _header.Width - 92, 34, 128, 128);
        }

        TextRenderer.DrawText(g, "自定义关机", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s), (int)(_header.Width - 140f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "所有关机均为强制关机 · 倒计时期间可随时取消", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s), (int)(_header.Width - 160f * s), (int)(26f * s)),
            Color.FromArgb(219, 234, 254), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

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
    }

    private void BuildPresetCard(CardPanel card)
    {
        var title = new Label
        {
            Text = "快速预设",
            Location = new Point(18, 12),
            Size = new Size(300, 26),
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        var hint = new Label
        {
            Text = "点击任意按钮，立即开始强制关机倒计时",
            Location = new Point(18, 38),
            Size = new Size(430, 20),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        var grid = new TableLayoutPanel
        {
            Location = new Point(16, 64),
            Size = new Size(458, 124),
            BackColor = Color.White,
            ColumnCount = 5,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (int i = 0; i < 5; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        }
        for (int i = 0; i < 3; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        }
        for (int i = 0; i < Presets.Length; i++)
        {
            var preset = Presets[i];
            var button = new PillButton
            {
                Text = preset.Text,
                ButtonStyle = PillButtonStyle.Light,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                BackColor = Color.White,
                Tag = preset.Span
            };
            button.Click += (_, _) =>
            {
                if (_enableToggle.Checked && button.Tag is TimeSpan span)
                {
                    Commit(DateTime.Now.Add(span));
                }
            };
            grid.Controls.Add(button, i % 5, i / 5);
        }
        card.Controls.Add(title);
        card.Controls.Add(hint);
        card.Controls.Add(grid);
    }

    private void BuildDurationCard(CardPanel card)
    {
        var title = new Label
        {
            Text = "自定义时长",
            Location = new Point(18, 10),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        var hint = new Label
        {
            Text = "默认 10 秒后强制关机",
            Location = new Point(18, 34),
            Size = new Size(300, 20),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        _durationValue = new NumericUpDown
        {
            Location = new Point(18, 60),
            Size = new Size(110, 30),
            Minimum = 1,
            Maximum = 999999,
            Value = 10,
            Font = new Font("Microsoft YaHei UI", 10.5f),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _durationUnit = new ComboBox
        {
            Location = new Point(138, 60),
            Size = new Size(96, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            BackColor = Color.White
        };
        _durationUnit.Items.AddRange(new object[] { "秒", "分钟", "小时", "天" });
        _durationUnit.SelectedIndex = 0;
        var startButton = new PillButton
        {
            Text = "开始倒计时",
            ButtonStyle = PillButtonStyle.Primary,
            Location = new Point(356, 58),
            Size = new Size(116, 34),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            BackColor = Color.White
        };
        startButton.Click += (_, _) =>
        {
            if (!_enableToggle.Checked)
            {
                return;
            }
            try
            {
                Commit(DateTime.Now.Add(GetCustomDuration()));
            }
            catch (ArgumentOutOfRangeException)
            {
                MessageBox.Show(this, "时长超出可支持范围，请设置更短的时间。", "时间无效",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        _durationPreview = new Label
        {
            Text = "",
            Location = new Point(18, 96),
            Size = new Size(454, 20),
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        card.Controls.Add(title);
        card.Controls.Add(hint);
        card.Controls.Add(_durationValue);
        card.Controls.Add(_durationUnit);
        card.Controls.Add(startButton);
        card.Controls.Add(_durationPreview);
    }

    private TimeSpan GetCustomDuration()
    {
        double unit = _durationUnit.SelectedIndex switch
        {
            1 => 60d,
            2 => 3600d,
            3 => 86400d,
            _ => 1d
        };
        double seconds = (double)_durationValue.Value * unit;
        if (seconds < 1d)
        {
            seconds = 1d;
        }
        return TimeSpan.FromSeconds(seconds);
    }

    private void UpdateDurationPreview()
    {
        if (_durationPreview == null)
        {
            return;
        }
        try
        {
            var target = DateTime.Now.Add(GetCustomDuration());
            _durationPreview.Text = "预计关机：" + target.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "（约 " + Describe(GetCustomDuration()) + "后）";
        }
        catch
        {
            _durationPreview.Text = "预计关机时间超出可支持范围";
        }
    }

    private void SyncTextFromPickers()
    {
        if (_syncing)
        {
            return;
        }
        var target = _datePicker.Value.Date + _timePicker.Value.TimeOfDay;
        _dateTimeInput.Text = target.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        UpdateDateStatus(target);
    }

    private void ParseTextIntoPickers()
    {
        if (_syncing)
        {
            return;
        }
        if (!TryReadDateTime(out DateTime target))
        {
            SetDateStatus("格式无效，请按 yyyy-MM-dd HH:mm:ss 输入", true);
            return;
        }
        if (target.Date < _datePicker.MinDate.Date)
        {
            SetDateStatus("时间已过，请选择未来时间", true);
            return;
        }
        if (target.Date > _datePicker.MaxDate.Date)
        {
            SetDateStatus("日期超出可支持范围（10 年内）", true);
            return;
        }
        _syncing = true;
        _datePicker.Value = target.Date;
        _timePicker.Value = DateTime.Today.Add(target.TimeOfDay);
        _syncing = false;
        _dateTimeInput.Text = target.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        UpdateDateStatus(target);
    }

    private bool TryGetDateTarget(out DateTime target)
    {
        if (!TryReadDateTime(out target))
        {
            SetDateStatus("格式无效，请按 yyyy-MM-dd HH:mm:ss 输入", true);
            return false;
        }
        if (target.Date < _datePicker.MinDate.Date)
        {
            SetDateStatus("时间已过，请选择未来时间", true);
            return false;
        }
        if (target.Date > _datePicker.MaxDate.Date)
        {
            SetDateStatus("日期超出可支持范围（10 年内）", true);
            return false;
        }
        if (target <= DateTime.Now)
        {
            SetDateStatus("时间已过，请选择未来时间", true);
            return false;
        }
        return true;
    }

    private bool TryReadDateTime(out DateTime value)
    {
        string text = _dateTimeInput.Text.Trim();
        if (DateTime.TryParseExact(text, InputFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value))
        {
            return true;
        }
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out value);
    }

    private void UpdateDateStatus(DateTime target)
    {
        if (target <= DateTime.Now)
        {
            SetDateStatus("时间已过，请选择未来时间", true);
            return;
        }
        SetDateStatus("将在约 " + Describe(target - DateTime.Now) + "后关机", false);
    }

    private void SetDateStatus(string text, bool error)
    {
        _dateTimeStatus.Text = text;
        _dateTimeStatus.ForeColor = error ? ShutdownTheme.Danger : ShutdownTheme.Success;
    }

    private static string Describe(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} 天 {span.Hours} 小时";
        }
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} 小时 {span.Minutes} 分钟";
        }
        if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes} 分钟";
        }
        return $"{(int)span.TotalSeconds} 秒";
    }

    private bool Commit(DateTime deadline)
    {
        if (deadline <= DateTime.Now)
        {
            MessageBox.Show(this, "关机时间必须晚于当前时间。", "时间无效",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        Deadline = deadline;
        DialogResult = DialogResult.OK;
        Close();
        return true;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float scale = Height / 640f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), 18f * scale);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }
}
