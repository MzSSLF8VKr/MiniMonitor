using System.Drawing;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>Navicat 风格连接属性对话框（常规 / 高级 / SSL / SSH）。</summary>
internal sealed class ConnectionDialog : Form
{
    private readonly ConnectionProfile _profile;
    private readonly TextBox _name;
    private readonly TextBox _group;
    private readonly ComboBox _kind;
    private readonly TextBox _host;
    private readonly NumericUpDown _port;
    private readonly TextBox _database;
    private readonly TextBox _user;
    private readonly TextBox _password;
    private readonly CheckBox _savePassword;
    private readonly TextBox _charset;
    private readonly NumericUpDown _timeout;
    private readonly TextBox _raw;
    private readonly TextBox _description;
    private readonly CheckBox _useSsl;
    private readonly ComboBox _sslMode;
    private readonly CheckBox _useSsh;
    private readonly TextBox _sshHost;
    private readonly NumericUpDown _sshPort;
    private readonly TextBox _sshUser;
    private readonly TextBox _sshPassword;
    private readonly TextBox _sshKey;
    private readonly TextBox _sshPassphrase;
    private readonly TextBox _sshRemoteHost;
    private readonly NumericUpDown _sshRemotePort;
    private readonly Label _hostLabel;
    private readonly Label _databaseLabel;
    private readonly Label _userLabel;
    private readonly Label _passwordLabel;
    private readonly Label _fileLabel;
    private readonly TextBox _file;
    private readonly Button _browseFile;
    private readonly TabControl _tabs;
    private readonly Label _testResult;
    private readonly Button _testButton;
    private readonly Button _saveButton;
    private bool _loading = true;

    public ConnectionProfile Profile => _profile;

    /// <summary>false 表示用户只点击了“保存”，不要求自动连接。</summary>
    public bool ConnectAfterSave { get; private set; } = true;

