using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>用户与权限管理。</summary>
internal sealed class NvUserManagerForm : Form
{
    private readonly DbSession _session;
    private readonly DataGridView _usersGrid;
    private readonly DataGridView _privilegeGrid;
    private readonly Label _status;
    private readonly Button _refresh;
    private readonly Button _create;
    private readonly Button _drop;
    private readonly Button _password;
    private readonly Button _grant;

    public NvUserManagerForm(DbSession session)
    {
        _session = session;
        Text = "用户管理 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(860, 620);
        MinimumSize = new Size(700, 520);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("用户管理", session.Profile.Name + " · " + session.Kind.ShortName()
            + " · 管理数据库账号与权限", NvIcon.User);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = NvTheme.Panel };
        _refresh = NvTheme.FlatButton("刷新", null, null, 80);
        _refresh.Location = new Point(10, 6);
        _refresh.Click += async (_, _) => await LoadUsersAsync();
        _create = NvTheme.FlatButton("新建用户", NvTheme.Accent, Color.White, 92);
        _create.Location = new Point(96, 6);
        _create.Click += async (_, _) => await CreateUserAsync();
        _drop = NvTheme.FlatButton("删除用户", null, null, 92);
        _drop.Location = new Point(194, 6);
        _drop.Click += async (_, _) => await DropUserAsync();
        _password = NvTheme.FlatButton("修改密码", null, null, 92);
        _password.Location = new Point(292, 6);
        _password.Click += async (_, _) => await ChangePasswordAsync();
        _grant = NvTheme.FlatButton("授权…", null, null, 80);
        _grant.Location = new Point(390, 6);
        _grant.Click += async (_, _) => await GrantAsync();
        toolbar.Controls.AddRange(new Control[] { _refresh, _create, _drop, _password, _grant });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var userPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 0) };
        userPanel.Controls.Add(new Label
        {
            Text = "用户",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _usersGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false
        };
        NvTheme.StyleGrid(_usersGrid, true);
        userPanel.Controls.Add(_usersGrid);
        userPanel.Controls.SetChildIndex(_usersGrid, 0);
        split.Panel1.Controls.Add(userPanel);

        var privilegePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 8) };
        privilegePanel.Controls.Add(new Label
        {
            Text = "权限（选中用户）",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _privilegeGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false
        };
        NvTheme.StyleGrid(_privilegeGrid, true);
        privilegePanel.Controls.Add(_privilegeGrid);
        privilegePanel.Controls.SetChildIndex(_privilegeGrid, 0);
        split.Panel2.Controls.Add(privilegePanel);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Text = "就绪",
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0)
        };
        bottom.Controls.Add(_status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(header);
        Load += async (_, _) => await LoadUsersAsync();
        _usersGrid.SelectionChanged += async (_, _) => await LoadPrivilegesAsync();
    }

    private async Task LoadUsersAsync()
    {
        _status.Text = "正在读取用户…";
        try
        {
            var users = await Task.Run(() => _session.GetUsers());
            var table = new DataTable();
            table.Columns.Add("用户名", typeof(string));
            table.Columns.Add("主机", typeof(string));
            table.Columns.Add("说明", typeof(string));
            foreach (var user in users)
            {
                table.Rows.Add(user.Name, user.Host, user.Info ?? "");
            }
            _usersGrid.DataSource = table;
            foreach (DataGridViewColumn column in _usersGrid.Columns)
            {
                column.Width = 160;
            }
            _status.Text = users.Count == 0
                ? "当前数据库不支持用户列表，或没有查看权限。"
                : "共 " + users.Count + " 个用户。";
        }
        catch (Exception ex)
        {
            _status.Text = "读取用户失败：" + ex.Message;
        }
    }

    private UserInfo? SelectedUser()
    {
        if (_usersGrid.CurrentRow == null)
        {
            return null;
        }
        var row = _usersGrid.CurrentRow;
        string name = Convert.ToString(row.Cells[0].Value) ?? "";
        string host = _usersGrid.Columns.Count > 1 ? Convert.ToString(row.Cells[1].Value) ?? "" : "";
        return string.IsNullOrWhiteSpace(name) ? null : new UserInfo { Name = name, Host = host };
    }

    private async Task LoadPrivilegesAsync()
    {
        var user = SelectedUser();
        if (user == null)
        {
            return;
        }
        try
        {
            var table = await Task.Run(() => _session.GetPrivileges(user));
            if (table.Columns.Count == 0)
            {
                var fallback = new DataTable();
                fallback.Columns.Add("说明", typeof(string));
                fallback.Rows.Add("当前数据库或权限不足，无法读取该用户的权限列表。");
                _privilegeGrid.DataSource = fallback;
                return;
            }
            _privilegeGrid.DataSource = table;
        }
        catch (Exception ex)
        {
            var table = new DataTable();
            table.Columns.Add("错误", typeof(string));
            table.Rows.Add(ex.Message);
            _privilegeGrid.DataSource = table;
        }
    }

    private async Task CreateUserAsync()
    {
        if (_session.Kind == DbKind.SQLite)
        {
            NvPrompt.Info(this, "新建用户", "SQLite 是单文件数据库，不支持用户账号。");
            return;
        }
        string? name = NvPrompt.Show(this, "新建用户", "用户名：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        string host = "";
        if (_session.Kind == DbKind.MySQL)
        {
            host = NvPrompt.Show(this, "新建用户", "允许登录的主机（如 localhost 或 %）：", "%") ?? "%";
        }
        string? password = NvPrompt.Show(this, "新建用户", "密码：");
        if (password == null)
        {
            return;
        }
        string sql;
        try
        {
            sql = _session.Kind switch
            {
                DbKind.MySQL => "CREATE USER " + _session.Quote(name) + "@" + _session.Quote(host)
                    + " IDENTIFIED BY '" + password.Replace("'", "''") + "'",
                DbKind.PostgreSQL => "CREATE USER " + _session.Quote(name) + " WITH PASSWORD '"
                    + password.Replace("'", "''") + "'",
                DbKind.SqlServer => "CREATE LOGIN " + _session.Quote(name) + " WITH PASSWORD = N'"
                    + password.Replace("'", "''") + "'",
                DbKind.Oracle => "CREATE USER " + _session.Quote(name.ToUpperInvariant()) + " IDENTIFIED BY \""
                    + password.Replace("\"", "\"\"") + "\"",
                _ => throw new NotSupportedException()
            };
            await Task.Run(() => _session.Execute(sql, 300));
            _status.Text = "用户“" + name + "”已创建。";
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：新建用户");
            NvPrompt.Error(this, "新建用户", ex.Message);
        }
    }

    private async Task DropUserAsync()
    {
        var user = SelectedUser();
        if (user == null)
        {
            return;
        }
        if (!NvPrompt.Confirm(this, "删除用户", "确定删除用户“" + user.DisplayName + "”吗？", true))
        {
            return;
        }
        try
        {
            string sql = _session.Kind switch
            {
                DbKind.MySQL => "DROP USER " + _session.Quote(user.Name) + "@" + _session.Quote(user.Host),
                DbKind.PostgreSQL => "DROP USER " + _session.Quote(user.Name),
                DbKind.SqlServer => "DROP LOGIN " + _session.Quote(user.Name),
                DbKind.Oracle => "DROP USER " + _session.Quote(user.Name) + " CASCADE",
                _ => throw new NotSupportedException()
            };
            await Task.Run(() => _session.Execute(sql, 300));
            _status.Text = "用户已删除。";
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：删除用户");
            NvPrompt.Error(this, "删除用户", ex.Message);
        }
    }

    private async Task ChangePasswordAsync()
    {
        var user = SelectedUser();
        if (user == null)
        {
            return;
        }
        string? password = NvPrompt.Show(this, "修改密码", "用户 " + user.DisplayName + " 的新密码：");
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }
        try
        {
            string sql = _session.Kind switch
            {
                DbKind.MySQL => "ALTER USER " + _session.Quote(user.Name) + "@" + _session.Quote(user.Host)
                    + " IDENTIFIED BY '" + password.Replace("'", "''") + "'",
                DbKind.PostgreSQL => "ALTER USER " + _session.Quote(user.Name) + " WITH PASSWORD '"
                    + password.Replace("'", "''") + "'",
                DbKind.SqlServer => "ALTER LOGIN " + _session.Quote(user.Name) + " WITH PASSWORD = N'"
                    + password.Replace("'", "''") + "'",
                DbKind.Oracle => "ALTER USER " + _session.Quote(user.Name.ToUpperInvariant())
                    + " IDENTIFIED BY \"" + password.Replace("\"", "\"\"") + "\"",
                _ => throw new NotSupportedException()
            };
            await Task.Run(() => _session.Execute(sql, 300));
            _status.Text = "密码已修改。";
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：修改密码");
            NvPrompt.Error(this, "修改密码", ex.Message);
        }
    }

    private async Task GrantAsync()
    {
        var user = SelectedUser();
        if (user == null)
        {
            return;
        }
        string database = _session.CurrentDatabase;
        string defaultSql = _session.Kind switch
        {
            DbKind.MySQL => "GRANT ALL PRIVILEGES ON " + _session.Quote(database) + ".* TO "
                + _session.Quote(user.Name) + "@" + _session.Quote(user.Host) + "; FLUSH PRIVILEGES;",
            DbKind.PostgreSQL => "GRANT ALL PRIVILEGES ON DATABASE " + _session.Quote(database) + " TO "
                + _session.Quote(user.Name) + ";",
            DbKind.SqlServer => "ALTER SERVER ROLE sysadmin ADD MEMBER " + _session.Quote(user.Name) + ";",
            DbKind.Oracle => "GRANT DBA TO " + _session.Quote(user.Name) + ";",
            _ => ""
        };
        string? sql = NvPrompt.Show(this, "授权", "为 " + user.DisplayName + " 执行授权 SQL：", defaultSql, true);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }
        try
        {
            await Task.Run(() => _session.Execute(sql, 300));
            _status.Text = "授权语句已执行。";
            await LoadPrivilegesAsync();
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：授权");
            NvPrompt.Error(this, "授权", ex.Message);
        }
    }
}

