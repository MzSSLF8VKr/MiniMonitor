using System.Windows.Forms;

namespace MiniMonitor.XFtp;

/// <summary>XFtp 右侧远程文件浏览器：快速连接、远程目录、上传下载与拖拽。</summary>
internal sealed class XFtpRemotePanel : UserControl
{
    public const string RemoteDataFormat = "XFtp.Remote";

    private readonly TextBox _path;
    private readonly ListView _list;
    private readonly Label _status;
    private readonly Panel _empty;
    private readonly ToolStrip _toolbar;
    private readonly ColumnHeader _nameColumn;
    private readonly Panel _quickBar;
    private ComboBox _quickProtocol = null!;
    private TextBox _quickHost = null!;
    private TextBox _quickPort = null!;
    private TextBox _quickUser = null!;
    private TextBox _quickPassword = null!;

    private IXFtpSession? _session;
    private string _currentPath = "/";

    public event Action<XFtpSite, bool>? QuickConnectRequested;
    public event Action<IReadOnlyList<XFtpEntry>>? DownloadRequested;
    public event Action<IReadOnlyList<string>>? UploadRequested;
    public event Action? SelectionChanged;
    public event Action? Activated;
    public event Action? SessionStateChanged;

    public string CurrentPath => _currentPath;
    public bool IsConnected => _session?.IsConnected == true;

