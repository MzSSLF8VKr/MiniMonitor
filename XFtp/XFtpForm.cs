using System.Diagnostics;
using System.Windows.Forms;

namespace MiniMonitor.XFtp;

/// <summary>
/// XFtp 复刻版主窗口：Xftp 8 风格的菜单、功能工具栏、本地 / 远程双栏、
/// 快速连接、传输队列与状态栏，支持 FTP / FTPS / SFTP。
/// </summary>
public sealed class XFtpForm : Form
{
    private readonly XFtpStore _store;
    private readonly XFtpSettings _settings;
    private readonly XFtpHeaderPanel _header;
    private readonly MenuStrip _menu;
    private readonly ToolStrip _toolbar;
    private readonly SplitContainer _split;
    private readonly XFtpLocalPanel _local;
    private readonly XFtpRemotePanel _remote;
    private readonly XFtpTransferPanel _queue;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _stateLabel;
    private readonly ToolStripStatusLabel _serverLabel;
    private readonly ToolStripStatusLabel _transferLabel;
    private readonly ToolStripStatusLabel _clockLabel;
    private readonly ToolStripMenuItem _toolbarItem;
    private readonly ToolStripMenuItem _quickItem;
    private readonly ToolStripMenuItem _queueItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _taskbarItem;
    private readonly ToolStripMenuItem _quickMenuItem;
    private ToolStripButton _quickButton = null!;
    private ToolStripButton _connectButton = null!;
    private ToolStripButton _uploadButton = null!;
    private ToolStripButton _downloadButton = null!;
    private readonly System.Windows.Forms.Timer _clock;

    private readonly List<XFtpTransferTask> _pending = new();
    private readonly object _pumpLock = new();
    private IXFtpSession? _session;
    private XFtpTransferTask? _activeTask;
    private XFtpSite? _lastSite;
    private XFtpSite? _autoSite;
    private bool _pumpRunning;
    private bool _connecting;
    private bool _activeLocal = true;
    private bool _closing;

    public XFtpForm() : this(null, null)
    {
    }

