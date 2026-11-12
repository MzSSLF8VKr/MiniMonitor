using System.Diagnostics;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>Nmap 扫描选项卡：内置扫描引擎，提供快速命令生成与结果自动保存。</summary>
public sealed class NmapTestTab : UserControl
{
    private readonly NetConsole _console;
    private readonly Panel _top;
    private readonly TableLayoutPanel _layout;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _targetLabel;
    private readonly TextBox _targetBox;
    private readonly Label _modeLabel;
    private readonly ComboBox _modeCombo;
    private readonly Label _commandLabel;
    private readonly TextBox _commandBox;
    private readonly Label _hintLabel;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly PillButton _openFolderButton;
    private readonly PillButton _clearButton;
    private readonly Font _titleFont;
    private readonly Font _hintFont;
    private readonly Font _statusFont;
    private readonly Font _commandFont;
    private readonly Font _bodyFont;

    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _acceptProgress;
    private bool _disposed;
    private string? _lastSavedPath;

    public NmapTestTab()
    {
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _hintFont = new Font("Microsoft YaHei UI", 8.5f);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _commandFont = new Font("Consolas", 9.5f);
        _bodyFont = new Font("Microsoft YaHei UI", 9f);

        _console = new NetConsole { Dock = DockStyle.Fill, PromptText = "nmap> " };

        _top = new Panel { Dock = DockStyle.Top, Height = 142, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 4,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108f));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