    public ConnectionDialog(ConnectionProfile? existing = null)
    {
        _profile = existing?.CopyForConnect() ?? new ConnectionProfile();
        _profile.Normalize();

        Text = existing == null ? "新建连接" : "编辑连接 - " + existing.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(720, 600);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = NvTheme.HeaderBack
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        header.Controls.Add(new Label
        {
            Text = existing == null ? "新建连接" : "连接属性",
            Location = new Point(20, 12),
            AutoSize = true,
            Font = NvTheme.Ui(13f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        header.Controls.Add(new Label
        {
            Text = "配置数据库连接参数，保存后双击连接树即可打开。",
            Location = new Point(22, 38),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        var headerIcon = new PictureBox
        {
            Image = NvIcons.Get(NvIcon.NewConnection, 32),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(0, 16)
        };
        header.Controls.Add(headerIcon);
        header.Resize += (_, _) =>
            headerIcon.Location = new Point(Math.Max(8, header.Width - 56), 16);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Ui(9f),
            Padding = new Point(16, 6)
        };

        _name = RowTextBox();
        _group = RowTextBox();
        _kind = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f),
            Width = 300
        };
        _kind.Items.AddRange(new object[]
        {
            "SQLite", "MySQL / MariaDB", "PostgreSQL", "SQL Server", "Oracle"
        });
        _host = RowTextBox();
        _port = RowNumeric(0, 65535);
        _database = RowTextBox();
        _user = RowTextBox();
        _password = RowTextBox();
        _password.UseSystemPasswordChar = true;
        _savePassword = new CheckBox
        {
            Text = "保存密码",
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _file = RowTextBox();
        _browseFile = NvTheme.FlatButton("浏览…", null, null, 74);
        _browseFile.Click += (_, _) => BrowseSqliteFile();

        _charset = RowTextBox();
        _timeout = RowNumeric(1, 600);
        _raw = RowTextBox();
        _raw.Multiline = true;
        _raw.Height = 72;
        _raw.ScrollBars = ScrollBars.Vertical;
        _description = RowTextBox();
        _description.Multiline = true;
        _description.Height = 72;

        _useSsl = new CheckBox { Text = "使用 SSL 加密连接", AutoSize = true, ForeColor = NvTheme.Text };
        _sslMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            Font = NvTheme.Ui(9.2f)
        };
        _sslMode.Items.AddRange(new[] { "Preferred", "Required", "VerifyCA", "VerifyFull", "Disable" });

        _useSsh = new CheckBox { Text = "使用 SSH 隧道", AutoSize = true, ForeColor = NvTheme.Text };
        _sshHost = RowTextBox();
        _sshPort = RowNumeric(0, 65535);
        _sshUser = RowTextBox();
        _sshPassword = RowTextBox();
        _sshPassword.UseSystemPasswordChar = true;
        _sshKey = RowTextBox();
        _sshPassphrase = RowTextBox();
        _sshPassphrase.UseSystemPasswordChar = true;
        _sshRemoteHost = RowTextBox();
        _sshRemotePort = RowNumeric(0, 65535);

        _hostLabel = MakeLabel("主机");
        _databaseLabel = MakeLabel("数据库");
        _userLabel = MakeLabel("用户名");
        _passwordLabel = MakeLabel("密码");
        _fileLabel = MakeLabel("数据库文件");

        _tabs.TabPages.Add(BuildGeneralPage());
        _tabs.TabPages.Add(BuildAdvancedPage());
        _tabs.TabPages.Add(BuildSslPage());
        _tabs.TabPages.Add(BuildSshPage());

        _testResult = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = NvTheme.Muted,
            Location = new Point(18, 13),
            Size = new Size(400, 26),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        var testResultMenu = new ContextMenuStrip();
        testResultMenu.Items.Add("复制错误信息", null, (_, _) =>
        {
            string text = _testResult.Tag as string ?? _testResult.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                Clipboard.SetText(text);
            }
        });
        _testResult.ContextMenuStrip = testResultMenu;
        _testButton = NvTheme.FlatButton("测试连接", null, null, 96);
        _testButton.Location = new Point(468, 14);
        _testButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _testButton.Click += async (_, _) => await TestAsync();
        _saveButton = NvTheme.FlatButton("保存并连接", NvTheme.Accent, Color.White, 112);
        _saveButton.Location = new Point(572, 14);
        _saveButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _saveButton.Click += (_, _) => SaveAndClose();
        var saveOnly = NvTheme.FlatButton("保存", null, null, 88);
        saveOnly.Location = new Point(480, 14);
        saveOnly.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        saveOnly.Click += (_, _) => SaveOnly();
        var cancel = NvTheme.FlatButton("取消", null, null, 88);
        cancel.Location = new Point(690, 14);
        cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        cancel.DialogResult = DialogResult.Cancel;

        var bottom = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = NvTheme.Background
        };
        bottom.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
        };
        bottom.Controls.AddRange(new Control[] { _testResult, _testButton, saveOnly, _saveButton, cancel });
        void PositionBottomButtons()
        {
            int right = bottom.ClientSize.Width - 14;
            cancel.Location = new Point(right - cancel.Width, 14);
            _saveButton.Location = new Point(cancel.Left - _saveButton.Width - 8, 14);
            saveOnly.Location = new Point(_saveButton.Left - saveOnly.Width - 8, 14);
            _testButton.Location = new Point(saveOnly.Left - _testButton.Width - 8, 14);
            _testResult.Location = new Point(16, 15);
            _testResult.Width = Math.Max(80, _testButton.Left - 28);
        }
        bottom.Resize += (_, _) => PositionBottomButtons();
        PositionBottomButtons();

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 0) };
        body.Controls.Add(_tabs);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = NvTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
        Shown += (_, _) => PositionBottomButtons();

        AcceptButton = _saveButton;
        CancelButton = cancel;

        LoadProfile();
        _loading = false;
        ApplyKindVisibility();
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = 88,
        Height = 24,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = NvTheme.Text
    };

    private static TextBox RowTextBox() => new()
    {
        Font = NvTheme.Ui(9.2f),
        BorderStyle = BorderStyle.FixedSingle,
        Width = 380
    };

    private static NumericUpDown RowNumeric(int min, int max) => new()
    {
        Font = NvTheme.Ui(9.2f),
        Minimum = min,
        Maximum = max,
        Width = 120,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static void AddRow(Control parent, Control label, Control field, int y)
    {
        label.Location = new Point(18, y + 2);
        field.Location = new Point(112, y);
        parent.Controls.Add(label);
        parent.Controls.Add(field);
    }

    private TabPage BuildGeneralPage()
    {
        var page = new TabPage("常规") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = NvTheme.Panel };

        AddRow(panel, MakeLabel("连接类型"), _kind, 18);
        AddRow(panel, MakeLabel("连接名"), _name, 58);

        _hostLabel.Text = "主机";
        AddRow(panel, _hostLabel, _host, 98);
        _port.Location = new Point(504, 98);
        panel.Controls.Add(_port);

        AddRow(panel, _databaseLabel, _database, 138);
        AddRow(panel, _fileLabel, _file, 138);
        _browseFile.Location = new Point(500, 137);
        panel.Controls.Add(_browseFile);

        AddRow(panel, _userLabel, _user, 178);
        AddRow(panel, _passwordLabel, _password, 218);
        _savePassword.Location = new Point(112, 250);
        panel.Controls.Add(_savePassword);
        AddRow(panel, MakeLabel("分组"), _group, 282);
        panel.Controls.Add(new Label
        {
            Text = "可选：填写后连接会显示在连接树的对应分组下。",
            Location = new Point(500, 286),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });

        var tip = new Label
        {
            Text = "提示：SQLite 只需选择数据库文件；MySQL / PostgreSQL / SQL Server / Oracle 需要主机、端口和账号。",
            Location = new Point(18, 322),
            Size = new Size(600, 40),
            ForeColor = NvTheme.Muted
        };
        panel.Controls.Add(tip);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildAdvancedPage()
    {
        var page = new TabPage("高级") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = NvTheme.Panel };
        AddRow(panel, MakeLabel("字符集"), _charset, 18);
        panel.Controls.Add(new Label
        {
            Text = "如 utf8mb4、UTF8；留空使用服务器默认值。",
            Location = new Point(500, 22),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        AddRow(panel, MakeLabel("连接超时"), _timeout, 58);
        panel.Controls.Add(new Label
        {
            Text = "秒",
            Location = new Point(240, 62),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        AddRow(panel, MakeLabel("附加参数"), _raw, 98);
        panel.Controls.Add(new Label
        {
            Text = "追加到连接字符串，例如 SslMode=Required;AllowPublicKeyRetrieval=true",
            Location = new Point(112, 178),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        AddRow(panel, MakeLabel("描述"), _description, 208);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildSslPage()
    {
        var page = new TabPage("SSL") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = NvTheme.Panel };
        _useSsl.Location = new Point(20, 24);
        panel.Controls.Add(_useSsl);
        AddRow(panel, MakeLabel("SSL 模式"), _sslMode, 60);
        panel.Controls.Add(new Label
        {
            Text = "VerifyCA / VerifyFull 会校验证书主机名；SQLite 不需要 SSL。",
            Location = new Point(20, 110),
            Size = new Size(600, 40),
            ForeColor = NvTheme.Muted
        });
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildSshPage()
    {
        var page = new TabPage("SSH") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = NvTheme.Panel };
        _useSsh.Location = new Point(20, 18);
        panel.Controls.Add(_useSsh);
        AddRow(panel, MakeLabel("SSH 主机"), _sshHost, 54);
        _sshPort.Location = new Point(504, 54);
        panel.Controls.Add(_sshPort);
        AddRow(panel, MakeLabel("SSH 用户"), _sshUser, 94);
        AddRow(panel, MakeLabel("SSH 密码"), _sshPassword, 134);
        AddRow(panel, MakeLabel("私钥文件"), _sshKey, 174);
        var browse = NvTheme.FlatButton("浏览…", null, null, 74);
        browse.Location = new Point(500, 173);
        browse.Click += (_, _) => BrowseSshKey();
        panel.Controls.Add(browse);
        AddRow(panel, MakeLabel("私钥密码"), _sshPassphrase, 214);
        AddRow(panel, MakeLabel("远程主机"), _sshRemoteHost, 254);
        _sshRemotePort.Location = new Point(504, 254);
        panel.Controls.Add(_sshRemotePort);
        panel.Controls.Add(new Label
        {
            Text = "SSH 隧道会把远程数据库端口映射到本机随机端口，再通过本机连接。远程主机通常为 127.0.0.1。",
            Location = new Point(20, 304),
            Size = new Size(600, 44),
            ForeColor = NvTheme.Muted
        });
        page.Controls.Add(panel);
        return page;
    }

    private void LoadProfile()
    {
        _kind.SelectedIndex = (int)_profile.Kind;
        _name.Text = _profile.Name;
        _group.Text = _profile.Group;
        _host.Text = _profile.Host;
        _port.Value = Math.Clamp(_profile.Port <= 0 ? _profile.Kind.DefaultPort() : _profile.Port, 0, 65535);
        _database.Text = _profile.Database;
        _user.Text = _profile.Username;
        _password.Text = _profile.Password;
        _savePassword.Checked = _profile.SavePassword;
        _charset.Text = _profile.Charset;
        _timeout.Value = Math.Clamp(_profile.ConnectTimeout, 1, 600);
        _raw.Text = _profile.RawConnectionString;
        _description.Text = _profile.Description;
        _useSsl.Checked = _profile.UseSsl;
        _sslMode.SelectedItem = _profile.SslMode;
        if (_sslMode.SelectedIndex < 0)
        {
            _sslMode.SelectedIndex = 0;
        }
        _useSsh.Checked = _profile.UseSsh;
        _sshHost.Text = _profile.SshHost;
        _sshPort.Value = Math.Clamp(_profile.SshPort <= 0 ? 22 : _profile.SshPort, 0, 65535);
        _sshUser.Text = _profile.SshUser;
        _sshPassword.Text = _profile.SshPassword;
        _sshKey.Text = _profile.SshKeyFile;
        _sshPassphrase.Text = _profile.SshPassphrase;
        _sshRemoteHost.Text = _profile.SshRemoteHost;
        _sshRemotePort.Value = Math.Clamp(_profile.SshRemotePort <= 0 ? _profile.Port : _profile.SshRemotePort, 0, 65535);
        _file.Text = _profile.Kind == DbKind.SQLite ? _profile.Database : "";
        _kind.SelectedIndexChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }
            ApplyKindVisibility();
        };
        _useSsh.CheckedChanged += (_, _) => _useSsh.Enabled = true;
    }

    private void ApplyKindVisibility()
    {
        var kind = (DbKind)Math.Max(0, _kind.SelectedIndex);
        bool sqlite = kind == DbKind.SQLite;
        _hostLabel.Visible = _host.Visible = _port.Visible = !sqlite;
        _databaseLabel.Visible = _database.Visible = !sqlite;
        _fileLabel.Visible = _file.Visible = _browseFile.Visible = sqlite;
        _userLabel.Visible = _user.Visible = !sqlite;
        _passwordLabel.Visible = _password.Visible = !sqlite;
        _savePassword.Visible = !sqlite;
        _tabs.TabPages[2].Text = sqlite ? "SSL（不适用）" : "SSL";
        _tabs.TabPages[3].Text = sqlite ? "SSH（不适用）" : "SSH";
        if (sqlite)
        {
            _database.Text = _file.Text;
        }
        else
        {
            int defaultPort = kind.DefaultPort();
            int oldDefault = _profile.Kind.DefaultPort();
            if (_port.Value == oldDefault || _port.Value == 0)
            {
                _port.Value = defaultPort;
            }
            _databaseLabel.Text = kind == DbKind.Oracle ? "服务名" : "数据库";
        }
    }

    private void BrowseSqliteFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择或新建 SQLite 数据库文件",
            Filter = "SQLite 数据库 (*.db;*.sqlite;*.sqlite3;*.db3)|*.db;*.sqlite;*.sqlite3;*.db3|所有文件 (*.*)|*.*",
            CheckFileExists = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _file.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void BrowseSshKey()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 SSH 私钥",
            Filter = "私钥文件 (*.pem;*.ppk;*.key;id_rsa;id_ed25519)|*.pem;*.ppk;*.key;id_rsa;id_ed25519|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sshKey.Text = dialog.FileName;
        }
    }

    private void Collect()
    {
        _profile.Kind = (DbKind)Math.Max(0, _kind.SelectedIndex);
        _profile.Name = _name.Text.Trim();
        _profile.Group = _group.Text.Trim();
        _profile.Host = _profile.Kind == DbKind.SQLite ? "127.0.0.1" : _host.Text.Trim();
        _profile.Port = _profile.Kind == DbKind.SQLite ? 0 : (int)_port.Value;
        _profile.Database = _profile.Kind == DbKind.SQLite ? _file.Text.Trim() : _database.Text.Trim();
        _profile.Username = _user.Text.Trim();
        _profile.Password = _password.Text;
        _profile.SavePassword = _savePassword.Checked;
        _profile.Charset = _charset.Text.Trim();
        _profile.ConnectTimeout = (int)_timeout.Value;
        _profile.RawConnectionString = _raw.Text.Trim();
        _profile.Description = _description.Text.Trim();
        _profile.UseSsl = _useSsl.Checked;
        _profile.SslMode = _sslMode.SelectedItem?.ToString() ?? "Preferred";
        _profile.UseSsh = _useSsh.Checked;
        _profile.SshHost = _sshHost.Text.Trim();
        _profile.SshPort = (int)_sshPort.Value;
        _profile.SshUser = _sshUser.Text.Trim();
        _profile.SshPassword = _sshPassword.Text;
        _profile.SshKeyFile = _sshKey.Text.Trim();
        _profile.SshPassphrase = _sshPassphrase.Text;
        _profile.SshRemoteHost = _sshRemoteHost.Text.Trim();
        _profile.SshRemotePort = (int)_sshRemotePort.Value;
        _profile.Normalize();
    }

    private async Task TestAsync()
    {
        Collect();
        string? error = ValidateInput();
        if (error != null)
        {
            _testResult.ForeColor = NvTheme.Red;
            _testResult.Text = error;
            return;
        }
        _testButton.Enabled = false;
        _testResult.ForeColor = NvTheme.Muted;
        _testResult.Text = "正在连接…";
        var profile = _profile.CopyForConnect();
        int timeoutSeconds = Math.Max(10,
            (profile.ConnectTimeout <= 0 ? 15 : profile.ConnectTimeout) + 10);
        bool ok;
        string message;
        try
        {
            var (testOk, testMessage) = await Task.Run(() => DbSession.Test(profile))
                .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            ok = testOk;
            message = testMessage;
        }
        catch (TimeoutException)
        {
            ok = false;
            message = $"连接超时：超过 {timeoutSeconds} 秒仍未完成。"
                + "请检查主机地址、端口、防火墙、SSH 隧道或数据库服务是否可用。";
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _testButton.Enabled = true;
            }
        }
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (ok)
        {
            NvLog.Info("测试连接成功：" + profile.Name + " · " + message);
        }
        else
        {
            NvLog.Warn("测试连接失败：" + profile.Name + " · " + message);
        }
        _testResult.ForeColor = ok ? NvTheme.Green : NvTheme.Red;
        _testResult.Text = message.Length > 80 ? message[..80] + "…" : message;
        _testResult.Tag = message;
    }

    private string? ValidateInput()
    {
        if (_profile.Kind == DbKind.SQLite)
        {
            if (string.IsNullOrWhiteSpace(_profile.Database))
            {
                return "请选择或输入 SQLite 数据库文件路径。";
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_profile.Host))
            {
                return "请填写主机地址。";
            }
            if (_profile.Port <= 0)
            {
                return "请填写端口。";
            }
            if (_profile.Kind == DbKind.Oracle && string.IsNullOrWhiteSpace(_profile.Database))
            {
                return "请填写 Oracle 服务名。";
            }
        }
        if (_profile.UseSsh && string.IsNullOrWhiteSpace(_profile.SshHost))
        {
            return "请填写 SSH 主机。";
        }
        if (_profile.UseSsh && string.IsNullOrWhiteSpace(_profile.SshUser))
        {
            return "请填写 SSH 用户名。";
        }
        return null;
    }

    private void SaveOnly()
    {
        Collect();
        // 只保存：不校验主机/端口/文件是否可达，连接是否可用由用户后续自行测试
        if (string.IsNullOrWhiteSpace(_profile.Name))
        {
            _profile.Name = _profile.Kind == DbKind.SQLite
                ? (string.IsNullOrWhiteSpace(_profile.Database)
                    ? "SQLite"
                    : Path.GetFileNameWithoutExtension(_profile.Database))
                : (string.IsNullOrWhiteSpace(_profile.Host) ? "localhost" : _profile.Host);
        }
        _profile.Normalize();
        ConnectAfterSave = false;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SaveAndClose()
    {
        Collect();
        string? error = ValidateInput();
        if (error != null)
        {
            NvPrompt.Error(this, "连接属性", error);
            return;
        }
        if (string.IsNullOrWhiteSpace(_profile.Name))
        {
            _profile.Name = _profile.Kind == DbKind.SQLite
                ? Path.GetFileNameWithoutExtension(_profile.Database)
                : _profile.Host;
        }
        ConnectAfterSave = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
