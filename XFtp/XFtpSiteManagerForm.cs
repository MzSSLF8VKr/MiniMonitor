using System.Windows.Forms;

namespace MiniMonitor.XFtp;

/// <summary>XFtp 站点管理器：保存、编辑并连接常用 FTP / FTPS / SFTP 站点。</summary>
internal sealed class XFtpSiteManagerForm : Form
{
    private readonly XFtpStore _store;
    private readonly ListBox _list;
    private readonly TextBox _name;
    private readonly ComboBox _protocol;
    private readonly TextBox _host;
    private readonly NumericUpDown _port;
    private readonly TextBox _user;
    private readonly TextBox _password;
    private readonly TextBox _remotePath;
    private readonly TextBox _localPath;
    private readonly Label _hint;

    public XFtpSite? SelectedSite { get; private set; }

    public XFtpSiteManagerForm(XFtpStore store, XFtpSite? preset = null)
    {
        _store = store;
        Text = "XFTP 复刻版 - 站点管理器";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(860, 600);
        ClientSize = new Size(940, 660);
        BackColor = XFtpTheme.Background;
        ForeColor = XFtpTheme.Text;
        Font = XFtpTheme.Ui9;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 图标读取失败不影响窗口。
        }
        HandleCreated += (_, _) => XFtpTheme.ApplyWindow(this);