    /// <summary>从 XShell 复刻版打开时，自动使用当前 SSH 会话信息与 shell 目录。</summary>
    internal XFtpForm(XFtpSite? autoSite, string? localStartPath)
    {
        _autoSite = autoSite;
        _store = XFtpStore.Load();
        _settings = _store.Settings;

        Text = "XFTP 复刻版";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 640);
        ClientSize = new Size(
            Math.Max(1100, _settings.WindowWidth),
            Math.Max(680, _settings.WindowHeight));
        BackColor = XFtpTheme.Background;
        ForeColor = XFtpTheme.Text;
        Font = XFtpTheme.Ui9;
        AutoScaleMode = AutoScaleMode.Font;
        DoubleBuffered = true;
        KeyPreview = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 图标读取失败不影响窗口。
        }
        HandleCreated += (_, _) => XFtpTheme.ApplyWindow(this);

        _toolbarItem = new ToolStripMenuItem("工具栏(&T)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowToolbar
        };
        _quickItem = new ToolStripMenuItem("快速连接栏(&Q)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowQuickConnect
        };
        _queueItem = new ToolStripMenuItem("传输队列(&U)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowTransferQueue
        };
        _statusItem = new ToolStripMenuItem("状态栏(&S)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowStatusBar
        };
        _taskbarItem = new ToolStripMenuItem("在 Windows 任务栏显示(&W)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowInTaskbar
        };
        _quickMenuItem = new ToolStripMenuItem("快速连接(&Q)");
        _quickMenuItem.Click += (_, _) =>
        {
            if (!_settings.EnableQuickConnect)
            {
                return;
            }
            _quickItem.Checked = true;
            _remote.FocusQuickConnect();
        };
        ShowInTaskbar = _settings.ShowInTaskbar;

        _header = new XFtpHeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 64,
            Title = "XFTP 复刻版",
            Subtitle = "FTP / FTPS / SFTP 双栏文件传输  ·  参考 Xftp 8 界面"
        };

        _menu = BuildMenu();
        MainMenuStrip = _menu;
        _toolbar = BuildToolbar();

        _local = new XFtpLocalPanel(
            string.IsNullOrWhiteSpace(localStartPath)
                ? _settings.LastLocalFolder : localStartPath)
        {
            Dock = DockStyle.Fill
        };
        _remote = new XFtpRemotePanel { Dock = DockStyle.Fill };
        _remote.QuickConnectVisible = _settings.ShowQuickConnect;

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = XFtpTheme.Border,
            BorderStyle = BorderStyle.None,
            FixedPanel = FixedPanel.None
        };
        _split.Panel1.BackColor = XFtpTheme.Panel;
        _split.Panel2.BackColor = XFtpTheme.Panel;
        _split.Panel1.Controls.Add(_local);
        _split.Panel2.Controls.Add(_remote);

        _queue = new XFtpTransferPanel
        {
            Dock = DockStyle.Bottom,
            Height = 190
        };

        _status = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = XFtpTheme.StatusFrom,
            ForeColor = XFtpTheme.Text,
            SizingGrip = false,
            Font = XFtpTheme.Ui85,
            Padding = new Padding(8, 2, 8, 2),
            RenderMode = ToolStripRenderMode.Professional
        };
        XFtpTheme.StyleMenu(_status, true);
        _stateLabel = new ToolStripStatusLabel("● 未连接")
        {
            AutoSize = false,
            Width = 150,
            ForeColor = XFtpTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _serverLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 380,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _transferLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 200,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _clockLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 150,
            TextAlign = ContentAlignment.MiddleRight
        };
        _status.Items.AddRange(new ToolStripItem[]
        {
            _stateLabel,
            Separator(),
            _serverLabel,
            Separator(),
            _transferLabel,
            new ToolStripStatusLabel { Spring = true },
            _clockLabel
        });

        Controls.Add(_split);
        Controls.Add(_queue);
        Controls.Add(_status);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        Controls.Add(_header);
        Controls.SetChildIndex(_split, 0);
        Controls.SetChildIndex(_queue, 1);
        Controls.SetChildIndex(_status, 2);
        Controls.SetChildIndex(_toolbar, 3);
        Controls.SetChildIndex(_menu, 4);
        Controls.SetChildIndex(_header, 5);

        _queue.Visible = _settings.ShowTransferQueue;
        _status.Visible = _settings.ShowStatusBar;
        _toolbar.Visible = _settings.ShowToolbar;

        WireEvents();

        _clock = new System.Windows.Forms.Timer { Interval = 1000 };
        _clock.Tick += (_, _) =>
        {
            _clockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        };
        _clock.Start();
        _clockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ApplyQuickConnectFeature();
        UpdateCommands();
        UpdateTransferLabel();
    }

    private static ToolStripStatusLabel Separator() => new("│")
    {
        ForeColor = Color.FromArgb(110, XFtpTheme.Accent),
        AutoSize = false,
        Width = 2
    };

    private static ToolStripMenuItem Entry(string text, EventHandler? click = null,
        Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text);
        if (click != null)
        {
            item.Click += click;
        }
        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
        }
        return item;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = XFtpTheme.MenuBack,
            ForeColor = XFtpTheme.Text,
            Font = XFtpTheme.Ui9,
            Padding = new Padding(6, 2, 0, 2),
            RenderMode = ToolStripRenderMode.Professional
        };
        XFtpTheme.StyleMenu(menu);

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.Add(Entry("新建连接(&N)...", (_, _) => ShowSiteManager(),
            Keys.Control | Keys.N));
        file.DropDownItems.Add(_quickMenuItem);
        file.DropDownItems.Add(Entry("站点管理器(&S)...", (_, _) => ShowSiteManager()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Entry("上传文件(&U)...", (_, _) => UploadFilesFromDialog()));
        file.DropDownItems.Add(Entry("下载(&D)", (_, _) => _remote.Download()));
        file.DropDownItems.Add(Entry("刷新(&R)", (_, _) => _ = RefreshAllAsync(),
            Keys.F5));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Entry("断开连接(&T)", (_, _) => DisconnectSession()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Entry("退出(&X)", (_, _) => Close()));

        var edit = new ToolStripMenuItem("编辑(&E)");
        edit.DropDownItems.Add(Entry("全选(&A)", (_, _) => SelectActivePaneAll(),
            Keys.Control | Keys.A));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Entry("上传选中(&U)", (_, _) =>
        {
            _local.UploadSelected();
        }));
        edit.DropDownItems.Add(Entry("下载选中(&D)", (_, _) => _remote.Download()));

        var view = new ToolStripMenuItem("查看(&V)");
        view.DropDownItems.Add(_toolbarItem);
        view.DropDownItems.Add(_quickItem);
        view.DropDownItems.Add(_queueItem);
        view.DropDownItems.Add(_statusItem);
        view.DropDownItems.Add(_taskbarItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Entry("刷新(&R)", (_, _) => _ = RefreshAllAsync()));

        var tools = new ToolStripMenuItem("工具(&T)");
        tools.DropDownItems.Add(Entry("站点管理器(&S)...", (_, _) => ShowSiteManager()));
        tools.DropDownItems.Add(Entry("清空已完成传输(&C)", (_, _) =>
        {
            _queue.ClearFinished();
            UpdateTransferLabel();
        }));
        tools.DropDownItems.Add(Entry("取消全部传输(&A)", (_, _) => CancelAll()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Entry("设置(&S)...", (_, _) => ShowSettings()));
        tools.DropDownItems.Add(Entry("打开本地目录(&O)", (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(_local.CurrentPath)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // 打开目录失败不提示。
            }
        }));

        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(Entry("快捷键(&K)...", (_, _) => ShowShortcuts()));
        help.DropDownItems.Add(Entry("关于 XFTP 复刻版(&A)...", (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, view, tools, help });
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 62,
            GripStyle = ToolStripGripStyle.Hidden,
            ImageScalingSize = new Size(20, 20),
            Padding = new Padding(6, 4, 6, 4)
        };
        XFtpTheme.StyleToolbar(toolbar);

        _connectButton = BigButton("连接", "\uE8B7", XFtpTheme.Green,
            (_, _) =>
            {
                if (_remote.IsConnected)
                {
                    DisconnectSession();
                }
                else
                {
                    ShowSiteManager();
                }
            });
        toolbar.Items.Add(BigButton("站点", "\uE71D", XFtpTheme.Accent,
            (_, _) => ShowSiteManager()));
        _quickButton = BigButton("快速连接", "\uE768", XFtpTheme.Accent,
            (_, _) =>
            {
                if (!_settings.EnableQuickConnect)
                {
                    return;
                }
                _quickItem.Checked = true;
                _remote.FocusQuickConnect();
            });
        toolbar.Items.Add(_quickButton);
        toolbar.Items.Add(_connectButton);
        toolbar.Items.Add(new ToolStripSeparator());
        _uploadButton = BigButton("上传", "\uE898", XFtpTheme.Green,
            (_, _) => _local.UploadSelected());
        _downloadButton = BigButton("下载", "\uE896", XFtpTheme.Accent,
            (_, _) => _remote.Download());
        toolbar.Items.Add(_uploadButton);
        toolbar.Items.Add(_downloadButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(BigButton("刷新", "\uE72C", XFtpTheme.Muted,
            (_, _) => _ = RefreshAllAsync()));
        toolbar.Items.Add(BigButton("新建文件夹", "\uE8F4", XFtpTheme.Accent,
            (_, _) => ActiveNewFolder()));
        toolbar.Items.Add(BigButton("重命名", "\uE70F", XFtpTheme.Orange,
            (_, _) => ActiveRename()));
        toolbar.Items.Add(BigButton("删除", "\uE74D", XFtpTheme.Red,
            (_, _) => ActiveDelete()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(BigButton("传输队列", "\uE81C", XFtpTheme.Muted,
            (_, _) => _queueItem.Checked = !_queueItem.Checked));
        return toolbar;
    }

    private static ToolStripButton BigButton(string text, string glyph, Color color,
        EventHandler click)
    {
        return new ToolStripButton(text, XFtpTheme.Icon(glyph, color, 20), click)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageAboveText,
            TextAlign = ContentAlignment.BottomCenter,
            ImageAlign = ContentAlignment.TopCenter,
            Font = XFtpTheme.Ui85,
            ForeColor = XFtpTheme.Text,
            AutoSize = false,
            Width = 64,
            Height = 52,
            Margin = new Padding(1, 0, 1, 0)
        };
    }

    private void WireEvents()
    {
        _local.UploadRequested += paths => EnqueueUpload(paths, _remote.CurrentPath);
        _local.RemoteDropRequested += entries =>
            _ = EnqueueDownloadAsync(entries, _local.CurrentPath);
        _local.SelectionChanged += UpdateCommands;
        _local.Activated += () =>
        {
            _activeLocal = true;
            UpdateCommands();
        };

        _remote.QuickConnectRequested += (site, save) => _ = ConnectAsync(site, save);
        _remote.DownloadRequested += entries =>
            _ = EnqueueDownloadAsync(entries, _local.CurrentPath);
        _remote.UploadRequested += files => EnqueueUpload(files, _remote.CurrentPath);
        _remote.SelectionChanged += UpdateCommands;
        _remote.SessionStateChanged += UpdateCommands;
        _remote.Activated += () =>
        {
            _activeLocal = false;
            UpdateCommands();
        };

        _queue.CancelAllRequested += CancelAll;

        _toolbarItem.CheckedChanged += (_, _) =>
        {
            _toolbar.Visible = _toolbarItem.Checked;
            _settings.ShowToolbar = _toolbarItem.Checked;
            _store.Save();
        };
        _quickItem.CheckedChanged += (_, _) =>
        {
            _remote.QuickConnectVisible = _quickItem.Checked;
            _settings.ShowQuickConnect = _quickItem.Checked;
            _store.Save();
        };
        _queueItem.CheckedChanged += (_, _) =>
        {
            _queue.Visible = _queueItem.Checked;
            _settings.ShowTransferQueue = _queueItem.Checked;
            _store.Save();
        };
        _statusItem.CheckedChanged += (_, _) =>
        {
            _status.Visible = _statusItem.Checked;
            _settings.ShowStatusBar = _statusItem.Checked;
            _store.Save();
        };
        _taskbarItem.CheckedChanged += (_, _) =>
        {
            bool wasVisible = Visible;
            ShowInTaskbar = _taskbarItem.Checked;
            if (wasVisible && !Visible)
            {
                Show();
            }
            _settings.ShowInTaskbar = _taskbarItem.Checked;
            _store.Save();
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.N))
        {
            ShowSiteManager();
            return true;
        }
        if (keyData == Keys.F5)
        {
            _ = RefreshAllAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.F))
        {
            if (_settings.EnableQuickConnect)
            {
                _quickItem.Checked = true;
                _remote.FocusQuickConnect();
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        XFtpTheme.ApplyWindow(this);
        XFtpTheme.ApplyNative(this);
        try
        {
            // 必须在窗口完成布局后再设置最小宽度与分隔位置，
            // 否则 SplitterDistance 可能落在 Panel1MinSize / Panel2MinSize 之外。
            int width = _split.Width;
            if (width > 0)
            {
                int min1 = Math.Min(320, Math.Max(140, width / 4));
                int min2 = Math.Min(360, Math.Max(140, width / 4));
                int max = width - min2 - _split.SplitterWidth;
                if (max > min1)
                {
                    _split.Panel1MinSize = min1;
                    _split.Panel2MinSize = min2;
                    _split.SplitterDistance = Math.Clamp(width / 2, min1, max);
                }
            }
        }
        catch
        {
            // 窗口过窄时保持默认分隔位置。
        }
        if (_settings.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
        if (_autoSite != null)
        {
            var site = _autoSite;
            _autoSite = null;
            _remote.FillQuickConnect(site);
            _ = ConnectAsync(site, false);
            return;
        }
        _lastSite = _store.Sites
            .OrderByDescending(site => site.LastUsedAt)
            .FirstOrDefault();
        if (_lastSite != null)
        {
            _remote.FillQuickConnect(_lastSite);
        }
    }

    /// <summary>已打开的 XFtp 窗口切换到新的 XShell 会话并自动连接。</summary>
    internal void AutoConnect(XFtpSite site, string? localStartPath)
    {
        if (IsDisposed)
        {
            return;
        }
        if (!Visible)
        {
            Show();
        }
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
        if (!string.IsNullOrWhiteSpace(localStartPath) && Directory.Exists(localStartPath))
        {
            _ = _local.NavigateAsync(localStartPath);
        }
        _remote.FillQuickConnect(site);
        _ = ConnectAsync(site, false);
    }

    // ================= 设置 =================

    private void ApplyQuickConnectFeature()
    {
        bool enabled = _settings.EnableQuickConnect;
        _quickButton.Visible = enabled;
        _quickMenuItem.Visible = enabled;
        _quickItem.Visible = enabled;
        _remote.QuickConnectVisible = enabled && _quickItem.Checked;
    }

    private void ShowSettings()
    {
        using var dialog = new XFtpSettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _settings.EnableQuickConnect = dialog.EnableQuickConnect;
        _settings.ShowToolbar = dialog.ShowToolbar;
        _settings.ShowTransferQueue = dialog.ShowTransferQueue;
        _settings.ShowStatusBar = dialog.ShowStatusBar;
        _settings.ShowInTaskbar = dialog.ShowTaskbar;
        _toolbarItem.Checked = _settings.ShowToolbar;
        _queueItem.Checked = _settings.ShowTransferQueue;
        _statusItem.Checked = _settings.ShowStatusBar;
        _taskbarItem.Checked = _settings.ShowInTaskbar;
        ApplyQuickConnectFeature();
        _store.Save();
    }

    // ================= 连接 =================

    private void ShowSiteManager()
    {
        using var dialog = new XFtpSiteManagerForm(_store, _lastSite);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedSite != null)
        {
            _ = ConnectAsync(dialog.SelectedSite, true);
        }
    }

    private async Task ConnectAsync(XFtpSite site, bool saveSite)
    {
        if (_connecting)
        {
            return;
        }
        DisconnectSession();
        _connecting = true;
        _stateLabel.Text = "● 正在连接 " + site.Host + " ...";
        _stateLabel.ForeColor = XFtpTheme.Orange;
        UpdateCommands();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        IXFtpSession? session = null;
        try
        {
            session = XFtpSessionFactory.Create(site);
            await session.ConnectAsync(cts.Token);
            _session = session;
            _lastSite = site;
            _remote.FillQuickConnect(site);
            _remote.AttachSession(session);
            // SFTP 默认进入 /opt；未指定远程目录的 FTP / FTPS 使用服务器登录目录。
            string path = string.IsNullOrWhiteSpace(site.RemotePath)
                ? (site.Protocol == XFtpProtocol.Sftp ? "/opt" : session.HomePath)
                : site.RemotePath;
            bool loaded = await _remote.LoadDirectoryAsync(path);
            if (!loaded && site.Protocol == XFtpProtocol.Sftp
                && !string.Equals(path, session.HomePath, StringComparison.Ordinal))
            {
                // /opt 不存在或没有权限时，回退到 SFTP 登录目录。
                await _remote.LoadDirectoryAsync(session.HomePath);
            }
            if (!string.IsNullOrWhiteSpace(site.LocalPath) && Directory.Exists(site.LocalPath))
            {
                await _local.NavigateAsync(site.LocalPath);
            }
            if (saveSite)
            {
                _lastSite = _store.AddOrUpdate(site);
            }
            _settings.LastLocalFolder = _local.CurrentPath;
            _stateLabel.Text = "● 已连接";
            _stateLabel.ForeColor = XFtpTheme.Green;
            _serverLabel.Text = site.DisplayAddress
                + (string.IsNullOrWhiteSpace(site.User) ? "" : "  ·  " + site.User);
            Text = "XFTP 复刻版 - "
                + (string.IsNullOrWhiteSpace(site.Name) ? site.Host : site.Name);
        }
        catch (Exception ex)
        {
            try { session?.Dispose(); } catch { }
            _session = null;
            _remote.AttachSession(null);
            _stateLabel.Text = "● 连接失败";
            _stateLabel.ForeColor = XFtpTheme.Red;
            XFtpMessageBox.Show(this, "连接 " + site.Host + " 失败：\r\n\r\n" + ex.Message,
                "XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _connecting = false;
            UpdateCommands();
        }
    }

    private void DisconnectSession()
    {
        CancelAll();
        var session = _session;
        _session = null;
        _remote.AttachSession(null);
        try { session?.Dispose(); } catch { }
        _serverLabel.Text = "";
        _stateLabel.Text = "● 未连接";
        _stateLabel.ForeColor = XFtpTheme.Muted;
        Text = "XFTP 复刻版";
        UpdateCommands();
    }

    private async Task RefreshAllAsync()
    {
        await _local.RefreshAsync();
        if (_remote.IsConnected)
        {
            await _remote.RefreshAsync();
        }
    }

    // ================= 工具栏命令 =================

    private void ActiveNewFolder()
    {
        if (_activeLocal || !_remote.IsConnected)
        {
            _local.NewFolder();
        }
        else
        {
            _remote.NewFolder();
        }
    }

    private void ActiveRename()
    {
        if (_activeLocal || !_remote.IsConnected)
        {
            _local.RenameSelected();
        }
        else
        {
            _remote.RenameSelected();
        }
    }

    private void ActiveDelete()
    {
        if (_activeLocal || !_remote.IsConnected)
        {
            _local.DeleteSelected();
        }
        else
        {
            _remote.DeleteSelected();
        }
    }

    private void UploadFilesFromDialog()
    {
        if (!_remote.IsConnected)
        {
            XFtpMessageBox.Show(this, "请先连接远程服务器。", "XFTP 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "上传文件到 " + _remote.CurrentPath,
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(_local.CurrentPath)
                ? _local.CurrentPath : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            EnqueueUpload(dialog.FileNames, _remote.CurrentPath);
        }
    }

    private void SelectActivePaneAll()
    {
        if (_activeLocal)
        {
            _local.SelectAllItems();
        }
        else
        {
            _remote.SelectAllItems();
        }
    }

    private void UpdateCommands()
    {
        bool connected = _remote.IsConnected;
        _connectButton.Text = connected ? "断开" : "连接";
        _connectButton.Image = XFtpTheme.Icon(
            connected ? "\uE711" : "\uE8B7",
            connected ? XFtpTheme.Red : XFtpTheme.Green, 20);
        _uploadButton.Enabled = connected && _local.SelectedPaths().Count > 0;
        _downloadButton.Enabled = connected && _remote.SelectedItems().Count > 0;
        _connectButton.Invalidate();
    }

    // ================= 传输队列 =================

    private void EnqueueUpload(IReadOnlyList<string> localPaths, string remoteDir)
    {
        if (!_remote.IsConnected || _session == null)
        {
            XFtpMessageBox.Show(this, "请先连接远程服务器。", "XFTP 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var tasks = new List<XFtpTransferTask>();
        foreach (string path in localPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    string name = Path.GetFileName(path.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "上传目录";
                    }
                    string remoteRoot = CombineRemote(remoteDir, name);
                    AddDirectoryUploadTasks(tasks, path, remoteRoot);
                }
                else if (File.Exists(path))
                {
                    tasks.Add(new XFtpTransferTask
                    {
                        Name = Path.GetFileName(path),
                        LocalPath = path,
                        RemotePath = CombineRemote(remoteDir, Path.GetFileName(path)),
                        IsUpload = true,
                        Total = new FileInfo(path).Length
                    });
                }
            }
            catch (Exception ex)
            {
                XFtpMessageBox.Show(this, "无法准备上传：" + path + "\r\n\r\n" + ex.Message,
                    "XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        EnqueueTasks(tasks);
    }

    private static void AddDirectoryUploadTasks(List<XFtpTransferTask> tasks,
        string localRoot, string remoteRoot)
    {
        tasks.Add(new XFtpTransferTask
        {
            Name = remoteRoot + "/",
            RemotePath = remoteRoot,
            IsUpload = true,
            IsDirectoryCreate = true
        });
        foreach (string dir in Directory.EnumerateDirectories(localRoot, "*",
            SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(localRoot, dir).Replace('\\', '/');
            tasks.Add(new XFtpTransferTask
            {
                Name = relative + "/",
                RemotePath = remoteRoot + "/" + relative,
                IsUpload = true,
                IsDirectoryCreate = true
            });
        }
        foreach (string file in Directory.EnumerateFiles(localRoot, "*",
            SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(localRoot, file).Replace('\\', '/');
            tasks.Add(new XFtpTransferTask
            {
                Name = relative,
                LocalPath = file,
                RemotePath = remoteRoot + "/" + relative,
                IsUpload = true,
                Total = new FileInfo(file).Length
            });
        }
    }

    private async Task EnqueueDownloadAsync(IReadOnlyList<XFtpEntry> entries,
        string localDir)
    {
        var session = _session;
        if (session == null || !session.IsConnected)
        {
            XFtpMessageBox.Show(this, "请先连接远程服务器。", "XFTP 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var tasks = new List<XFtpTransferTask>();
        _stateLabel.Text = "● 正在准备下载...";
        try
        {
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    await AddDirectoryDownloadTasksAsync(tasks, session, entry,
                        Path.Combine(localDir, entry.Name));
                }
                else
                {
                    tasks.Add(new XFtpTransferTask
                    {
                        Name = entry.Name,
                        LocalPath = Path.Combine(localDir, entry.Name),
                        RemotePath = entry.FullPath,
                        Total = entry.Size
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _stateLabel.Text = "● 准备下载失败";
            XFtpMessageBox.Show(this, "准备下载失败：\r\n\r\n" + ex.Message,
                "XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            if (_remote.IsConnected)
            {
                _stateLabel.Text = "● 已连接";
            }
        }
        EnqueueTasks(tasks);
    }

    private static async Task AddDirectoryDownloadTasksAsync(
        List<XFtpTransferTask> tasks, IXFtpSession session,
        XFtpEntry directory, string localPath)
    {
        tasks.Add(new XFtpTransferTask
        {
            Name = directory.Name + "\\",
            LocalPath = localPath,
            IsDirectoryCreate = true
        });
        var children = await session.ListAsync(directory.FullPath, CancellationToken.None);
        foreach (var child in children)
        {
            if (child.IsDirectory)
            {
                await AddDirectoryDownloadTasksAsync(tasks, session, child,
                    Path.Combine(localPath, child.Name));
            }
            else
            {
                tasks.Add(new XFtpTransferTask
                {
                    Name = child.Name,
                    LocalPath = Path.Combine(localPath, child.Name),
                    RemotePath = child.FullPath,
                    Total = child.Size
                });
            }
        }
    }

    private void EnqueueTasks(IReadOnlyList<XFtpTransferTask> tasks)
    {
        if (tasks.Count == 0)
        {
            return;
        }
        foreach (var task in tasks)
        {
            task.State = XFtpTaskState.Waiting;
            _queue.AddTask(task);
            lock (_pumpLock)
            {
                _pending.Add(task);
            }
        }
        if (!_queueItem.Checked)
        {
            _queueItem.Checked = true;
        }
        UpdateTransferLabel();
        StartPump();
    }

    private void StartPump()
    {
        lock (_pumpLock)
        {
            if (_pumpRunning)
            {
                return;
            }
            _pumpRunning = true;
        }
        _ = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        while (!_closing)
        {
            XFtpTransferTask? task;
            lock (_pumpLock)
            {
                if (_pending.Count == 0)
                {
                    _pumpRunning = false;
                    return;
                }
                task = _pending[0];
                _pending.RemoveAt(0);
            }
            if (task.Cancellation.IsCancellationRequested)
            {
                task.State = XFtpTaskState.Canceled;
                _queue.UpdateTask(task);
                continue;
            }
            await ExecuteTaskAsync(task);
            bool idle;
            lock (_pumpLock)
            {
                idle = _pending.Count == 0;
            }
            if (idle)
            {
                try
                {
                    if (task.IsUpload && _remote.IsConnected)
                    {
                        await InvokeAsync(() => _remote.RefreshAsync());
                    }
                    else if (!task.IsUpload)
                    {
                        await InvokeAsync(() => _local.RefreshAsync());
                    }
                }
                catch
                {
                    // 刷新失败不影响后续任务。
                }
                UpdateTransferLabel();
            }
        }
    }

    /// <summary>把后台传输完成后的目录刷新封送到 UI 线程。</summary>
    private Task InvokeAsync(Func<Task> action)
    {
        if (IsDisposed)
        {
            return Task.CompletedTask;
        }
        if (!InvokeRequired)
        {
            return action();
        }
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        return completion.Task;
    }

    private async Task ExecuteTaskAsync(XFtpTransferTask task)
    {
        var session = _session;
        if (session == null || !session.IsConnected)
        {
            task.State = XFtpTaskState.Failed;
            task.Error = "未连接服务器。";
            _queue.UpdateTask(task);
            return;
        }
        task.State = XFtpTaskState.Running;
        task.StartedAt = DateTime.Now;
        task.LastUpdate = DateTime.Now;
        task.LastDone = 0;
        task.Speed = 0;
        _activeTask = task;
        _queue.UpdateTask(task);
        try
        {
            var token = task.Cancellation.Token;
            if (task.IsDirectoryCreate)
            {
                if (task.IsUpload)
                {
                    try
                    {
                        await session.CreateDirectoryAsync(task.RemotePath, token);
                    }
                    catch
                    {
                        // 目录可能已存在，继续上传文件。
                    }
                }
                else
                {
                    Directory.CreateDirectory(task.LocalPath);
                }
            }
            else if (task.IsUpload)
            {
                await session.UploadFileAsync(task.LocalPath, task.RemotePath,
                    done => ReportProgress(task, done), token);
            }
            else
            {
                await session.DownloadFileAsync(task.RemotePath, task.LocalPath,
                    done => ReportProgress(task, done), token);
            }
            task.State = XFtpTaskState.Success;
            if (task.Total > 0)
            {
                task.Done = task.Total;
            }
            task.Speed = 0;
        }
        catch (OperationCanceledException)
        {
            task.State = XFtpTaskState.Canceled;
        }
        catch (Exception ex)
        {
            task.State = XFtpTaskState.Failed;
            task.Error = ex.Message;
        }
        finally
        {
            _activeTask = null;
            _queue.UpdateTask(task);
            UpdateTransferLabel();
        }
    }

    private void ReportProgress(XFtpTransferTask task, long done)
    {
        task.Done = done;
        var now = DateTime.UtcNow;
        double seconds = (now - task.LastUpdate).TotalSeconds;
        if (seconds < 0.35)
        {
            return;
        }
        task.Speed = Math.Max(0, (done - task.LastDone) / seconds);
        task.LastUpdate = now;
        task.LastDone = done;
        _queue.UpdateTask(task);
    }

    private void CancelAll()
    {
        _activeTask?.Cancellation.Cancel();
        lock (_pumpLock)
        {
            foreach (var task in _pending)
            {
                task.Cancellation.Cancel();
                task.State = XFtpTaskState.Canceled;
                _queue.UpdateTask(task);
            }
            _pending.Clear();
        }
        UpdateTransferLabel();
    }

    private void UpdateTransferLabel()
    {
        if (_status.InvokeRequired)
        {
            try { BeginInvoke(UpdateTransferLabel); } catch { }
            return;
        }
        int pending;
        lock (_pumpLock)
        {
            pending = _pending.Count + (_activeTask != null ? 1 : 0);
        }
        _transferLabel.Text = pending > 0 ? "传输中 " + pending + " 项" : "";
    }

    // ================= 帮助 =================

    private void ShowShortcuts()
    {
        XFtpMessageBox.Show(this,
            "F5\t刷新本地与远程目录\r\n"
            + "Ctrl+N\t打开站点管理器\r\n"
            + "Ctrl+Shift+F\t显示并聚焦快速连接栏\r\n"
            + "Ctrl+A\t全选当前窗格\r\n"
            + "Enter（路径栏）\t跳转到输入的目录\r\n"
            + "双击目录\t进入目录\r\n"
            + "双击远程文件\t下载\r\n\r\n"
            + "上传：把本地文件拖到右侧远程窗格，或点击“上传”。\r\n"
            + "下载：把远程文件 / 目录拖到左侧本地窗格，或点击“下载”。",
            "XFTP 复刻版 - 快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        string version;
        try
        {
            version = Application.ProductVersion.Split('+')[0];
        }
        catch
        {
            version = "1.66.4";
        }
        XFtpMessageBox.Show(this,
            "XFTP 复刻版  v" + version + "\r\n\r\n"
            + "参考 Xftp 8 界面的双栏 FTP / FTPS / SFTP 文件传输工具。\r\n"
            + "支持拖拽上传下载、传输队列、站点管理器与深色主题。\r\n"
            + "所有连接信息仅保存在本机，密码使用 Windows DPAPI 加密。\r\n\r\n"
            + "2026年制作",
            "关于 XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string CombineRemote(string path, string name) =>
        string.IsNullOrEmpty(path) || path == "/" ? "/" + name
            : path.TrimEnd('/') + "/" + name;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        CancelAll();
        var session = _session;
        _session = null;
        try { session?.Dispose(); } catch { }
        _settings.ShowQuickConnect = _quickItem.Checked;
        _settings.ShowTransferQueue = _queueItem.Checked;
        _settings.ShowStatusBar = _statusItem.Checked;
        _settings.ShowInTaskbar = _taskbarItem.Checked;
        _settings.ShowToolbar = _toolbarItem.Checked;
        _settings.LastLocalFolder = _local.CurrentPath;
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowWidth = ClientSize.Width;
            _settings.WindowHeight = ClientSize.Height;
        }
        _store.Save();
        _clock.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Dispose();
        }
        base.Dispose(disposing);
    }
}
