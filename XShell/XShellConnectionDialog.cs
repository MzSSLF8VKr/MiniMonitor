using System.IO.Ports;
using System.Net.Sockets;
using System.Windows.Forms;
using Renci.SshNet;

namespace MiniMonitor.XShell;

/// <summary>XShell 会话属性对话框：连接、认证、终端、SFTP、串口、本地 Shell、跳板机、隧道。</summary>
public sealed class XShellConnectionDialog : Form
{
    private readonly XShellStore _store;
    private readonly XSessionProfile _edit;
    private readonly ListBox _nav;
    private readonly Panel _content;
    private readonly Dictionary<string, Panel> _pages = new();
    private readonly TextBox _name;
    private readonly ComboBox _protocol;
    private readonly TextBox _host;
    private readonly NumericUpDown _port;
    private readonly TextBox _user;
    private readonly ComboBox _group;
    private readonly ComboBox _auth;
    private readonly TextBox _password;
    private readonly TextBox _keyPath;
    private readonly TextBox _passphrase;
    private readonly ComboBox _terminalType;
    private readonly ComboBox _encoding;
    private readonly ComboBox _palette;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _scrollback;
    private readonly NumericUpDown _columns;
    private readonly NumericUpDown _rows;
    private readonly CheckBox _logSession;
    private readonly TextBox _logFolder;
    private readonly CheckBox _sftpEnabled;
    private readonly TextBox _remoteStart;
    private readonly ComboBox _serialPort;
    private readonly ComboBox _baud;
    private readonly NumericUpDown _dataBits;
    private readonly ComboBox _parity;
    private readonly ComboBox _stopBits;
    private readonly ComboBox _handshake;
    private readonly CheckBox _dtr;
    private readonly CheckBox _rts;
    private readonly TextBox _localCommand;
    private readonly TextBox _localArgs;
    private readonly CheckBox _useJump;
    private readonly TextBox _jumpHost;
    private readonly NumericUpDown _jumpPort;
    private readonly TextBox _jumpUser;
    private readonly TextBox _jumpPassword;
    private readonly TextBox _jumpKey;
    private readonly DataGridView _tunnels;
    private readonly List<XTunnelRule> _tunnelRules;

    public XSessionProfile Profile { get; private set; }

    public XShellConnectionDialog(XSessionProfile profile, XShellStore store)
    {
        _store = store;
        _edit = profile;
        _tunnelRules = profile.Tunnels.Select(t => t.Clone()).ToList();
        Profile = profile;

        Text = "会话属性 - " + profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(860, 620);
        ClientSize = new Size(900, 650);
        BackColor = XShellTheme.Background;
        ForeColor = XShellTheme.Text;
        Font = XShellTheme.Ui(9f);
        ShowInTaskbar = false;
        MaximizeBox = true;
        MinimizeBox = false;
        HandleCreated += (_, _) => XShellWindowTheme.Apply(this);

        var header = new XShellHeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 64,
            Title = "会话属性",
            Subtitle = "SSH / Telnet / Serial / Local Shell  ·  XShell 复刻版"
        };
        Controls.Add(header);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = XShellTheme.PanelAlt,
            Padding = new Padding(0, 11, 16, 11)
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(XShellTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var ok = XShellTheme.PrimaryButton("确定", 88, 30);
        ok.Dock = DockStyle.Right;
        ok.Margin = new Padding(8, 0, 0, 0);
        ok.Click += (_, _) => SaveAndClose();
        var cancel = XShellTheme.FlatButton("取消", 88, 30);
        cancel.Dock = DockStyle.Right;
        cancel.Margin = new Padding(8, 0, 0, 0);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var test = XShellTheme.FlatButton("测试连接", 100, 30);
        test.Dock = DockStyle.Right;
        test.Click += (_, _) => TestConnection();
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);
        footer.Controls.Add(test);
        Controls.Add(footer);