/// <summary>服务器监控：会话 / 变量。</summary>
internal sealed class NvServerMonitorForm : Form
{
    private readonly DbSession _session;
    private readonly DataGridView _sessionGrid;
    private readonly DataGridView _variableGrid;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly CheckBox _autoRefresh;
    private bool _loading;

    public NvServerMonitorForm(DbSession session)
    {
        _session = session;
        Text = "服务器监控 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 660);
        MinimumSize = new Size(820, 540);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("服务器监控", session.Profile.Name + " · " + session.Kind.ShortName()
            + " · " + (session.ServerVersion ?? ""), NvIcon.Server);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = NvTheme.Panel };
        var refresh = NvTheme.FlatButton("刷新", null, null, 80);
        refresh.Location = new Point(10, 6);
        refresh.Click += async (_, _) => await LoadAllAsync();
        var kill = NvTheme.FlatButton("终止会话", null, null, 92);
        kill.Location = new Point(96, 6);
        kill.Click += async (_, _) => await KillSessionAsync();
        _autoRefresh = new CheckBox
        {
            Text = "每 3 秒自动刷新",
            Location = new Point(198, 12),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        toolbar.Controls.AddRange(new Control[] { refresh, kill, _autoRefresh });

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = NvTheme.Ui(9f), Padding = new Point(14, 4) };
        var sessionPage = new TabPage("会话 / 进程") { BackColor = NvTheme.Panel };
        _sessionGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false
        };
        NvTheme.StyleGrid(_sessionGrid, true);
        sessionPage.Controls.Add(_sessionGrid);
        var variablePage = new TabPage("服务器变量") { BackColor = NvTheme.Panel };
        _variableGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false
        };
        NvTheme.StyleGrid(_variableGrid, true);
        variablePage.Controls.Add(_variableGrid);
        tabs.TabPages.Add(sessionPage);
        tabs.TabPages.Add(variablePage);
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (tabs.SelectedIndex == 1)
            {
                await LoadVariablesAsync();
            }
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0)
        };
        bottom.Controls.Add(_status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        body.Controls.Add(tabs);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(header);

        _timer = new System.Windows.Forms.Timer { Interval = 3000 };
        _timer.Tick += async (_, _) =>
        {
            if (_autoRefresh.Checked)
            {
                await LoadSessionsAsync();
            }
        };
        Load += async (_, _) =>
        {
            await LoadAllAsync();
            _timer.Start();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
        };
    }

    private async Task LoadAllAsync()
    {
        await LoadSessionsAsync();
        await LoadVariablesAsync();
    }

    private async Task LoadSessionsAsync()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        try
        {
            var table = await Task.Run(() => _session.GetProcesses());
            _sessionGrid.DataSource = table;
            foreach (DataGridViewColumn column in _sessionGrid.Columns)
            {
                column.Width = Math.Min(240, Math.Max(90, column.HeaderText.Length * 12));
            }
            _status.Text = "会话数：" + table.Rows.Count + " · " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            _status.Text = "读取会话失败：" + ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadVariablesAsync()
    {
        try
        {
            var table = await Task.Run(() => _session.GetServerVariables());
            _variableGrid.DataSource = table;
            foreach (DataGridViewColumn column in _variableGrid.Columns)
            {
                column.Width = 200;
            }
        }
        catch (Exception ex)
        {
            _status.Text = "读取变量失败：" + ex.Message;
        }
    }

    private async Task KillSessionAsync()
    {
        if (_sessionGrid.CurrentRow == null)
        {
            return;
        }
        string id = "";
        if (_session.Kind == DbKind.MySQL)
        {
            id = Convert.ToString(_sessionGrid.CurrentRow.Cells[0].Value) ?? "";
        }
        else if (_session.Kind == DbKind.PostgreSQL)
        {
            id = Convert.ToString(GetCellValue(_sessionGrid.CurrentRow, "pid")) ?? "";
        }
        else if (_session.Kind == DbKind.SqlServer)
        {
            id = Convert.ToString(GetCellValue(_sessionGrid.CurrentRow, "session_id")) ?? "";
        }
        else if (_session.Kind == DbKind.Oracle)
        {
            string sid = Convert.ToString(GetCellValue(_sessionGrid.CurrentRow, "SID")) ?? "";
            string serial = Convert.ToString(GetCellValue(_sessionGrid.CurrentRow, "SERIAL#")) ?? "";
            if (string.IsNullOrWhiteSpace(sid))
            {
                sid = Convert.ToString(_sessionGrid.CurrentRow.Cells[0].Value) ?? "";
            }
            if (string.IsNullOrWhiteSpace(serial) && _sessionGrid.CurrentRow.Cells.Count > 1)
            {
                serial = Convert.ToString(_sessionGrid.CurrentRow.Cells[1].Value) ?? "";
            }
            id = sid + "," + serial;
        }
        if (string.IsNullOrWhiteSpace(id))
        {
            NvPrompt.Error(this, "终止会话", "无法识别当前会话的标识列。");
            return;
        }
        if (!NvPrompt.Confirm(this, "终止会话", "确定终止会话 " + id + " 吗？", true))
        {
            return;
        }
        try
        {
            await Task.Run(() => _session.KillProcess(id));
            _status.Text = "会话 " + id + " 已终止。";
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：终止会话");
            NvPrompt.Error(this, "终止会话", ex.Message);
        }
    }

    private static object? GetCellValue(DataGridViewRow row, string name)
    {
        foreach (DataGridViewCell cell in row.Cells)
        {
            string header = row.DataGridView?.Columns[cell.ColumnIndex].HeaderText ?? "";
            string property = row.DataGridView?.Columns[cell.ColumnIndex].DataPropertyName ?? "";
            if (header.Equals(name, StringComparison.OrdinalIgnoreCase)
                || property.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return cell.Value;
            }
        }
        return null;
    }
}