        // 标题行：标题在左，进度状态紧跟在标题文字后面，尽可能留出更多进度显示宽度。
        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        titleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _titleLabel = new Label
        {
            Text = "内置 Nmap 扫描（无需安装 Nmap / Npcap）",
            Font = _titleFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 14, 0),
            Anchor = AnchorStyles.Left
        };
        titleRow.Controls.Add(_titleLabel, 0, 0);

        _statusLabel = new Label
        {
            Text = "● 就绪",
            Font = _statusFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        titleRow.Controls.Add(_statusLabel, 1, 0);

        _layout.Controls.Add(titleRow, 0, 0);
        _layout.SetColumnSpan(titleRow, 6);

        _targetLabel = new Label
        {
            Text = "目标",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 0)
        };
        _layout.Controls.Add(_targetLabel, 0, 1);

        _targetBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = _bodyFont,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            PlaceholderText = "IP / 域名，例如 192.168.1.100 或 example.com",
            Margin = new Padding(0, 5, 12, 5)
        };
        _targetBox.TextChanged += (_, _) => RegenerateCommand();
        _targetBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (_commandBox != null)
                {
                    _commandBox.Focus();
                    _commandBox.SelectionStart = _commandBox.TextLength;
                }
            }
        };
        _layout.Controls.Add(_targetBox, 1, 1);

        _modeLabel = new Label
        {
            Text = "扫描模式",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 0)
        };
        _layout.Controls.Add(_modeLabel, 2, 1);

        _modeCombo = new ComboBox
        {
            Width = 245,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Margin = new Padding(0, 5, 12, 5)
        };
        _modeCombo.Items.AddRange(new object[]
        {
            "快速 TCP（全端口 1-65535）",
            "快速 UDP（全端口 1-65535）",
            "快速 TCP + UDP（全端口）"
        });
        _modeCombo.SelectedIndex = 0;
        _modeCombo.SelectedIndexChanged += (_, _) => RegenerateCommand();
        _layout.Controls.Add(_modeCombo, 3, 1);

        _startButton = new PillButton
        {
            Text = "开始扫描",
            ButtonStyle = PillButtonStyle.Primary,
            Width = 100,
            Height = 30,
            Margin = new Padding(0, 4, 8, 4),
            Anchor = AnchorStyles.None
        };
        _startButton.Click += (_, _) =>
        {
            if (_commandBox != null)
            {
                RunCommand(_commandBox.Text);
            }
        };
        _layout.Controls.Add(_startButton, 4, 1);

        _stopButton = new PillButton
        {
            Text = "停止",
            ButtonStyle = PillButtonStyle.Danger,
            Width = 72,
            Height = 30,
            Enabled = false,
            Margin = new Padding(0, 4, 0, 4),
            Anchor = AnchorStyles.None
        };
        _stopButton.Click += (_, _) => StopScan();
        _layout.Controls.Add(_stopButton, 5, 1);

        _commandLabel = new Label
        {
            Text = "命令",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 6, 0)
        };
        _layout.Controls.Add(_commandLabel, 0, 2);

        _commandBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = _commandFont,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 5, 12, 5)
        };
        _layout.Controls.Add(_commandBox, 1, 2);
        _layout.SetColumnSpan(_commandBox, 3);

        _openFolderButton = new PillButton
        {
            Text = "打开结果目录",
            ButtonStyle = PillButtonStyle.Light,
            Width = 122,
            Height = 30,
            Margin = new Padding(0, 4, 8, 4),
            Anchor = AnchorStyles.None
        };
        _openFolderButton.Click += (_, _) => OpenResultFolder();
        _layout.Controls.Add(_openFolderButton, 4, 2);

        _clearButton = new PillButton
        {
            Text = "清屏",
            ButtonStyle = PillButtonStyle.Light,
            Width = 70,
            Height = 30,
            Margin = new Padding(0, 4, 0, 4),
            Anchor = AnchorStyles.None
        };
        _clearButton.Click += (_, _) => _console.ClearOutput();
        _layout.Controls.Add(_clearButton, 5, 2);

        _hintLabel = new Label
        {
            Text = "填写目标后自动生成推荐命令；也可直接编辑命令，或在下方命令行输入 nmap 命令。结果自动保存为 Nmap_results_日期_时间_随机数.txt",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        _layout.Controls.Add(_hintLabel, 0, 3);
        _layout.SetColumnSpan(_hintLabel, 6);

        _top.Controls.Add(_layout);

        _console.CommandSubmitted += OnCommandSubmitted;

        Controls.Add(_console);
        Controls.Add(_top);

        RegenerateCommand();
        PrintHelp();
    }

    private void RegenerateCommand()
    {
        if (_commandBox == null || _modeCombo == null || _targetBox == null)
        {
            return;
        }

        string target = _targetBox.Text.Trim();
        string command = _modeCombo.SelectedIndex switch
        {
            1 => $"nmap -sU -p- -T4 --min-rate=5000 -Pn {target}",
            2 => $"nmap -sS -sU -p- -T4 --min-rate=5000 -Pn {target}",
            _ => $"nmap -sS -p- -T4 --min-rate=5000 -Pn {target}"
        };
        _commandBox.Text = command.TrimEnd();
    }

    private void PrintHelp()
    {
        _console.WriteLine("MiniMonitor 内置 Nmap 兼容扫描引擎", Color.FromArgb(103, 232, 249));
        _console.WriteLine("无需安装 Nmap / Npcap，直接在程序内执行扫描。");
        _console.WriteLine("");
        _console.WriteLine("推荐命令：");
        _console.WriteLine("  nmap -sS -p- -T4 --min-rate=5000 -Pn <目标>     快速 TCP 全端口（推荐）");
        _console.WriteLine("  nmap -sU -p- -T4 --min-rate=5000 -Pn <目标>     快速 UDP 全端口");
        _console.WriteLine("  nmap -sS -sU -p- -T4 --min-rate=5000 -Pn <目标> TCP + UDP");
        _console.WriteLine("");
        _console.WriteLine("支持参数：");
        _console.WriteLine("  -sS / -sT    TCP 扫描（内置引擎以并发连接扫描实现）");
        _console.WriteLine("  -sU          UDP 扫描");
        _console.WriteLine("  -p-          全端口 1-65535；-p 80,443 或 -p 1-1000 指定端口");
        _console.WriteLine("  -T0 ~ -T5    时序模板；--min-rate=N 最小发包速率");
        _console.WriteLine("  -Pn          跳过主机发现；-F / --top-ports 100 常用端口");
        _console.WriteLine("");
        _console.WriteLine("目标支持：IP / 域名 / CIDR（192.168.1.0/24）/ 地址范围（192.168.1.1-100）");
        _console.WriteLine("结果自动保存为 Nmap_results_日期_时间_随机数.txt。");
        _console.WriteLine("说明：内置引擎不支持 -sV / -sC / -O / -A 等版本、脚本、系统探测。");
        _console.WriteLine("");
    }

    private void OnCommandSubmitted(string line)
    {
        string command = line.Trim();
        if (command.Length == 0)
        {
            return;
        }

        string head = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        if (head is "help" or "?")
        {
            PrintHelp();
            return;
        }
        if (head is "cls" or "clear")
        {
            _console.ClearOutput();
            return;
        }
        if (head == "stop")
        {
            StopScan();
            return;
        }
        if (head == "open")
        {
            OpenResultFolder();
            return;
        }

        RunCommand(command);
    }

    private async void RunCommand(string commandText)
    {
        if (_running)
        {
            _console.WriteWarning("当前已有扫描正在运行，请先停止或等待结束。");
            return;
        }

        string command = commandText.Trim();
        if (command.Length == 0)
        {
            _console.WriteError("请输入 nmap 命令。输入 help 查看用法。");
            return;
        }
        bool startsWithNmap = command.Equals("nmap", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("nmap ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("nmap.exe", StringComparison.OrdinalIgnoreCase);
        if (!startsWithNmap && !command.StartsWith("-", StringComparison.Ordinal))
        {
            _console.WriteError("命令应以 nmap 开头，例如：nmap -sS -p- -T4 --min-rate=5000 -Pn 192.168.1.100");
            return;
        }

        NmapScanOptions options;
        try
        {
            options = NmapCommandParser.Parse(command);
        }
        catch (Exception ex)
        {
            _console.WriteError("命令解析失败：" + ex.Message);
            return;
        }

        _running = true;
        _acceptProgress = true;
        _cts = new CancellationTokenSource();
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _commandBox.Text = command;
        SetStatus("准备扫描…", ShutdownTheme.Accent);

        _console.WriteCommand(command);
        foreach (string warning in options.Warnings)
        {
            _console.WriteWarning("[提示] " + warning);
        }
        _console.WriteLine("引擎：MiniMonitor 内置扫描（TCP 连接探测 / UDP 协议探测），无需外部 Nmap 或 Npcap。");
        _console.WriteLine(
            $"参数：{(options.ScanTcp ? "TCP " : "")}{(options.ScanUdp ? "UDP" : "")}"
            + $" · 端口 {options.Ports.Count} 个 · 目标 {options.Targets.Count} 个"
            + $" · 并发 {options.Concurrency} · 超时 TCP {options.TcpTimeoutMs} ms / UDP {options.UdpTimeoutMs} ms");
        if (options.ScanUdp && options.Ports.Count > 1000)
        {
            _console.WriteWarning("[提示] UDP 扫描依赖目标响应，全端口扫描可能耗时较长，可随时点击“停止”并保留已完成结果。");
        }
        _console.WriteLine("");

        try
        {
            var progress = new Progress<NmapProgress>(OnProgress);
            var result = await NmapScanner.ScanAsync(options, progress, _cts.Token);
            _acceptProgress = false;

            foreach (string line in result.BuildReportLines())
            {
                _console.WriteLine(line);
            }

            _lastSavedPath = null;
            try
            {
                _lastSavedPath = NmapResultStorage.Save(result);
                _console.WriteSuccess("扫描结果已保存：" + _lastSavedPath);
                if (!_disposed && !IsDisposed)
                {
                    _openFolderButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                _console.WriteError("结果保存失败：" + ex.Message);
            }

            if (result.Cancelled)
            {
                SetStatus("已停止", ShutdownTheme.WarningText);
            }
            else
            {
                SetStatus($"完成 · 开放 {result.OpenPortCount} 个", ShutdownTheme.Success);
            }
        }
        catch (OperationCanceledException)
        {
            _acceptProgress = false;
            _console.WriteWarning("扫描已停止。");
            SetStatus("已停止", ShutdownTheme.WarningText);
        }
        catch (Exception ex)
        {
            _acceptProgress = false;
            _console.WriteError("扫描失败：" + ex.Message);
            SetStatus("扫描失败", ShutdownTheme.Danger);
        }
        finally
        {
            _acceptProgress = false;
            _running = false;
            if (!_disposed && !IsDisposed)
            {
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
            }
            var cts = _cts;
            _cts = null;
            try
            {
                cts?.Dispose();
            }
            catch
            {
                // 忽略释放异常
            }
        }
    }

    private void OnProgress(NmapProgress progress)
    {
        if (!_acceptProgress || _disposed || IsDisposed)
        {
            return;
        }

        if (progress.FoundOpenPort is { } found)
        {
            string latency = found.LatencyMs.HasValue ? $" ({found.LatencyMs.Value:F1} ms)" : "";
            _console.WriteLine(
                $"[发现] {found.Host} {found.PortText} {found.State} {found.Service}{latency}",
                Color.FromArgb(74, 222, 128));
        }

        double percent = progress.Total > 0
            ? progress.Done * 100.0 / progress.Total
            : 0;
        SetStatus(
            $"{progress.Phase} 扫描 {progress.Host} ({progress.HostIndex}/{progress.HostCount}) "
            + $"{percent:F1}% · 开放 {progress.OpenCount} 个",
            ShutdownTheme.Accent);
    }

    private void StopScan()
    {
        if (!_running)
        {
            _console.WriteInfo("当前没有正在运行的扫描。");
            return;
        }
        _console.WriteWarning("正在停止扫描…");
        SetStatus("正在停止…", ShutdownTheme.WarningText);
        _cts?.Cancel();
    }

    private void OpenResultFolder()
    {
        try
        {
            string folder = NmapResultStorage.GetFolder();
            Directory.CreateDirectory(folder);
            if (_lastSavedPath != null && File.Exists(_lastSavedPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastSavedPath}\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _console.WriteError("无法打开结果目录：" + ex.Message);
        }
    }

    private void SetStatus(string text, Color color)
    {
        RunOnUi(() =>
        {
            _statusLabel.Text = "● " + text;
            _statusLabel.ForeColor = color;
        });
    }

    private void RunOnUi(Action action)
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
                // 窗口已释放
            }
            catch (InvalidOperationException)
            {
                // 窗口正在关闭
            }
            return;
        }
        action();
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        if (disposing)
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // 忽略取消异常
            }
            var cts = _cts;
            _cts = null;
            try
            {
                cts?.Dispose();
            }
            catch
            {
                // 忽略释放异常
            }
            _titleFont.Dispose();
            _hintFont.Dispose();
            _statusFont.Dispose();
            _commandFont.Dispose();
            _bodyFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
