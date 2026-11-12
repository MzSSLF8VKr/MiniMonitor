using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 磁盘安全覆写窗口。默认关闭，必须手动启用后才可选择磁盘和算法；
/// 开始前还需要在确认窗口输入 WIPE &lt;磁盘号&gt;。系统盘会自动排除。
/// </summary>
public sealed class DiskWipeForm : Form
{
    internal enum ResultKind
    {
        Success = 0,
        Cancelled = 1,
        Failure = 2
    }

    private readonly Panel _header;
    private readonly Panel _setupHost;
    private readonly Panel _progressHost;
    private readonly Panel _bodyHost;
    private readonly CardPanel _enableCard;
    private readonly ToggleSwitch _enableToggle;
    private readonly Label _enableHint;
    private readonly Label _enableStatus;
    private readonly FlowLayoutPanel _diskList;
    private readonly Label _diskCountLabel;
    private readonly PillButton _refreshButton;
    private readonly PillButton[] _algoButtons;
    private readonly Label _algoDetail;
    private readonly ToggleSwitch _encryptToggle;
    private readonly Label _encryptHint;
    private readonly PillButton _startButton;
    private readonly CardPanel _progressCard;
    private readonly CardPanel _resultCard;
    private readonly WipeProgressBar _progressBar;
    private readonly Label _percentLabel;
    private readonly Label _stageLabel;
    private readonly Label _speedLabel;
    private readonly Label _elapsedLabel;
    private readonly Label _remainingLabel;
    private readonly Label _diskInfoLabel;
    private readonly Label _progressHint;
    private readonly PillButton _cancelButton;
    private readonly ResultBadge _resultBadge;
    private readonly Label _resultTitle;
    private readonly Label _resultSubtitle;
    private readonly TextBox _resultDetails;
    private readonly PillButton _resultOkButton;
    private readonly System.Windows.Forms.Timer _progressTimer;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private string _headerSubtitle = "物理磁盘数据销毁 · 操作不可恢复";
    private bool _closeHover;

    private List<WipeDiskInfo> _disks = new();
    private WipeDiskInfo? _selectedDisk;
    private WipeAlgorithm _algorithm = WipeAlgorithm.Random;
    private WipeJob? _job;
    private WipeDiskInfo? _jobDisk;
    private bool _cancelRequested;
    private long _lastBytes;
    private long _lastTick;
    private double _speed;

