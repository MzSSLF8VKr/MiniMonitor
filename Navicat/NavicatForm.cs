using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>
/// Navicat Premium 风格主窗口：连接树 + 多文档标签 + 查询 / 表数据 / 表设计器，
/// 以及数据传输、同步、备份、计划任务、用户管理、服务器监控、ER 图、查询构建器、报表等工具。
/// </summary>
public sealed class NavicatForm : Form
{
    private const string AppTitle = "Navicat 复刻版";
    private readonly ConnectionStore _store;
    private readonly Dictionary<string, DbSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NvScheduledTask> _tasks;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _runningTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SplitContainer _split;
    private readonly TreeView _tree;
    private readonly TextBox _searchBox;
    private readonly NvTabHost _tabs;
    private readonly NavicatWelcomePage _welcome;
    private readonly MenuStrip _menu;
    private readonly ToolStrip _toolbar;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _statusServer;
    private readonly ToolStripStatusLabel _statusInfo;
    private readonly ToolStripStatusLabel _statusClock;
    private readonly ToolStripProgressBar _progress;
    private readonly System.Windows.Forms.Timer _clockTimer;
    private readonly System.Windows.Forms.Timer _schedulerTimer;
    private readonly Font _treeNodeFont = NvTheme.Ui(9.2f);
    private readonly Font _treeBoldFont = NvTheme.Ui(9.2f, FontStyle.Bold);
    private readonly Font _treeSmallFont = NvTheme.Ui(8.2f);
    private ConfigurationProfileHolder _last = new();
    private TreeNode? _hoverNode;
    private string _searchText = "";
    private bool _closing;

    private sealed class ConfigurationProfileHolder
    {
        public ConnectionProfile? Profile { get; set; }
        public string? Database { get; set; }
    }

    public NavicatForm()
    {
        NvLog.Startup();
        _store = ConnectionStore.Load();
        _tasks = NvTaskStore.Load();
        _last = new ConfigurationProfileHolder();

        Text = AppTitle + " - MiniMonitor";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 700);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Font;
        try
        {
            using var iconStream = typeof(NavicatForm).Assembly.GetManifestResourceStream("MiniMonitor.app.ico");
            if (iconStream != null)
            {
                Icon = new Icon(iconStream);
            }
        }
        catch
        {
            // 图标加载失败不影响主功能
        }