        _nav = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 166,
            BorderStyle = BorderStyle.None,
            BackColor = XShellTheme.PanelAlt,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(9.2f),
            ItemHeight = 38,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed
        };
        _nav.DrawItem += DrawNavItem;
        _nav.SelectedIndexChanged += (_, _) => ShowPage();
        Controls.Add(_nav);

        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = XShellTheme.Panel,
            Padding = new Padding(0)
        };
        Controls.Add(_content);
        _content.BringToFront();

        _name = XShellTheme.Input(300);
        _protocol = XShellTheme.Combo(220);
        _protocol.Items.AddRange(new object[] { "SSH", "Telnet", "Serial", "Local Shell" });
        _host = XShellTheme.Input(300);
        _port = Number(1, 65535, 22);
        _user = XShellTheme.Input(300);
        _group = XShellTheme.Combo(220);
        _group.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (string group in store.AllGroups())
        {
            _group.Items.Add(group);
        }
        if (_group.Items.Count == 0)
        {
            _group.Items.Add("默认");
        }

        _auth = XShellTheme.Combo(220);
        _auth.Items.AddRange(new object[] { "密码", "公钥", "键盘交互", "无" });
        _password = PasswordInput(300);
        _keyPath = XShellTheme.Input(300);
        _passphrase = PasswordInput(300);

        _terminalType = XShellTheme.Combo(220);
        _terminalType.DropDownStyle = ComboBoxStyle.DropDown;
        _terminalType.Items.AddRange(new object[] { "xterm-256color", "xterm", "vt100", "linux" });
        _encoding = XShellTheme.Combo(220);
        _encoding.DropDownStyle = ComboBoxStyle.DropDown;
        _encoding.Items.AddRange(new object[] { "UTF-8", "GB18030", "GBK", "Big5", "Shift-JIS" });
        _palette = XShellTheme.Combo(220);
        foreach (var palette in XShellPalettes.All)
        {
            _palette.Items.Add(palette.Name);
        }
        _fontSize = Number(8, 32, 16);
        _scrollback = Number(500, 200000, 10000, 100);
        _columns = Number(20, 500, 120);
        _rows = Number(5, 300, 32);
        _logSession = XShellTheme.CheckBox("记录会话日志");
        _logSession.AutoSize = true;
        _logFolder = XShellTheme.Input(300);

        _sftpEnabled = XShellTheme.CheckBox("启用 SFTP 文件传输面板");
        _sftpEnabled.AutoSize = true;
        _remoteStart = XShellTheme.Input(300);

        _serialPort = XShellTheme.Combo(180);
        _serialPort.DropDownStyle = ComboBoxStyle.DropDown;
        try
        {
            foreach (string port in SerialPort.GetPortNames().OrderBy(x => x))
            {
                _serialPort.Items.Add(port);
            }
        }
        catch
        {
            // 无串口设备
        }
        _baud = XShellTheme.Combo(180);
        _baud.DropDownStyle = ComboBoxStyle.DropDown;
        _baud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" });
        _dataBits = Number(5, 8, 8);
        _parity = XShellTheme.Combo(180);
        _parity.Items.AddRange(new object[] { "None", "Odd", "Even", "Mark", "Space" });
        _stopBits = XShellTheme.Combo(180);
        _stopBits.Items.AddRange(new object[] { "One", "OnePointFive", "Two" });
        _handshake = XShellTheme.Combo(180);
        _handshake.Items.AddRange(new object[] { "None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff" });
        _dtr = XShellTheme.CheckBox("DTR");
        _dtr.AutoSize = true;
        _rts = XShellTheme.CheckBox("RTS");
        _rts.AutoSize = true;

        _localCommand = XShellTheme.Input(300);
        _localArgs = XShellTheme.Input(300);

        _useJump = XShellTheme.CheckBox("通过 SSH 跳板机连接");
        _useJump.AutoSize = true;
        _jumpHost = XShellTheme.Input(300);
        _jumpPort = Number(1, 65535, 22);
        _jumpUser = XShellTheme.Input(300);
        _jumpPassword = PasswordInput(300);
        _jumpKey = XShellTheme.Input(300);

        _tunnels = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoGenerateColumns = false
        };
        XShellTheme.StyleGrid(_tunnels);
        _tunnels.AllowUserToAddRows = true;
        _tunnels.AllowUserToDeleteRows = true;
        _tunnels.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "名称", DataPropertyName = "Name", FillWeight = 18
        });
        _tunnels.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "类型",
            DataPropertyName = "Type",
            DataSource = Enum.GetValues<XTunnelType>(),
            FillWeight = 12
        });
        _tunnels.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "监听地址", DataPropertyName = "BindHost", FillWeight = 16
        });
        _tunnels.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "监听端口", DataPropertyName = "BindPort", FillWeight = 12
        });
        _tunnels.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "目标地址", DataPropertyName = "TargetHost", FillWeight = 16
        });
        _tunnels.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "目标端口", DataPropertyName = "TargetPort", FillWeight = 12
        });
        _tunnels.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "启用", DataPropertyName = "Enabled", FillWeight = 8,
            FlatStyle = FlatStyle.Flat
        });
        _tunnels.DataSource = new BindingSource { DataSource = _tunnelRules };
        _tunnels.DataError += (_, _) => { };

        BuildPages();
        LoadValues();
        _nav.SelectedIndex = 0;
        _protocol.SelectedIndexChanged += (_, _) => ApplyProtocolState();
        _auth.SelectedIndexChanged += (_, _) => ApplyAuthState();
        _protocol.SelectedIndex = _protocol.Items.IndexOf("SSH");
        ApplyProtocolState();
        ApplyAuthState();

        // 顶部标题栏横跨整个窗口，底部按钮栏同理；左侧导航与右侧内容占据中间区域。
        Controls.SetChildIndex(_content, 0);
        Controls.SetChildIndex(_nav, 1);
        Controls.SetChildIndex(footer, 2);
        Controls.SetChildIndex(header, 3);

        XShellTheme.StyleTextControls(this);
        _nav.BackColor = XShellTheme.PanelAlt;
        _nav.ForeColor = XShellTheme.Text;
        _content.BackColor = XShellTheme.Panel;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        XShellTheme.ApplyNativeTheme(this);
    }

    private static NumericUpDown Number(int min, int max, int value, int increment = 1)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Increment = increment,
            Width = 110,
            Font = XShellTheme.Ui(9f),
            ThousandsSeparator = false
        };
    }

    private static TextBox PasswordInput(int width)
    {
        var box = XShellTheme.Input(width);
        box.UseSystemPasswordChar = true;
        return box;
    }

    private void BuildPages()
    {
        AddPage("连接", BuildConnectionPage());
        AddPage("身份验证", BuildAuthPage());
        AddPage("终端", BuildTerminalPage());
        AddPage("SFTP", BuildSftpPage());
        AddPage("串口", BuildSerialPage());
        AddPage("本地 Shell", BuildLocalPage());
        AddPage("跳板机", BuildJumpPage());
        AddPage("隧道", BuildTunnelPage());
    }

    private void AddPage(string name, Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        page.AutoScroll = true;
        page.BackColor = XShellTheme.Panel;
        page.Padding = new Padding(26, 18, 26, 18);
        _pages[name] = page;
        _nav.Items.Add(name);
        _content.Controls.Add(page);
    }

    private void ShowPage()
    {
        if (_nav.SelectedItem is not string name || !_pages.TryGetValue(name, out var page))
        {
            return;
        }
        foreach (var pair in _pages)
        {
            pair.Value.Visible = pair.Key == name;
        }
        page.BringToFront();
    }

    private void DrawNavItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(XShellTheme.PanelAlt))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }
        if (selected)
        {
            var rect = new Rectangle(e.Bounds.Left + 7, e.Bounds.Top + 5,
                e.Bounds.Width - 14, e.Bounds.Height - 10);
            using var path = XShellTheme.RoundedRect(rect, 8);
            using var brush = new SolidBrush(XShellTheme.AccentLight);
            e.Graphics.FillPath(brush, path);
            using var accent = new SolidBrush(XShellTheme.Accent);
            e.Graphics.FillRectangle(accent, rect.Left, rect.Top + 6, 3, rect.Height - 12);
        }
        string text = _nav.Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, _nav.Font,
            new Rectangle(e.Bounds.Left + 22, e.Bounds.Top, e.Bounds.Width - 26, e.Bounds.Height),
            selected ? XShellTheme.Accent : XShellTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private Panel NewPage(string title, string subtitle)
    {
        var page = new Panel();
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 30,
            Font = XShellTheme.Ui(12f, FontStyle.Bold),
            ForeColor = XShellTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subLabel = new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 24,
            Font = XShellTheme.Ui(8.6f),
            ForeColor = XShellTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        page.Controls.Add(subLabel);
        page.Controls.Add(titleLabel);
        return page;
    }

    private TableLayoutPanel FormTable(DockStyle dock = DockStyle.Top)
    {
        var table = new TableLayoutPanel
        {
            Dock = dock,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = XShellTheme.Panel
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return table;
    }

    private static void AddField(TableLayoutPanel table, string label, Control control)
    {
        var caption = new Label
        {
            Text = label,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = XShellTheme.Text,
            Margin = new Padding(0, 4, 8, 4)
        };
        control.Margin = new Padding(0, 4, 0, 4);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(caption);
        table.Controls.Add(control);
    }

    private Panel BuildConnectionPage()
    {
        var page = NewPage("连接", "选择协议并填写目标主机；Serial / Local Shell 不需要主机和端口。");
        var table = FormTable();
        AddField(table, "会话名称", _name);
        AddField(table, "协议", _protocol);
        AddField(table, "主机", _host);
        AddField(table, "端口", _port);
        AddField(table, "用户名", _user);
        AddField(table, "分组", _group);
        page.Controls.Add(table);
        return page;
    }

    private Panel BuildAuthPage()
    {
        var page = NewPage("身份验证", "支持密码、OpenSSH / PEM 私钥与键盘交互认证；密码使用 Windows DPAPI 加密保存。");
        var table = FormTable();
        AddField(table, "认证方式", _auth);
        AddField(table, "密码", _password);
        var keyPanel = new Panel
        {
            Height = 30,
            Width = 460,
            BackColor = XShellTheme.Panel
        };
        _keyPath.Dock = DockStyle.Fill;
        var browse = XShellTheme.FlatButton("浏览...", 70, 27);
        browse.Dock = DockStyle.Right;
        browse.Click += (_, _) => BrowseKey();
        keyPanel.Controls.Add(_keyPath);
        keyPanel.Controls.Add(browse);
        AddField(table, "私钥文件", keyPanel);
        AddField(table, "私钥口令", _passphrase);
        page.Controls.Add(table);
        return page;
    }

    private Panel BuildTerminalPage()
    {
        var page = NewPage("终端", "终端仿真、编码、配色、字体和回滚缓冲区；日志可保存到指定目录。");
        var table = FormTable();
        AddField(table, "终端类型", _terminalType);
        AddField(table, "字符编码", _encoding);
        AddField(table, "配色方案", _palette);
        AddField(table, "字号", _fontSize);
        AddField(table, "回滚行数", _scrollback);
        AddField(table, "初始列数", _columns);
        AddField(table, "初始行数", _rows);
        AddField(table, "会话日志", _logSession);
        AddField(table, "日志目录", _logFolder);
        page.Controls.Add(table);
        return page;
    }

    private Panel BuildSftpPage()
    {
        var page = NewPage("SFTP", "连接后按需打开 SFTP 面板，支持拖拽上传、下载、重命名和删除。");
        var table = FormTable();
        AddField(table, "SFTP", _sftpEnabled);
        AddField(table, "远程起始目录", _remoteStart);
        page.Controls.Add(table);
        return page;
    }

    private Panel BuildSerialPage()
    {
        var page = NewPage("串口", "使用 Windows 原生串口驱动，无需安装额外组件。");
        var table = FormTable();
        AddField(table, "串口", _serialPort);
        AddField(table, "波特率", _baud);
        AddField(table, "数据位", _dataBits);
        AddField(table, "校验位", _parity);
        AddField(table, "停止位", _stopBits);
        AddField(table, "流控制", _handshake);
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = XShellTheme.Panel
        };
        flow.Controls.Add(_dtr);
        flow.Controls.Add(_rts);
        AddField(table, "信号线", flow);
        page.Controls.Add(table);
        return page;
    }

    private Panel BuildLocalPage()
    {
        var page = NewPage("本地 Shell", "直接启动 PowerShell、cmd 或 wsl.exe；输出使用 UTF-8。");
        var table = FormTable();
        AddField(table, "命令", _localCommand);
        AddField(table, "参数", _localArgs);
        var presets = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = XShellTheme.Panel
        };
        var ps = XShellTheme.FlatButton("PowerShell", 90, 26);
        ps.Click += (_, _) => { _localCommand.Text = "powershell.exe"; _localArgs.Text = "-NoLogo"; };
        var cmd = XShellTheme.FlatButton("cmd", 70, 26);
        cmd.Click += (_, _) => { _localCommand.Text = "cmd.exe"; _localArgs.Text = ""; };
        var wsl = XShellTheme.FlatButton("WSL", 70, 26);
        wsl.Click += (_, _) => { _localCommand.Text = "wsl.exe"; _localArgs.Text = ""; };
        presets.Controls.AddRange(new Control[] { ps, cmd, wsl });
        AddField(table, "快速选择", presets);
        page.Controls.Add(table);
        return page;
    }

    private Panel BuildJumpPage()
    {
        var page = NewPage("跳板机", "先连接跳板机，再通过本地端口转发连接目标 SSH 主机。");
        var table = FormTable();
        AddField(table, "跳板机", _useJump);
        AddField(table, "跳板机主机", _jumpHost);
        AddField(table, "跳板机端口", _jumpPort);
        AddField(table, "跳板机用户", _jumpUser);
        AddField(table, "跳板机密码", _jumpPassword);
        AddField(table, "跳板机私钥", _jumpKey);
        page.Controls.Add(table);
        return page;
    }

    private Panel BuildTunnelPage()
    {
        var page = NewPage("隧道", "连接 SSH 后自动启动启用的端口转发规则；本地 / 远程 / 动态 SOCKS。");
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = XShellTheme.PanelAlt
        };
        var add = XShellTheme.FlatButton("添加规则", 90, 27);
        add.Location = new Point(0, 3);
        add.Click += (_, _) =>
        {
            _tunnelRules.Add(new XTunnelRule
            {
                Name = "转发 " + (_tunnelRules.Count + 1),
                BindHost = "127.0.0.1",
                BindPort = 8080,
                TargetHost = "127.0.0.1",
                TargetPort = 80
            });
            _tunnels.DataSource = new BindingSource { DataSource = null };
            _tunnels.DataSource = new BindingSource { DataSource = _tunnelRules };
        };
        var remove = XShellTheme.FlatButton("删除规则", 90, 27);
        remove.Location = new Point(98, 3);
        remove.Click += (_, _) =>
        {
            if (_tunnels.CurrentRow?.DataBoundItem is XTunnelRule rule)
            {
                _tunnelRules.Remove(rule);
                _tunnels.DataSource = new BindingSource { DataSource = null };
                _tunnels.DataSource = new BindingSource { DataSource = _tunnelRules };
            }
        };
        toolbar.Controls.Add(add);
        toolbar.Controls.Add(remove);
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = XShellTheme.Panel
        };
        host.Controls.Add(_tunnels);
        page.Controls.Add(host);
        page.Controls.Add(toolbar);
        return page;
    }

    private void LoadValues()
    {
        _name.Text = _edit.Name;
        _protocol.SelectedItem = _edit.Protocol switch
        {
            XProtocol.Ssh => "SSH",
            XProtocol.Telnet => "Telnet",
            XProtocol.Serial => "Serial",
            XProtocol.Local => "Local Shell",
            _ => "SSH"
        };
        _host.Text = _edit.Host;
        _port.Value = Math.Clamp(_edit.Port, 1, 65535);
        _user.Text = _edit.UserName;
        _group.Text = _edit.Group;
        _auth.SelectedIndex = _edit.AuthMethod switch
        {
            XAuthMethod.Password => 0,
            XAuthMethod.PrivateKey => 1,
            XAuthMethod.KeyboardInteractive => 2,
            _ => 3
        };
        _password.Text = _edit.Password;
        _keyPath.Text = _edit.PrivateKeyPath;
        _passphrase.Text = _edit.PrivateKeyPassphrase;
        _terminalType.SelectedItem = _edit.TerminalType;
        if (_terminalType.SelectedIndex < 0)
        {
            _terminalType.Text = _edit.TerminalType;
        }
        _encoding.SelectedItem = _edit.EncodingName;
        if (_encoding.SelectedIndex < 0)
        {
            _encoding.Text = _edit.EncodingName;
        }
        _palette.SelectedItem = _edit.ColorScheme;
        if (_palette.SelectedIndex < 0)
        {
            _palette.SelectedIndex = 0;
        }
        _fontSize.Value = Math.Clamp(_edit.FontSize, 8, 32);
        _scrollback.Value = Math.Clamp(_edit.Scrollback, 500, 200000);
        _columns.Value = Math.Clamp(_edit.Columns, 20, 500);
        _rows.Value = Math.Clamp(_edit.Rows, 5, 300);
        _logSession.Checked = _edit.LogSession;
        _logFolder.Text = _edit.LogFolder;
        _sftpEnabled.Checked = _edit.SftpEnabled;
        _remoteStart.Text = _edit.RemoteStartFolder;
        if (!_serialPort.Items.Contains(_edit.SerialPortName))
        {
            _serialPort.Items.Add(_edit.SerialPortName);
        }
        _serialPort.SelectedItem = _edit.SerialPortName;
        if (_baud.Items.Contains(_edit.BaudRate.ToString()))
        {
            _baud.SelectedItem = _edit.BaudRate.ToString();
        }
        else
        {
            _baud.Text = _edit.BaudRate.ToString();
        }
        _dataBits.Value = Math.Clamp(_edit.DataBits, 5, 8);
        _parity.SelectedItem = _edit.Parity.ToString();
        _stopBits.SelectedItem = _edit.StopBits.ToString();
        _handshake.SelectedItem = _edit.Handshake.ToString();
        _dtr.Checked = _edit.DtrEnable;
        _rts.Checked = _edit.RtsEnable;
        _localCommand.Text = _edit.LocalCommand;
        _localArgs.Text = _edit.LocalArguments;
        _useJump.Checked = _edit.UseJumpHost;
        _jumpHost.Text = _edit.JumpHost;
        _jumpPort.Value = Math.Clamp(_edit.JumpPort, 1, 65535);
        _jumpUser.Text = _edit.JumpUserName;
        _jumpPassword.Text = _edit.JumpPassword;
        _jumpKey.Text = _edit.JumpPrivateKeyPath;
    }

    private void ApplyProtocolState()
    {
        var protocol = SelectedProtocol();
        bool network = protocol is XProtocol.Ssh or XProtocol.Telnet;
        _host.Enabled = network;
        _port.Enabled = network;
        _user.Enabled = protocol == XProtocol.Ssh;
        _auth.Enabled = protocol == XProtocol.Ssh;
        if (protocol == XProtocol.Telnet && _port.Value == 22)
        {
            _port.Value = 23;
        }
        else if (protocol == XProtocol.Ssh && _port.Value == 23)
        {
            _port.Value = 22;
        }
        if (protocol == XProtocol.Local)
        {
            _host.Text = "";
        }
        if (protocol == XProtocol.Serial)
        {
            _sftpEnabled.Checked = false;
        }
    }

    private void ApplyAuthState()
    {
        bool key = _auth.SelectedIndex == 1;
        _keyPath.Enabled = key;
        _passphrase.Enabled = key;
        _password.Enabled = !key && _auth.SelectedIndex != 3;
    }

    private XProtocol SelectedProtocol() => _protocol.SelectedIndex switch
    {
        1 => XProtocol.Telnet,
        2 => XProtocol.Serial,
        3 => XProtocol.Local,
        _ => XProtocol.Ssh
    };

    private void BrowseKey()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择私钥文件",
            Filter = "私钥文件 (*.pem;*.key;*.ppk;*)|*.pem;*.key;*.ppk;*|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _keyPath.Text = dialog.FileName;
        }
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            XShellMessageBox.Show(this, "请输入会话名称。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nav.SelectedIndex = 0;
            _name.Focus();
            return;
        }
        var protocol = SelectedProtocol();
        if (protocol is XProtocol.Ssh or XProtocol.Telnet
            && string.IsNullOrWhiteSpace(_host.Text))
        {
            XShellMessageBox.Show(this, "请输入主机地址。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nav.SelectedIndex = 0;
            _host.Focus();
            return;
        }
        if (protocol == XProtocol.Serial && string.IsNullOrWhiteSpace(_serialPort.Text))
        {
            XShellMessageBox.Show(this, "请选择串口。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nav.SelectedIndex = 4;
            return;
        }
        if (protocol == XProtocol.Local && string.IsNullOrWhiteSpace(_localCommand.Text))
        {
            XShellMessageBox.Show(this, "请输入本地命令。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nav.SelectedIndex = 5;
            return;
        }

        _tunnels.EndEdit();
        _edit.Name = _name.Text.Trim();
        _edit.Group = string.IsNullOrWhiteSpace(_group.Text) ? "默认" : _group.Text.Trim();
        _edit.Protocol = protocol;
        _edit.Host = _host.Text.Trim();
        _edit.Port = (int)_port.Value;
        _edit.UserName = _user.Text.Trim();
        _edit.AuthMethod = _auth.SelectedIndex switch
        {
            1 => XAuthMethod.PrivateKey,
            2 => XAuthMethod.KeyboardInteractive,
            3 => XAuthMethod.None,
            _ => XAuthMethod.Password
        };
        _edit.Password = _password.Text;
        _edit.PrivateKeyPath = _keyPath.Text.Trim();
        _edit.PrivateKeyPassphrase = _passphrase.Text;
        _edit.TerminalType = string.IsNullOrWhiteSpace(_terminalType.Text)
            ? "xterm-256color" : _terminalType.Text;
        _edit.EncodingName = string.IsNullOrWhiteSpace(_encoding.Text)
            ? "UTF-8" : _encoding.Text;
        _edit.ColorScheme = _palette.SelectedItem?.ToString() ?? "XShell 经典";
        _edit.FontSize = (int)_fontSize.Value;
        _edit.Scrollback = (int)_scrollback.Value;
        _edit.Columns = (int)_columns.Value;
        _edit.Rows = (int)_rows.Value;
        _edit.LogSession = _logSession.Checked;
        _edit.LogFolder = _logFolder.Text.Trim();
        _edit.SftpEnabled = _sftpEnabled.Checked;
        _edit.RemoteStartFolder = _remoteStart.Text.Trim();
        _edit.SerialPortName = _serialPort.Text.Trim();
        _edit.BaudRate = int.TryParse(_baud.Text, out int baud) ? baud : 115200;
        _edit.DataBits = (int)_dataBits.Value;
        _edit.Parity = Enum.TryParse(_parity.Text, out Parity parity) ? parity : Parity.None;
        _edit.StopBits = Enum.TryParse(_stopBits.Text, out StopBits stop) ? stop : StopBits.One;
        _edit.Handshake = Enum.TryParse(_handshake.Text, out Handshake handshake) ? handshake : Handshake.None;
        _edit.DtrEnable = _dtr.Checked;
        _edit.RtsEnable = _rts.Checked;
        _edit.LocalCommand = _localCommand.Text.Trim();
        _edit.LocalArguments = _localArgs.Text.Trim();
        _edit.UseJumpHost = _useJump.Checked;
        _edit.JumpHost = _jumpHost.Text.Trim();
        _edit.JumpPort = (int)_jumpPort.Value;
        _edit.JumpUserName = _jumpUser.Text.Trim();
        _edit.JumpPassword = _jumpPassword.Text;
        _edit.JumpPrivateKeyPath = _jumpKey.Text.Trim();
        _edit.Tunnels = _tunnelRules.Where(t => !string.IsNullOrWhiteSpace(t.Name)).ToList();
        _edit.Normalize();
        Profile = _edit;
        _store.AddOrUpdate(_edit);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void TestConnection()
    {
        var profile = _edit.Clone();
        profile.Name = _name.Text.Trim();
        profile.Protocol = SelectedProtocol();
        profile.Host = _host.Text.Trim();
        profile.Port = (int)_port.Value;
        profile.UserName = _user.Text.Trim();
        profile.Password = _password.Text;
        profile.PrivateKeyPath = _keyPath.Text.Trim();
        profile.PrivateKeyPassphrase = _passphrase.Text;
        profile.SerialPortName = _serialPort.Text.Trim();
        profile.BaudRate = int.TryParse(_baud.Text, out int baud) ? baud : 115200;
        profile.LocalCommand = _localCommand.Text.Trim();
        profile.LocalArguments = _localArgs.Text.Trim();
        profile.AuthMethod = _auth.SelectedIndex switch
        {
            1 => XAuthMethod.PrivateKey,
            2 => XAuthMethod.KeyboardInteractive,
            3 => XAuthMethod.None,
            _ => XAuthMethod.Password
        };
        try
        {
            switch (profile.Protocol)
            {
                case XProtocol.Ssh:
                    TestSsh(profile);
                    break;
                case XProtocol.Telnet:
                    TestTcp(profile);
                    break;
                case XProtocol.Serial:
                    TestSerial(profile);
                    break;
                default:
                    XShellMessageBox.Show(this,
                        "本地 Shell 会在会话连接时启动，请直接保存并连接验证。",
                        "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }
        catch (Exception ex)
        {
            XShellMessageBox.Show(this, "连接失败：\r\n\r\n" + ex.Message, "测试连接",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TestSsh(XSessionProfile profile)
    {
        var methods = new List<AuthenticationMethod>();
        if (profile.AuthMethod == XAuthMethod.PrivateKey)
        {
            if (!File.Exists(profile.PrivateKeyPath))
            {
                throw new FileNotFoundException("找不到私钥文件。", profile.PrivateKeyPath);
            }
            var key = string.IsNullOrEmpty(profile.PrivateKeyPassphrase)
                ? new PrivateKeyFile(profile.PrivateKeyPath)
                : new PrivateKeyFile(profile.PrivateKeyPath, profile.PrivateKeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(profile.UserName, key));
        }
        else if (profile.AuthMethod == XAuthMethod.KeyboardInteractive)
        {
            var keyboard = new KeyboardInteractiveAuthenticationMethod(profile.UserName);
            keyboard.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = profile.Password;
                }
            };
            methods.Add(keyboard);
        }
        else if (profile.AuthMethod == XAuthMethod.None)
        {
            methods.Add(new NoneAuthenticationMethod(profile.UserName));
        }
        else
        {
            methods.Add(new PasswordAuthenticationMethod(profile.UserName, profile.Password));
            var keyboard = new KeyboardInteractiveAuthenticationMethod(profile.UserName);
            keyboard.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = profile.Password;
                }
            };
            methods.Add(keyboard);
        }
        var info = new ConnectionInfo(profile.Host, profile.Port, profile.UserName,
            methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(8),
            RetryAttempts = 1
        };
        using var client = new SshClient(info);
        client.Connect();
        client.Disconnect();
        XShellMessageBox.Show(this, "SSH 连接成功。", "测试连接",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void TestTcp(XSessionProfile profile)
    {
        using var client = new TcpClient();
        var task = client.ConnectAsync(profile.Host, profile.Port);
        if (!task.Wait(TimeSpan.FromSeconds(8)))
        {
            throw new TimeoutException("连接超时（8 秒）。");
        }
        task.GetAwaiter().GetResult();
        XShellMessageBox.Show(this, "Telnet 端口连接成功。", "测试连接",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void TestSerial(XSessionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SerialPortName))
        {
            throw new InvalidOperationException("请选择串口。");
        }
        using var port = new SerialPort(profile.SerialPortName, profile.BaudRate,
            profile.Parity, profile.DataBits, profile.StopBits)
        {
            Handshake = profile.Handshake,
            DtrEnable = profile.DtrEnable,
            RtsEnable = profile.RtsEnable
        };
        port.Open();
        port.Close();
        XShellMessageBox.Show(this, "串口 " + profile.SerialPortName + " 打开成功。", "测试连接",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