        var header = new XFtpHeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 62,
            Title = "站点管理器",
            Subtitle = "保存站点、快速连接与默认目录"
        };

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = XFtpTheme.PanelAlt,
            Padding = new Padding(0, 12, 18, 12)
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(XFtpTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var connect = XFtpTheme.PrimaryButton("连接", 92, 30);
        connect.Dock = DockStyle.Right;
        connect.Margin = new Padding(8, 0, 0, 0);
        connect.Click += (_, _) => ConnectSelected();
        var save = XFtpTheme.FlatButton("保存", 86, 30);
        save.Dock = DockStyle.Right;
        save.Margin = new Padding(8, 0, 0, 0);
        save.Click += (_, _) => SaveCurrent();
        var delete = XFtpTheme.FlatButton("删除", 86, 30);
        delete.Dock = DockStyle.Right;
        delete.Margin = new Padding(8, 0, 0, 0);
        delete.Click += (_, _) => DeleteSelected();
        var create = XFtpTheme.FlatButton("新建", 86, 30);
        create.Dock = DockStyle.Right;
        create.Click += (_, _) => NewSite();
        var close = XFtpTheme.FlatButton("关闭", 86, 30);
        close.Dock = DockStyle.Right;
        close.Margin = new Padding(8, 0, 0, 0);
        close.Click += (_, _) => Close();
        footer.Controls.Add(connect);
        footer.Controls.Add(save);
        footer.Controls.Add(delete);
        footer.Controls.Add(create);
        footer.Controls.Add(close);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = XFtpTheme.Border,
            Padding = new Padding(10)
        };
        body.Panel1.BackColor = XFtpTheme.Panel;
        body.Panel2.BackColor = XFtpTheme.Panel;

        var listHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = XFtpTheme.PanelAlt
        };
        listHeader.Paint += (_, e) =>
        {
            using var background = new SolidBrush(XFtpTheme.PanelAlt);
            e.Graphics.FillRectangle(background, listHeader.ClientRectangle);
            using var accent = new SolidBrush(XFtpTheme.Accent);
            e.Graphics.FillRectangle(accent, 10, 9, 3, 16);
            TextRenderer.DrawText(e.Graphics, "已保存站点", XFtpTheme.UiBold,
                new Rectangle(20, 0, 200, listHeader.Height), XFtpTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
            using var pen = new Pen(XFtpTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, listHeader.Height - 1,
                listHeader.Width, listHeader.Height - 1);
        };
        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = XFtpTheme.Panel,
            ForeColor = XFtpTheme.Text,
            Font = XFtpTheme.Ui(9.2f),
            ItemHeight = 42,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed
        };
        _list.DrawItem += DrawSiteItem;
        _list.SelectedIndexChanged += (_, _) => LoadSelectedIntoForm();
        body.Panel1.Controls.Add(_list);
        body.Panel1.Controls.Add(listHeader);

        var formHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = XFtpTheme.Panel,
            Padding = new Padding(18, 14, 18, 12),
            AutoScroll = true
        };
        var caption = new Label
        {
            Text = "站点信息",
            Dock = DockStyle.Top,
            Height = 28,
            Font = XFtpTheme.Ui(12f, FontStyle.Bold),
            ForeColor = XFtpTheme.Text,
            BackColor = Color.Transparent
        };
        _hint = new Label
        {
            Text = "密码使用 Windows DPAPI 加密后保存在本机。",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = XFtpTheme.Muted,
            BackColor = Color.Transparent
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = XFtpTheme.Panel,
            Padding = new Padding(0, 6, 0, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _name = XFtpTheme.Input(360);
        _protocol = XFtpTheme.Combo(150);
        _protocol.Items.AddRange(new object[] { "FTP", "FTPS", "SFTP" });
        _protocol.SelectedIndex = 0;
        _protocol.SelectedIndexChanged += (_, _) =>
        {
            if (_port == null)
            {
                return;
            }
            _port.Value = _protocol.SelectedIndex == 2 ? 22 : 21;
            if (_protocol.SelectedIndex == 2 && string.IsNullOrWhiteSpace(_remotePath?.Text))
            {
                _remotePath.Text = "/opt";
            }
        };
        _host = XFtpTheme.Input(360);
        _port = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Value = 21,
            Width = 110,
            Font = XFtpTheme.Ui9,
            BackColor = XFtpTheme.InputBack,
            ForeColor = XFtpTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _user = XFtpTheme.Input(360);
        _password = XFtpTheme.Input(360);
        _password.UseSystemPasswordChar = true;
        _remotePath = XFtpTheme.Input(360);
        _remotePath.PlaceholderText = "留空则使用服务器默认目录";
        _localPath = XFtpTheme.Input(360);
        _localPath.PlaceholderText = "留空则使用上次的本地目录";

        AddRow(table, "站点名称", _name);
        AddRow(table, "协议", _protocol);
        AddRow(table, "主机地址", _host);
        AddRow(table, "端口", _port);
        AddRow(table, "用户名", _user);
        AddRow(table, "密码", _password);
        AddRow(table, "远程目录", _remotePath);
        AddRow(table, "本地目录", _localPath);

        formHost.Controls.Add(table);
        formHost.Controls.Add(_hint);
        formHost.Controls.Add(caption);
        body.Panel2.Controls.Add(formHost);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        body.BringToFront();
        Controls.SetChildIndex(body, 0);
        Controls.SetChildIndex(footer, 1);
        Controls.SetChildIndex(header, 2);

        Shown += (_, _) =>
        {
            try
            {
                int width = body.Width;
                if (width > 0)
                {
                    int min1 = Math.Min(240, Math.Max(160, width / 4));
                    int min2 = Math.Min(380, Math.Max(200, width / 4));
                    int max = width - min2 - body.SplitterWidth;
                    if (max > min1)
                    {
                        body.Panel1MinSize = min1;
                        body.Panel2MinSize = min2;
                        body.SplitterDistance = Math.Clamp(width / 3, min1, max);
                    }
                }
            }
            catch
            {
                // 窗口过窄时保持默认分隔位置。
            }
            XFtpTheme.ApplyNative(this);
            RefreshList(preset);
        };
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = XFtpTheme.Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 8, 6)
        };
        control.Margin = new Padding(0, 6, 0, 6);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(caption);
        table.Controls.Add(control);
    }

    private void DrawSiteItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count)
        {
            return;
        }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(
            selected ? XFtpTheme.SelectionBack : XFtpTheme.Panel))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }
        if (_list.Items[e.Index] is not XFtpSite site)
        {
            return;
        }
        var rect = e.Bounds;
        using (var accent = new SolidBrush(selected ? XFtpTheme.Accent : XFtpTheme.BorderLight))
        {
            e.Graphics.FillRectangle(accent, rect.Left + 8,
                rect.Top + (rect.Height - 20) / 2, 3, 20);
        }
        TextRenderer.DrawText(e.Graphics,
            string.IsNullOrWhiteSpace(site.Name) ? site.Host : site.Name,
            XFtpTheme.Ui(9.2f, FontStyle.Bold),
            new Rectangle(rect.Left + 20, rect.Top + 6, rect.Width - 30, 18),
            selected ? Color.White : XFtpTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics,
            site.DisplayAddress + (string.IsNullOrWhiteSpace(site.User)
                ? "" : "  ·  " + site.User),
            XFtpTheme.Ui85,
            new Rectangle(rect.Left + 20, rect.Top + 22, rect.Width - 30, 16),
            selected ? Color.FromArgb(200, 226, 255) : XFtpTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void RefreshList(XFtpSite? preset = null)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var site in _store.Sites
                .OrderByDescending(site => site.LastUsedAt)
                .ThenBy(site => site.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _list.Items.Add(site);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        if (preset != null)
        {
            FillForm(preset);
            _list.SelectedIndex = -1;
            return;
        }
        if (_store.Sites.Count == 0)
        {
            NewSite();
            return;
        }
        _list.SelectedIndex = 0;
    }

    private void FillForm(XFtpSite site)
    {
        _name.Text = site.Name;
        _protocol.SelectedIndex = site.Protocol switch
        {
            XFtpProtocol.Ftps => 1,
            XFtpProtocol.Sftp => 2,
            _ => 0
        };
        _host.Text = site.Host;
        _port.Value = Math.Clamp(site.Port <= 0 ? 21 : site.Port, 1, 65535);
        _user.Text = site.User;
        _password.Text = site.Password;
        _remotePath.Text = site.Protocol == XFtpProtocol.Sftp
            && string.IsNullOrWhiteSpace(site.RemotePath)
                ? "/opt" : site.RemotePath;
        _localPath.Text = site.LocalPath;
    }

    private void LoadSelectedIntoForm()
    {
        if (_list.SelectedItem is XFtpSite site)
        {
            FillForm(site);
        }
    }

    private XFtpSite? ReadForm()
    {
        string host = _host.Text.Trim();
        if (host.Length == 0)
        {
            XFtpMessageBox.Show(this, "请输入主机地址。", "站点管理器",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _host.Focus();
            return null;
        }
        return new XFtpSite
        {
            Name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : host,
            Protocol = _protocol.SelectedIndex switch
            {
                1 => XFtpProtocol.Ftps,
                2 => XFtpProtocol.Sftp,
                _ => XFtpProtocol.Ftp
            },
            Host = host,
            Port = (int)_port.Value,
            User = _user.Text.Trim(),
            Password = _password.Text,
            RemotePath = _remotePath.Text.Trim(),
            LocalPath = _localPath.Text.Trim()
        };
    }

    private void NewSite()
    {
        _list.SelectedIndex = -1;
        _name.Clear();
        _protocol.SelectedIndex = 0;
        _host.Clear();
        _port.Value = 21;
        _user.Clear();
        _password.Clear();
        _remotePath.Clear();
        _localPath.Clear();
        _name.Focus();
    }

    private void SaveCurrent()
    {
        var site = ReadForm();
        if (site == null)
        {
            return;
        }
        _store.AddOrUpdate(site);
        RefreshList(site);
        _hint.Text = "已保存站点：" + site.Name;
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not XFtpSite site)
        {
            return;
        }
        if (XFtpMessageBox.Show(this, "确定要删除站点“" + site.Name + "”吗？",
                "站点管理器", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
        {
            return;
        }
        _store.Sites.Remove(site);
        _store.Save();
        RefreshList();
    }

    private void ConnectSelected()
    {
        var site = ReadForm();
        if (site == null)
        {
            return;
        }
        _store.AddOrUpdate(site);
        SelectedSite = site;
        DialogResult = DialogResult.OK;
        Close();
    }
}
