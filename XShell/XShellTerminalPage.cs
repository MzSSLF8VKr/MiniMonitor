using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>一个 XShell 会话标签页：终端 + 可选 SFTP 面板 + Compose 命令栏。</summary>
public sealed class XShellTerminalPage : UserControl
{
    private readonly XSessionProfile _profile;
    private readonly XShellStore _store;
    private readonly XShellSettings _settings;
    private readonly SplitContainer _split;
    private readonly Panel _compose;
    private readonly TextBox _composeInput;
    private XShellSftpPanel? _sftp;
    private bool _sftpActivated;

    public XShellTerminalControl Terminal { get; }
    public XSessionProfile Profile => _profile;

    public event Action<XShellTerminalPage>? StateChanged;
    public event Action<XShellTerminalPage, string>? TitleChanged;
    public event Action<XShellTerminalPage>? FullScreenRequested;

    public XShellTerminalPage(XSessionProfile profile, XShellStore store, XShellSettings settings)
    {
        _profile = profile;
        _store = store;
        _settings = settings;
        BackColor = XShellTheme.Panel;
        Padding = new Padding(0);
        DoubleBuffered = true;

        Terminal = new XShellTerminalControl(profile, store, settings)
        {
            Dock = DockStyle.Fill
        };
        Terminal.StateChanged += _ => StateChanged?.Invoke(this);
        Terminal.TitleChanged += (_, title) => TitleChanged?.Invoke(this, title);
        Terminal.FullScreenRequested += _ => FullScreenRequested?.Invoke(this);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1Collapsed = false,
            Panel2Collapsed = true,
            SplitterWidth = 5,
            BackColor = XShellTheme.Border,
            BorderStyle = BorderStyle.None
        };
        _split.Panel1.Controls.Add(Terminal);
        _split.Panel1.BackColor = XShellTheme.Panel;
        _split.Panel2.BackColor = XShellTheme.Panel;

        _compose = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 33,
            BackColor = XShellTheme.PanelAlt,
            Padding = new Padding(8, 4, 8, 4)
        };
        _compose.Paint += (_, e) =>
        {
            using var pen = new Pen(XShellTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, 0, _compose.Width, 0);
        };
        var label = new Label
        {
            Text = "撰写",
            Width = 42,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = XShellTheme.Muted,
            Font = XShellTheme.Ui(8.5f)
        };
        _composeInput = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = XShellTheme.Mono(9f),
            BackColor = XShellTheme.InputBack,
            ForeColor = XShellTheme.Text
        };
        var send = XShellTheme.FlatButton("发送", 58, 24);
        send.Dock = DockStyle.Right;
        send.Click += (_, _) => SendCompose();
        _composeInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SendCompose();
            }
        };
        _compose.Controls.Add(_composeInput);
        _compose.Controls.Add(send);
        _compose.Controls.Add(label);

        Controls.Add(_split);
        Controls.Add(_compose);
        // 撰写栏默认不开启，可在 查看 → 撰写栏 或标签右键菜单中按需打开。
        _compose.Visible = false;
        HandleCreated += (_, _) => XShellTheme.ApplyNativeTheme(this);
    }

    public Task ConnectAsync() => Terminal.ConnectAsync();

    public Task ReconnectAsync() => Terminal.ReconnectAsync();

    public void Disconnect() => Terminal.Disconnect();

    public void ApplySettings() => Terminal.ApplySettings();

    public void ToggleCompose()
    {
        _compose.Visible = !_compose.Visible;
    }

    public void ToggleSftp() => SetSftpVisible(_sftp == null || _split.Panel2Collapsed);

    public void SetSftpVisible(bool visible)
    {
        if (!visible)
        {
            _split.Panel2Collapsed = true;
            return;
        }
        if (_profile.Protocol != XProtocol.Ssh || !_profile.SftpEnabled)
        {
            XShellMessageBox.Show(FindForm(),
                _profile.Protocol == XProtocol.Ssh
                    ? "当前会话未启用 SFTP，请在会话属性中勾选“启用 SFTP 文件传输面板”。"
                    : "SFTP 仅支持 SSH 会话。",
                "XShell 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_sftp == null || _sftp.IsDisposed)
        {
            _sftp = new XShellSftpPanel(_profile, _settings, Terminal.CreateSftpClientAsync)
            {
                Dock = DockStyle.Fill
            };
            _split.Panel2.Controls.Add(_sftp);
        }
        _split.Panel2Collapsed = false;
        try
        {
            _split.SplitterDistance = Math.Max(120, _split.Height - 250);
        }
        catch
        {
            // 窗口过矮时保持默认分隔位置。
        }
        if (!_sftpActivated)
        {
            _sftpActivated = true;
            _ = _sftp.ActivateAsync();
        }
    }

    private void SendCompose()
    {
        string text = _composeInput.Text;
        if (text.Length == 0)
        {
            return;
        }
        _ = Terminal.SendComposeLineAsync(text);
        _composeInput.Clear();
        Terminal.FocusTerminal();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _sftp?.Dispose(); } catch { }
            try { Terminal.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