    public XFtpRemotePanel()
    {
        BackColor = XFtpTheme.Panel;
        Font = XFtpTheme.Ui88;
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            if (IsConnected
                && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        DragDrop += (_, e) =>
        {
            if (IsConnected && e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                UploadRequested?.Invoke(files);
            }
        };

        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 33,
            GripStyle = ToolStripGripStyle.Hidden
        };
        XFtpTheme.StyleToolbar(_toolbar);
        _toolbar.Items.Add(Button("上传", "\uE898", XFtpTheme.Green, (_, _) => Upload()));
        _toolbar.Items.Add(Button("下载", "\uE896", XFtpTheme.Accent, (_, _) => Download()));
        _toolbar.Items.Add(Button("刷新", "\uE72C", XFtpTheme.Accent, (_, _) => _ = RefreshAsync()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("新建文件夹", "\uE8F4", XFtpTheme.Accent, (_, _) => NewFolder()));
        _toolbar.Items.Add(Button("重命名", "\uE70F", XFtpTheme.Orange, (_, _) => RenameSelected()));
        _toolbar.Items.Add(Button("删除", "\uE74D", XFtpTheme.Red, (_, _) => DeleteSelected()));

        _quickBar = BuildQuickBar();

        var pathPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = XFtpTheme.Panel,
            Padding = new Padding(6, 4, 6, 4)
        };
        var remoteLabel = new Label
        {
            Text = "远程",
            Dock = DockStyle.Left,
            Width = 42,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = XFtpTheme.Green,
            Font = XFtpTheme.UiBold,
            BackColor = Color.Transparent
        };
        var up = XFtpTheme.FlatButton("上级", 52, 24);
        up.Dock = DockStyle.Right;
        up.Margin = new Padding(6, 0, 0, 0);
        up.Click += (_, _) =>
        {
            if (IsConnected)
            {
                _ = LoadDirectoryAsync(ParentPath(_currentPath));
            }
        };
        _path = XFtpTheme.Input(300);
        _path.Dock = DockStyle.Fill;
        _path.Font = XFtpTheme.Mono(8.8f);
        _path.ReadOnly = true;
        _path.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (IsConnected)
                {
                    _ = LoadDirectoryAsync(_path.Text.Trim());
                }
            }
        };
        pathPanel.Controls.Add(_path);
        pathPanel.Controls.Add(up);
        pathPanel.Controls.Add(remoteLabel);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = XFtpTheme.PanelAlt
        };
        header.Paint += (_, e) => DrawHeader(e.Graphics, header);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            BackColor = XFtpTheme.Panel,
            ForeColor = XFtpTheme.Text,
            Font = XFtpTheme.Ui88,
            HeaderStyle = ColumnHeaderStyle.None,
            AllowDrop = true
        };
        _nameColumn = _list.Columns.Add("名称", 300);
        _list.Columns.Add("大小", 90, HorizontalAlignment.Right);
        _list.Columns.Add("类型", 88);
        _list.Columns.Add("修改日期", 132);
        _list.Columns.Add("权限", 92);
        XFtpTheme.StyleList(_list);
        var images = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };
        images.Images.Add(XFtpTheme.Icon("\uE8B7", XFtpTheme.Orange, 16));
        images.Images.Add(XFtpTheme.Icon("\uE7C3", XFtpTheme.Muted, 16));
        _list.SmallImageList = images;
        _list.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke();
        _list.Enter += (_, _) => Activated?.Invoke();
        _list.Resize += (_, _) => ResizeColumns();
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.ItemDrag += (_, _) =>
        {
            var entries = SelectedEntries();
            if (entries.Count == 0)
            {
                return;
            }
            var data = new DataObject();
            data.SetData(RemoteDataFormat, entries.ToArray());
            _list.DoDragDrop(data, DragDropEffects.Copy);
        };
        _list.DragEnter += (_, e) =>
        {
            if (IsConnected && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        _list.DragDrop += (_, e) =>
        {
            if (IsConnected && e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                UploadRequested?.Invoke(files);
            }
        };
        _list.ContextMenuStrip = BuildMenu();

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = XFtpTheme.PanelAlt,
            ForeColor = XFtpTheme.Muted,
            Text = "尚未连接。请使用快速连接或站点管理器。"
        };

        _empty = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = XFtpTheme.Panel
        };
        _empty.Paint += (_, e) => DrawEmptyState(e.Graphics, _empty);
        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = XFtpTheme.Panel
        };
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_empty);
        _empty.BringToFront();

        Controls.Add(listHost);
        Controls.Add(header);
        Controls.Add(pathPanel);
        Controls.Add(_quickBar);
        Controls.Add(_toolbar);
        Controls.Add(_status);
        Controls.SetChildIndex(listHost, 0);
        Controls.SetChildIndex(_status, 1);
        Controls.SetChildIndex(header, 2);
        Controls.SetChildIndex(pathPanel, 3);
        Controls.SetChildIndex(_quickBar, 4);
        Controls.SetChildIndex(_toolbar, 5);
    }

    private static ToolStripButton Button(string text, string glyph, Color color,
        EventHandler click)
    {
        var button = new ToolStripButton(text, XFtpTheme.Icon(glyph, color, 16), click)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Font = XFtpTheme.Ui85,
            ForeColor = XFtpTheme.Text,
            Margin = new Padding(1, 0, 1, 0)
        };
        return button;
    }

    private Panel BuildQuickBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = XFtpTheme.PanelAlt,
            Padding = new Padding(8, 7, 8, 7),
            Visible = false
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(XFtpTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = XFtpTheme.PanelAlt
        };
        var title = new Label
        {
            Text = "快速连接",
            AutoSize = true,
            ForeColor = XFtpTheme.Accent,
            Font = XFtpTheme.UiBold,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 6, 10, 0)
        };
        _quickProtocol = XFtpTheme.Combo(78);
        _quickProtocol.Items.AddRange(new object[] { "FTP", "FTPS", "SFTP" });
        _quickProtocol.Margin = new Padding(0, 2, 8, 0);
        _quickHost = QuickInput("主机地址", 170);
        _quickPort = QuickInput("21", 52);
        _quickUser = QuickInput("用户名", 96);
        _quickPassword = QuickInput("密码", 96);
        _quickPassword.UseSystemPasswordChar = true;
        _quickProtocol.SelectedIndex = 0;
        _quickProtocol.SelectedIndexChanged += (_, _) =>
        {
            int port = _quickProtocol.SelectedIndex switch
            {
                1 => 21,
                2 => 22,
                _ => 21
            };
            _quickPort.Text = port.ToString();
        };
        var connect = XFtpTheme.PrimaryButton("连接", 66, 26);
        connect.Margin = new Padding(6, 2, 4, 0);
        connect.Click += (_, _) => RaiseQuickConnect(false);
        var save = XFtpTheme.FlatButton("保存站点", 82, 26);
        save.Margin = new Padding(4, 2, 0, 0);
        save.Click += (_, _) => RaiseQuickConnect(true);

        flow.Controls.Add(title);
        flow.Controls.Add(_quickProtocol);
        flow.Controls.Add(_quickHost);
        flow.Controls.Add(_quickPort);
        flow.Controls.Add(_quickUser);
        flow.Controls.Add(_quickPassword);
        flow.Controls.Add(connect);
        flow.Controls.Add(save);
        bar.Controls.Add(flow);
        return bar;
    }

    private static TextBox QuickInput(string placeholder, int width)
    {
        var box = XFtpTheme.Input(width);
        box.PlaceholderText = placeholder;
        box.Margin = new Padding(0, 2, 8, 0);
        return box;
    }

    private void RaiseQuickConnect(bool save)
    {
        string host = _quickHost.Text.Trim();
        if (host.Length == 0)
        {
            _quickHost.Focus();
            return;
        }
        int port = int.TryParse(_quickPort.Text.Trim(), out int parsed) && parsed > 0
            ? parsed : (_quickProtocol.SelectedIndex == 2 ? 22 : 21);
        var site = new XFtpSite
        {
            Name = (string.IsNullOrWhiteSpace(_quickUser.Text)
                ? host : _quickUser.Text.Trim() + "@" + host),
            Protocol = _quickProtocol.SelectedIndex switch
            {
                1 => XFtpProtocol.Ftps,
                2 => XFtpProtocol.Sftp,
                _ => XFtpProtocol.Ftp
            },
            Host = host,
            Port = port,
            User = _quickUser.Text.Trim(),
            Password = _quickPassword.Text,
            LocalPath = ""
        };
        QuickConnectRequested?.Invoke(site, save);
    }

    public void FillQuickConnect(XFtpSite site)
    {
        _quickProtocol.SelectedIndex = site.Protocol switch
        {
            XFtpProtocol.Ftps => 1,
            XFtpProtocol.Sftp => 2,
            _ => 0
        };
        _quickHost.Text = site.Host;
        _quickPort.Text = site.Port.ToString();
        _quickUser.Text = site.User;
        _quickPassword.Text = site.Password;
    }

    public bool QuickConnectVisible
    {
        get => _quickBar.Visible;
        set => _quickBar.Visible = value;
    }

    public void FocusQuickConnect()
    {
        if (!_quickBar.Visible)
        {
            QuickConnectVisible = true;
        }
        _quickHost.Focus();
        _quickHost.SelectAll();
    }

    public void AttachSession(IXFtpSession? session)
    {
        _session = session;
        _path.ReadOnly = !IsConnected;
        _empty.Visible = !IsConnected;
        if (!IsConnected)
        {
            _list.Items.Clear();
            _currentPath = "/";
            _path.Text = "";
            _status.Text = "尚未连接。请使用快速连接或站点管理器。";
        }
        else
        {
            _status.Text = "已连接，正在读取目录...";
        }
        SessionStateChanged?.Invoke();
    }

    private void DrawEmptyState(Graphics g, Panel host)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int width = host.ClientSize.Width;
        int height = host.ClientSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        int centerX = width / 2;
        int centerY = height / 2;
        var iconRect = new Rectangle(centerX - 32, centerY - 92, 64, 64);
        using (var back = new SolidBrush(XFtpTheme.PanelAlt))
        using (var path = XFtpTheme.RoundedRect(iconRect, 18))
        {
            g.FillPath(back, path);
            using var pen = new Pen(XFtpTheme.BorderLight);
            g.DrawPath(pen, path);
        }
        if (XFtpTheme.EmptyServerIcon is Image icon)
        {
            g.DrawImage(icon, new Rectangle(centerX - 21, centerY - 81, 42, 42));
        }
        TextRenderer.DrawText(g, "尚未连接远程站点", XFtpTheme.UiTitle,
            new Rectangle(0, centerY - 12, width, 28), XFtpTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g,
            "点击上方“快速连接”或“站点”选择 FTP / FTPS / SFTP 服务器",
            XFtpTheme.Ui85,
            new Rectangle(0, centerY + 18, width, 22), XFtpTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);
    }

    public async Task<bool> LoadDirectoryAsync(string path)
    {
        var session = _session;
        if (session == null || !session.IsConnected)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            path = session.HomePath;
        }
        path = path.Replace('\\', '/');
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }
        try
        {
            _status.Text = "正在读取 " + path + " ...";
            var entries = await session.ListAsync(path, CancellationToken.None);
            if (IsDisposed || !ReferenceEquals(_session, session))
            {
                return false;
            }
            _currentPath = path;
            _path.Text = path;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var entry in entries
                    .OrderByDescending(entry => entry.IsDirectory)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ListViewItem(entry.Name, entry.IsDirectory ? 0 : 1)
                    {
                        Tag = entry
                    };
                    item.SubItems.Add(entry.SizeText);
                    item.SubItems.Add(entry.TypeText);
                    item.SubItems.Add(entry.Modified == DateTime.MinValue
                        ? "" : entry.Modified.ToString("yyyy-MM-dd HH:mm"));
                    item.SubItems.Add(entry.Permissions);
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            int folders = entries.Count(entry => entry.IsDirectory);
            _status.Text = path + "  ·  " + folders + " 个文件夹，"
                + (entries.Count - folders) + " 个文件";
            ResizeColumns();
            return true;
        }
        catch (Exception ex)
        {
            _status.Text = "读取远程目录失败：" + ex.Message;
            return false;
        }
    }

    public async Task RefreshAsync()
    {
        await LoadDirectoryAsync(_currentPath);
    }

    private void ResizeColumns()
    {
        if (_list.Columns.Count < 5)
        {
            return;
        }
        int fixedWidth = 90 + 88 + 132 + 92;
        _nameColumn.Width = Math.Max(180, _list.ClientSize.Width - fixedWidth - 20);
    }

    private void DrawHeader(Graphics g, Panel header)
    {
        using var background = new SolidBrush(XFtpTheme.PanelAlt);
        g.FillRectangle(background, header.ClientRectangle);
        using var pen = new Pen(XFtpTheme.BorderLight);
        g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        var font = XFtpTheme.Ui(8.4f, FontStyle.Bold);
        int x = 8;
        for (int i = 0; i < _list.Columns.Count; i++)
        {
            int width = _list.Columns[i].Width;
            var align = i == 1 ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(g, _list.Columns[i].Text, font,
                new Rectangle(x, 0, Math.Max(10, width - 12), header.Height),
                XFtpTheme.Muted,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                | TextFormatFlags.EndEllipsis);
            x += width;
        }
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count != 1
            || _list.SelectedItems[0].Tag is not XFtpEntry entry)
        {
            return;
        }
        if (entry.IsDirectory)
        {
            _ = LoadDirectoryAsync(entry.FullPath);
        }
        else
        {
            DownloadRequested?.Invoke(new[] { entry });
        }
    }

    public void Upload()
    {
        if (!IsConnected)
        {
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "上传文件到 " + _currentPath,
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            UploadRequested?.Invoke(dialog.FileNames);
        }
    }

    public void Download()
    {
        var entries = SelectedEntries();
        if (entries.Count > 0)
        {
            DownloadRequested?.Invoke(entries);
        }
    }

    private List<XFtpEntry> SelectedEntries()
    {
        return _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as XFtpEntry)
            .Where(entry => entry != null)
            .Cast<XFtpEntry>()
            .ToList();
    }

    public void SelectAllItems()
    {
        if (_list.Items.Count == 0)
        {
            return;
        }
        _list.BeginUpdate();
        try
        {
            foreach (ListViewItem item in _list.Items)
            {
                item.Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    public List<XFtpEntry> SelectedItems()
    {
        return SelectedEntries();
    }

    public void NewFolder()
    {
        if (!IsConnected)
        {
            return;
        }
        string? name = XFtpPrompt.Show(FindForm(), "新建远程文件夹", "请输入文件夹名称：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        _ = RunRemoteActionAsync(async session =>
        {
            await session.CreateDirectoryAsync(Combine(_currentPath, name),
                CancellationToken.None);
            await LoadDirectoryAsync(_currentPath);
        }, "新建文件夹失败");
    }

    public void RenameSelected()
    {
        if (!IsConnected || _list.SelectedItems.Count != 1
            || _list.SelectedItems[0].Tag is not XFtpEntry entry)
        {
            return;
        }
        string? name = XFtpPrompt.Show(FindForm(), "重命名", "请输入新的名称：", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name)
        {
            return;
        }
        _ = RunRemoteActionAsync(async session =>
        {
            await session.RenameAsync(entry.FullPath, Combine(_currentPath, name),
                CancellationToken.None);
            await LoadDirectoryAsync(_currentPath);
        }, "重命名失败");
    }

    public void DeleteSelected()
    {
        if (!IsConnected)
        {
            return;
        }
        var entries = SelectedEntries();
        if (entries.Count == 0)
        {
            return;
        }
        if (XFtpMessageBox.Show(FindForm(),
                "确定要删除远程选中的 " + entries.Count + " 项吗？\r\n\r\n此操作不可撤销。",
                "XFTP 复刻版", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            != DialogResult.Yes)
        {
            return;
        }
        _ = RunRemoteActionAsync(async session =>
        {
            foreach (var entry in entries)
            {
                await session.DeleteAsync(entry, CancellationToken.None);
            }
            await LoadDirectoryAsync(_currentPath);
        }, "删除失败");
    }

    private async Task RunRemoteActionAsync(Func<IXFtpSession, Task> action, string errorTitle)
    {
        var session = _session;
        if (session == null)
        {
            return;
        }
        try
        {
            await action(session);
        }
        catch (Exception ex)
        {
            XFtpMessageBox.Show(FindForm(), errorTitle + "：\r\n\r\n" + ex.Message,
                "XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Combine(string path, string name) =>
        string.IsNullOrEmpty(path) || path == "/" ? "/" + name
            : path.TrimEnd('/') + "/" + name;

    private static string ParentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "/";
        }
        int index = path.TrimEnd('/').LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional };
        XFtpTheme.StyleMenu(menu);
        menu.Items.Add("下载", null, (_, _) => Download());
        menu.Items.Add("上传文件...", null, (_, _) => Upload());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("新建文件夹...", null, (_, _) => NewFolder());
        menu.Items.Add("重命名...", null, (_, _) => RenameSelected());
        menu.Items.Add("删除", null, (_, _) => DeleteSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("刷新", null, (_, _) => _ = RefreshAsync());
        menu.Items.Add("上级", null, (_, _) =>
        {
            if (IsConnected)
            {
                _ = LoadDirectoryAsync(ParentPath(_currentPath));
            }
        });
        return menu;
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Activated?.Invoke();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        XFtpTheme.ApplyNative(this);
    }
}