        _menu = BuildMenu();
        _toolbar = BuildToolbar();

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            BackColor = NvTheme.BorderLight
        };

        var leftHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = NvTheme.HeaderBack
        };
        leftHeader.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.Border);
            e.Graphics.DrawLine(pen, 0, leftHeader.Height - 1, leftHeader.Width, leftHeader.Height - 1);
        };
        leftHeader.Controls.Add(new Label
        {
            Text = "连接",
            Location = new Point(10, 8),
            AutoSize = true,
            Font = NvTheme.Ui(9.6f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        var addConnection = new Button
        {
            Text = "+",
            Size = new Size(26, 24),
            Location = new Point(0, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = NvTheme.HeaderBack,
            ForeColor = NvTheme.Accent,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        addConnection.FlatAppearance.BorderSize = 0;
        addConnection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addConnection.Click += async (_, _) => await NewConnectionAsync();
        var refresh = new Button
        {
            Text = "⟳",
            Size = new Size(26, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = NvTheme.HeaderBack,
            ForeColor = NvTheme.Muted,
            Font = new Font("Segoe UI", 10f),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        refresh.FlatAppearance.BorderSize = 0;
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Click += (_, _) => RebuildTree();
        leftHeader.Controls.Add(addConnection);
        leftHeader.Controls.Add(refresh);
        leftHeader.Resize += (_, _) =>
        {
            addConnection.Location = new Point(leftHeader.Width - 34, 6);
            refresh.Location = new Point(leftHeader.Width - 64, 6);
        };

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = NvTheme.Panel };
        _searchBox = NvTheme.SearchBox("搜索连接 / 表 / 视图…");
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(6, 5, 6, 5);
        var searchHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 5, 6, 5),
            BackColor = NvTheme.Panel
        };
        searchHost.Controls.Add(_searchBox);
        searchPanel.Controls.Add(searchHost);
        _searchBox.TextChanged += (_, _) =>
        {
            _searchText = _searchBox.Text.Trim();
            _tree.Invalidate();
        };
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                JumpToSearch();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _tree = new TreeView { Dock = DockStyle.Fill };
        NvTheme.StyleTree(_tree);
        _tree.ItemHeight = 24;
        _tree.HideSelection = false;
        _tree.ShowNodeToolTips = true;
        _tree.DrawNode += TreeDrawNode;
        _tree.AfterExpand += TreeAfterExpand;
        _tree.NodeMouseDoubleClick += TreeNodeDoubleClick;
        _tree.NodeMouseClick += TreeNodeClick;
        _tree.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var node = _tree.GetNodeAt(e.Location);
                if (node != null && node.Tag is NvNodeTag rightTag)
                {
                    _tree.SelectedNode = node;
                    _tree.ContextMenuStrip = BuildNodeMenu(node, rightTag);
                }
            }
        };
        _tree.MouseMove += (_, e) =>
        {
            var node = _tree.GetNodeAt(e.Location);
            if (!ReferenceEquals(node, _hoverNode))
            {
                var old = _hoverNode;
                _hoverNode = node;
                if (old != null)
                {
                    _tree.Invalidate(new Rectangle(0, old.Bounds.Top, _tree.ClientSize.Width, old.Bounds.Height));
                }
                if (node != null)
                {
                    _tree.Invalidate(new Rectangle(0, node.Bounds.Top, _tree.ClientSize.Width, node.Bounds.Height));
                }
            }
        };
        _tree.MouseLeave += (_, _) =>
        {
            if (_hoverNode != null)
            {
                var old = _hoverNode;
                _hoverNode = null;
                _tree.Invalidate(new Rectangle(0, old.Bounds.Top, _tree.ClientSize.Width, old.Bounds.Height));
            }
        };
        _tree.AfterSelect += (_, _) => UpdateSelectionStatus();
        _tree.ContextMenuStrip = new ContextMenuStrip();

        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = NvTheme.Panel };
        leftPanel.Controls.Add(_tree);
        leftPanel.Controls.Add(searchPanel);
        leftPanel.Controls.Add(leftHeader);
        _split.Panel1.Controls.Add(leftPanel);

        _welcome = new NavicatWelcomePage { Dock = DockStyle.Fill };
        _welcome.NewConnectionRequested += async (_, _) => await NewConnectionAsync();
        _welcome.QuickConnectRequested += async (_, kind) => await NewConnectionAsync(kind);
        _welcome.OpenSqliteRequested += async (_, _) => await OpenSqliteAsync();
        _welcome.ImportRequested += (_, _) => ImportConnections();
        _welcome.RecentOpenRequested += async (_, profile) => await OpenRecentAsync(profile);
        _welcome.ToolRequested += async (_, key) => await WelcomeToolAsync(key);

        _tabs = new NvTabHost { Dock = DockStyle.Fill };
        _tabs.TabClosed += (_, tab) =>
        {
            if (_tabs.IsEmpty)
            {
                ShowWelcome();
            }
        };
        _split.Panel2.Controls.Add(_tabs);

        _status = new StatusStrip
        {
            BackColor = NvTheme.HeaderBack,
            Font = NvTheme.Ui(8.6f),
            SizingGrip = false
        };
        _statusServer = new ToolStripStatusLabel
        {
            Text = "未连接",
            ForeColor = NvTheme.Muted,
            Spring = false
        };
        _statusInfo = new ToolStripStatusLabel
        {
            Text = "就绪",
            ForeColor = NvTheme.Muted,
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _progress = new ToolStripProgressBar
        {
            Visible = false,
            Width = 140,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24
        };
        _statusClock = new ToolStripStatusLabel
        {
            Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ForeColor = NvTheme.Muted,
            Spring = false
        };
        _status.Items.AddRange(new ToolStripItem[]
        {
            _statusServer, new ToolStripSeparator(), _statusInfo, _progress, _statusClock
        });

        Controls.Add(_split);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        Controls.Add(_status);

        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) =>
        {
            _statusClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        };
        _schedulerTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _schedulerTimer.Tick += async (_, _) => await CheckScheduledTasksAsync();

        Load += (_, _) =>
        {
            try
            {
                _split.Panel1MinSize = 200;
                _split.Panel2MinSize = 500;
                _split.SplitterDistance = 280;
            }
            catch
            {
                // 窗口尺寸不足时保持默认分隔位置
            }
            RebuildTree();
            if (NvSettingsStore.Current.ShowWelcomePage)
            {
                ShowWelcome();
            }
            RefreshRecent();
            _clockTimer.Start();
            _schedulerTimer.Start();
            _ = CheckScheduledTasksAsync();
            _searchBox.Focus();
        };
        KeyDown += MainKeyDown;
        FormClosing += NavicatFormClosing;
    }

    // ================= 菜单 / 工具栏 =================

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        NvTheme.ApplyMenu(menu);

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.Add(Item("新建连接(&N)", Keys.Control | Keys.N, async () => await NewConnectionAsync(), NvIcon.NewConnection));
        file.DropDownItems.Add(Item("新建查询(&Q)", Keys.Control | Keys.T, async () => await NewQueryAsync(), NvIcon.Query));
        file.DropDownItems.Add(Item("打开 SQL 文件(&O)…", Keys.Control | Keys.O, () => OpenSqlFile(), NvIcon.Open));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("导入连接(&I)…", Keys.None, ImportConnections, NvIcon.Import));
        file.DropDownItems.Add(Item("导出连接(&E)…", Keys.None, ExportConnections, NvIcon.Export));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("关闭当前标签(&W)", Keys.Control | Keys.W, CloseActiveTab, NvIcon.Close));
        file.DropDownItems.Add(Item("退出(&X)", Keys.None, Close, NvIcon.Drop));

        var edit = new ToolStripMenuItem("编辑(&E)");
        edit.DropDownItems.Add(Item("复制(&C)", Keys.Control | Keys.C, () => SendKeys.Send("^C"), NvIcon.Copy));
        edit.DropDownItems.Add(Item("粘贴(&V)", Keys.Control | Keys.V, () => SendKeys.Send("^V"), NvIcon.File));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item("格式化 SQL(&F)", Keys.Control | Keys.Shift | Keys.F, FormatActiveSql, NvIcon.Format));
        edit.DropDownItems.Add(Item("查找(&F)…", Keys.Control | Keys.F, FindInActiveEditor, NvIcon.Search));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item("选项(&O)…", Keys.None, OpenSettings, NvIcon.Server));

        var view = new ToolStripMenuItem("查看(&V)");
        view.DropDownItems.Add(Item("刷新连接树(&R)", Keys.F5, () => RebuildTree(), NvIcon.Refresh));
        view.DropDownItems.Add(Item("欢迎页(&W)", Keys.None, ShowWelcome, NvIcon.Logo));
        view.DropDownItems.Add(Item("显示/隐藏连接窗格(&P)", Keys.None, ToggleLeftPane, NvIcon.Folder));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("全屏(&F)", Keys.F11, ToggleFullScreen, NvIcon.Er));

        var tools = new ToolStripMenuItem("工具(&T)");
        tools.DropDownItems.Add(Item("数据传输(&T)…", Keys.None, async () => await OpenTransferAsync(), NvIcon.Transfer));
        tools.DropDownItems.Add(Item("数据同步(&D)…", Keys.None, async () => await OpenDataSyncAsync(), NvIcon.Sync));
        tools.DropDownItems.Add(Item("结构同步(&S)…", Keys.None, async () => await OpenStructureSyncAsync(), NvIcon.Structure));
        tools.DropDownItems.Add(Item("数据生成(&G)…", Keys.None, async () => await OpenDataGeneratorAsync(), NvIcon.Add));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("备份 / 还原(&B)…", Keys.None, async () => await OpenBackupAsync(), NvIcon.Backup));
        tools.DropDownItems.Add(Item("计划任务(&J)…", Keys.None, OpenScheduler, NvIcon.Schedule));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("用户管理(&U)…", Keys.None, async () => await OpenUsersAsync(), NvIcon.User));
        tools.DropDownItems.Add(Item("服务器监控(&M)…", Keys.None, async () => await OpenMonitorAsync(), NvIcon.Server));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("ER 图(&E)…", Keys.None, async () => await OpenErAsync(), NvIcon.Er));
        tools.DropDownItems.Add(Item("查询构建器(&Q)…", Keys.None, async () => await OpenQueryBuilderAsync(), NvIcon.Chart));
        tools.DropDownItems.Add(Item("报表(&R)…", Keys.None, async () => await OpenReportAsync(), NvIcon.Report));
        tools.DropDownItems.Add(Item("数据库文档(&D)…", Keys.None, async () => await OpenDbDocAsync(), NvIcon.File));
        tools.DropDownItems.Add(Item("数据库搜索(&F)…", Keys.Control | Keys.Shift | Keys.S, async () => await OpenSearchAsync(), NvIcon.Search));

        var window = new ToolStripMenuItem("窗口(&W)");
        window.DropDownItems.Add(Item("下一个标签(&N)", Keys.Control | Keys.Tab, NextTab, NvIcon.Folder));
        window.DropDownItems.Add(Item("关闭全部标签(&A)", Keys.None, CloseAllTabs, NvIcon.Close));

        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(Item("Navicat 复刻版 帮助(&H)", Keys.F1, ShowHelp, NvIcon.Info));
        help.DropDownItems.Add(Item("查看日志(&L)…", Keys.Control | Keys.Shift | Keys.L, OpenLogViewer, NvIcon.File));
        help.DropDownItems.Add(Item("打开日志目录(&O)", Keys.None, NvLog.OpenFolder, NvIcon.FolderOpen));
        help.DropDownItems.Add(Item("复制日志路径(&P)", Keys.None, () =>
        {
            Clipboard.SetText(NvLog.Folder);
            SetStatus("日志目录已复制：" + NvLog.Folder);
        }, NvIcon.Copy));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("关于(&A)", Keys.None, ShowAbout, NvIcon.Logo));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, view, tools, window, help });
        return menu;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, Action action, NvIcon icon)
    {
        var item = new ToolStripMenuItem(text, NvIcons.Get(icon, 16));
        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
        }
        item.Click += (_, _) => action();
        return item;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 64,
            ImageScalingSize = new Size(24, 24),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 3, 6, 3)
        };
        NvTheme.ApplyToolStrip(toolbar);
        toolbar.Items.Add(BigButton("连接", NvIcon.NewConnection, async () => await NewConnectionAsync()));
        toolbar.Items.Add(BigButton("查询", NvIcon.Query, async () => await NewQueryAsync()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(BigButton("表", NvIcon.Table, async () => await NewTableAsync()));
        toolbar.Items.Add(BigButton("视图", NvIcon.View, async () => await NewViewAsync()));
        toolbar.Items.Add(BigButton("用户", NvIcon.User, async () => await OpenUsersAsync()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(BigButton("备份", NvIcon.Backup, async () => await OpenBackupAsync()));
        toolbar.Items.Add(BigButton("计划", NvIcon.Schedule, OpenScheduler));
        toolbar.Items.Add(BigButton("报表", NvIcon.Report, async () => await OpenReportAsync()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(BigButton("传输", NvIcon.Transfer, async () => await OpenTransferAsync()));
        toolbar.Items.Add(BigButton("数据同步", NvIcon.Sync, async () => await OpenDataSyncAsync()));
        toolbar.Items.Add(BigButton("结构同步", NvIcon.Structure, async () => await OpenStructureSyncAsync()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(BigButton("ER 图", NvIcon.Er, async () => await OpenErAsync()));
        toolbar.Items.Add(BigButton("查询构建器", NvIcon.Chart, async () => await OpenQueryBuilderAsync()));
        toolbar.Items.Add(BigButton("搜索", NvIcon.Search, async () => await OpenSearchAsync()));
        toolbar.Items.Add(BigButton("刷新", NvIcon.Refresh, () => RebuildTree()));
        return toolbar;
    }

    private static ToolStripButton BigButton(string text, NvIcon icon, Action action)
    {
        var button = new ToolStripButton(text, NvIcons.Get(icon, 24))
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageAboveText,
            AutoSize = false,
            Width = 62,
            Height = 56,
            Font = NvTheme.Ui(8.4f),
            TextAlign = ContentAlignment.BottomCenter,
            ImageAlign = ContentAlignment.TopCenter,
            ToolTipText = text
        };
        button.Click += (_, _) => action();
        return button;
    }

    // ================= 连接树 =================

    private void RebuildTree()
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            var groups = _store.Connections
                .Where(c => !string.IsNullOrWhiteSpace(c.Group))
                .GroupBy(c => c.Group)
                .OrderBy(g => g.Key);
            foreach (var group in groups)
            {
                var groupNode = new TreeNode("组：" + group.Key)
                {
                    Tag = new NvNodeTag { Kind = "group", Name = group.Key }
                };
                foreach (var profile in group.OrderBy(p => p.Name))
                {
                    groupNode.Nodes.Add(CreateConnectionNode(profile));
                }
                _tree.Nodes.Add(groupNode);
            }
            foreach (var profile in _store.Connections
                .Where(c => string.IsNullOrWhiteSpace(c.Group)).OrderBy(p => p.Name))
            {
                _tree.Nodes.Add(CreateConnectionNode(profile));
            }
            if (_tree.Nodes.Count == 0)
            {
                var hint = new TreeNode("点击“+”新建数据库连接")
                {
                    Tag = new NvNodeTag { Kind = "hint" },
                    ForeColor = NvTheme.Muted
                };
                _tree.Nodes.Add(hint);
            }
        }
        finally
        {
            _tree.EndUpdate();
        }
        _statusInfo.Text = $"共 {_store.Connections.Count} 个连接";
    }

    private TreeNode CreateConnectionNode(ConnectionProfile profile)
    {
        var node = new TreeNode(profile.Name)
        {
            Tag = new NvNodeTag { Kind = "connection", Profile = profile, Database = profile.Database },
            ToolTipText = profile.Kind.DisplayName() + "  " + profile.ServerLabel
        };
        node.Nodes.Add(new TreeNode("未连接") { Tag = new NvNodeTag { Kind = "dummy" } });
        return node;
    }

    private async void TreeAfterExpand(object? sender, TreeViewEventArgs e)
    {
        if (_closing)
        {
            return;
        }
        var tag = e.Node.Tag as NvNodeTag;
        if (tag == null)
        {
            return;
        }
        try
        {
            switch (tag.Kind)
            {
                case "connection":
                    await ExpandConnectionAsync(e.Node, tag);
                    break;
                case "database":
                    EnsureDatabaseFolders(e.Node, tag);
                    break;
                case "folder_tables":
                case "folder_views":
                case "folder_functions":
                case "folder_users":
                case "folder_backups":
                case "folder_plans":
                    await LoadFolderAsync(e.Node, tag);
                    break;
                case "table":
                    await LoadColumnsAsync(e.Node, tag);
                    break;
            }
        }
        catch (Exception ex)
        {
            e.Node.Nodes.Clear();
            e.Node.Nodes.Add(new TreeNode("加载失败：" + ex.Message)
            {
                Tag = new NvNodeTag { Kind = "error" },
                ForeColor = NvTheme.Red
            });
            SetStatus("加载失败：" + ex.Message, true);
        }
    }

    private async Task ExpandConnectionAsync(TreeNode node, NvNodeTag tag)
    {
        var profile = tag.Profile;
        if (profile == null || tag.State == "loading")
        {
            return;
        }
        if (tag.State == "loaded")
        {
            return;
        }
        tag.State = "loading";
        node.Nodes.Clear();
        node.Nodes.Add(new TreeNode("正在连接…") { Tag = new NvNodeTag { Kind = "dummy" } });
        SetBusy(true, "正在连接 " + profile.Name + "…");
        int timeoutSeconds = Math.Max(10,
            (profile.ConnectTimeout <= 0 ? 15 : profile.ConnectTimeout) + 10);
        DbSession? session = null;
        try
        {
            session = GetSessionObject(profile, null);
            if (!session.IsOpen)
            {
                DbSession target = session;
                await Task.Run(() => target.Open()).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            }
            DbSession dbSession = session;
            var databases = await Task.Run(() => dbSession.GetDatabases())
                .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            if (IsDisposed || _closing)
            {
                return;
            }
            node.Nodes.Clear();
            _tree.BeginUpdate();
            int added = 0;
            try
            {
                foreach (string database in databases)
                {
                    var dbNode = new TreeNode(database)
                    {
                        Tag = new NvNodeTag
                        {
                            Kind = "database",
                            Profile = profile,
                            Session = session,
                            Database = database
                        },
                        ToolTipText = profile.Kind.ShortName() + " · " + database
                    };
                    EnsureDatabaseFolders(dbNode, (NvNodeTag)dbNode.Tag);
                    node.Nodes.Add(dbNode);
                    added++;
                }
                if (added == 0)
                {
                    node.Nodes.Add(new TreeNode("（没有可访问的数据库）")
                    {
                        Tag = new NvNodeTag { Kind = "hint" },
                        ForeColor = NvTheme.Muted
                    });
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
            tag.State = "loaded";
            _last.Profile = profile;
            _last.Database = profile.Database;
            UpdateServerStatus(session, database: profile.Database);
            SetStatus("已连接 " + profile.Name + "（" + session.ServerVersion + "）");
            RefreshRecent();
        }
        catch (Exception ex)
        {
            bool timeout = ex is TimeoutException;
            string message = timeout
                ? $"连接超时：超过 {timeoutSeconds} 秒仍未完成。\n请检查主机地址、端口、防火墙、SSH 隧道或数据库服务是否可用。"
                : ex.Message;
            NvLog.Error(ex, "展开连接失败：" + profile.Name + " · " + profile.ServerLabel);
            if (timeout && session != null)
            {
                DiscardSession(profile, null, session);
            }
            if (IsDisposed || _closing)
            {
                return;
            }
            node.Nodes.Clear();
            node.Nodes.Add(new TreeNode("连接失败：" + message)
            {
                Tag = new NvNodeTag { Kind = "error" },
                ForeColor = NvTheme.Red
            });
            tag.State = "";
            SetStatus("连接失败：" + message, true);
            if (timeout)
            {
                NvErrorDialog.Show(this, "连接失败", "无法连接数据库：" + message,
                    BuildConnectionErrorDetails(profile, null,
                        new TimeoutException(message, ex)), "日志目录：" + NvLog.Folder);
            }
            else
            {
                NvErrorDialog.Show(this, "连接失败", "无法连接数据库：" + message,
                    BuildConnectionErrorDetails(profile, null, ex), "日志目录：" + NvLog.Folder);
            }
        }
        finally
        {
            if (!IsDisposed && !_closing)
            {
                SetBusy(false, null);
                _tree.Invalidate();
            }
        }
    }

    private void EnsureDatabaseFolders(TreeNode dbNode, NvNodeTag dbTag)
    {
        if (dbTag.State == "folders")
        {
            return;
        }
        dbTag.State = "folders";
        dbNode.Nodes.Clear();
        _tree.BeginUpdate();
        try
        {
            AddFolder(dbNode, dbTag, "folder_tables", "表", NvIcon.Table, true);
            AddFolder(dbNode, dbTag, "folder_views", "视图", NvIcon.View, true);
            AddFolder(dbNode, dbTag, "folder_functions", "函数", NvIcon.Function, true);
            AddFolder(dbNode, dbTag, "folder_queries", "查询", NvIcon.Query, false);
            AddFolder(dbNode, dbTag, "folder_reports", "报表", NvIcon.Report, false);
            AddFolder(dbNode, dbTag, "folder_backups", "备份", NvIcon.Backup, true);
            AddFolder(dbNode, dbTag, "folder_plans", "计划", NvIcon.Schedule, true);
            AddFolder(dbNode, dbTag, "folder_users", "用户", NvIcon.User, true);
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private void AddFolder(TreeNode parent, NvNodeTag parentTag, string kind, string text, NvIcon icon, bool lazy)
    {
        var node = new TreeNode(text)
        {
            Tag = new NvNodeTag
            {
                Kind = kind,
                Profile = parentTag.Profile,
                Database = parentTag.Database,
                Session = parentTag.Session,
                Name = text,
                State = lazy ? "" : "loaded"
            }
        };
        if (lazy)
        {
            node.Nodes.Add(new TreeNode("…") { Tag = new NvNodeTag { Kind = "dummy" } });
        }
        parent.Nodes.Add(node);
    }

    private async Task LoadFolderAsync(TreeNode node, NvNodeTag tag)
    {
        if (tag.State == "loading" || tag.State == "loaded")
        {
            return;
        }
        tag.State = "loading";
        node.Nodes.Clear();
        node.Nodes.Add(new TreeNode("正在加载…") { Tag = new NvNodeTag { Kind = "dummy" } });
        var profile = tag.Profile;
        if (profile == null)
        {
            return;
        }
        SetBusy(true, "正在读取 " + tag.Name + "…");
        try
        {
            var session = GetSessionObject(profile, tag.Database);
            if (!session.IsOpen)
            {
                await Task.Run(() => session.Open());
            }
            string database = tag.Database ?? session.CurrentDatabase;
            var nodes = new List<TreeNode>();
            switch (tag.Kind)
            {
                case "folder_tables":
                {
                    var tables = await Task.Run(() => session.GetTables(database));
                    foreach (var table in tables.OrderBy(t => t.Name))
                    {
                        nodes.Add(new TreeNode(table.Name)
                        {
                            Tag = new NvNodeTag
                            {
                                Kind = "table",
                                Profile = profile,
                                Database = database,
                                Schema = table.Schema,
                                Name = table.Name,
                                Extra = table.Rows?.ToString() ?? "",
                                IsView = false,
                                Session = session
                            },
                            ToolTipText = table.DisplayName + (table.Comment != null ? "\n" + table.Comment : "")
                        });
                        nodes[^1].Nodes.Add(new TreeNode("…") { Tag = new NvNodeTag { Kind = "dummy" } });
                    }
                    node.Text = "表 (" + tables.Count + ")";
                    break;
                }
                case "folder_views":
                {
                    var views = await Task.Run(() => session.GetViews(database));
                    foreach (var view in views.OrderBy(v => v.Name))
                    {
                        nodes.Add(new TreeNode(view.Name)
                        {
                            Tag = new NvNodeTag
                            {
                                Kind = "view",
                                Profile = profile,
                                Database = database,
                                Schema = view.Schema,
                                Name = view.Name,
                                IsView = true,
                                Session = session
                            }
                        });
                        nodes[^1].Nodes.Add(new TreeNode("…") { Tag = new NvNodeTag { Kind = "dummy" } });
                    }
                    node.Text = "视图 (" + views.Count + ")";
                    break;
                }
                case "folder_functions":
                {
                    var routines = await Task.Run(() => session.GetRoutines(database));
                    foreach (var routine in routines.OrderBy(r => r.Name))
                    {
                        nodes.Add(new TreeNode(routine.Name)
                        {
                            Tag = new NvNodeTag
                            {
                                Kind = "routine",
                                Profile = profile,
                                Database = database,
                                Schema = routine.Schema,
                                Name = routine.Name,
                                Extra = routine.Type,
                                Session = session
                            },
                            ToolTipText = routine.Type + " " + routine.DisplayName
                        });
                    }
                    node.Text = "函数 (" + routines.Count + ")";
                    break;
                }
                case "folder_users":
                {
                    var users = await Task.Run(() => session.GetUsers());
                    foreach (var user in users)
                    {
                        nodes.Add(new TreeNode(user.DisplayName)
                        {
                            Tag = new NvNodeTag
                            {
                                Kind = "user",
                                Profile = profile,
                                Database = database,
                                Name = user.Name,
                                Extra = user.Host,
                                Session = session
                            },
                            ToolTipText = user.Info ?? ""
                        });
                    }
                    node.Text = "用户 (" + users.Count + ")";
                    break;
                }
                case "folder_backups":
                {
                    string dir = Path.Combine(NvPaths.BackupsDir, profile.Id);
                    if (Directory.Exists(dir))
                    {
                        foreach (string file in Directory.GetFiles(dir, "*.sql").OrderByDescending(File.GetLastWriteTime))
                        {
                            nodes.Add(new TreeNode(Path.GetFileName(file))
                            {
                                Tag = new NvNodeTag
                                {
                                    Kind = "backup",
                                    Profile = profile,
                                    Database = database,
                                    Name = Path.GetFileName(file),
                                    Extra = file
                                },
                                ToolTipText = file
                            });
                        }
                    }
                    node.Text = "备份 (" + nodes.Count + ")";
                    break;
                }
                case "folder_plans":
                {
                    foreach (var task in _tasks.Where(t => t.ProfileId == profile.Id))
                    {
                        nodes.Add(new TreeNode(task.Name + (task.Enabled ? "" : "（已停用）"))
                        {
                            Tag = new NvNodeTag
                            {
                                Kind = "plan",
                                Profile = profile,
                                Database = database,
                                Name = task.Name,
                                Extra = task.Id
                            },
                            ToolTipText = task.LastResult
                        });
                    }
                    node.Text = "计划 (" + nodes.Count + ")";
                    break;
                }
            }
            node.Nodes.Clear();
            _tree.BeginUpdate();
            try
            {
                foreach (var child in nodes)
                {
                    node.Nodes.Add(child);
                }
                if (node.Nodes.Count == 0)
                {
                    node.Nodes.Add(new TreeNode("（空）")
                    {
                        Tag = new NvNodeTag { Kind = "hint" },
                        ForeColor = NvTheme.Muted
                    });
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
            tag.State = "loaded";
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "加载连接树节点失败：" + tag.Kind + " · " + tag.Name);
            node.Nodes.Clear();
            node.Nodes.Add(new TreeNode("读取失败：" + ex.Message)
            {
                Tag = new NvNodeTag { Kind = "error" },
                ForeColor = NvTheme.Red
            });
            tag.State = "";
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task LoadColumnsAsync(TreeNode node, NvNodeTag tag)
    {
        if (tag.State == "columns" || tag.State == "loading-columns")
        {
            return;
        }
        tag.State = "loading-columns";
        var profile = tag.Profile;
        if (profile == null)
        {
            return;
        }
        try
        {
            var session = GetSessionObject(profile, tag.Database);
            if (!session.IsOpen)
            {
                await Task.Run(() => session.Open());
            }
            var table = new TableInfo
            {
                Database = tag.Database ?? "",
                Schema = tag.Schema ?? "",
                Name = tag.Name ?? "",
                IsView = tag.IsView
            };
            var columns = await Task.Run(() => session.GetColumns(table));
            if (node.IsExpanded)
            {
                node.Nodes.Clear();
                _tree.BeginUpdate();
                try
                {
                    foreach (var column in columns)
                    {
                        string text = column.Name + "  " + column.DisplayType;
                        node.Nodes.Add(new TreeNode(text)
                        {
                            Tag = new NvNodeTag
                            {
                                Kind = "column",
                                Profile = profile,
                                Database = tag.Database,
                                Schema = tag.Schema,
                                Name = column.Name,
                                Extra = (column.PrimaryKey ? "PK|" : "") + column.DisplayType,
                                Session = session
                            },
                            ToolTipText = column.Comment ?? ""
                        });
                    }
                }
                finally
                {
                    _tree.EndUpdate();
                }
            }
            tag.State = "columns";
        }
        catch
        {
            tag.State = "columns";
        }
    }

    private void TreeDrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        e.DrawDefault = false;
        var g = e.Graphics;
        var node = e.Node;
        if (node == null)
        {
            return;
        }
        var tag = node.Tag as NvNodeTag;
        bool selected = (e.State & TreeNodeStates.Selected) != 0;
        bool hover = ReferenceEquals(node, _hoverNode);
        var rect = new Rectangle(0, e.Bounds.Top, _tree.ClientSize.Width, e.Bounds.Height);
        Color back = selected ? NvTheme.AccentLight
            : hover ? Color.FromArgb(240, 246, 252)
            : NvTheme.Panel;
        using (var brush = new SolidBrush(back))
        {
            g.FillRectangle(brush, rect);
        }
        int x = 6 + node.Level * 18;
        if (node.Nodes.Count > 0)
        {
            var glyph = new Rectangle(x, rect.Top + 8, 9, 9);
            NvTheme.DrawTriangle(g, glyph, NvTheme.Muted, node.IsExpanded);
        }
        x += 14;
        Image? icon = IconForTag(tag);
        if (icon != null)
        {
            g.DrawImage(icon, new Rectangle(x, rect.Top + 4, 16, 16));
            x += 20;
        }
        bool bold = tag?.Kind == "connection";
        if (tag?.Kind == "group")
        {
            bold = true;
        }
        Font font = bold ? _treeBoldFont : _treeNodeFont;
        bool match = _searchText.Length > 0
            && node.Text.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
        Color fore = match ? NvTheme.Orange
            : tag?.Kind is "error" ? NvTheme.Red
            : tag?.Kind is "hint" or "dummy" ? NvTheme.Muted
            : NvTheme.Text;
        int rightSpace = 40;
        if (tag?.Kind == "table" || tag?.Kind == "view")
        {
            rightSpace = 76;
        }
        TextRenderer.DrawText(g, node.Text, font,
            new Rectangle(x, rect.Top, Math.Max(10, rect.Width - x - rightSpace), rect.Height),
            fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis);

        if (tag?.Kind is "table")
        {
            string rows = string.IsNullOrEmpty(tag.Extra) ? "" : tag.Extra + " 行";
            TextRenderer.DrawText(g, rows, _treeSmallFont,
                new Rectangle(rect.Width - 70, rect.Top, 64, rect.Height), NvTheme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        if (tag?.Kind == "connection")
        {
            bool connected = tag.Profile != null && HasOpenSession(tag.Profile.Id);
            using var dot = new SolidBrush(connected ? NvTheme.Green : Color.FromArgb(170, 180, 190));
            g.FillEllipse(dot, rect.Width - 18, rect.Top + 9, 7, 7);
        }
        if ((e.State & TreeNodeStates.Focused) != 0 && selected)
        {
            using var pen = new Pen(Color.FromArgb(150, 185, 214));
            g.DrawRectangle(pen, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
        }
    }

    private static Image? IconForTag(NvNodeTag? tag)
    {
        return tag?.Kind switch
        {
            "connection" => tag.Profile != null ? NvIcons.Get(tag.Profile.Kind.Icon(), 16) : NvIcons.Get(NvIcon.Database, 16),
            "group" => NvIcons.Get(NvIcon.Folder, 16),
            "database" => NvIcons.Get(NvIcon.Database, 16),
            "folder_tables" => NvIcons.Get(NvIcon.Table, 16),
            "folder_views" => NvIcons.Get(NvIcon.View, 16),
            "folder_functions" => NvIcons.Get(NvIcon.Function, 16),
            "folder_queries" => NvIcons.Get(NvIcon.Query, 16),
            "folder_reports" => NvIcons.Get(NvIcon.Report, 16),
            "folder_backups" => NvIcons.Get(NvIcon.Backup, 16),
            "folder_plans" => NvIcons.Get(NvIcon.Schedule, 16),
            "folder_users" => NvIcons.Get(NvIcon.User, 16),
            "table" => NvIcons.Get(NvIcon.Table, 16),
            "view" => NvIcons.Get(NvIcon.View, 16),
            "routine" => NvIcons.Get(NvIcon.Function, 16),
            "column" => tag.Extra?.StartsWith("PK|", StringComparison.Ordinal) == true
                ? NvIcons.Get(NvIcon.Key, 16)
                : NvIcons.Get(NvIcon.Column, 16),
            "user" => NvIcons.Get(NvIcon.User, 16),
            "backup" => NvIcons.Get(NvIcon.Backup, 16),
            "plan" => NvIcons.Get(NvIcon.Schedule, 16),
            _ => null
        };
    }

    private bool HasOpenSession(string profileId)
    {
        foreach (var (key, session) in _sessions)
        {
            if (key.StartsWith(profileId + "|", StringComparison.OrdinalIgnoreCase) && session.IsOpen)
            {
                return true;
            }
        }
        return false;
    }

    private void JumpToSearch()
    {
        if (_searchText.Length == 0)
        {
            return;
        }
        TreeNode? found = null;
        foreach (TreeNode root in _tree.Nodes)
        {
            found = FindNode(root, _searchText);
            if (found != null)
            {
                break;
            }
        }
        if (found == null)
        {
            SetStatus("未找到包含“" + _searchText + "”的节点");
            return;
        }
        var parent = found.Parent;
        while (parent != null)
        {
            parent.Expand();
            parent = parent.Parent;
        }
        _tree.SelectedNode = found;
        found.EnsureVisible();
        found.Expand();
    }

    private static TreeNode? FindNode(TreeNode node, string text)
    {
        if (node.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }
        foreach (TreeNode child in node.Nodes)
        {
            var found = FindNode(child, text);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    // ================= 树事件 / 上下文菜单 =================

    private void TreeNodeClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        var tag = e.Node.Tag as NvNodeTag;
        if (tag == null)
        {
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            return;
        }
        if (e.Clicks == 1)
        {
            // 点击展开三角
            int x = 6 + e.Node.Level * 18;
            if (e.X >= x && e.X <= x + 12 && e.Node.Nodes.Count > 0)
            {
                if (e.Node.IsExpanded)
                {
                    e.Node.Collapse();
                }
                else
                {
                    e.Node.Expand();
                }
            }
        }
    }

    private async void TreeNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        var tag = e.Node.Tag as NvNodeTag;
        if (tag == null)
        {
            return;
        }
        switch (tag.Kind)
        {
            case "connection":
                if (!e.Node.IsExpanded)
                {
                    e.Node.Expand();
                }
                break;
            case "database":
                e.Node.Expand();
                break;
            case "table":
            case "view":
                await OpenTableAsync(tag, tag.IsView);
                break;
            case "column":
                if (e.Node.Parent?.Tag is NvNodeTag parentTag)
                {
                    await OpenTableAsync(parentTag, parentTag.IsView);
                }
                break;
            case "routine":
                await OpenRoutineAsync(tag);
                break;
            case "user":
                await OpenUsersAsync(tag);
                break;
            case "backup":
                await OpenBackupAsync(tag);
                break;
            case "plan":
                OpenScheduler();
                break;
            case "folder_queries":
                await NewQueryAsync(tag);
                break;
            case "folder_reports":
                await OpenReportAsync(tag);
                break;
            case "folder_tables":
            case "folder_views":
            case "folder_functions":
            case "folder_users":
            case "folder_backups":
            case "folder_plans":
                e.Node.Expand();
                break;
            case "group":
                e.Node.Expand();
                break;
        }
    }

    private ContextMenuStrip BuildNodeMenu(TreeNode node, NvNodeTag tag)
    {
        var menu = new ContextMenuStrip { Font = NvTheme.Ui(9f) };
        switch (tag.Kind)
        {
            case "connection":
                menu.Items.Add(MenuItem("连接", () => node.Expand(), NvIcon.Connect));
                menu.Items.Add(MenuItem("断开连接", () =>
                {
                    DisconnectProfile(tag.Profile);
                    node.Collapse();
                    node.Nodes.Clear();
                    node.Nodes.Add(new TreeNode("未连接") { Tag = new NvNodeTag { Kind = "dummy" } });
                    tag.State = "";
                    _tree.Invalidate();
                }, NvIcon.Close));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuItem("新建数据库…", async () => await CreateDatabaseAsync(tag), NvIcon.Database));
                menu.Items.Add(MenuItem("新建查询", async () => await NewQueryAsync(tag), NvIcon.Query));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuItem("编辑连接…", async () => await EditConnectionAsync(tag.Profile), NvIcon.Edit));
                menu.Items.Add(MenuItem("复制连接", () => DuplicateConnection(tag.Profile), NvIcon.Copy));
                menu.Items.Add(MenuItem("删除连接", () => DeleteConnection(tag.Profile), NvIcon.Delete));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuItem("服务器监控…", async () => await OpenMonitorAsync(tag), NvIcon.Server));
                menu.Items.Add(MenuItem("用户管理…", async () => await OpenUsersAsync(tag), NvIcon.User));
                menu.Items.Add(MenuItem("备份 / 还原…", async () => await OpenBackupAsync(tag), NvIcon.Backup));
                menu.Items.Add(MenuItem("ER 图…", async () => await OpenErAsync(tag), NvIcon.Er));
                menu.Items.Add(MenuItem("数据库文档…", async () => await OpenDbDocAsync(tag), NvIcon.File));
                menu.Items.Add(MenuItem("刷新", () => RefreshConnectionNode(node, tag.Profile), NvIcon.Refresh));
                break;
            case "group":
                menu.Items.Add(MenuItem("新建连接…", async () => await NewConnectionAsync(), NvIcon.NewConnection));
                menu.Items.Add(MenuItem("刷新", () => RebuildTree(), NvIcon.Refresh));
                break;
            case "database":
                menu.Items.Add(MenuItem("新建表…", async () => await NewTableAsync(tag), NvIcon.Table));
                menu.Items.Add(MenuItem("新建查询", async () => await NewQueryAsync(tag), NvIcon.Query));
                menu.Items.Add(MenuItem("导入数据…", async () => await OpenImportAsync(tag), NvIcon.Import));
                menu.Items.Add(MenuItem("数据生成…", async () => await OpenDataGeneratorAsync(tag), NvIcon.Add));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuItem("备份 / 还原…", async () => await OpenBackupAsync(tag), NvIcon.Backup));
                menu.Items.Add(MenuItem("数据传输…", async () => await OpenTransferAsync(tag), NvIcon.Transfer));
                menu.Items.Add(MenuItem("数据同步…", async () => await OpenDataSyncAsync(tag), NvIcon.Sync));
                menu.Items.Add(MenuItem("结构同步…", async () => await OpenStructureSyncAsync(tag), NvIcon.Structure));
                menu.Items.Add(MenuItem("ER 图…", async () => await OpenErAsync(tag), NvIcon.Er));
                menu.Items.Add(MenuItem("数据库文档…", async () => await OpenDbDocAsync(tag), NvIcon.File));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuItem("用户管理…", async () => await OpenUsersAsync(tag), NvIcon.User));
                menu.Items.Add(MenuItem("服务器监控…", async () => await OpenMonitorAsync(tag), NvIcon.Server));
                menu.Items.Add(MenuItem("刷新", () => NodeRefresh(node), NvIcon.Refresh));
                break;
            case "folder_tables":
                menu.Items.Add(MenuItem("新建表…", async () => await NewTableAsync(tag), NvIcon.Table));
                menu.Items.Add(MenuItem("刷新", () => NodeRefresh(node), NvIcon.Refresh));
                break;
            case "folder_views":
                menu.Items.Add(MenuItem("新建视图…", async () => await NewViewAsync(tag), NvIcon.View));
                menu.Items.Add(MenuItem("刷新", () => NodeRefresh(node), NvIcon.Refresh));
                break;
            case "folder_functions":
                menu.Items.Add(MenuItem("刷新", () => NodeRefresh(node), NvIcon.Refresh));
                break;
            case "folder_users":
                menu.Items.Add(MenuItem("用户管理…", async () => await OpenUsersAsync(tag), NvIcon.User));
                menu.Items.Add(MenuItem("刷新", () => NodeRefresh(node), NvIcon.Refresh));
                break;
            case "folder_queries":
                menu.Items.Add(MenuItem("新建查询", async () => await NewQueryAsync(tag), NvIcon.Query));
                break;
            case "folder_reports":
                menu.Items.Add(MenuItem("新建报表…", async () => await OpenReportAsync(tag), NvIcon.Report));
                break;
            case "folder_backups":
                menu.Items.Add(MenuItem("备份 / 还原…", async () => await OpenBackupAsync(tag), NvIcon.Backup));
                menu.Items.Add(MenuItem("刷新", () => NodeRefresh(node), NvIcon.Refresh));
                break;
            case "folder_plans":
                menu.Items.Add(MenuItem("计划任务…", OpenScheduler, NvIcon.Schedule));
                menu.Items.Add(MenuItem("刷新", () => NodeRefresh(node), NvIcon.Refresh));
                break;
            case "table":
            case "view":
                menu.Items.Add(MenuItem(tag.IsView ? "打开视图" : "打开表",
                    async () => await OpenTableAsync(tag, tag.IsView), NvIcon.Table));
                if (!tag.IsView)
                {
                    menu.Items.Add(MenuItem("设计表…", async () => await OpenDesignerAsync(tag), NvIcon.Edit));
                }
                menu.Items.Add(MenuItem("新建查询", async () => await NewQueryAsync(tag), NvIcon.Query));
                menu.Items.Add(MenuItem("导入数据…", async () => await OpenImportAsync(tag), NvIcon.Import));
                menu.Items.Add(MenuItem("数据生成…", async () => await OpenDataGeneratorAsync(tag), NvIcon.Add));
                menu.Items.Add(MenuItem("导出数据…", async () => await OpenExportAsync(tag), NvIcon.Export));
                menu.Items.Add(MenuItem("查看 DDL", () => ShowTableDdl(tag), NvIcon.File));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuItem("复制名称", () =>
                {
                    if (!string.IsNullOrEmpty(tag.Name))
                    {
                        Clipboard.SetText(tag.Name);
                    }
                }, NvIcon.Copy));
                menu.Items.Add(MenuItem(tag.IsView ? "删除视图" : "删除表", async () => await DropObjectAsync(tag), NvIcon.Drop));
                if (!tag.IsView)
                {
                    menu.Items.Add(MenuItem("重命名表…", async () => await RenameTableAsync(tag), NvIcon.Rename));
                    menu.Items.Add(MenuItem("清空表", async () => await TruncateTableAsync(tag), NvIcon.Truncate));
                    menu.Items.Add(MenuItem("复制表结构…", () => CopyTableStructure(tag), NvIcon.Structure));
                }
                break;
            case "routine":
                menu.Items.Add(MenuItem("查看定义", async () => await OpenRoutineAsync(tag), NvIcon.File));
                menu.Items.Add(MenuItem("新建查询", async () => await NewQueryAsync(tag), NvIcon.Query));
                break;
            case "user":
                menu.Items.Add(MenuItem("用户管理…", async () => await OpenUsersAsync(tag), NvIcon.User));
                break;
            case "backup":
                menu.Items.Add(MenuItem("还原此备份…", async () => await OpenBackupAsync(tag), NvIcon.Import));
                break;
            case "plan":
                menu.Items.Add(MenuItem("计划任务…", OpenScheduler, NvIcon.Schedule));
                break;
        }
        return menu;
    }

    private static ToolStripMenuItem MenuItem(string text, Action action, NvIcon icon)
    {
        var item = new ToolStripMenuItem(text, NvIcons.Get(icon, 16));
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripMenuItem MenuItem(string text, Func<Task> action, NvIcon icon)
    {
        var item = new ToolStripMenuItem(text, NvIcons.Get(icon, 16));
        item.Click += async (_, _) => await action();
        return item;
    }

    private void NodeRefresh(TreeNode node)
    {
        var tag = node.Tag as NvNodeTag;
        if (tag == null)
        {
            return;
        }
        tag.State = "";
        node.Nodes.Clear();
        node.Nodes.Add(new TreeNode("…") { Tag = new NvNodeTag { Kind = "dummy" } });
        if (node.IsExpanded)
        {
            node.Collapse();
            node.Expand();
        }
    }

    private void RefreshConnectionNode(TreeNode node, ConnectionProfile? profile)
    {
        if (profile == null)
        {
            return;
        }
        if (!CloseTabsForProfile(profile))
        {
            return;
        }
        DisconnectProfile(profile);
        var tag = node.Tag as NvNodeTag;
        if (tag != null)
        {
            tag.State = "";
        }
        node.Nodes.Clear();
        node.Nodes.Add(new TreeNode("未连接") { Tag = new NvNodeTag { Kind = "dummy" } });
        node.Collapse();
        _tree.Invalidate();
    }

    // ================= 会话管理 =================

    private string SessionKey(ConnectionProfile profile, string? database)
    {
        string db = profile.Kind switch
        {
            DbKind.SQLite => profile.Database,
            DbKind.Oracle => profile.Database,
            _ => database ?? profile.Database
        };
        return profile.Id + "|" + db;
    }

    private DbSession GetSessionObject(ConnectionProfile profile, string? database)
    {
        string key = SessionKey(profile, database);
        lock (_sessions)
        {
            if (_sessions.TryGetValue(key, out var existing))
            {
                return existing;
            }
            string? initial = profile.Kind switch
            {
                DbKind.SQLite => profile.Database,
                DbKind.Oracle => profile.Database,
                _ => database ?? profile.Database
            };
            var session = new DbSession(profile, string.IsNullOrWhiteSpace(initial) ? null : initial);
            _sessions[key] = session;
            return session;
        }
    }

    private async Task<DbSession?> EnsureSessionAsync(ConnectionProfile profile, string? database)
    {
        DbSession? session = null;
        try
        {
            session = GetSessionObject(profile, database);
            if (!session.IsOpen)
            {
                if (IsDisposed || Disposing)
                {
                    return null;
                }
                SetBusy(true, "正在连接 " + profile.Name + "…");
                try
                {
                    int timeoutSeconds = Math.Max(10,
                        (profile.ConnectTimeout <= 0 ? 15 : profile.ConnectTimeout) + 10);
                    DbSession target = session;
                    var openTask = Task.Run(() => target.Open());
                    try
                    {
                        await openTask.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
                    }
                    catch (TimeoutException)
                    {
                        DiscardSession(profile, database, session);
                        throw new TimeoutException(
                            $"连接超时：超过 {timeoutSeconds} 秒仍未完成。\n"
                            + "请检查主机地址、端口、防火墙、SSH 隧道或数据库服务是否可用。");
                    }
                }
                finally
                {
                    if (!IsDisposed && !Disposing)
                    {
                        SetBusy(false, null);
                    }
                }
            }
            if (IsDisposed || Disposing)
            {
                return null;
            }
            _last.Profile = profile;
            _last.Database = database ?? session.CurrentDatabase;
            UpdateServerStatus(session, database);
            return session;
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing)
            {
                return null;
            }
            NvLog.Error(ex, "连接数据库失败：" + profile.Name + " · " + profile.ServerLabel
                + " · 数据库=" + database);
            SetStatus("连接失败：" + ex.Message, true);
            NvErrorDialog.Show(this, "连接失败",
                "无法连接数据库：" + ex.Message,
                BuildConnectionErrorDetails(profile, database, ex),
                "日志目录：" + NvLog.Folder);
            return null;
        }
    }

    private void DiscardSession(ConnectionProfile profile, string? database, DbSession session)
    {
        string key = SessionKey(profile, database);
        lock (_sessions)
        {
            if (_sessions.TryGetValue(key, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(key);
            }
        }
        _ = Task.Run(() =>
        {
            try
            {
                session.Dispose();
            }
            catch
            {
                // ignore
            }
        });
    }

    private static string BuildConnectionErrorDetails(ConnectionProfile profile, string? database,
        Exception exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("连接：" + profile.Name);
        sb.AppendLine("类型：" + profile.Kind.ShortName());
        sb.AppendLine("主机：" + profile.Host);
        sb.AppendLine("端口：" + profile.Port);
        sb.AppendLine("数据库：" + (database ?? profile.Database));
        sb.AppendLine("用户：" + profile.Username);
        sb.AppendLine("SSL：" + profile.UseSsl);
        sb.AppendLine("SSH 隧道：" + profile.UseSsh);
        if (profile.UseSsh)
        {
            sb.AppendLine("SSH 主机：" + profile.SshHost + ":" + profile.SshPort);
            sb.AppendLine("SSH 用户：" + profile.SshUser);
        }
        sb.AppendLine("日志：" + NvLog.CurrentFile);
        sb.AppendLine();
        sb.AppendLine("错误信息：");
        sb.AppendLine(exception.Message);
        sb.AppendLine();
        sb.AppendLine("异常详情：");
        sb.AppendLine(exception.ToString());
        return sb.ToString();
    }

    private bool CloseTabsForProfile(ConnectionProfile? profile)
    {
        if (profile == null)
        {
            return true;
        }
        foreach (var tab in _tabs.Tabs.ToList())
        {
            DbSession? session = tab.Content switch
            {
                QueryTab query => query.Session,
                TableDataTab data => data.Session,
                TableDesignerTab designer => designer.Session,
                _ => null
            };
            if (session?.Profile.Id != profile.Id)
            {
                continue;
            }
            if (tab.Content is NvClosable closable && !closable.TryClose())
            {
                return false;
            }
            _tabs.Close(tab, true);
        }
        return true;
    }

    private void DisconnectProfile(ConnectionProfile? profile)
    {
        if (profile == null)
        {
            return;
        }
        List<string> keys;
        lock (_sessions)
        {
            keys = _sessions.Keys.Where(k => k.StartsWith(profile.Id + "|", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        foreach (string key in keys)
        {
            if (_sessions.Remove(key, out var session))
            {
                // 可能仍有连接正在进行，Close/Dispose 会等待驱动，放到后台线程执行
                _ = Task.Run(() =>
                {
                    try
                    {
                        session.Close();
                        session.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                });
            }
        }
    }

    private void DisposeSessions()
    {
        List<DbSession> sessions;
        lock (_sessions)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }
        if (sessions.Count == 0)
        {
            return;
        }
        // 连接中的会话 Dispose 会等待驱动返回，放到后台线程避免关闭窗口时卡住界面
        _ = Task.Run(() =>
        {
            foreach (var session in sessions)
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        });
    }

    private ConnectionProfile? CurrentProfile()
    {
        if (_tree.SelectedNode?.Tag is NvNodeTag tag && tag.Profile != null)
        {
            return tag.Profile;
        }
        return _last.Profile;
    }

    private string? CurrentDatabase()
    {
        if (_tree.SelectedNode?.Tag is NvNodeTag tag && tag.Database != null)
        {
            return tag.Database;
        }
        return _last.Database;
    }

    private async Task<(DbSession? Session, NvNodeTag? Tag)> RequireContextAsync()
    {
        var node = _tree.SelectedNode;
        var tag = node?.Tag as NvNodeTag;
        var profile = tag?.Profile ?? _last.Profile;
        if (profile == null)
        {
            NvPrompt.Info(this, AppTitle, "请先在左侧连接树中选择一个连接或数据库。");
            return (null, null);
        }
        string? database = tag?.Database ?? _last.Database ?? profile.Database;
        var session = await EnsureSessionAsync(profile, database);
        if (session == null || IsDisposed || Disposing)
        {
            return (null, null);
        }
        return (session, tag);
    }

    private async Task<(DbSession? Session, TableInfo? Table)> RequireTableContextAsync()
    {
        var node = _tree.SelectedNode;
        var tag = node?.Tag as NvNodeTag;
        if (tag?.Kind is "table" or "view")
        {
            var profile = tag.Profile ?? _last.Profile;
            if (profile != null)
            {
                var session = await EnsureSessionAsync(profile, tag.Database);
                if (session != null)
                {
                    return (session, new TableInfo
                    {
                        Database = tag.Database ?? "",
                        Schema = tag.Schema ?? "",
                        Name = tag.Name ?? "",
                        IsView = tag.IsView
                    });
                }
            }
        }
        var context = await RequireContextAsync();
        return (context.Session, null);
    }

    // ================= 标签页 =================

    private void ShowWelcome()
    {
        var existing = _tabs.Find("welcome");
        if (existing != null)
        {
            _tabs.Activate(existing);
            return;
        }
        _tabs.Add("欢迎", _welcome, NvIcons.Get(NvIcon.Logo, 16), false, "welcome");
    }

    private async Task NewQueryAsync(NvNodeTag? tag = null)
    {
        var profile = tag?.Profile ?? CurrentProfile();
        if (profile == null)
        {
            var context = await RequireContextAsync();
            profile = context.Tag?.Profile ?? _last.Profile;
            if (profile == null)
            {
                return;
            }
        }
        string? database = tag?.Database ?? CurrentDatabase() ?? profile.Database;
        var session = await EnsureSessionAsync(profile, database);
        if (session == null)
        {
            return;
        }
        OpenQueryTab(session, "");
    }

    private void OpenQueryTab(DbSession session, string sql)
    {
        var tab = new QueryTab(session, sql);
        var doc = _tabs.Add(tab.TabTitle, tab, NvIcons.Get(NvIcon.Query, 16), true);
        tab.TitleChanged += (_, _) => doc.Title = tab.TabTitle;
        tab.DirtyChanged += (_, _) =>
        {
            doc.Title = tab.TabTitle;
            _tabs.SetDirty(doc, tab.IsDirty);
        };
        tab.Load += (_, _) => tab.FocusEditor();
    }

    private async Task OpenTableAsync(NvNodeTag tag, bool readOnly)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        string key = "table|" + profile.Id + "|" + tag.Database + "|" + tag.Schema + "|" + tag.Name;
        var existing = _tabs.Find(key);
        if (existing != null)
        {
            _tabs.Activate(existing);
            return;
        }
        var session = await EnsureSessionAsync(profile, tag.Database);
        if (session == null)
        {
            return;
        }
        var table = new TableInfo
        {
            Database = tag.Database ?? "",
            Schema = tag.Schema ?? "",
            Name = tag.Name ?? "",
            IsView = readOnly
        };
        var tab = new TableDataTab(session, table, readOnly);
        NvLog.Info((readOnly ? "打开视图：" : "打开表：") + table.DisplayName
            + " · " + session.Profile.Name + " · " + session.CurrentDatabase);
        var doc = _tabs.Add(table.Name + (readOnly ? "（视图）" : ""), tab, NvIcons.Get(NvIcon.Table, 16), true, key);
        doc.Tag = table;
        tab.DesignRequested += async (_, _) =>
        {
            if (!readOnly)
            {
                await OpenDesignerForTableAsync(profile, table);
            }
        };
        tab.GenerateDataRequested += async (_, _) => await OpenDataGeneratorAsync(tag);
    }

    private async Task OpenDesignerForTableAsync(ConnectionProfile profile, TableInfo table)
    {
        string key = "design|" + profile.Id + "|" + table.Database + "|" + table.Schema + "|" + table.Name;
        var existing = _tabs.Find(key);
        if (existing != null)
        {
            _tabs.Activate(existing);
            return;
        }
        var session = await EnsureSessionAsync(profile, table.Database);
        if (session == null)
        {
            return;
        }
        OpenDesignerTab(session, table, key);
    }

    private async Task OpenDesignerAsync(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        await OpenDesignerForTableAsync(profile, new TableInfo
        {
            Database = tag.Database ?? "",
            Schema = tag.Schema ?? "",
            Name = tag.Name ?? ""
        });
    }

    private void OpenDesignerTab(DbSession session, TableInfo? table, string? key = null)
    {
        var designer = new TableDesignerTab(session, table);
        var doc = _tabs.Add(designer.TabTitle, designer, NvIcons.Get(NvIcon.Table, 16), true, key);
        designer.TitleChanged += (_, _) =>
        {
            doc.Title = designer.TabTitle;
        };
        designer.SchemaChanged += (_, _) =>
        {
            RefreshDatabaseObjects(session.Profile, session.CurrentDatabase);
        };
        designer.TableCreated += (_, created) =>
        {
            BeginInvoke(new Action(() =>
            {
                RefreshDatabaseObjects(session.Profile, session.CurrentDatabase);
                _tabs.Close(doc, true);
                _ = OpenTableAsync(new NvNodeTag
                {
                    Kind = "table",
                    Profile = session.Profile,
                    Database = created.Database,
                    Schema = created.Schema,
                    Name = created.Name
                }, false);
            }));
        };
    }

    private async Task OpenRoutineAsync(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        var session = await EnsureSessionAsync(profile, tag.Database);
        if (session == null)
        {
            return;
        }
        try
        {
            SetBusy(true, "正在读取定义…");
            var routine = new RoutineInfo
            {
                Database = tag.Database ?? "",
                Schema = tag.Schema ?? "",
                Name = tag.Name ?? "",
                Type = tag.Extra ?? "FUNCTION"
            };
            string ddl = await Task.Run(() => session.GetRoutineDdl(routine));
            SetBusy(false, null);
            OpenQueryTab(session, ddl);
        }
        catch (Exception ex)
        {
            SetBusy(false, null);
            NvLog.Error(ex, "工具异常：读取定义");
            NvPrompt.Error(this, "读取定义", ex.Message);
        }
    }

    private void CloseActiveTab()
    {
        var active = _tabs.ActiveTab;
        if (active != null)
        {
            _tabs.Close(active);
        }
    }

    private void CloseAllTabs()
    {
        foreach (var tab in _tabs.Tabs.ToList())
        {
            if (tab.Closable)
            {
                _tabs.Close(tab);
            }
        }
    }

    private void NextTab()
    {
        if (_tabs.Tabs.Count < 2)
        {
            return;
        }
        var active = _tabs.ActiveTab;
        var list = _tabs.Tabs.ToList();
        int index = active == null ? 0 : list.IndexOf(active);
        var next = list[(index + 1) % list.Count];
        _tabs.Activate(next);
    }

    // ================= 连接管理 =================

    private async Task NewConnectionAsync(DbKind? presetKind = null)
    {
        var profile = new ConnectionProfile();
        if (presetKind.HasValue)
        {
            profile.Kind = presetKind.Value;
            profile.Port = presetKind.Value.DefaultPort();
            profile.Name = presetKind.Value.ShortName() + " - 新连接";
            if (presetKind == DbKind.PostgreSQL)
            {
                profile.Database = "postgres";
            }
            else if (presetKind == DbKind.SqlServer)
            {
                profile.Database = "master";
            }
        }
        using var dialog = new ConnectionDialog(presetKind.HasValue ? profile : null);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var created = dialog.Profile;
        bool autoConnect = dialog.ConnectAfterSave;
        created.LastUsed = DateTime.Now;
        _store.Connections.Add(created);
        _store.Save();
        NvLog.Info("保存连接：" + created.Name + " · " + created.Kind.ShortName() + " · "
            + created.ServerLabel + " · 自动连接=" + autoConnect);
        RebuildTree();
        RefreshRecent();
        SetStatus("连接“" + created.Name + "”已保存" + (autoConnect ? "，正在连接…" : "（未自动连接）。"));
        if (autoConnect)
        {
            var node = FindConnectionNode(created.Id);
            if (node != null)
            {
                _tree.SelectedNode = node;
                node.Expand();
            }
        }
    }

    private async Task OpenSqliteAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开 SQLite 数据库",
            Filter = "SQLite 数据库 (*.db;*.sqlite;*.sqlite3;*.db3)|*.db;*.sqlite;*.sqlite3;*.db3|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var profile = new ConnectionProfile
        {
            Kind = DbKind.SQLite,
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            Database = dialog.FileName,
            Port = 0,
            Host = "127.0.0.1",
            LastUsed = DateTime.Now
        };
        var existing = _store.Connections.FirstOrDefault(c =>
            c.Kind == DbKind.SQLite && string.Equals(c.Database, profile.Database, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            _store.Connections.Add(profile);
            _store.Save();
            RebuildTree();
        }
        else
        {
            profile = existing;
        }
        RefreshRecent();
        var node = FindConnectionNode(profile.Id);
        if (node != null)
        {
            _tree.SelectedNode = node;
            node.Expand();
        }
        await NewQueryAsync(new NvNodeTag { Profile = profile, Database = profile.Database });
    }

    private TreeNode? FindConnectionNode(string profileId)
    {
        foreach (TreeNode root in _tree.Nodes)
        {
            var node = FindConnectionNode(root, profileId);
            if (node != null)
            {
                return node;
            }
        }
        return null;
    }

    private static TreeNode? FindConnectionNode(TreeNode node, string profileId)
    {
        if (node.Tag is NvNodeTag { Kind: "connection" } tag && tag.Profile?.Id == profileId)
        {
            return node;
        }
        foreach (TreeNode child in node.Nodes)
        {
            var found = FindConnectionNode(child, profileId);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private async Task EditConnectionAsync(ConnectionProfile? profile)
    {
        if (profile == null)
        {
            return;
        }
        using var dialog = new ConnectionDialog(profile);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var updated = dialog.Profile;
        if (!CloseTabsForProfile(profile))
        {
            return;
        }
        int index = _store.Connections.FindIndex(c => c.Id == profile.Id);
        if (index >= 0)
        {
            updated.Id = profile.Id;
            _store.Connections[index] = updated;
            _store.Save();
        }
        DisconnectProfile(profile);
        RebuildTree();
        RefreshRecent();
        _statusInfo.Text = "连接“" + updated.Name + "”已更新。";
    }

    private void DuplicateConnection(ConnectionProfile? profile)
    {
        if (profile == null)
        {
            return;
        }
        var copy = profile.Clone();
        _store.Connections.Add(copy);
        _store.Save();
        RebuildTree();
        RefreshRecent();
        SetStatus("已复制连接：" + copy.Name);
    }

    private void DeleteConnection(ConnectionProfile? profile)
    {
        if (profile == null)
        {
            return;
        }
        if (!NvPrompt.Confirm(this, "删除连接",
            "确定删除连接“" + profile.Name + "”吗？\n这不会删除数据库中的数据。", true))
        {
            return;
        }
        if (!CloseTabsForProfile(profile))
        {
            return;
        }
        DisconnectProfile(profile);
        _store.Connections.RemoveAll(c => c.Id == profile.Id);
        _store.Save();
        RebuildTree();
        RefreshRecent();
    }

    private void ImportConnections()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入连接",
            Filter = "连接配置 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            int count = _store.Import(dialog.FileName);
            RebuildTree();
            RefreshRecent();
            NvPrompt.Info(this, "导入连接", "成功导入 " + count + " 个连接。");
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：导入连接");
            NvPrompt.Error(this, "导入连接", ex.Message);
        }
    }

    private void ExportConnections()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出连接",
            Filter = "连接配置 (*.json)|*.json|所有文件 (*.*)|*.*",
            FileName = "navicat_connections_" + DateTime.Now.ToString("yyyyMMdd") + ".json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            _store.Export(dialog.FileName, true);
            NvPrompt.Info(this, "导出连接", "连接已导出到：\n" + dialog.FileName);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：导出连接");
            NvPrompt.Error(this, "导出连接", ex.Message);
        }
    }

    private async Task OpenRecentAsync(ConnectionProfile profile)
    {
        profile.LastUsed = DateTime.Now;
        _store.Save();
        var node = FindConnectionNode(profile.Id);
        if (node != null)
        {
            _tree.SelectedNode = node;
            node.Expand();
        }
        await NewQueryAsync(new NvNodeTag { Profile = profile, Database = profile.Database });
    }

    private void RefreshRecent()
    {
        _welcome.SetRecent(_store.Connections);
    }

    // ================= 数据库 / 表操作 =================

    private async Task CreateDatabaseAsync(NvNodeTag tag)
    {
        var profile = tag.Profile;
        if (profile == null)
        {
            return;
        }
        if (profile.Kind is DbKind.SQLite or DbKind.Oracle)
        {
            NvPrompt.Info(this, "新建数据库", profile.Kind == DbKind.SQLite
                ? "SQLite 使用文件作为数据库，请通过“打开 SQLite 数据库”创建。"
                : "Oracle 数据库由 DBA 创建，客户端不支持直接新建。");
            return;
        }
        string? name = NvPrompt.Show(this, "新建数据库", "数据库名称：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        try
        {
            var session = await EnsureSessionAsync(profile, null);
            if (session == null)
            {
                return;
            }
            string sql = profile.Kind switch
            {
                DbKind.MySQL => "CREATE DATABASE " + session.Quote(name),
                DbKind.PostgreSQL => "CREATE DATABASE " + session.Quote(name),
                DbKind.SqlServer => "CREATE DATABASE " + session.Quote(name),
                _ => ""
            };
            await Task.Run(() => session.Execute(sql, 600));
            SetStatus("数据库“" + name + "”已创建。");
            if (!CloseTabsForProfile(profile))
            {
                return;
            }
            DisconnectProfile(profile);
            RebuildTree();
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：新建数据库");
            NvPrompt.Error(this, "新建数据库", ex.Message);
        }
    }

    private async Task NewTableAsync(NvNodeTag? tag = null)
    {
        var profile = tag?.Profile ?? CurrentProfile();
        if (profile == null)
        {
            var context = await RequireContextAsync();
            profile = context.Tag?.Profile ?? _last.Profile;
            if (profile == null)
            {
                return;
            }
            tag ??= context.Tag;
        }
        string? database = tag?.Database ?? CurrentDatabase() ?? profile.Database;
        var session = await EnsureSessionAsync(profile, database);
        if (session == null)
        {
            return;
        }
        OpenDesignerTab(session, null);
    }

    private async Task NewViewAsync(NvNodeTag? tag = null)
    {
        var profile = tag?.Profile ?? CurrentProfile();
        if (profile == null)
        {
            var context = await RequireContextAsync();
            profile = context.Tag?.Profile ?? _last.Profile;
            if (profile == null)
            {
                return;
            }
            tag ??= context.Tag;
        }
        string? database = tag?.Database ?? CurrentDatabase() ?? profile.Database;
        var session = await EnsureSessionAsync(profile, database);
        if (session == null)
        {
            return;
        }
        string? name = NvPrompt.Show(this, "新建视图", "视图名称：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        string? select = NvPrompt.Show(this, "新建视图", "SELECT 语句：", "SELECT 1 AS id", true);
        if (string.IsNullOrWhiteSpace(select))
        {
            return;
        }
        try
        {
            string sql = "CREATE VIEW " + session.Quote(name) + " AS " + select.Trim().TrimEnd(';');
            await Task.Run(() => session.Execute(sql, 600));
            SetStatus("视图“" + name + "”已创建。");
            RefreshDatabaseObjects(profile, database);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：新建视图");
            NvPrompt.Error(this, "新建视图", ex.Message);
        }
    }

    private void RefreshDatabaseObjects(ConnectionProfile? profile, string? database)
    {
        if (profile == null)
        {
            return;
        }
        var node = FindDatabaseNode(profile.Id, database);
        if (node == null)
        {
            RebuildTree();
            return;
        }
        var tag = node.Tag as NvNodeTag;
        if (tag != null)
        {
            tag.State = "";
        }
        foreach (TreeNode child in node.Nodes)
        {
            if (child.Tag is NvNodeTag childTag && childTag.Kind.StartsWith("folder_", StringComparison.Ordinal))
            {
                childTag.State = "";
                child.Nodes.Clear();
                child.Nodes.Add(new TreeNode("…") { Tag = new NvNodeTag { Kind = "dummy" } });
            }
        }
        if (node.IsExpanded)
        {
            node.Collapse();
            node.Expand();
        }
    }

    private TreeNode? FindDatabaseNode(string profileId, string? database)
    {
        foreach (TreeNode root in _tree.Nodes)
        {
            var found = FindDatabaseNode(root, profileId, database);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static TreeNode? FindDatabaseNode(TreeNode node, string profileId, string? database)
    {
        if (node.Tag is NvNodeTag { Kind: "database" } tag && tag.Profile?.Id == profileId
            && string.Equals(tag.Database, database, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }
        foreach (TreeNode child in node.Nodes)
        {
            var found = FindDatabaseNode(child, profileId, database);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private void ShowTableDdl(NvNodeTag tag)
    {
        try
        {
            var profile = tag.Profile ?? _last.Profile;
            if (profile == null)
            {
                return;
            }
            var session = GetSessionObject(profile, tag.Database);
            var table = new TableInfo
            {
                Database = tag.Database ?? "",
                Schema = tag.Schema ?? "",
                Name = tag.Name ?? "",
                IsView = tag.IsView
            };
            string sql = tag.IsView ? session.GetViewDdl(table) : session.GetTableDdl(table);
            using var dialog = new NvSqlPreviewDialog("DDL - " + table.DisplayName, sql);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：查看 DDL");
            NvPrompt.Error(this, "查看 DDL", ex.Message);
        }
    }

    private async Task DropObjectAsync(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        string type = tag.IsView ? "视图" : "表";
        if (!NvPrompt.Confirm(this, "删除" + type,
            "确定删除" + type + "“" + tag.Name + "”吗？\n此操作不可恢复。", true))
        {
            return;
        }
        try
        {
            var session = await EnsureSessionAsync(profile, tag.Database);
            if (session == null)
            {
                return;
            }
            string sql = "DROP " + (tag.IsView ? "VIEW " : "TABLE ")
                + session.Qualify(tag.Database, tag.Schema, tag.Name ?? "");
            await Task.Run(() => session.Execute(sql, 600));
            SetStatus(type + "“" + tag.Name + "”已删除。");
            RefreshDatabaseObjects(profile, tag.Database);
        }
        catch (Exception ex)
        {
            NvPrompt.Error(this, "删除" + type, ex.Message);
        }
    }

    private async Task TruncateTableAsync(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        if (!NvPrompt.Confirm(this, "清空表",
            "确定清空表“" + tag.Name + "”的全部数据吗？此操作不可恢复。", true))
        {
            return;
        }
        try
        {
            var session = await EnsureSessionAsync(profile, tag.Database);
            if (session == null)
            {
                return;
            }
            string qualified = session.Qualify(tag.Database, tag.Schema, tag.Name ?? "");
            string sql = session.Kind == DbKind.SQLite ? "DELETE FROM " + qualified : "TRUNCATE TABLE " + qualified;
            await Task.Run(() => session.Execute(sql, 600));
            SetStatus("表“" + tag.Name + "”已清空。");
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：清空表");
            NvPrompt.Error(this, "清空表", ex.Message);
        }
    }

    private async Task RenameTableAsync(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        string? newName = NvPrompt.Show(this, "重命名表", "新表名：", tag.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == tag.Name)
        {
            return;
        }
        try
        {
            var session = await EnsureSessionAsync(profile, tag.Database);
            if (session == null)
            {
                return;
            }
            string oldTable = session.Qualify(tag.Database, tag.Schema, tag.Name ?? "");
            string sql = session.Kind switch
            {
                DbKind.MySQL => "RENAME TABLE " + oldTable + " TO " + session.Quote(newName),
                DbKind.SqlServer => "EXEC sp_rename N'"
                    + (string.IsNullOrWhiteSpace(tag.Schema) ? tag.Name : tag.Schema + "." + tag.Name)
                    .Replace("'", "''")
                    + "', N'" + newName.Replace("'", "''") + "'",
                _ => "ALTER TABLE " + oldTable + " RENAME TO " + session.Quote(newName)
            };
            await Task.Run(() => session.Execute(sql, 600));
            SetStatus("表已重命名为“" + newName + "”。");
            RefreshDatabaseObjects(profile, tag.Database);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：重命名表");
            NvPrompt.Error(this, "重命名表", ex.Message);
        }
    }

    private void CopyTableStructure(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        try
        {
            var session = GetSessionObject(profile, tag.Database);
            var table = new TableInfo
            {
                Database = tag.Database ?? "",
                Schema = tag.Schema ?? "",
                Name = tag.Name ?? ""
            };
            string ddl = session.GetTableDdl(table);
            string target = tag.Name + "_copy";
            ddl = ddl.Replace(session.Quote(tag.Name ?? ""), session.Quote(target), StringComparison.OrdinalIgnoreCase);
            Clipboard.SetText(ddl);
            NvPrompt.Info(this, "复制表结构", "已生成创建表 SQL 并复制到剪贴板，目标表名：" + target);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：复制表结构");
            NvPrompt.Error(this, "复制表结构", ex.Message);
        }
    }

    private async Task OpenImportAsync(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        var session = await EnsureSessionAsync(profile, tag.Database);
        if (session == null)
        {
            return;
        }
        TableInfo? table = null;
        if (tag.Kind is "table" or "view")
        {
            table = new TableInfo
            {
                Database = tag.Database ?? "",
                Schema = tag.Schema ?? "",
                Name = tag.Name ?? "",
                IsView = tag.IsView
            };
        }
        if (table != null)
        {
            NvImportDialog.ShowForTable(this, session, table,
                async () => await Task.CompletedTask);
        }
        else
        {
            NvImportDialog.ShowForDatabase(this, session);
        }
        RefreshDatabaseObjects(profile, tag.Database);
    }

    private async Task OpenDataGeneratorAsync(NvNodeTag? tag = null)
    {
        tag ??= _tree.SelectedNode?.Tag as NvNodeTag;
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        TableInfo? preset = null;
        if (tag?.Kind is "table" or "view" && tag.Profile != null)
        {
            if (tag.IsView)
            {
                NvPrompt.Info(this, "数据生成", "视图是只读对象，请选择一张表生成数据。");
                return;
            }
            session = await EnsureSessionAsync(tag.Profile, tag.Database);
            if (session == null)
            {
                return;
            }
            preset = new TableInfo
            {
                Database = tag.Database ?? "",
                Schema = tag.Schema ?? "",
                Name = tag.Name ?? ""
            };
        }
        using var form = new NvDataGeneratorForm(session, preset);
        form.ShowDialog(this);
        RefreshDatabaseObjects(session.Profile, session.CurrentDatabase);
    }

    private async Task OpenExportAsync(NvNodeTag tag)
    {
        var profile = tag.Profile ?? _last.Profile;
        if (profile == null)
        {
            return;
        }
        var session = await EnsureSessionAsync(profile, tag.Database);
        if (session == null)
        {
            return;
        }
        try
        {
            var table = new TableInfo
            {
                Database = tag.Database ?? "",
                Schema = tag.Schema ?? "",
                Name = tag.Name ?? ""
            };
            SetBusy(true, "正在读取数据…");
            var data = await Task.Run(() => session.Query("SELECT * FROM " + session.Qualify(table), null, 600));
            SetBusy(false, null);
            NvExportService.ExportTableWithDialog(this, data, table.Name, session.Kind, true);
        }
        catch (Exception ex)
        {
            SetBusy(false, null);
            NvLog.Error(ex, "工具异常：导出数据");
            NvPrompt.Error(this, "导出数据", ex.Message);
        }
    }

    // ================= 工具窗口 =================

    private async Task OpenTransferAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvDataTransferForm(session, _store);
        form.ShowDialog(this);
        RefreshDatabaseObjects(session.Profile, session.CurrentDatabase);
    }

    private async Task OpenDataSyncAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvDataSyncForm(session, _store);
        form.ShowDialog(this);
    }

    private async Task OpenStructureSyncAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvStructureSyncForm(session, _store);
        form.ShowDialog(this);
        RefreshDatabaseObjects(session.Profile, session.CurrentDatabase);
    }

    private async Task OpenBackupAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvBackupRestoreForm(session);
        form.ShowDialog(this);
        RefreshDatabaseObjects(session.Profile, session.CurrentDatabase);
    }

    private void OpenScheduler()
    {
        using var form = new NvSchedulerForm(_store);
        form.ShowDialog(this);
        _tasks.Clear();
        _tasks.AddRange(NvTaskStore.Load());
        RebuildTree();
    }

    private async Task OpenUsersAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null || IsDisposed || Disposing)
        {
            return;
        }
        using var form = new NvUserManagerForm(session);
        form.ShowDialog(this);
    }

    private async Task OpenMonitorAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null || IsDisposed || Disposing)
        {
            return;
        }
        using var form = new NvServerMonitorForm(session);
        form.ShowDialog(this);
    }

    private async Task OpenErAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvErDiagramForm(session);
        form.ShowDialog(this);
    }

    private async Task OpenQueryBuilderAsync()
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvQueryBuilderForm(session);
        if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(form.GeneratedSql))
        {
            OpenQueryTab(session, form.GeneratedSql);
        }
    }

    private async Task OpenReportAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvReportForm(session);
        form.ShowDialog(this);
    }

    private async Task OpenDbDocAsync(NvNodeTag? tag = null)
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvDbDocForm(session);
        form.ShowDialog(this);
    }

    private async Task OpenSearchAsync()
    {
        var context = await RequireContextAsync();
        var session = context.Session;
        if (session == null)
        {
            return;
        }
        using var form = new NvDbSearchForm(session);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var hit = form.SelectedHit;
            if (hit != null)
            {
                if (hit.ObjectType.Contains("表", StringComparison.OrdinalIgnoreCase))
                {
                    await OpenTableAsync(new NvNodeTag
                    {
                        Kind = "table",
                        Profile = session.Profile,
                        Database = session.CurrentDatabase,
                        Schema = hit.Schema,
                        Name = hit.Name
                    }, false);
                }
                else if (hit.ObjectType.Contains("视图", StringComparison.OrdinalIgnoreCase))
                {
                    await OpenTableAsync(new NvNodeTag
                    {
                        Kind = "view",
                        Profile = session.Profile,
                        Database = session.CurrentDatabase,
                        Schema = hit.Schema,
                        Name = hit.Name,
                        IsView = true
                    }, true);
                }
            }
        }
    }

    private void OpenSettings()
    {
        using var form = new NvSettingsForm();
        form.ShowDialog(this);
    }

    private void OpenLogViewer()
    {
        using var form = new NvLogViewerForm();
        form.ShowDialog(this);
    }

    private void ShowAbout()
    {
        using var form = new NavicatAboutForm();
        form.ShowDialog(this);
    }

    private void ShowHelp()
    {
        string text = "Navicat 复刻版 帮助\r\n\r\n"
            + "1. 点击左上角“+”新建数据库连接，支持 MySQL / MariaDB / PostgreSQL / SQL Server / Oracle / SQLite。\r\n"
            + "2. 展开连接后双击数据库中的表即可浏览和编辑数据；右键可设计表、导入导出、备份。\r\n"
            + "3. 工具栏“查询”打开多标签 SQL 编辑器，F5 执行，Ctrl+Enter 执行选中，Ctrl+S 保存脚本。\r\n"
            + "4. “传输 / 数据同步 / 结构同步”支持跨连接复制和比较数据库。\r\n"
            + "5. “备份 / 还原”生成完整 SQL 备份；计划任务可定时执行查询、备份和导出。\r\n"
            + "6. 密码使用 Windows DPAPI 加密保存在 %APPDATA%\\MiniMonitor\\navicat。\r\n"
            + "7. 如遇问题请打开“帮助 → 查看日志”，日志目录：%APPDATA%\\MiniMonitor\\navicat\\logs，"
            + "其中包含连接、SQL、导入导出、备份同步的错误与完整异常堆栈。\r\n\r\n"
            + "兼容性：Oracle / SQL Server 需要服务器允许 TCP 连接；SQLite 无需安装任何客户端。";
        MessageBox.Show(this, text, "Navicat 复刻版帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task WelcomeToolAsync(string key)
    {
        switch (key)
        {
            case "query":
                await NewQueryAsync();
                break;
            case "transfer":
                await OpenTransferAsync();
                break;
            case "structure":
                await OpenStructureSyncAsync();
                break;
            case "datasync":
                await OpenDataSyncAsync();
                break;
            case "er":
                await OpenErAsync();
                break;
            case "schedule":
                OpenScheduler();
                break;
        }
    }

    // ================= 编辑器命令 =================

    private QueryTab? ActiveQuery()
    {
        return _tabs.ActiveTab?.Content as QueryTab;
    }

    private void FormatActiveSql()
    {
        var query = ActiveQuery();
        if (query == null)
        {
            NvPrompt.Info(this, "格式化 SQL", "请先打开一个查询标签页。");
            return;
        }
        query.FormatSqlFromMenu();
    }

    private void FindInActiveEditor()
    {
        var query = ActiveQuery();
        if (query == null)
        {
            return;
        }
        string? text = NvPrompt.Show(this, "查找", "查找内容：");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (!query.FindText(text))
        {
            NvPrompt.Info(this, "查找", "未找到“" + text + "”。");
        }
    }

    private void OpenSqlFile()
    {
        var context = _tabs.ActiveTab?.Content as QueryTab;
        if (context == null)
        {
            NvPrompt.Info(this, "打开 SQL 文件", "请先打开一个查询标签页。");
            return;
        }
        context.OpenFileFromMenu();
    }

    // ================= 快捷键 / 状态 / 关闭 =================

    private void MainKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5 && !(_tabs.ActiveTab?.Content is QueryTab))
        {
            RebuildTree();
            e.Handled = true;
        }
    }

    private void UpdateSelectionStatus()
    {
        if (_tree.SelectedNode?.Tag is not NvNodeTag tag)
        {
            return;
        }
        if (tag.Profile != null)
        {
            _last.Profile = tag.Profile;
            _last.Database = tag.Database ?? _last.Database;
            _statusServer.Text = tag.Profile.Name + " · " + tag.Profile.Kind.ShortName();
        }
        switch (tag.Kind)
        {
            case "connection":
                _statusInfo.Text = "连接：" + tag.Profile?.ServerLabel;
                break;
            case "database":
                _statusInfo.Text = "数据库：" + tag.Database;
                break;
            case "table":
            case "view":
                _statusInfo.Text = (tag.IsView ? "视图：" : "表：")
                    + (string.IsNullOrEmpty(tag.Schema) ? "" : tag.Schema + ".") + tag.Name;
                break;
            default:
                _statusInfo.Text = tag.Name ?? "就绪";
                break;
        }
    }

    private void UpdateServerStatus(DbSession session, string? database)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        _statusServer.Text = session.Profile.Name + " · " + session.Kind.ShortName()
            + " · " + (string.IsNullOrWhiteSpace(session.ServerVersion) ? "已连接" : session.ServerVersion);
        if (!string.IsNullOrWhiteSpace(database))
        {
            _statusInfo.Text = "数据库：" + database;
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        _statusInfo.Text = text;
        _statusInfo.ForeColor = error ? NvTheme.Red : NvTheme.Muted;
    }

    private void SetBusy(bool busy, string? text)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        _progress.Visible = busy;
        if (text != null)
        {
            _statusInfo.Text = text;
        }
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _menu.Enabled = !busy;
        _toolbar.Enabled = !busy;
    }

    private void ToggleLeftPane()
    {
        _split.Panel1Collapsed = !_split.Panel1Collapsed;
    }

    private bool _fullScreen;
    private FormBorderStyle _oldBorder;
    private FormWindowState _oldState;

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _oldBorder = FormBorderStyle;
            _oldState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
            _fullScreen = true;
        }
        else
        {
            FormBorderStyle = _oldBorder;
            WindowState = _oldState;
            _fullScreen = false;
        }
    }

    private async Task CheckScheduledTasksAsync()
    {
        if (_closing)
        {
            return;
        }
        foreach (var task in _tasks.Where(t => t.Enabled))
        {
            if (_runningTasks.ContainsKey(task.Id))
            {
                continue;
            }
            if (task.NextRun == null)
            {
                task.NextRun = ComputeNextRun(task);
                NvTaskStore.Save(_tasks);
                continue;
            }
            if (task.NextRun > DateTime.Now)
            {
                continue;
            }
            _runningTasks.TryAdd(task.Id, 0);
            task.LastRun = DateTime.Now;
            task.NextRun = ComputeNextRun(task);
            NvTaskStore.Save(_tasks);
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunScheduledTaskAsync(task);
                }
                finally
                {
                    _runningTasks.TryRemove(task.Id, out _);
                }
            });
        }
        await Task.CompletedTask;
    }

    private static DateTime ComputeNextRun(NvScheduledTask task)
    {
        if (task.ScheduleType == "Daily"
            && TimeSpan.TryParse(task.DailyTime, out TimeSpan time))
        {
            var next = DateTime.Today.Add(time);
            if (next <= DateTime.Now)
            {
                next = next.AddDays(1);
            }
            return next;
        }
        return DateTime.Now.AddMinutes(Math.Max(1, task.IntervalMinutes));
    }

    private async Task RunScheduledTaskAsync(NvScheduledTask task)
    {
        string logFile = Path.Combine(NvPaths.LogsDir, "task_" + NvTaskRunner.Sanitize(task.Name) + ".log");
        void Log(string message)
        {
            try
            {
                File.AppendAllText(logFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            string result = await NvTaskRunner.RunAsync(task, _store, Log);
            task.LastResult = result;
            Log(result);
        }
        catch (Exception ex)
        {
            task.LastResult = "失败：" + ex.Message;
            Log(task.LastResult);
        }
        finally
        {
            NvTaskStore.Save(_tasks);
            try
            {
                if (!IsDisposed)
                {
                    BeginInvoke(new Action(() => RebuildTree()));
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string Sanitize(string name)
    {
        return NvTaskRunner.Sanitize(name);
    }

    private void NavicatFormClosing(object? sender, FormClosingEventArgs e)
    {
        foreach (var tab in _tabs.Tabs.ToList())
        {
            if (tab.Content is NvClosable closable && !closable.TryClose())
            {
                e.Cancel = true;
                return;
            }
        }
        _closing = true;
        _clockTimer.Stop();
        _schedulerTimer.Stop();
        DisposeSessions();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockTimer?.Dispose();
            _schedulerTimer?.Dispose();
            _treeNodeFont.Dispose();
            _treeBoldFont.Dispose();
            _treeSmallFont.Dispose();
            DisposeSessions();
        }
        base.Dispose(disposing);
    }
}
