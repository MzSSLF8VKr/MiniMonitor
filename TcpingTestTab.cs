using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>TCPing 测试选项卡：内置 TCP 连接探测，不依赖 tcping.exe。</summary>
public sealed class TcpingTestTab : UserControl
{
    private readonly NetConsole _console;
    private readonly Panel _top;
    private readonly TableLayoutPanel _layout;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _hintLabel;
    private readonly PillButton _stopButton;
    private readonly PillButton _clearButton;
    private readonly Font _titleFont;
    private readonly Font _hintFont;
    private readonly Font _statusFont;

    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _disposed;

    public TcpingTestTab()
    {
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _hintFont = new Font("Microsoft YaHei UI", 8.5f);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);

        _console = new NetConsole { Dock = DockStyle.Fill };

        _top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        _titleLabel = new Label
        {
            Text = "内置 TCPing（无需安装 tcping.exe）",
            Font = _titleFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 8, 0)
        };
        _layout.Controls.Add(_titleLabel, 0, 0);

        _statusLabel = new Label
        {
            Text = "● 就绪",
            Font = _statusFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false,
            Width = 110,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 12, 0)
        };
        _layout.Controls.Add(_statusLabel, 1, 0);

        _stopButton = new PillButton
        {
            Text = "停止",
            ButtonStyle = PillButtonStyle.Danger,
            Width = 72,
            Height = 30,
            Enabled = false,
            Margin = new Padding(0, 4, 8, 4),
            Anchor = AnchorStyles.None
        };
        _stopButton.Click += (_, _) => _cts?.Cancel();
        _layout.Controls.Add(_stopButton, 2, 0);

        _clearButton = new PillButton
        {
            Text = "清屏",
            ButtonStyle = PillButtonStyle.Light,
            Width = 72,
            Height = 30,
            Margin = new Padding(0, 4, 0, 4),
            Anchor = AnchorStyles.None
        };
        _clearButton.Click += (_, _) => _console.ClearOutput();
        _layout.Controls.Add(_clearButton, 3, 0);

        _hintLabel = new Label
        {
            Text = "直接输入 <主机> [端口] [次数]（无需输入 tcping）  ·  选项：-p 端口  -n 次数  -i 间隔毫秒  -t 持续探测",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        _layout.Controls.Add(_hintLabel, 0, 1);
        _layout.SetColumnSpan(_hintLabel, 4);

        _top.Controls.Add(_layout);

        _console.CommandSubmitted += OnCommandSubmitted;

        Controls.Add(_console);
        Controls.Add(_top);

        PrintHelp();
    }

    private void PrintHelp()
    {
        _console.WriteLine("MiniMonitor 内置 TCPing", Color.FromArgb(103, 232, 249));
        _console.WriteLine("通过 TCP 连接耗时判断目标端口是否开放，无需安装 tcping.exe。");
        _console.WriteLine("");
        _console.WriteLine("用法（tcping 前缀可省略）：");
        _console.WriteLine("  <主机> [端口] [次数]");
        _console.WriteLine("  <主机> -p <端口> -n <次数> -i <间隔毫秒> [-t]");
        _console.WriteLine("");
        _console.WriteLine("示例：");
        _console.WriteLine("  192.168.1.100 80");
        _console.WriteLine("  192.168.1.100 -p 443 -n 10 -i 1000");
        _console.WriteLine("  192.168.1.100 -p 3389 -t");
        _console.WriteLine("");
        _console.WriteLine("默认端口 80，默认次数 4，默认间隔 1000 ms；-t 表示持续探测直到点击“停止”。");
        _console.WriteLine("");
    }

    private async void OnCommandSubmitted(string line)
    {
        string command = line.Trim();
        if (command.Length == 0)
        {
            return;
        }

        string head = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        if (head == "stop")
        {
            if (_running)
            {
                _cts?.Cancel();
                _console.WriteWarning("正在停止 TCPing …");
            }
            else
            {
                _console.WriteInfo("当前没有正在运行的 TCPing。");
            }
            return;
        }
        if (head == "help" || head == "?")
        {
            PrintHelp();
            return;
        }
        if (head == "cls" || head == "clear")
        {
            _console.ClearOutput();
            return;
        }

        if (_running)
        {
            _console.WriteWarning("当前已有 TCPing 正在运行，请先停止或等待结束。");
            return;
        }

        try
        {
            var request = ParseRequest(command);
            await RunPingAsync(request, command);
        }
        catch (Exception ex)
        {
            _console.WriteError(ex.Message);
        }
    }

    private TcpingRequest ParseRequest(string command)
    {
        string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int start = 0;
        if (tokens.Length > 0
            && (tokens[0].Equals("tcping", StringComparison.OrdinalIgnoreCase)
                || tokens[0].Equals("tcping.exe", StringComparison.OrdinalIgnoreCase)))
        {
            start = 1;
        }
        if (start >= tokens.Length)
        {
            throw new ArgumentException("用法：<主机> [端口] [次数]");
        }

        var positionals = new List<string>();
        string host = "";
        int port = 80;
        int count = 4;
        int interval = 1000;
        bool continuous = false;
        bool portExplicit = false;
        bool countExplicit = false;

        for (int i = start; i < tokens.Length; i++)
        {
            string token = tokens[i];
            string lower = token.ToLowerInvariant();
            if (lower is "-t" or "--continuous")
            {
                continuous = true;
                continue;
            }
            if (lower is "-p" or "--port")
            {
                if (i + 1 >= tokens.Length || !int.TryParse(tokens[++i], out port))
                {
                    throw new ArgumentException("-p 后缺少有效端口。");
                }
                portExplicit = true;
                continue;
            }
            if (lower is "-n" or "--count")
            {
                if (i + 1 >= tokens.Length || !int.TryParse(tokens[++i], out count))
                {
                    throw new ArgumentException("-n 后缺少有效次数。");
                }
                countExplicit = true;
                continue;
            }
            if (lower is "-i" or "--interval")
            {
                if (i + 1 >= tokens.Length || !int.TryParse(tokens[++i], out interval))
                {
                    throw new ArgumentException("-i 后缺少有效间隔（毫秒）。");
                }
                continue;
            }
            if (lower.StartsWith("-p", StringComparison.Ordinal) && lower.Length > 2)
            {
                if (!int.TryParse(token[2..], out port))
                {
                    throw new ArgumentException("-p 后缺少有效端口。");
                }
                portExplicit = true;
                continue;
            }
            if (lower.StartsWith("-n", StringComparison.Ordinal) && lower.Length > 2)
            {
                if (!int.TryParse(token[2..], out count))
                {
                    throw new ArgumentException("-n 后缺少有效次数。");
                }
                countExplicit = true;
                continue;
            }
            if (lower.StartsWith("-i", StringComparison.Ordinal) && lower.Length > 2)
            {
                if (!int.TryParse(token[2..], out interval))
                {
                    throw new ArgumentException("-i 后缺少有效间隔（毫秒）。");
                }
                continue;
            }
            if (lower.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"未知选项：{token}");
            }

            positionals.Add(token);
        }

        if (positionals.Count == 0)
        {
            throw new ArgumentException("用法：tcping <主机> [端口] [次数]");
        }

        host = positionals[0];
        if (positionals.Count >= 2)
        {
            if (portExplicit)
            {
                throw new ArgumentException("端口已通过 -p 指定，位置参数中不要再写端口。");
            }
            if (!int.TryParse(positionals[1], out port))
            {
                throw new ArgumentException($"无法识别的端口：{positionals[1]}");
            }
            portExplicit = true;
        }
        if (positionals.Count >= 3)
        {
            if (countExplicit)
            {
                throw new ArgumentException("次数已通过 -n 指定，位置参数中不要再写次数。");
            }
            if (!int.TryParse(positionals[2], out count))
            {
                throw new ArgumentException($"无法识别的次数：{positionals[2]}");
            }
            countExplicit = true;
        }
        if (positionals.Count > 3)
        {
            throw new ArgumentException("参数过多。用法：tcping <主机> [端口] [次数]");
        }

        if (host.Length == 0)
        {
            throw new ArgumentException("主机不能为空。");
        }
        if (port < 1 || port > 65535)
        {
            throw new ArgumentException("端口必须在 1-65535 之间。");
        }
        if (count < 1 || count > 1_000_000)
        {
            throw new ArgumentException("次数必须在 1-1000000 之间。");
        }
        if (interval < 100 || interval > 600000)
        {
            throw new ArgumentException("间隔必须在 100-600000 毫秒之间。");
        }

        return new TcpingRequest(host, port, count, interval, continuous);
    }

    private async Task RunPingAsync(TcpingRequest request, string originalCommand)
    {
        _running = true;
        _cts = new CancellationTokenSource();
        _stopButton.Enabled = true;
        SetStatus("运行中", ShutdownTheme.Accent);
        _console.WriteCommand(originalCommand);

        int attempts = 0;
        int successes = 0;
        try
        {
            var summary = await TcpingEngine.RunAsync(
                request.Host,
                request.Port,
                request.Count,
                request.IntervalMs,
                request.Continuous,
                3000,
                attempt =>
                {
                    attempts = attempt.Index;
                    if (attempt.Success)
                    {
                        successes++;
                        _console.WriteLine(
                            $"[{attempt.Index}] 来自 {attempt.TargetText} 的 TCP 回复: 时间={attempt.ElapsedMs:F1} ms",
                            Color.FromArgb(74, 222, 128));
                    }
                    else
                    {
                        _console.WriteError($"[{attempt.Index}] {attempt.TargetText} 失败: {attempt.Error}");
                    }
                    SetStatus($"{successes}/{attempts} 成功", ShutdownTheme.Accent);
                },
                info => _console.WriteInfo("[提示] " + info),
                _cts.Token);

            if (summary.Cancelled)
            {
                _console.WriteWarning("TCPing 已停止。");
            }

            _console.WriteLine("");
            _console.WriteLine($"--- TCPing 统计：{request.Host}:{request.Port} ---", Color.FromArgb(103, 232, 249));
            _console.WriteLine($"发送 {summary.Sent}，成功 {summary.Success}，失败 {summary.Failed}，丢包率 {summary.LossPercent:F1}%");
            if (summary.Success > 0)
            {
                _console.WriteLine($"最小 {summary.MinMs:F1} ms / 平均 {summary.AverageMs:F1} ms / 最大 {summary.MaxMs:F1} ms");
            }
            else
            {
                _console.WriteLine("没有任何成功连接，无法统计延迟。");
            }

            SetStatus("就绪", ShutdownTheme.Muted);
        }
        catch (Exception ex)
        {
            _console.WriteError("TCPing 失败：" + ex.Message);
            SetStatus("就绪", ShutdownTheme.Muted);
        }
        finally
        {
            _running = false;
            if (!_disposed && !IsDisposed)
            {
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
        }
        base.Dispose(disposing);
    }

    private sealed record TcpingRequest(string Host, int Port, int Count, int IntervalMs, bool Continuous);
}
