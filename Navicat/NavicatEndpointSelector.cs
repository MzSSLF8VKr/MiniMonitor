using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>连接 / 数据库选择器，供传输、同步等工具复用。</summary>
internal sealed class NvEndpointSelector : UserControl
{
    private readonly ConnectionStore _store;
    private readonly ComboBox _profileBox;
    private readonly ComboBox _databaseBox;
    private readonly Label _status;
    private readonly Button _refresh;
    private DbSession? _session;
    private ConnectionProfile? _profile;
    private string? _database;
    private bool _loading;
    private int _version;

    public event EventHandler? SelectionChanged;

    public NvEndpointSelector(string title, ConnectionStore store)
    {
        _store = store;
        BackColor = NvTheme.Panel;
        Height = 92;
        Width = 420;

        var caption = new Label
        {
            Text = title,
            Location = new Point(0, 0),
            AutoSize = true,
            Font = NvTheme.Ui(9.2f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        };
        Controls.Add(caption);
        Controls.Add(new Label
        {
            Text = "连接",
            Location = new Point(0, 26),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        _profileBox = new ComboBox
        {
            Location = new Point(48, 22),
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9f)
        };
        Controls.Add(_profileBox);
        Controls.Add(new Label
        {
            Text = "数据库",
            Location = new Point(0, 58),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        _databaseBox = new ComboBox
        {
            Location = new Point(48, 54),
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9f)
        };
        Controls.Add(_databaseBox);
        _refresh = new Button
        {
            Text = "刷新",
            Location = new Point(306, 22),
            Size = new Size(56, 24),
            FlatStyle = FlatStyle.Flat,
            Font = NvTheme.Ui(8.6f),
            BackColor = NvTheme.Panel,
            ForeColor = NvTheme.Text
        };
        _refresh.FlatAppearance.BorderColor = NvTheme.Border;
        _refresh.Click += async (_, _) => await LoadDatabasesAsync(true);
        Controls.Add(_refresh);
        _status = new Label
        {
            Text = "未连接",
            Location = new Point(48, 78),
            AutoSize = true,
            ForeColor = NvTheme.Muted,
            Font = NvTheme.Ui(8.4f)
        };
        Controls.Add(_status);

        _profileBox.SelectedIndexChanged += async (_, _) => await OnProfileChangedAsync();
        _databaseBox.SelectedIndexChanged += async (_, _) => await OnDatabaseChangedAsync();
        Load += async (_, _) =>
        {
            _loading = true;
            foreach (var profile in _store.Connections)
            {
                _profileBox.Items.Add(profile.Name);
            }
            _loading = false;
            if (_profileBox.Items.Count > 0)
            {
                _profileBox.SelectedIndex = 0;
            }
        };
    }

    public DbSession? SelectedSession => _session;
    public ConnectionProfile? SelectedProfile => _profile;
    public string? SelectedDatabase => _database;
    public bool IsReady => _session is { IsOpen: true };

    public void ReloadProfiles()
    {
        _profileBox.BeginUpdate();
        _profileBox.Items.Clear();
        foreach (var profile in _store.Connections)
        {
            _profileBox.Items.Add(profile.Name);
        }
        _profileBox.EndUpdate();
    }

    private async Task OnProfileChangedAsync()
    {
        if (_loading)
        {
            return;
        }
        int index = _profileBox.SelectedIndex;
        if (index < 0 || index >= _store.Connections.Count)
        {
            return;
        }
        var profile = _store.Connections[index];
        _profile = profile;
        _databaseBox.Items.Clear();
        _status.Text = "正在连接 " + profile.Name + "…";
        DisposeSession();
        _session = new DbSession(profile);
        try
        {
            await Task.Run(() => _session.Open());
            if (IsDisposed)
            {
                return;
            }
            _status.Text = "已连接 " + (_session.ServerVersion ?? "");
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "端点选择器连接失败：" + profile.Name + " · " + profile.ServerLabel);
            _status.Text = "连接失败：" + ex.Message;
            return;
        }
        await LoadDatabasesAsync(false);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadDatabasesAsync(bool reconnect)
    {
        if (_profile == null || _session == null)
        {
            return;
        }
        int version = ++_version;
        _loading = true;
        try
        {
            if (!_session.IsOpen)
            {
                await Task.Run(() => _session.Open());
            }
            var databases = await Task.Run(() => _session.GetDatabases());
            if (version != _version || IsDisposed)
            {
                return;
            }
            _databaseBox.BeginUpdate();
            _databaseBox.Items.Clear();
            foreach (string database in databases)
            {
                _databaseBox.Items.Add(database);
            }
            _databaseBox.EndUpdate();
            string? desired = _profile.Database;
            int selected = 0;
            if (!string.IsNullOrWhiteSpace(desired))
            {
                int index = _databaseBox.Items.IndexOf(desired);
                if (index >= 0)
                {
                    selected = index;
                }
            }
            if (_databaseBox.Items.Count > 0)
            {
                _database = null;
                _databaseBox.SelectedIndex = selected;
            }
            _status.Text = "已连接 " + (_session.ServerVersion ?? "") + "  ·  " + databases.Count + " 个数据库";
        }
        catch (Exception ex)
        {
            _status.Text = "读取数据库失败：" + ex.Message;
        }
        finally
        {
            _loading = false;
        }
        await OnDatabaseChangedAsync();
        if (reconnect)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task OnDatabaseChangedAsync()
    {
        if (_loading || _profile == null)
        {
            return;
        }
        string? database = _databaseBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }
        if (string.Equals(database, _database, StringComparison.OrdinalIgnoreCase) && _session is { IsOpen: true })
        {
            return;
        }
        _database = database;
        DisposeSession();
        _session = new DbSession(_profile,
            _profile.Kind is DbKind.SQLite or DbKind.Oracle ? _profile.Database : database);
        try
        {
            await Task.Run(() => _session.Open());
            if (IsDisposed)
            {
                return;
            }
            _status.Text = "已连接 " + (_session.ServerVersion ?? "") + " · " + database;
        }
        catch (Exception ex)
        {
            _status.Text = "连接数据库失败：" + ex.Message;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeSession()
    {
        try
        {
            _session?.Dispose();
        }
        catch
        {
            // ignore
        }
        _session = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeSession();
        }
        base.Dispose(disposing);
    }
}
