using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>Telnet 测试选项卡：内置 Telnet 客户端，不依赖系统 telnet 功能。</summary>
public sealed class TelnetTestTab : UserControl
{
    private readonly TelnetSession _session = new();
    private readonly NetConsole _console;
    private readonly Panel _top;
    private readonly TableLayoutPanel _layout;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _encodingLabel;
    private readonly ComboBox _encodingCombo;
    private readonly CheckBox _localEchoCheck;
    private readonly PillButton _disconnectButton;
    private readonly PillButton _clearButton;
    private readonly Label _hintLabel;
    private readonly Font _titleFont;
    private readonly Font _hintFont;
    private readonly Font _statusFont;
    private bool _disposed;

    public TelnetTestTab()
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
            ColumnCount = 7,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        _titleLabel = new Label
        {
            Text = "内置 Telnet 客户端（无需安装 Windows telnet 功能）",
            Font = _titleFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0),
            AutoEllipsis = true
        };
        _layout.Controls.Add(_titleLabel, 0, 0);

        _statusLabel = new Label
        {
            Text = "● 未连接",
            Font = _statusFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false,
            Width = 110,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 12, 0)
        };
        _layout.Controls.Add(_statusLabel, 1, 0);

        _encodingLabel = new Label
        {
            Text = "编码",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 4, 0)
        };
        _layout.Controls.Add(_encodingLabel, 2, 0);

        _encodingCombo = new ComboBox
        {
            Width = 176,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Margin = new Padding(0, 4, 10, 4)
        };
        _encodingCombo.Items.AddRange(new object[] { "UTF-8", "GB18030（兼容 GBK / GB2312）" });
        _encodingCombo.SelectedIndex = 0;
        _encodingCombo.SelectedIndexChanged += (_, _) => ApplyEncoding();
        _layout.Controls.Add(_encodingCombo, 3, 0);

        _localEchoCheck = new CheckBox
        {
            Text = "本地回显",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 0)
        };
        _layout.Controls.Add(_localEchoCheck, 4, 0);

        _disconnectButton = new PillButton
        {
            Text = "断开",
            ButtonStyle = PillButtonStyle.Light,
            Width = 72,
            Height = 30,
            Enabled = false,
            Margin = new Padding(0, 4, 8, 4),
            Anchor = AnchorStyles.None
        };
        _disconnectButton.Click += (_, _) => _session.Disconnect("已主动断开连接。");
        _layout.Controls.Add(_disconnectButton, 5, 0);

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
        _layout.Controls.Add(_clearButton, 6, 0);

        _hintLabel = new Label
        {
            Text = "直接输入 <主机> [端口] 回车连接（无需输入 telnet）  ·  默认端口 23  ·  连接后输入内容发送  ·  Ctrl+] 断开",
            Font = _hintFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        _layout.Controls.Add(_hintLabel, 0, 1);
        _layout.SetColumnSpan(_hintLabel, 7);

        _top.Controls.Add(_layout);

        _console.CommandSubmitted += OnCommandSubmitted;
        _console.CtrlCloseBracketPressed += (_, _) =>
        {
            if (_session.IsConnected)
            {
                _session.Disconnect("已通过 Ctrl+] 断开连接。");
            }
        };

        Controls.Add(_console);
        Controls.Add(_top);

        _session.TextReceived += OnTextReceived;
        _session.Closed += OnSessionClosed;

        PrintHelp();
    }

    private void PrintHelp()
    {
        _console.WriteLine("MiniMonitor 内置 Telnet 客户端", Color.FromArgb(103, 232, 249));
        _console.WriteLine("无需安装 Windows 的 telnet 客户端功能，直接使用 TCP + Telnet 协议连接。");
        _console.WriteLine("");
        _console.WriteLine("用法：");
        _console.WriteLine("  <主机> [端口]           直接连接（默认端口 23，无需输入 telnet）");
        _console.WriteLine("  telnet <主机> [端口]    兼容写法");
        _console.WriteLine("  close                   断开当前连接");
        _console.WriteLine("  cls                     清屏");
        _console.WriteLine("  help                    显示本帮助");
        _console.WriteLine("");
        _console.WriteLine("示例：");
        _console.WriteLine("  192.168.1.100");
        _console.WriteLine("  192.168.1.100 2323");
        _console.WriteLine("");
        _console.WriteLine("连接成功后直接输入内容并回车发送；按 Ctrl+] 或点击“断开”结束会话。");
        _console.WriteLine("部分服务端不回显输入时可勾选“本地回显”；中文乱码可切换编码为 GB18030。");
        _console.WriteLine("");
    }

    private async void OnCommandSubmitted(string line)
    {
        try
        {
            if (_session.IsConnected)
            {
                if (_localEchoCheck.Checked && line.Length > 0)
                {
                    _console.WriteLine(line, ShutdownTheme.Muted);
                }
                _session.SendLine(line);
                return;
            }

            string command = line.Trim();
            if (command.Length == 0)
            {
                return;
            }

            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string head = parts[0].ToLowerInvariant();
            switch (head)
            {
                case "telnet":
                case "open":
                    await ConnectFromParts(parts, 1);
                    return;
                case "close":
                case "disconnect":
                case "quit":
                case "exit":
                    _console.WriteInfo("当前没有 Telnet 连接。直接输入 <主机> [端口] 即可连接。");
                    return;
                case "help":
                case "?":
                    PrintHelp();
                    return;
                case "cls":
                case "clear":
                    _console.ClearOutput();
                    return;
                default:
                    // 不写 telnet 前缀，直接输入主机和端口
                    await ConnectFromParts(parts, 0);
                    return;
            }
        }
        catch (Exception ex)
        {
            _console.WriteError(ex.Message);
            SetStatus("未连接", ShutdownTheme.Muted);
            _console.PromptText = "telnet> ";
            if (!_disposed && !IsDisposed)
            {
                _disconnectButton.Enabled = false;
            }
        }
    }

    private async Task ConnectFromParts(string[] parts, int start)
    {
        if (parts.Length <= start)
        {
            _console.WriteError("用法：<主机> [端口]，例如 192.168.1.100 23");
            return;
        }

        string host = parts[start];
        int port = 23;
        if (parts.Length > start + 1 && !int.TryParse(parts[start + 1], out port))
        {
            _console.WriteError("端口必须是 1-65535 的整数。");
            return;
        }
        if (parts.Length > start + 2)
        {
            _console.WriteError("参数过多。用法：<主机> [端口]");
            return;
        }
        if (port < 1 || port > 65535)
        {
            _console.WriteError("端口必须在 1-65535 之间。");
            return;
        }

        await ConnectAsync(host, port);
    }

    private async Task ConnectAsync(string host, int port)
    {
        SetStatus("正在连接…", ShutdownTheme.WarningText);
        _console.WriteLine($"正在连接 {host}:{port} …", ShutdownTheme.Muted);
        try
        {
            _session.Encoding = GetSelectedEncoding();
            await _session.ConnectAsync(host, port, 10000);
            if (_disposed || IsDisposed)
            {
                _session.Disconnect("Telnet 窗口已关闭。");
                return;
            }
            _console.WriteSuccess($"已连接到 {host}:{port}（远程地址 {_session.RemoteAddress}）");
            _console.WriteInfo("Telnet 协议协商已自动处理。输入内容并回车发送，Ctrl+] 断开。");
            _console.PromptText = $"{host}:{port}> ";
            _disconnectButton.Enabled = true;
            SetStatus("已连接", ShutdownTheme.Success);
            _console.FocusInput();
        }
        catch (Exception ex)
        {
            _console.WriteError("连接失败：" + ex.Message);
            SetStatus("未连接", ShutdownTheme.Muted);
            _console.PromptText = "telnet> ";
            if (!_disposed && !IsDisposed)
            {
                _disconnectButton.Enabled = false;
            }
        }
    }

    private Encoding GetSelectedEncoding()
    {
        if (_encodingCombo.SelectedIndex == 1)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030");
        }
        return new UTF8Encoding(false);
    }

    private void ApplyEncoding()
    {
        if (_session.IsConnected)
        {
            try
            {
                _session.Encoding = GetSelectedEncoding();
                _console.WriteInfo("编码已切换为 " + _encodingCombo.SelectedItem);
            }
            catch (Exception ex)
            {
                _console.WriteError("切换编码失败：" + ex.Message);
            }
        }
    }

    private void OnTextReceived(string text)
    {
        _console.AppendTerminal(text);
    }

    private void OnSessionClosed(string reason)
    {
        RunOnUi(() =>
        {
            _disconnectButton.Enabled = false;
            _console.PromptText = "telnet> ";
            SetStatus("未连接", ShutdownTheme.Muted);
            _console.WriteWarning("[连接已关闭] " + reason);
        });
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
            _session.TextReceived -= OnTextReceived;
            _session.Closed -= OnSessionClosed;
            _session.Dispose();
            _titleFont.Dispose();
            _hintFont.Dispose();
            _statusFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
