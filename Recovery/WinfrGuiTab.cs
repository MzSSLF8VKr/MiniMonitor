using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MiniMonitor.Recovery;

/// <summary>
/// WinfrGUI 复刻：调用 Windows 文件恢复（winfr.exe）的图形界面。
/// 支持常规 / 广泛 / NTFS / 段模式 / 签名模式、/n 筛选、/y 签名类型、
/// /a /u /k /g /e /o /p /s /b 等开关、命令预览、实时日志与中止。
/// </summary>
public sealed class WinfrGuiTab : UserControl
{
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _winfrStatus;
    private readonly Label _bottomStatus;
    private readonly ComboBox _sourceCombo;
    private readonly ComboBox _destCombo;
    private readonly TextBox _destFolderBox;
    private readonly PillButton _browseDestButton;
    private readonly ComboBox _modeCombo;
    private readonly CheckBox _docs;
    private readonly CheckBox _photos;
    private readonly CheckBox _videos;
    private readonly CheckBox _audios;
    private readonly CheckBox _archives;
    private readonly CheckBox _mails;
    private readonly TextBox _signatureTypes;
    private readonly TextBox _customFilters;
    private readonly ComboBox _overwriteCombo;
    private readonly CheckBox _autoYes;
    private readonly CheckBox _undelete;
    private readonly CheckBox _systemFiles;
    private readonly CheckBox _noPrimary;
    private readonly CheckBox _removeFilter;
    private readonly NumericUpDown _sectors;
    private readonly NumericUpDown _bytes;
    private readonly TextBox _logFolderBox;
    private readonly PillButton _browseLogButton;
    private readonly PillButton _detectButton;
    private readonly PillButton _installButton;
    private readonly PillButton _previewButton;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly PillButton _openDestButton;
    private readonly PillButton _openLogButton;
    private readonly TextBox _commandBox;
    private readonly TextBox _logBox;
    private readonly ProgressBar _progress;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _monoFont;

    private string _winfrPath = "";
    private Process? _process;
    private bool _running;
    private bool _loadedVolumes;
    private bool _disposed;