    public DiskWipeForm()
    {
        Text = "磁盘安全覆写 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(940, 720);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ClientSize.Width, 104),
            BackColor = Color.FromArgb(127, 29, 29)
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

        // ---------- 设置视图 ----------
        _setupHost = new Panel
        {
            Location = new Point(0, 104),
            Size = new Size(940, 616),
            BackColor = ShutdownTheme.Background
        };

        _enableCard = new CardPanel
        {
            Location = new Point(24, 12),
            Size = new Size(892, 74)
        };
        _enableToggle = new ToggleSwitch
        {
            Location = new Point(20, 22),
            Size = new Size(56, 30),
            OnColor = ShutdownTheme.Danger,
            Checked = false
        };
        var enableTitle = MakeLabel("启用磁盘安全覆写功能", 90, 12, 480, 26,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _enableHint = MakeLabel(
            "默认关闭，需要手动开启；关闭状态下磁盘扫描和覆写操作均不可用",
            92, 40, 600, 22,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _enableStatus = new Label
        {
            Text = "未启用",
            Location = new Point(_enableCard.Width - 112, 22),
            Size = new Size(92, 28),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        _enableCard.Controls.AddRange(new Control[] { _enableToggle, enableTitle, _enableHint, _enableStatus });
        _setupHost.Controls.Add(_enableCard);

        _bodyHost = new Panel
        {
            Location = new Point(24, 98),
            Size = new Size(892, 430),
            BackColor = ShutdownTheme.Background,
            Enabled = false
        };

        var diskCard = new CardPanel
        {
            Location = new Point(0, 0),
            Size = new Size(592, 430)
        };
        var diskTitle = MakeLabel("目标物理磁盘", 18, 12, 360, 26,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var diskHint = MakeLabel("系统盘会自动排除；请核对磁盘编号、容量与型号",
            18, 38, 500, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _diskCountLabel = MakeLabel("正在扫描磁盘…", 18, 60, 500, 20,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _refreshButton = new PillButton
        {
            Text = "刷新",
            ButtonStyle = PillButtonStyle.Light,
            Location = new Point(492, 14),
            Size = new Size(78, 32),
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            BackColor = Color.White
        };
        _refreshButton.Click += (_, _) => RefreshDisks();
        _diskList = new FlowLayoutPanel
        {
            Location = new Point(14, 84),
            Size = new Size(564, 332),
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(8),
            Margin = new Padding(0)
        };
        _diskList.SizeChanged += (_, _) => ResizeDiskCards();
        diskCard.Controls.AddRange(new Control[]
        {
            diskTitle, diskHint, _diskCountLabel, _refreshButton, _diskList
        });
        _bodyHost.Controls.Add(diskCard);

        var algoCard = new CardPanel
        {
            Location = new Point(604, 0),
            Size = new Size(288, 430)
        };
        var algoTitle = MakeLabel("覆写算法", 18, 12, 252, 26,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var algoHint = MakeLabel("选择数据覆写强度", 18, 38, 252, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);

        _algoButtons = new PillButton[4];
        var algoNames = new[]
        {
            "随机数覆写 · 1 遍",
            "全零覆写 · 1 遍",
            "DoD 5220.22-M · 3 遍",
            "Gutmann · 35 遍"
        };
        for (int i = 0; i < _algoButtons.Length; i++)
        {
            WipeAlgorithm algo = (WipeAlgorithm)i;
            var button = new PillButton
            {
                Text = algoNames[i],
                ButtonStyle = i == 0 ? PillButtonStyle.Primary : PillButtonStyle.Light,
                Location = new Point(18, 60 + i * 52),
                Size = new Size(252, 42),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                BackColor = Color.White,
                Tag = algo
            };
            button.Click += (_, _) => SelectAlgorithm(button.Tag is WipeAlgorithm a ? a : WipeAlgorithm.Random);
            _algoButtons[i] = button;
            algoCard.Controls.Add(button);
        }

        _algoDetail = new Label
        {
            Location = new Point(18, 266),
            MaximumSize = new Size(252, 0),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White
        };
        _encryptToggle = new ToggleSwitch
        {
            Location = new Point(18, 334),
            Size = new Size(44, 24),
            OnColor = ShutdownTheme.Accent,
            Checked = true
        };
        var encryptLabel = MakeLabel("AES-256 加密预覆盖", 70, 332, 200, 20,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _encryptHint = MakeLabel("默认开启 · 包含 1 遍加密预覆盖", 70, 352, 200, 18,
            new Font("Microsoft YaHei UI", 7.5f), ShutdownTheme.Muted, Color.White);
        _startButton = new PillButton
        {
            Text = "开始安全覆写",
            ButtonStyle = PillButtonStyle.Danger,
            Location = new Point(18, 380),
            Size = new Size(252, 42),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            BackColor = Color.White,
            Enabled = false
        };
        _startButton.Click += (_, _) => StartWipe();
        _encryptToggle.CheckedChanged += (_, _) => UpdateAlgorithmUi();
        algoCard.Controls.AddRange(new Control[]
        {
            algoTitle, algoHint, _algoDetail, _encryptToggle,
            encryptLabel, _encryptHint, _startButton
        });
        _bodyHost.Controls.Add(algoCard);
        _setupHost.Controls.Add(_bodyHost);

        var notice = new CardPanel
        {
            Location = new Point(24, 540),
            Size = new Size(892, 58),
            FillColor = ShutdownTheme.WarningBg,
            BorderColor = ShutdownTheme.WarningBorder,
            ShowShadow = false,
            CornerRadius = 12
        };
        var noticeText = MakeLabel(
            "警告：磁盘安全覆写会永久销毁目标磁盘上的全部数据，无法恢复。\n"
            + "系统盘已自动排除；SSD 预留空间与磨损均衡区域可能保留数据副本，软件覆写无法保证覆盖。",
            18, 11, 856, 40,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.WarningText, ShutdownTheme.WarningBg);
        notice.Controls.Add(noticeText);
        _setupHost.Controls.Add(notice);
        Controls.Add(_setupHost);

        // ---------- 进度 / 结果视图 ----------
        _progressHost = new Panel
        {
            Location = new Point(0, 104),
            Size = new Size(940, 616),
            BackColor = ShutdownTheme.Background,
            Visible = false
        };

        _progressCard = new CardPanel
        {
            Location = new Point(24, 12),
            Size = new Size(892, 570)
        };
        var progressTitle = MakeLabel("正在安全覆写", 24, 18, 500, 34,
            new Font("Microsoft YaHei UI", 20f, FontStyle.Bold), ShutdownTheme.Danger, Color.White);
        _diskInfoLabel = MakeLabel("", 24, 56, 844, 22,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        _percentLabel = new Label
        {
            Text = "0%",
            Location = new Point(0, 88),
            Size = new Size(892, 84),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 42f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Danger,
            BackColor = Color.White,
            AutoSize = false
        };
        _progressBar = new WipeProgressBar
        {
            Location = new Point(70, 180),
            Size = new Size(752, 24)
        };
        _stageLabel = new Label
        {
            Text = "准备中…",
            Location = new Point(70, 216),
            Size = new Size(752, 24),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        _speedLabel = MakeLabel("速度 —", 70, 248, 220, 22,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        _elapsedLabel = MakeLabel("已用 —", 330, 248, 220, 22,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        _remainingLabel = MakeLabel("剩余 —", 590, 248, 232, 22,
            new Font("Microsoft YaHei UI", 9f), ShutdownTheme.Muted, Color.White);
        _progressHint = MakeLabel(
            "覆写期间请勿关闭程序、断电或拔出磁盘。中途取消也会导致磁盘部分覆写，已写入区域的数据无法恢复。",
            70, 300, 752, 44,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Danger, Color.White);
        _cancelButton = new PillButton
        {
            Text = "中止并取消覆写",
            ButtonStyle = PillButtonStyle.Danger,
            Location = new Point(346, 450),
            Size = new Size(200, 46),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            BackColor = Color.White,
            Enabled = false
        };
        _cancelButton.Click += (_, _) => PromptCancelWipe();
        var progressNote = MakeLabel("取消操作不会恢复已经写入的数据。",
            0, 512, 892, 20,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        progressNote.TextAlign = ContentAlignment.MiddleCenter;
        _progressCard.Controls.AddRange(new Control[]
        {
            progressTitle, _diskInfoLabel, _percentLabel, _progressBar, _stageLabel,
            _speedLabel, _elapsedLabel, _remainingLabel, _progressHint,
            _cancelButton, progressNote
        });
        _progressHost.Controls.Add(_progressCard);

        _resultCard = new CardPanel
        {
            Location = new Point(24, 12),
            Size = new Size(892, 570),
            Visible = false
        };
        _resultBadge = new ResultBadge
        {
            Location = new Point(410, 30),
            Size = new Size(72, 72)
        };
        _resultTitle = new Label
        {
            Text = "",
            Location = new Point(0, 116),
            Size = new Size(892, 42),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 22f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false
        };
        _resultSubtitle = new Label
        {
            Text = "",
            Location = new Point(0, 160),
            Size = new Size(892, 26),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        _resultDetails = new TextBox
        {
            Location = new Point(60, 196),
            Size = new Size(772, 210),
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true
        };
        var resultNote = MakeLabel(
            "提示：SSD 的预留空间与磨损均衡区域可能保留数据副本，软件覆写无法覆盖这些物理块；对 SSD 建议使用厂商提供的 Secure Erase 工具。",
            60, 416, 772, 46,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _resultOkButton = new PillButton
        {
            Text = "完成",
            ButtonStyle = PillButtonStyle.Primary,
            Location = new Point(366, 480),
            Size = new Size(160, 46),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            BackColor = Color.White
        };
        _resultOkButton.Click += (_, _) => Close();
        _resultCard.Controls.AddRange(new Control[]
        {
            _resultBadge, _resultTitle, _resultSubtitle, _resultDetails,
            resultNote, _resultOkButton
        });
        _progressHost.Controls.Add(_resultCard);
        Controls.Add(_progressHost);
        _progressHost.BringToFront();
        _setupHost.BringToFront();

        _enableToggle.CheckedChanged += (_, _) =>
        {
            bool enabled = _enableToggle.Checked;
            _bodyHost.Enabled = enabled;
            _enableStatus.Text = enabled ? "已启用" : "未启用";
            _enableStatus.ForeColor = enabled ? ShutdownTheme.Success : ShutdownTheme.Muted;
            _enableHint.Text = enabled
                ? "已开启；请选择目标磁盘和覆写算法"
                : "默认关闭，需要手动开启；关闭状态下磁盘扫描和覆写操作均不可用";
            _enableCard.BorderColor = enabled ? ShutdownTheme.Danger : ShutdownTheme.CardBorder;
            _enableCard.Invalidate();
            UpdateStartButton();
        };

        _progressTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _progressTimer.Tick += (_, _) => OnProgressTick();

        UpdateAlgorithmUi();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        Shown += (_, _) => RefreshDisks();
    }

    private static Label MakeLabel(string text, int x, int y, int width, int height,
        Font font, Color foreColor, Color backColor)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = font,
            ForeColor = foreColor,
            BackColor = backColor,
            AutoSize = false
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

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(127, 29, 29), Color.FromArgb(220, 38, 38),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 230, -100, 310, 310);
            g.FillEllipse(glow, _header.Width - 96, 34, 130, 130);
        }

        TextRenderer.DrawText(g, "磁盘安全覆写", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s), (int)(_header.Width - 140f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, _headerSubtitle, _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s), (int)(_header.Width - 160f * s), (int)(26f * s)),
            Color.FromArgb(254, 226, 226), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

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

    private void RefreshDisks()
    {
        if (_job != null)
        {
            return;
        }
        Cursor = Cursors.WaitCursor;
        try
        {
            WipeEnumerationResult result = DiskWipeEngine.EnumerateDisks();
            _disks = result.Disks.ToList();
            if (result.Error != null)
            {
                _diskCountLabel.Text = "扫描失败";
                _diskCountLabel.ForeColor = ShutdownTheme.Danger;
                PopulateDiskList(result.Error);
                _selectedDisk = null;
            }
            else
            {
                _diskCountLabel.Text = "共 " + _disks.Count + " 块可覆写磁盘 · 已自动排除系统盘 "
                    + result.ExcludedSystemDisks + " 块";
                _diskCountLabel.ForeColor = ShutdownTheme.Muted;
                int selectedNumber = _selectedDisk?.Number ?? -1;
                _selectedDisk = _disks.FirstOrDefault(d => d.Number == selectedNumber);
                PopulateDiskList(null);
            }
            UpdateStartButton();
        }
        catch (Exception ex)
        {
            _disks = new List<WipeDiskInfo>();
            _selectedDisk = null;
            _diskCountLabel.Text = "扫描失败";
            _diskCountLabel.ForeColor = ShutdownTheme.Danger;
            PopulateDiskList("扫描磁盘时发生错误：" + ex.Message);
            UpdateStartButton();
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PopulateDiskList(string? error)
    {
        _diskList.Controls.Clear();
        int cardWidth = Math.Max(240, _diskList.ClientSize.Width - 24);

        if (error != null || _disks.Count == 0)
        {
            var empty = new Label
            {
                Text = error ?? "未检测到可覆写的非系统物理磁盘。\n请确认磁盘已连接，然后点击“刷新”。",
                Location = new Point(8, 8),
                Size = new Size(cardWidth - 8, 96),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = error == null ? ShutdownTheme.Muted : ShutdownTheme.Danger,
                BackColor = Color.White
            };
            _diskList.Controls.Add(empty);
            return;
        }

        foreach (WipeDiskInfo disk in _disks)
        {
            var card = new DiskCard(disk, cardWidth, 82)
            {
                Margin = new Padding(0, 0, 0, 8),
                IsSelected = _selectedDisk?.Number == disk.Number
            };
            card.Click += (_, _) => SelectDisk(disk);
            _diskList.Controls.Add(card);
        }
    }

    private void ResizeDiskCards()
    {
        int cardWidth = Math.Max(240, _diskList.ClientSize.Width - 24);
        foreach (Control control in _diskList.Controls)
        {
            if (control is DiskCard)
            {
                control.Width = cardWidth;
            }
            else
            {
                control.Width = cardWidth - 8;
            }
        }
    }

    private void SelectDisk(WipeDiskInfo disk)
    {
        if (!_bodyHost.Enabled || _job != null)
        {
            return;
        }
        _selectedDisk = disk;
        foreach (Control control in _diskList.Controls)
        {
            if (control is DiskCard card)
            {
                card.IsSelected = card.Disk.Number == disk.Number;
            }
        }
        UpdateStartButton();
    }

    private void SelectAlgorithm(WipeAlgorithm algorithm)
    {
        if (!_bodyHost.Enabled || _job != null)
        {
            return;
        }
        _algorithm = algorithm;
        UpdateAlgorithmUi();
        UpdateStartButton();
    }

    private void UpdateAlgorithmUi()
    {
        for (int i = 0; i < _algoButtons.Length; i++)
        {
            _algoButtons[i].ButtonStyle = (WipeAlgorithm)i == _algorithm
                ? PillButtonStyle.Primary
                : PillButtonStyle.Light;
            _algoButtons[i].Invalidate();
        }
        int passes = DiskWipeEngine.AlgorithmPasses(_algorithm);
        bool encrypt = _encryptToggle.Checked;
        int totalWrites = passes + (encrypt ? 1 : 0);
        _algoDetail.Text = DiskWipeEngine.AlgorithmDetail(_algorithm)
            + "\n\n总写入：" + totalWrites + " 遍"
            + (encrypt ? "（含 1 遍 AES-256 预覆盖）" : "（不含 AES-256 预覆盖）");
        _encryptHint.Text = encrypt
            ? "默认开启 · 包含 1 遍加密预覆盖"
            : "已关闭 · 仅执行普通覆写";
        _encryptHint.ForeColor = encrypt ? ShutdownTheme.Muted : ShutdownTheme.Danger;
    }

    private void UpdateStartButton()
    {
        bool enabled = _enableToggle.Checked && _selectedDisk != null && _job == null
            && _setupHost.Visible;
        _startButton.Enabled = enabled;
    }

    private void StartWipe()
    {
        if (!_enableToggle.Checked || _selectedDisk == null || _job != null)
        {
            return;
        }
        WipeDiskInfo disk = _selectedDisk;
        bool encryptPrePass = _encryptToggle.Checked;
        using var confirm = new WipeConfirmForm(disk, _algorithm, encryptPrePass);
        if (confirm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _jobDisk = disk;
        ShowProgressView();
        Cursor = Cursors.WaitCursor;
        try
        {
            _stageLabel.Text = "正在校验目标磁盘并锁定卷…";
            _percentLabel.Text = "0%";
            _progressBar.Value = 0;
            _speedLabel.Text = "速度 —";
            _elapsedLabel.Text = "已用 —";
            _remainingLabel.Text = "剩余 —";
            Update();

            WipeJob job = DiskWipeEngine.Prepare(disk, _algorithm, encryptPrePass);
            _job = job;
            _lastBytes = 0;
            _lastTick = Environment.TickCount64;
            _speed = 0;
            _cancelRequested = false;
            _cancelButton.Enabled = true;
            _cancelButton.Text = "中止并取消覆写";
            _progressTimer.Start();
            try
            {
                job.Start();
            }
            catch (Exception ex)
            {
                job.Dispose();
                _job = null;
                _progressTimer.Stop();
                ShowResult(ResultKind.Failure, "无法启动覆写", "尚未开始写入",
                    "无法创建覆写线程，操作已取消。\n\n" + ex.Message
                    + "\n\n准备阶段未写入任何数据。", null);
                return;
            }
            _headerSubtitle = "正在覆写 PhysicalDrive " + disk.Number + " · 请勿关闭电源";
            _header.Invalidate();
        }
        catch (Exception ex)
        {
            ShowResult(ResultKind.Failure, "无法开始覆写", "准备阶段未写入任何数据",
                ex.Message + "\n\n准备阶段未写入任何数据，目标磁盘内容未被修改。", null);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ShowProgressView()
    {
        _setupHost.Visible = false;
        _progressHost.Visible = true;
        _progressCard.Visible = true;
        _resultCard.Visible = false;
        _cancelButton.Visible = true;
        _cancelButton.Enabled = false;
        _headerSubtitle = "正在校验磁盘并锁定卷…";
        _header.Invalidate();
        UpdateStartButton();

        WipeDiskInfo disk = _jobDisk ?? _selectedDisk!;
        _diskInfoLabel.Text = "PhysicalDrive " + disk.Number + " · " + disk.Model;
        _stageLabel.Text = "正在校验目标磁盘并锁定卷…";
        _progressHint.Text = "覆写期间请勿关闭程序、断电或拔出磁盘。中途取消也会导致磁盘部分覆写，已写入区域的数据无法恢复。";
        _progressHint.ForeColor = ShutdownTheme.Danger;
        _progressHint.BackColor = Color.White;
    }

    private void PromptCancelWipe()
    {
        if (_job == null || _job.IsFinished || _cancelRequested)
        {
            return;
        }
        DialogResult result = MessageBox.Show(this,
            "中止后，目标磁盘将只被部分覆写，已写入区域的数据无法恢复。\n\n确定中止当前覆写吗？",
            "中止覆写",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }
        _cancelRequested = true;
        _job.Cancel();
        _cancelButton.Enabled = false;
        _cancelButton.Text = "正在中止…";
        _stageLabel.Text = "正在中止，请稍候…";
        _headerSubtitle = "正在中止覆写…";
        _header.Invalidate();
    }

    private void OnProgressTick()
    {
        WipeJob? job = _job;
        if (job == null)
        {
            return;
        }
        WipeProgressSnapshot progress = job.GetProgress();
        if (progress.Finished)
        {
            FinishWipe(progress);
            return;
        }

        long now = Environment.TickCount64;
        if (progress.CompletedBytes >= _lastBytes && now > _lastTick)
        {
            double instant = (progress.CompletedBytes - _lastBytes) * 1000.0 / (now - _lastTick);
            _speed = _speed <= 0 ? instant : _speed * 0.65 + instant * 0.35;
            _lastBytes = progress.CompletedBytes;
            _lastTick = now;
        }

        double percent = progress.TotalBytes > 0
            ? Math.Clamp(progress.CompletedBytes * 100.0 / progress.TotalBytes, 0.0, 100.0)
            : 0.0;
        _percentLabel.Text = percent.ToString("0", CultureInfo.InvariantCulture) + "%";
        _progressBar.Value = percent / 100.0;
        _speedLabel.Text = "速度 " + (_speed > 0 ? FormatBytes((long)_speed) + "/s" : "—");
        _elapsedLabel.Text = "已用 " + FormatDuration(TimeSpan.FromMilliseconds(progress.ElapsedMs));
        if (_speed > 0 && progress.TotalBytes > progress.CompletedBytes)
        {
            double remainSeconds = (progress.TotalBytes - progress.CompletedBytes) / _speed;
            _remainingLabel.Text = "剩余 " + FormatDuration(TimeSpan.FromSeconds(remainSeconds));
        }
        else if (progress.CompletedBytes >= progress.TotalBytes)
        {
            _remainingLabel.Text = "剩余 00:00";
        }
        else
        {
            _remainingLabel.Text = "剩余 —";
        }

        string stage;
        if (progress.EncryptionPrePass && progress.CurrentPass == 0)
        {
            stage = "AES-256 随机密钥加密预覆盖";
        }
        else
        {
            stage = "第 " + progress.CurrentPass + " / " + progress.TotalPasses + " 遍 · "
                + DiskWipeEngine.PassLabel(progress.Algorithm, progress.CurrentPass);
        }
        _stageLabel.Text = _cancelRequested ? "正在中止，请稍候…" : stage;
    }

    private void FinishWipe(WipeProgressSnapshot progress)
    {
        _progressTimer.Stop();
        WipeJob? job = _job;
        _job = null;
        job?.Dispose();

        WipeDiskInfo disk = _jobDisk ?? _selectedDisk!;
        string subtitle = "PhysicalDrive " + disk.Number + " · "
            + DiskWipeEngine.AlgorithmName(progress.Algorithm) + " · "
            + progress.TotalPasses + " 遍";
        string processed = FormatBytes(progress.CompletedBytes);
        string elapsed = FormatDuration(TimeSpan.FromMilliseconds(progress.ElapsedMs));

        if (progress.Success)
        {
            string doneText = progress.EncryptionPrePass
                ? "已完成 AES-256 随机密钥加密预覆盖，并按 "
                    + DiskWipeEngine.AlgorithmName(progress.Algorithm) + " 完成 "
                    + progress.TotalPasses + " 遍覆写。"
                : "已按 " + DiskWipeEngine.AlgorithmName(progress.Algorithm) + " 完成 "
                    + progress.TotalPasses + " 遍覆写（未启用 AES-256 加密预覆盖）。";
            ShowResult(ResultKind.Success, "覆写完成", subtitle,
                doneText + "\n\n"
                + "已处理：" + processed + "\n用时：" + elapsed + "\n\n"
                + "分区表、文件系统与原有盘符信息已被清除，磁盘当前等同于未初始化的新磁盘。\n"
                + "如需继续使用，请在 Windows“磁盘管理”中初始化并重新分区。",
                progress);
        }
        else if (progress.Cancelled)
        {
            ShowResult(ResultKind.Cancelled, "覆写已中止", subtitle,
                "操作已由用户中止。\n\n"
                + "目标磁盘可能已被部分加密或覆写，原有数据可能已无法恢复。\n"
                + "已处理：" + processed + "\n用时：" + elapsed + "\n\n"
                + "磁盘分区表可能已被破坏。如需继续使用，请重新初始化磁盘。",
                progress);
        }
        else
        {
            ShowResult(ResultKind.Failure, "覆写失败", subtitle,
                "覆写未完成。\n\n"
                + (progress.ErrorMessage.Length > 0 ? progress.ErrorMessage : "写入过程被异常终止。")
                + "\n\n已处理：" + processed + "\n\n"
                + "目标磁盘可能处于部分覆写状态，原有数据可能已部分或全部丢失。\n"
                + "请勿继续依赖该磁盘中的数据。",
                progress);
        }
    }

    private void ShowResult(ResultKind kind, string title, string subtitle, string details,
        WipeProgressSnapshot? progress)
    {
        _progressTimer.Stop();
        if (_job != null)
        {
            _job.Dispose();
            _job = null;
        }
        _setupHost.Visible = false;
        _progressHost.Visible = true;
        _progressCard.Visible = false;
        _resultCard.Visible = true;
        _resultCard.BringToFront();

        _resultBadge.Kind = kind;
        _resultTitle.Text = title;
        _resultSubtitle.Text = subtitle;
        _resultDetails.Text = details;
        _resultDetails.SelectionStart = 0;
        _resultDetails.SelectionLength = 0;
        _resultTitle.ForeColor = kind switch
        {
            ResultKind.Success => ShutdownTheme.Success,
            ResultKind.Cancelled => ShutdownTheme.WarningText,
            _ => ShutdownTheme.Danger
        };
        _headerSubtitle = kind switch
        {
            ResultKind.Success => "覆写完成 · 数据已不可恢复",
            ResultKind.Cancelled => "覆写已中止 · 磁盘可能已被部分覆写",
            _ => "覆写失败 · 请检查错误信息"
        };
        _header.Invalidate();
        _resultOkButton.Focus();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 40)
        {
            return (bytes / (double)(1L << 40)).ToString("0.00", CultureInfo.InvariantCulture) + " TB";
        }
        if (bytes >= 1L << 30)
        {
            return (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }
        if (bytes >= 1L << 20)
        {
            return (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }
        if (bytes >= 1L << 10)
        {
            return (bytes / (double)(1L << 10)).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }
        if (span.TotalHours >= 1)
        {
            return (int)span.TotalHours + ":" + span.Minutes.ToString("00", CultureInfo.InvariantCulture)
                + ":" + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }
        return span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
            + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        WipeJob? job = _job;
        if (job != null && !job.IsFinished)
        {
            DialogResult result = MessageBox.Show(this,
                "覆写任务正在运行。\n\n"
                + "关闭窗口将中止操作，目标磁盘将只被部分覆写，已写入区域的数据无法恢复。\n\n"
                + "确定中止并退出吗？",
                "中止并退出",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            UseWaitCursor = true;
            _progressTimer.Stop();
            job.Cancel();
            job.Dispose();
            _job = null;
        }
        _progressTimer.Stop();
        base.OnFormClosing(e);
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
            _progressTimer.Stop();
            _progressTimer.Dispose();
            _job?.Dispose();
            _job = null;
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
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }
}

/// <summary>磁盘列表中的可选中卡片。</summary>
internal sealed class DiskCard : Control
{
    private readonly Font _numberFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
    private readonly Font _modelFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
    private readonly Font _infoFont = new Font("Microsoft YaHei UI", 8.5f);
    private readonly Font _serialFont = new Font("Microsoft YaHei UI", 8f);
    private readonly Font _mediaFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
    private bool _hover;
    private bool _selected;

    public WipeDiskInfo Disk { get; }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }
            _selected = value;
            Invalidate();
        }
    }

    public DiskCard(WipeDiskInfo disk, int width, int height)
    {
        Disk = disk;
        Size = new Size(width, height);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        Color fill = !Enabled
            ? Color.FromArgb(248, 250, 252)
            : _selected ? Color.FromArgb(254, 242, 242)
            : _hover ? Color.FromArgb(248, 250, 252) : Color.White;
        Color border = !Enabled
            ? ShutdownTheme.CardBorder
            : _selected ? ShutdownTheme.Danger
            : _hover ? Color.FromArgb(203, 213, 225) : ShutdownTheme.CardBorder;
        ShutdownTheme.FillRounded(g, rect, 10f, fill);
        ShutdownTheme.DrawRoundedBorder(g, rect, 10f, border);

        Color text = Enabled ? ShutdownTheme.Text : Color.FromArgb(148, 163, 184);
        Color muted = Enabled ? ShutdownTheme.Muted : Color.FromArgb(148, 163, 184);

        var badge = new RectangleF(14f, (Height - 38f) / 2f, 38f, 38f);
        Color badgeFill = _selected && Enabled ? ShutdownTheme.Danger : Color.FromArgb(226, 232, 240);
        Color badgeText = _selected && Enabled ? Color.White : ShutdownTheme.Muted;
        ShutdownTheme.FillRounded(g, badge, 19f, badgeFill);
        TextRenderer.DrawText(g, Disk.Number.ToString(CultureInfo.InvariantCulture),
            _numberFont, Rectangle.Round(badge), badgeText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        int textLeft = 62;
        int textWidth = Math.Max(80, Width - textLeft - 90);
        TextRenderer.DrawText(g, Disk.Model, _modelFont,
            new Rectangle(textLeft, 12, textWidth, 24), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        string info = "PhysicalDrive " + Disk.Number + " · " + FormatBytes(Disk.Size)
            + " · " + Disk.BusName
            + (Disk.Partitions >= 0 ? " · " + Disk.Partitions + " 个分区" : "")
            + (Disk.Removable ? " · 可移动" : "");
        TextRenderer.DrawText(g, info, _infoFont,
            new Rectangle(textLeft, 36, Width - textLeft - 20, 20), muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        string serial = Disk.Serial.Length > 0 ? "SN " + Disk.Serial : "SN —";
        TextRenderer.DrawText(g, serial, _serialFont,
            new Rectangle(textLeft, 56, textWidth, 18), muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        string media = Disk.Media switch
        {
            WipeMediaKind.Ssd => "SSD",
            WipeMediaKind.Hdd => "HDD",
            _ => "未知"
        };
        Color mediaColor = Disk.Media switch
        {
            WipeMediaKind.Ssd => ShutdownTheme.Accent,
            WipeMediaKind.Hdd => Color.FromArgb(100, 116, 139),
            _ => Color.FromArgb(148, 163, 184)
        };
        var pill = new RectangleF(Width - 70f, 16f, 54f, 24f);
        ShutdownTheme.FillRounded(g, pill, 12f, Color.FromArgb(239, 246, 255));
        ShutdownTheme.DrawRoundedBorder(g, pill, 12f, Color.FromArgb(191, 219, 254));
        TextRenderer.DrawText(g, media, _mediaFont,
            Rectangle.Round(pill), mediaColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _numberFont.Dispose();
            _modelFont.Dispose();
            _infoFont.Dispose();
            _serialFont.Dispose();
            _mediaFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 40)
        {
            return (bytes / (double)(1L << 40)).ToString("0.00", CultureInfo.InvariantCulture) + " TB";
        }
        if (bytes >= 1L << 30)
        {
            return (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }
        return (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }
}

/// <summary>圆角覆写进度条。</summary>
internal sealed class WipeProgressBar : Control
{
    private double _value;

    public double Value
    {
        get => _value;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_value - clamped) < 0.0001)
            {
                return;
            }
            _value = clamped;
            Invalidate();
        }
    }

    public WipeProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0, 0, Width, Height);
        ShutdownTheme.FillRounded(g, track, Height / 2f, Color.FromArgb(226, 232, 240));

        if (_value <= 0)
        {
            return;
        }
        float fillWidth = (float)(Width * _value);
        if (fillWidth < 4f)
        {
            fillWidth = 4f;
        }
        var fill = new RectangleF(0, 0, fillWidth, Height);
        using var brush = new LinearGradientBrush(
            new RectangleF(0, 0, Math.Max(1f, Width), Height),
            ShutdownTheme.Danger, Color.FromArgb(245, 158, 11), LinearGradientMode.Horizontal);
        using var path = ShutdownTheme.RoundRect(fill, Height / 2f);
        g.FillPath(brush, path);
    }
}

/// <summary>结果状态圆标（成功 / 中止 / 失败）。</summary>
internal sealed class ResultBadge : Control
{
    private DiskWipeForm.ResultKind _kind = DiskWipeForm.ResultKind.Success;

    public DiskWipeForm.ResultKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
            {
                return;
            }
            _kind = value;
            Invalidate();
        }
    }

    public ResultBadge()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = _kind switch
        {
            DiskWipeForm.ResultKind.Success => ShutdownTheme.Success,
            DiskWipeForm.ResultKind.Cancelled => Color.FromArgb(217, 119, 6),
            _ => ShutdownTheme.Danger
        };
        var circle = new RectangleF(0, 0, Width, Height);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, circle);
        using var pen = new Pen(Color.White, Math.Max(3f, Width / 18f));
        if (_kind == DiskWipeForm.ResultKind.Success)
        {
            float x1 = Width * 0.24f, y1 = Height * 0.52f;
            float x2 = Width * 0.43f, y2 = Height * 0.70f;
            float x3 = Width * 0.76f, y3 = Height * 0.31f;
            g.DrawLine(pen, x1, y1, x2, y2);
            g.DrawLine(pen, x2, y2, x3, y3);
        }
        else if (_kind == DiskWipeForm.ResultKind.Cancelled)
        {
            float cx = Width / 2f;
            g.DrawLine(pen, cx, Height * 0.24f, cx, Height * 0.60f);
            using var dot = new SolidBrush(Color.White);
            float d = Width * 0.12f;
            g.FillEllipse(dot, cx - d / 2f, Height * 0.72f, d, d);
        }
        else
        {
            float inset = Width * 0.26f;
            g.DrawLine(pen, inset, inset, Width - inset, Height - inset);
            g.DrawLine(pen, Width - inset, inset, inset, Height - inset);
        }
    }
}