    public WinfrGuiTab()
    {
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);
        _monoFont = new Font("Consolas", 9f);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _top = new Panel { Dock = DockStyle.Top, Height = 334, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));

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
            Text = "WinfrGUI调用系统工具（Windows File Recovery）",
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

        var winfrBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _winfrStatus = new Label
        {
            Text = "winfr.exe：正在检测…",
            AutoSize = false,
            Width = 720,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            AutoEllipsis = true,
            Margin = new Padding(0, 2, 10, 0)
        };
        _detectButton = NewButton("重新检测", 86);
        _detectButton.Click += (_, _) => DetectWinfr();
        _installButton = NewButton("安装 Windows File Recovery", 176);
        _installButton.Click += (_, _) => OpenStore();
        winfrBar.Controls.AddRange(new Control[]
        {
            _winfrStatus, _detectButton, _installButton
        });
        layout.Controls.Add(winfrBar, 0, 1);

        var driveBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 0)
        };
        driveBar.Controls.Add(NewLabel("源磁盘：", 62));
        _sourceCombo = NewCombo(250);
        driveBar.Controls.Add(_sourceCombo);
        driveBar.Controls.Add(NewLabel("目标磁盘：", 72));
        _destCombo = NewCombo(230);
        driveBar.Controls.Add(_destCombo);
        driveBar.Controls.Add(NewLabel("目标文件夹：", 82));
        _destFolderBox = new TextBox
        {
            Width = 300,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "可留空，默认在目标盘根目录创建 Recovery_ 文件夹",
            Margin = new Padding(0, 1, 8, 0)
        };
        _browseDestButton = NewButton("浏览…", 72);
        _browseDestButton.Click += (_, _) => BrowseDestination();
        driveBar.Controls.AddRange(new Control[]
        {
            _destFolderBox, _browseDestButton
        });
        layout.Controls.Add(driveBar, 0, 2);

        var modeBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 0)
        };
        modeBar.Controls.Add(NewLabel("恢复模式：", 72));
        _modeCombo = NewCombo(180);
        _modeCombo.Items.AddRange(new object[]
        {
            "常规模式 /regular",
            "广泛模式 /extensive",
            "高级：NTFS /ntfs",
            "高级：段模式 /segment",
            "高级：签名模式 /signature"
        });
        _modeCombo.SelectedIndex = 0;
        _modeCombo.SelectedIndexChanged += (_, _) => OnModeChanged();
        modeBar.Controls.Add(_modeCombo);
        modeBar.Controls.Add(NewLabel("文件类型：", 72));
        _docs = NewCheck("文档", 62);
        _photos = NewCheck("图片", 62);
        _videos = NewCheck("视频", 62);
        _audios = NewCheck("音频", 62);
        _archives = NewCheck("压缩包", 76);
        _mails = NewCheck("邮件", 62);
        _docs.Checked = true;
        _photos.Checked = true;
        _videos.Checked = true;
        _audios.Checked = true;
        _archives.Checked = true;
        modeBar.Controls.AddRange(new Control[]
        {
            _docs, _photos, _videos, _audios, _archives, _mails
        });
        layout.Controls.Add(modeBar, 0, 3);

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 0)
        };
        filterBar.Controls.Add(NewLabel("自定义筛选 /n：", 102));
        _customFilters = new TextBox
        {
            Width = 380,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "分号分隔，如 *.pdf; \\Users\\name\\Documents\\",
            Margin = new Padding(0, 1, 10, 0)
        };
        filterBar.Controls.Add(_customFilters);
        filterBar.Controls.Add(NewLabel("签名类型 /y：", 94));
        _signatureTypes = new TextBox
        {
            Width = 230,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "如 jpg,png,pdf（仅签名模式）",
            Enabled = false,
            Margin = new Padding(0, 1, 10, 0)
        };
        _overwriteCombo = NewCombo(150);
        _overwriteCombo.Items.AddRange(new object[]
        {
            "覆盖：每次询问",
            "覆盖：始终 /o:a",
            "覆盖：从不 /o:n",
            "覆盖：保留两者 /o:b"
        });
        _overwriteCombo.SelectedIndex = 0;
        filterBar.Controls.AddRange(new Control[]
        {
            _signatureTypes, _overwriteCombo
        });
        layout.Controls.Add(filterBar, 0, 4);

        var advancedBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 0)
        };
        _autoYes = NewCheck("自动确认 /a", 100);
        _autoYes.Checked = true;
        _undelete = NewCheck("未删除文件 /u", 112);
        _systemFiles = NewCheck("系统文件 /k", 100);
        _noPrimary = NewCheck("无主数据流 /g", 110);
        _removeFilter = NewCheck("取消默认过滤 /e", 124);
        advancedBar.Controls.AddRange(new Control[]
        {
            _autoYes, _undelete, _systemFiles, _noPrimary, _removeFilter,
            NewLabel("段扇区 /s：", 82)
        });
        _sectors = NewNumber(0, 1_000_000_000, 0, 90);
        _bytes = NewNumber(0, 1_000_000_000, 0, 90);
        advancedBar.Controls.Add(_sectors);
        advancedBar.Controls.Add(NewLabel("簇字节 /b：", 72));
        advancedBar.Controls.Add(_bytes);
        layout.Controls.Add(advancedBar, 0, 5);

        var actionBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0)
        };
        actionBar.Controls.Add(NewLabel("日志目录 /p：", 82));
        _logFolderBox = new TextBox
        {
            Width = 260,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "可选，默认写入恢复盘",
            Margin = new Padding(0, 1, 8, 0)
        };
        _browseLogButton = NewButton("浏览…", 66);
        _browseLogButton.Click += (_, _) => BrowseLogFolder();
        _previewButton = NewButton("生成命令", 82);
        _previewButton.Click += (_, _) => UpdateCommandPreview();
        _startButton = NewButton("开始恢复", 88, PillButtonStyle.Primary);
        _startButton.Click += (_, _) => _ = StartRecoveryAsync();
        _stopButton = NewButton("停止", 62, PillButtonStyle.Danger);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopRecovery();
        _openDestButton = NewButton("打开目标目录", 104);
        _openDestButton.Click += (_, _) => OpenDestination();
        _openLogButton = NewButton("打开日志目录", 104);
        _openLogButton.Click += (_, _) => OpenLogFolder();
        actionBar.Controls.AddRange(new Control[]
        {
            _logFolderBox, _browseLogButton, _previewButton, _startButton,
            _stopButton, _openDestButton, _openLogButton
        });
        layout.Controls.Add(actionBar, 0, 6);

        _commandBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Horizontal,
            WordWrap = false,
            Font = _monoFont,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "命令预览将在生成后显示。",
            Margin = new Padding(0, 3, 0, 0)
        };
        layout.Controls.Add(_commandBox, 0, 7);
        _top.Controls.Add(layout);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = _monoFont,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None,
            Text = "WinfrGUI 复刻：选择源磁盘、目标磁盘与恢复模式后点击“开始恢复”。\r\n"
                + "提示：目标磁盘必须与源磁盘不同；常规 / NTFS / 段模式仅支持 NTFS。\r\n"
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
            Text = "尚未开始"
        };

        Controls.Add(_logBox);
        Controls.Add(_progress);
        Controls.Add(_bottomStatus);
        Controls.Add(_top);
        Controls.SetChildIndex(_logBox, 0);
        Controls.SetChildIndex(_progress, 1);
        Controls.SetChildIndex(_bottomStatus, 2);
        Controls.SetChildIndex(_top, 3);

        _sourceCombo.SelectedIndexChanged += (_, _) => UpdateModeHint();
        _destCombo.SelectedIndexChanged += (_, _) => UpdateModeHint();
        _destFolderBox.TextChanged += (_, _) => UpdateCommandPreview();
        _customFilters.TextChanged += (_, _) => UpdateCommandPreview();
        _signatureTypes.TextChanged += (_, _) => UpdateCommandPreview();
        _overwriteCombo.SelectedIndexChanged += (_, _) => UpdateCommandPreview();
        foreach (var check in new[]
        {
            _docs, _photos, _videos, _audios, _archives, _mails,
            _autoYes, _undelete, _systemFiles, _noPrimary, _removeFilter
        })
        {
            check.CheckedChanged += (_, _) => UpdateCommandPreview();
        }
        _sectors.ValueChanged += (_, _) => UpdateCommandPreview();
        _bytes.ValueChanged += (_, _) => UpdateCommandPreview();
        _logFolderBox.TextChanged += (_, _) => UpdateCommandPreview();

        Disposed += (_, _) =>
        {
            _disposed = true;
            StopRecovery();
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

    private ComboBox NewCombo(int width)
    {
        return new ComboBox
        {
            Width = width,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 1, 8, 0)
        };
    }

    private NumericUpDown NewNumber(int min, int max, int value, int width)
    {
        return new NumericUpDown
        {
            Width = width,
            Height = 24,
            Minimum = min,
            Maximum = max,
            Value = value,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 1, 8, 0)
        };
    }

    // ================= 初始化 / 检测 =================

    public void SetActive(bool active)
    {
        if (active && !_loadedVolumes)
        {
            _loadedVolumes = true;
            RefreshVolumes();
            DetectWinfr();
        }
    }

    private void RefreshVolumes()
    {
        var volumes = RecoveryDisk.GetVolumes();
        _sourceCombo.BeginUpdate();
        _destCombo.BeginUpdate();
        _sourceCombo.Items.Clear();
        _destCombo.Items.Clear();
        foreach (var volume in volumes)
        {
            _sourceCombo.Items.Add(volume);
            _destCombo.Items.Add(volume);
        }
        _sourceCombo.EndUpdate();
        _destCombo.EndUpdate();
        if (_sourceCombo.Items.Count > 0)
        {
            _sourceCombo.SelectedIndex = 0;
        }
        if (_destCombo.Items.Count > 1)
        {
            _destCombo.SelectedIndex = 1;
        }
        else if (_destCombo.Items.Count > 0)
        {
            _destCombo.SelectedIndex = 0;
        }
        UpdateModeHint();
    }

    private void DetectWinfr()
    {
        _winfrPath = FindWinfrExecutable();
        if (_winfrPath.Length > 0)
        {
            _winfrStatus.Text = "winfr.exe：已找到  " + _winfrPath;
            _winfrStatus.ForeColor = ShutdownTheme.Success;
            _startButton.Enabled = true;
        }
        else
        {
            _winfrStatus.Text = "winfr.exe：未找到。请先安装 Microsoft Store 的“Windows File Recovery”。";
            _winfrStatus.ForeColor = ShutdownTheme.Danger;
            _startButton.Enabled = false;
        }
        UpdateCommandPreview();
    }

    private static string FindWinfrExecutable()
    {
        // 优先使用 WindowsApps 包内的真实 winfr.exe（支持重定向输出）；
        // 其次使用应用执行别名和 PATH。
        try
        {
            string apps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps");
            if (Directory.Exists(apps))
            {
                foreach (string folder in Directory.EnumerateDirectories(apps,
                    "Microsoft.WindowsFileRecovery_*"))
                {
                    string candidate = Path.Combine(folder, "winfr.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch
        {
        }
        try
        {
            string local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winfr.exe");
            if (File.Exists(local))
            {
                return local;
            }
        }
        catch
        {
        }
        try
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(path))
            {
                foreach (string folder in path.Split(';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    try
                    {
                        string candidate = Path.Combine(folder, "winfr.exe");
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
        return "";
    }

    private void OpenStore()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-windows-store://search/?query=Windows%20File%20Recovery",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(FindForm(),
                "无法打开 Microsoft Store。请在应用商店搜索“Windows File Recovery”并安装。",
                "WinfrGUI调用系统工具", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    // ================= 选项 / 命令 =================

    private void OnModeChanged()
    {
        bool signature = _modeCombo.SelectedIndex == 4;
        _signatureTypes.Enabled = signature;
        UpdateModeHint();
        UpdateCommandPreview();
    }

    private void UpdateModeHint()
    {
        if (_sourceCombo.SelectedItem is not RecoveryVolume source)
        {
            return;
        }
        string mode = _modeCombo.SelectedIndex switch
        {
            0 => "常规",
            1 => "广泛",
            2 => "NTFS",
            3 => "段模式",
            _ => "签名"
        };
        string fs = source.FileSystem;
        bool ntfsOnly = _modeCombo.SelectedIndex is 0 or 2 or 3;
        if (ntfsOnly && !fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
        {
            _statusLabel.Text = "● " + mode + "模式仅支持 NTFS，当前源盘为 " + fs
                + "，请选择“广泛模式”或“签名模式”";
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
        else
        {
            _statusLabel.Text = "● 就绪 · 源盘 " + source.RootPath + "（" + fs + "）"
                + " · " + mode + "模式";
            _statusLabel.ForeColor = ShutdownTheme.Success;
        }
        UpdateCommandPreview();
    }

    private void UpdateCommandPreview()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        try
        {
            _commandBox.Text = "winfr " + BuildArguments();
        }
        catch (Exception ex)
        {
            _commandBox.Text = "命令生成失败：" + ex.Message;
        }
    }

    private string BuildArguments()
    {
        if (_sourceCombo.SelectedItem is not RecoveryVolume source
            || _destCombo.SelectedItem is not RecoveryVolume dest)
        {
            return "<请选择源磁盘和目标磁盘>";
        }
        var args = new List<string>
        {
            source.RootPath[..2],
            Quote(DestinationPath().Length > 0
                ? DestinationPath() : destinationPath(dest))
        };
        string mode = _modeCombo.SelectedIndex switch
        {
            0 => "/regular",
            1 => "/extensive",
            2 => "/ntfs",
            3 => "/segment",
            _ => "/signature"
        };
        args.Add(mode);
        foreach (string filter in BuildFilters())
        {
            args.Add("/n");
            args.Add(Quote(filter));
        }
        if (_modeCombo.SelectedIndex == 4
            && _signatureTypes.Text.Trim().Length > 0)
        {
            args.Add("/y:" + _signatureTypes.Text.Trim().Replace(" ", ""));
        }
        if (_autoYes.Checked) args.Add("/a");
        if (_undelete.Checked) args.Add("/u");
        if (_systemFiles.Checked) args.Add("/k");
        if (_noPrimary.Checked) args.Add("/g");
        if (_removeFilter.Checked) args.Add("/e");
        string overwrite = _overwriteCombo.SelectedIndex switch
        {
            1 => "/o:a",
            2 => "/o:n",
            3 => "/o:b",
            _ => ""
        };
        if (overwrite.Length > 0) args.Add(overwrite);
        string logFolder = _logFolderBox.Text.Trim();
        if (logFolder.Length > 0)
        {
            args.Add("/p:" + Quote(logFolder));
        }
        if (_modeCombo.SelectedIndex == 3)
        {
            if (_sectors.Value > 0)
            {
                args.Add("/s:" + _sectors.Value);
            }
            if (_bytes.Value > 0)
            {
                args.Add("/b:" + _bytes.Value);
            }
        }
        return string.Join(" ", args);
    }

    private static string destinationPath(RecoveryVolume dest)
    {
        string root = dest.RootPath.TrimEnd('\\');
        return root.Length > 0 ? root : dest.RootPath;
    }

    private string DestinationPath()
    {
        if (_destCombo.SelectedItem is not RecoveryVolume dest)
        {
            return "";
        }
        string folder = _destFolderBox.Text.Trim();
        if (folder.Length == 0)
        {
            return dest.RootPath.TrimEnd('\\');
        }
        try
        {
            string full = Path.GetFullPath(folder);
            if (!full.StartsWith(dest.RootPath,
                StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return full;
        }
        catch
        {
            return "";
        }
    }

    private IEnumerable<string> BuildFilters()
    {
        var filters = new List<string>();
        if (_docs.Checked)
        {
            filters.AddRange(new[]
            {
                "*.doc", "*.docx", "*.xls", "*.xlsx", "*.ppt", "*.pptx",
                "*.pdf", "*.txt", "*.rtf", "*.csv"
            });
        }
        if (_photos.Checked)
        {
            filters.AddRange(new[]
            {
                "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.tif",
                "*.tiff", "*.webp", "*.heic"
            });
        }
        if (_videos.Checked)
        {
            filters.AddRange(new[]
            {
                "*.mp4", "*.mov", "*.avi", "*.mkv", "*.wmv", "*.flv", "*.m4v"
            });
        }
        if (_audios.Checked)
        {
            filters.AddRange(new[]
            {
                "*.mp3", "*.wav", "*.flac", "*.aac", "*.m4a", "*.ogg", "*.wma"
            });
        }
        if (_archives.Checked)
        {
            filters.AddRange(new[]
            {
                "*.zip", "*.rar", "*.7z", "*.tar", "*.gz", "*.iso"
            });
        }
        if (_mails.Checked)
        {
            filters.AddRange(new[] { "*.eml", "*.msg", "*.pst", "*.ost" });
        }
        foreach (string part in _customFilters.Text.Split(
            new[] { ';', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            filters.Add(part);
        }
        return filters.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? "\"" + value + "\"" : value;

    // ================= 运行 =================

    private async Task StartRecoveryAsync()
    {
        if (_running || _disposed)
        {
            return;
        }
        if (_winfrPath.Length == 0 || !File.Exists(_winfrPath))
        {
            MessageBox.Show(FindForm(),
                "未找到 winfr.exe。请先在 Microsoft Store 安装“Windows File Recovery”。",
                "WinfrGUI调用系统工具", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        if (_sourceCombo.SelectedItem is not RecoveryVolume source
            || _destCombo.SelectedItem is not RecoveryVolume dest)
        {
            return;
        }
        if (string.Equals(source.RootPath[..2], dest.RootPath[..2],
            StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(FindForm(),
                "源磁盘与目标磁盘不能相同，请选择另一块磁盘。",
                "WinfrGUI调用系统工具", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        if (_modeCombo.SelectedIndex is 0 or 2 or 3
            && !source.FileSystem.Equals("NTFS",
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(FindForm(),
                "当前模式仅支持 NTFS 源盘。\n请改用“广泛模式”或“签名模式”。",
                "WinfrGUI调用系统工具", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        string destPath = DestinationPath();
        if (destPath.Length == 0)
        {
            MessageBox.Show(FindForm(),
                "目标文件夹必须位于所选目标磁盘上。",
                "WinfrGUI调用系统工具", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Directory.CreateDirectory(destPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "无法创建目标目录：\n" + ex.Message,
                "WinfrGUI调用系统工具", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        if (_logFolderBox.Text.Trim().Length > 0)
        {
            try
            {
                Directory.CreateDirectory(_logFolderBox.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "无法创建日志目录：\n" + ex.Message,
                    "WinfrGUI调用系统工具", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }
        string arguments = BuildArguments();
        _commandBox.Text = "winfr " + arguments;
        _running = true;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _previewButton.Enabled = false;
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 35;
        _statusLabel.Text = "● 正在运行 Windows File Recovery…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        AppendLog("");
        AppendLog("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + " 开始恢复 ===");
        AppendLog("winfr " + arguments);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _winfrPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_winfrPath) ?? ""
            };
            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                var encoding = Encoding.GetEncoding(codePage);
                psi.StandardOutputEncoding = encoding;
                psi.StandardErrorEncoding = encoding;
            }
            catch
            {
            }
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => AppendOutput(e.Data);
            _process.ErrorDataReceived += (_, e) => AppendOutput(e.Data);
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            await _process.WaitForExitAsync().ConfigureAwait(true);
            if (_disposed || IsDisposed)
            {
                return;
            }
            int exitCode = _process.ExitCode;
            string state = exitCode == 0 ? "完成" : "结束（退出码 " + exitCode + "）";
            AppendLog("=== 恢复" + state + " ===");
            _statusLabel.Text = "● 恢复" + state;
            _statusLabel.ForeColor = exitCode == 0
                ? ShutdownTheme.Success : ShutdownTheme.WarningText;
            _bottomStatus.Text = "恢复" + state + " · 输出目录：" + destPath;
            if (exitCode == 0
                && MessageBox.Show(FindForm(), "恢复完成，是否打开目标目录？",
                    "WinfrGUI调用系统工具", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                OpenDestination();
            }
        }
        catch (Exception ex)
        {
            AppendLog("运行失败：" + ex.Message);
            _statusLabel.Text = "● 运行失败：" + ex.Message;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
        finally
        {
            _running = false;
            _process?.Dispose();
            _process = null;
            if (!_disposed && !IsDisposed)
            {
                _startButton.Enabled = _winfrPath.Length > 0;
                _stopButton.Enabled = false;
                _previewButton.Enabled = true;
                _progress.Visible = false;
                _progress.Style = ProgressBarStyle.Continuous;
            }
        }
    }

    private void StopRecovery()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                AppendLog("用户已停止恢复。");
            }
        }
        catch
        {
        }
    }

    private void AppendOutput(string? line)
    {
        if (string.IsNullOrEmpty(line) || _disposed || IsDisposed)
        {
            return;
        }
        AppendLog(line);
        var match = Regex.Match(line, @"(\d{1,3})\s*%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int percent))
        {
            percent = Math.Clamp(percent, 0, 100);
            void Apply()
            {
                if (_disposed || IsDisposed)
                {
                    return;
                }
                if (_progress.Style == ProgressBarStyle.Marquee)
                {
                    _progress.Style = ProgressBarStyle.Continuous;
                }
                _progress.Value = percent;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(Apply)); } catch { }
            }
            else
            {
                Apply();
            }
        }
    }

    private void AppendLog(string text)
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
            if (_logBox.TextLength > 300_000)
            {
                _logBox.Text = _logBox.Text[^150_000..];
            }
            _logBox.AppendText(text + Environment.NewLine);
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(Apply)); } catch { }
            return;
        }
        Apply();
    }

    // ================= 目录 =================

    private void BrowseDestination()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择目标文件夹（必须位于另一块磁盘）",
            UseDescriptionForTitle = true
        };
        if (_destFolderBox.Text.Length > 0
            && Directory.Exists(_destFolderBox.Text))
        {
            dialog.SelectedPath = _destFolderBox.Text;
        }
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _destFolderBox.Text = dialog.SelectedPath;
            // 自动将目标磁盘下拉框切换到所选路径所在卷。
            string root = Path.GetPathRoot(dialog.SelectedPath) ?? "";
            for (int i = 0; i < _destCombo.Items.Count; i++)
            {
                if (_destCombo.Items[i] is RecoveryVolume volume
                    && string.Equals(volume.RootPath, root,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _destCombo.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void BrowseLogFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择日志保存目录（可选）",
            UseDescriptionForTitle = true
        };
        if (_logFolderBox.Text.Length > 0
            && Directory.Exists(_logFolderBox.Text))
        {
            dialog.SelectedPath = _logFolderBox.Text;
        }
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _logFolderBox.Text = dialog.SelectedPath;
        }
    }

    private void OpenDestination()
    {
        string path = DestinationPath();
        if (path.Length == 0
            || _destCombo.SelectedItem is not RecoveryVolume dest)
        {
            return;
        }
        if (!Directory.Exists(path))
        {
            path = dest.RootPath;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void OpenLogFolder()
    {
        string path = _logFolderBox.Text.Trim();
        if (path.Length == 0)
        {
            if (_destCombo.SelectedItem is RecoveryVolume dest)
            {
                path = dest.RootPath;
            }
        }
        if (path.Length == 0 || !Directory.Exists(path))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
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
            StopRecovery();
            _process?.Dispose();
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _monoFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
