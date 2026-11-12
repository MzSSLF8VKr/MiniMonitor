using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>计划任务执行器（主窗口定时器与计划任务窗口共用）。</summary>
internal static class NvTaskRunner
{
    public static async Task<string> RunAsync(NvScheduledTask task, ConnectionStore store, Action<string> log)
    {
        var profile = store.Find(task.ProfileId);
        if (profile == null)
        {
            throw new InvalidOperationException("连接不存在：" + task.ProfileId);
        }
        log("开始执行：" + task.Name + " (" + task.Type + ")");
        DbSession? session = null;
        try
        {
            session = new DbSession(profile, string.IsNullOrWhiteSpace(task.Database) ? null : task.Database);
            session.Open();
            string result;
            switch (task.Type)
            {
                case NvTaskType.Query:
                {
                    var batch = session.ExecuteBatch(task.Sql, 600, 100000);
                    result = "成功：执行 " + batch.ResultSets.Count + " 个结果集，用时 "
                        + batch.Elapsed.TotalMilliseconds.ToString("0") + " ms";
                    break;
                }
                case NvTaskType.Backup:
                {
                    string folder = string.IsNullOrWhiteSpace(task.OutputPath) ? NvPaths.BackupsDir : task.OutputPath;
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, Sanitize(task.Name) + "_"
                        + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".sql");
                    int rows = BackupService.DumpDatabase(session, task.Database, file, log);
                    result = "成功：备份 " + rows + " 行到 " + file;
                    break;
                }
                case NvTaskType.Export:
                {
                    string file = task.OutputPath;
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        file = Path.Combine(NvPaths.ExportsDir, Sanitize(task.Name) + "_"
                            + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                    }
                    var format = Enum.TryParse<NvExportFormat>(task.ExportFormat, true, out var f)
                        ? f : NvExportFormat.CSV;
                    int rows = NvExportService.Export(session.Query(task.Sql, null, 600), file, format,
                        true, false, session.Kind, "export");
                    result = "成功：导出 " + rows + " 行到 " + file;
                    break;
                }
                default:
                    result = "成功";
                    break;
            }
            log(result);
            return result;
        }
        catch (Exception ex)
        {
            log("失败：" + ex.Message);
            throw;
        }
        finally
        {
            session?.Dispose();
        }
    }

    public static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "task" : name;
    }

    public static DateTime ComputeNextRun(NvScheduledTask task)
    {
        if (task.ScheduleType == "Daily" && TimeSpan.TryParse(task.DailyTime, out TimeSpan time))
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
}

/// <summary>计划任务管理。</summary>
internal sealed class NvSchedulerForm : Form
{
    private readonly ConnectionStore _store;
    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly Button _runButton;
    private readonly Button _toggleButton;
    private readonly Button _editButton;
    private readonly Button _deleteButton;
    private readonly Button _logButton;
    private List<NvScheduledTask> _tasks;

    public NvSchedulerForm(ConnectionStore store)
    {
        _store = store;
        _tasks = NvTaskStore.Load();
        Text = "计划任务";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 620);
        MinimumSize = new Size(820, 520);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("计划任务", "定时执行 SQL 查询、数据库备份或数据导出（程序运行期间生效）",
            NvIcon.Schedule);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = NvTheme.Panel };
        var add = NvTheme.FlatButton("新建任务", NvTheme.Accent, Color.White, 92);
        add.Location = new Point(10, 6);
        add.Click += async (_, _) => await EditTaskAsync(null);
        _editButton = NvTheme.FlatButton("编辑", null, null, 76);
        _editButton.Location = new Point(108, 6);
        _editButton.Click += async (_, _) => await EditSelectedAsync();
        _deleteButton = NvTheme.FlatButton("删除", null, null, 76);
        _deleteButton.Location = new Point(190, 6);
        _deleteButton.Click += (_, _) => DeleteSelected();
        _toggleButton = NvTheme.FlatButton("启用 / 停用", null, null, 100);
        _toggleButton.Location = new Point(272, 6);
        _toggleButton.Click += (_, _) => ToggleSelected();
        _runButton = NvTheme.FlatButton("立即运行", null, null, 92);
        _runButton.Location = new Point(378, 6);
        _runButton.Click += async (_, _) => await RunSelectedAsync();
        _logButton = NvTheme.FlatButton("查看日志", null, null, 92);
        _logButton.Location = new Point(476, 6);
        _logButton.Click += (_, _) => ShowLog();
        var refresh = NvTheme.FlatButton("刷新", null, null, 76);
        refresh.Location = new Point(574, 6);
        refresh.Click += (_, _) => Reload();
        toolbar.Controls.AddRange(new Control[]
        {
            add, _editButton, _deleteButton, _toggleButton, _runButton, _logButton, refresh
        });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            MultiSelect = false
        };
        NvTheme.StyleGrid(_grid, true);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "任务名", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "类型", Width = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Profile", HeaderText = "连接", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Database", HeaderText = "数据库", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Schedule", HeaderText = "计划", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Enabled", HeaderText = "启用", Width = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastRun", HeaderText = "上次运行", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Result",
            HeaderText = "上次结果",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                await EditSelectedAsync();
            }
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0)
        };
        bottom.Controls.Add(_status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        body.Controls.Add(_grid);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(header);
        Load += (_, _) =>
        {
            Reload();
        };
    }

    private void Reload()
    {
        _tasks = NvTaskStore.Load();
        _grid.Rows.Clear();
        foreach (var task in _tasks)
        {
            var profile = _store.Find(task.ProfileId);
            int index = _grid.Rows.Add(
                task.Name,
                task.Type switch
                {
                    NvTaskType.Query => "查询",
                    NvTaskType.Backup => "备份",
                    _ => "导出"
                },
                profile?.Name ?? "(已删除)",
                task.Database,
                task.ScheduleType == "Daily"
                    ? "每天 " + task.DailyTime
                    : "每 " + task.IntervalMinutes + " 分钟",
                task.Enabled ? "是" : "否",
                task.LastRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                task.LastResult);
            _grid.Rows[index].Tag = task;
        }
        _status.Text = "共 " + _tasks.Count + " 个计划任务。";
        UpdateButtons();
    }

    private NvScheduledTask? SelectedTask()
    {
        return _grid.CurrentRow?.Tag as NvScheduledTask;
    }

    private void UpdateButtons()
    {
        bool has = SelectedTask() != null;
        _editButton.Enabled = has;
        _deleteButton.Enabled = has;
        _toggleButton.Enabled = has;
        _runButton.Enabled = has;
        _logButton.Enabled = has;
    }

    private async Task EditSelectedAsync()
    {
        var task = SelectedTask();
        if (task != null)
        {
            await EditTaskAsync(task);
        }
    }

    private async Task EditTaskAsync(NvScheduledTask? existing)
    {
        using var dialog = new NvTaskEditDialog(existing, _store);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var task = dialog.Task;
        if (!task.Enabled)
        {
            task.LastResult = "已停用";
        }
        task.NextRun = task.Enabled ? NvTaskRunner.ComputeNextRun(task) : null;
        int index = _tasks.FindIndex(t => t.Id == task.Id);
        if (index >= 0)
        {
            _tasks[index] = task;
        }
        else
        {
            _tasks.Add(task);
        }
        NvTaskStore.Save(_tasks);
        Reload();
        await Task.CompletedTask;
    }

    private void DeleteSelected()
    {
        var task = SelectedTask();
        if (task == null)
        {
            return;
        }
        if (!NvPrompt.Confirm(this, "删除任务", "确定删除任务“" + task.Name + "”吗？", true))
        {
            return;
        }
        _tasks.RemoveAll(t => t.Id == task.Id);
        NvTaskStore.Save(_tasks);
        Reload();
    }

    private void ToggleSelected()
    {
        var task = SelectedTask();
        if (task == null)
        {
            return;
        }
        task.Enabled = !task.Enabled;
        task.NextRun = task.Enabled ? NvTaskRunner.ComputeNextRun(task) : null;
        task.LastResult = task.Enabled ? "已启用" : "已停用";
        NvTaskStore.Save(_tasks);
        Reload();
    }

    private async Task RunSelectedAsync()
    {
        var task = SelectedTask();
        if (task == null)
        {
            return;
        }
        if (!NvPrompt.Confirm(this, "立即运行", "确定立即执行任务“" + task.Name + "”吗？", false))
        {
            return;
        }
        _runButton.Enabled = false;
        _status.Text = "正在执行 " + task.Name + "…";
        var log = new StringBuilder();
        try
        {
            string result = await Task.Run(() => NvTaskRunner.RunAsync(task, _store, message => log.AppendLine(message)));
            task.LastRun = DateTime.Now;
            task.LastResult = result;
            NvTaskStore.Save(_tasks);
            _status.Text = result;
            NvPrompt.Info(this, "任务完成", result);
        }
        catch (Exception ex)
        {
            task.LastRun = DateTime.Now;
            task.LastResult = "失败：" + ex.Message;
            NvTaskStore.Save(_tasks);
            _status.Text = task.LastResult;
            NvLog.Error(ex, "工具异常：任务失败");
            NvPrompt.Error(this, "任务失败", ex.Message);
        }
        finally
        {
            _runButton.Enabled = true;
            Reload();
        }
    }

    private void ShowLog()
    {
        var task = SelectedTask();
        if (task == null)
        {
            return;
        }
        string file = Path.Combine(NvPaths.LogsDir, "task_" + NvTaskRunner.Sanitize(task.Name) + ".log");
        if (!File.Exists(file))
        {
            NvPrompt.Info(this, "任务日志", "该任务还没有日志。");
            return;
        }
        using var dialog = new Form
        {
            Text = "任务日志 - " + task.Name,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(760, 520),
            Font = NvTheme.Ui(9f),
            BackColor = NvTheme.Background,
            ShowInTaskbar = false
        };
        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = NvTheme.Mono(9f),
            Text = File.ReadAllText(file)
        };
        dialog.Controls.Add(box);
        dialog.ShowDialog(this);
    }
}

/// <summary>计划任务编辑。</summary>
internal sealed class NvTaskEditDialog : Form
{
    private readonly NvScheduledTask _task;
    private readonly ConnectionStore _store;
    private readonly TextBox _name;
    private readonly ComboBox _type;
    private readonly ComboBox _profile;
    private readonly TextBox _database;
    private readonly TextBox _sql;
    private readonly TextBox _output;
    private readonly ComboBox _format;
    private readonly ComboBox _scheduleType;
    private readonly NumericUpDown _interval;
    private readonly MaskedTextBox _dailyTime;
    private readonly CheckBox _enabled;
    private readonly Label _outputLabel;
    private readonly Label _formatLabel;
    private readonly Label _sqlLabel;

    public NvScheduledTask Task => _task;

    public NvTaskEditDialog(NvScheduledTask? existing, ConnectionStore store)
    {
        _store = store;
        _task = existing ?? new NvScheduledTask();
        Text = existing == null ? "新建计划任务" : "编辑计划任务";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 600);
        MinimumSize = new Size(620, 520);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header(existing == null ? "新建计划任务" : "编辑计划任务",
            "在程序运行期间按间隔或每天固定时间执行", NvIcon.Schedule);

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 14, 20, 0) };
        body.Controls.Add(new Label { Text = "任务名", Location = new Point(4, 12), AutoSize = true, ForeColor = NvTheme.Text });
        _name = new TextBox
        {
            Location = new Point(110, 8),
            Width = 520,
            Font = NvTheme.Ui(9.2f),
            Text = _task.Name
        };
        body.Controls.Add(_name);

        body.Controls.Add(new Label { Text = "类型", Location = new Point(4, 50), AutoSize = true, ForeColor = NvTheme.Text });
        _type = new ComboBox
        {
            Location = new Point(110, 46),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        _type.Items.AddRange(new[] { "执行 SQL 查询", "数据库备份", "导出查询结果" });
        _type.SelectedIndex = (int)_task.Type;
        body.Controls.Add(_type);

        body.Controls.Add(new Label { Text = "连接", Location = new Point(4, 88), AutoSize = true, ForeColor = NvTheme.Text });
        _profile = new ComboBox
        {
            Location = new Point(110, 84),
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        foreach (var profile in store.Connections)
        {
            _profile.Items.Add(profile.Name);
        }
        int profileIndex = store.Connections.FindIndex(p => p.Id == _task.ProfileId);
        _profile.SelectedIndex = profileIndex >= 0 ? profileIndex : (store.Connections.Count > 0 ? 0 : -1);
        body.Controls.Add(_profile);

        body.Controls.Add(new Label { Text = "数据库", Location = new Point(430, 88), AutoSize = true, ForeColor = NvTheme.Text });
        _database = new TextBox
        {
            Location = new Point(490, 84),
            Width = 140,
            Font = NvTheme.Ui(9.2f),
            Text = _task.Database
        };
        body.Controls.Add(_database);

        _sqlLabel = new Label { Text = "SQL", Location = new Point(4, 126), AutoSize = true, ForeColor = NvTheme.Text };
        body.Controls.Add(_sqlLabel);
        _sql = new TextBox
        {
            Location = new Point(110, 122),
            Width = 520,
            Height = 150,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = NvTheme.Mono(9.5f),
            Text = _task.Sql
        };
        body.Controls.Add(_sql);

        _outputLabel = new Label { Text = "输出路径", Location = new Point(4, 290), AutoSize = true, ForeColor = NvTheme.Text };
        _output = new TextBox
        {
            Location = new Point(110, 286),
            Width = 520,
            Font = NvTheme.Ui(9.2f),
            Text = _task.OutputPath
        };
        body.Controls.Add(_outputLabel);
        body.Controls.Add(_output);

        _formatLabel = new Label { Text = "导出格式", Location = new Point(4, 328), AutoSize = true, ForeColor = NvTheme.Text };
        _format = new ComboBox
        {
            Location = new Point(110, 324),
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        _format.Items.AddRange(new[] { "CSV", "TXT", "JSON", "XML", "HTML", "SQL", "XLSX" });
        _format.SelectedItem = _task.ExportFormat;
        if (_format.SelectedIndex < 0)
        {
            _format.SelectedIndex = 0;
        }
        body.Controls.Add(_formatLabel);
        body.Controls.Add(_format);

        body.Controls.Add(new Label { Text = "计划", Location = new Point(4, 366), AutoSize = true, ForeColor = NvTheme.Text });
        _scheduleType = new ComboBox
        {
            Location = new Point(110, 362),
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        _scheduleType.Items.AddRange(new[] { "按间隔", "每天固定时间" });
        _scheduleType.SelectedIndex = _task.ScheduleType == "Daily" ? 1 : 0;
        body.Controls.Add(_scheduleType);
        _interval = new NumericUpDown
        {
            Location = new Point(280, 362),
            Width = 90,
            Minimum = 1,
            Maximum = 10080,
            Value = Math.Clamp(_task.IntervalMinutes <= 0 ? 60 : _task.IntervalMinutes, 1, 10080),
            Font = NvTheme.Ui(9.2f)
        };
        body.Controls.Add(_interval);
        body.Controls.Add(new Label { Text = "分钟", Location = new Point(374, 366), AutoSize = true, ForeColor = NvTheme.Muted });
        _dailyTime = new MaskedTextBox
        {
            Location = new Point(420, 362),
            Width = 80,
            Mask = "00:00",
            Font = NvTheme.Ui(9.2f),
            Text = string.IsNullOrWhiteSpace(_task.DailyTime) ? "02:00" : _task.DailyTime
        };
        body.Controls.Add(_dailyTime);

        _enabled = new CheckBox
        {
            Text = "启用该任务",
            Location = new Point(110, 402),
            AutoSize = true,
            Checked = _task.Enabled,
            ForeColor = NvTheme.Text
        };
        body.Controls.Add(_enabled);
        body.Controls.Add(new Label
        {
            Text = "说明：备份任务会把 SQL 脚本写入输出目录；导出任务把 SELECT 结果写入输出文件；查询任务的 SQL 必须可执行。",
            Location = new Point(4, 438),
            Size = new Size(620, 44),
            ForeColor = NvTheme.Muted
        });

        _type.SelectedIndexChanged += (_, _) => UpdateMode();
        _scheduleType.SelectedIndexChanged += (_, _) => UpdateMode();
        UpdateMode();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = NvTheme.Background };
        var ok = NvTheme.FlatButton("保存", NvTheme.Accent, Color.White, 92);
        ok.Location = new Point(490, 10);
        ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        ok.Click += (_, _) => Save();
        var cancel = NvTheme.FlatButton("取消", null, null, 88);
        cancel.Location = new Point(590, 10);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cancel.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { ok, cancel });

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 0) };
        content.Controls.Add(body);
        Controls.Add(content);
        Controls.Add(bottom);
        Controls.Add(header);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void UpdateMode()
    {
        var type = (NvTaskType)Math.Max(0, _type.SelectedIndex);
        _sqlLabel.Visible = true;
        _sql.Visible = true;
        _outputLabel.Visible = type != NvTaskType.Query;
        _output.Visible = type != NvTaskType.Query;
        _formatLabel.Visible = type == NvTaskType.Export;
        _format.Visible = type == NvTaskType.Export;
        _interval.Enabled = _scheduleType.SelectedIndex == 0;
        _dailyTime.Enabled = _scheduleType.SelectedIndex == 1;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            NvPrompt.Error(this, "计划任务", "请填写任务名。");
            return;
        }
        if (_profile.SelectedIndex < 0 || _profile.SelectedIndex >= _store.Connections.Count)
        {
            NvPrompt.Error(this, "计划任务", "请选择数据库连接。");
            return;
        }
        var type = (NvTaskType)Math.Max(0, _type.SelectedIndex);
        if (type is NvTaskType.Query or NvTaskType.Export && string.IsNullOrWhiteSpace(_sql.Text))
        {
            NvPrompt.Error(this, "计划任务", "请填写 SQL 语句。");
            return;
        }
        _task.Name = _name.Text.Trim();
        _task.Type = type;
        _task.ProfileId = _store.Connections[_profile.SelectedIndex].Id;
        _task.Database = _database.Text.Trim();
        _task.Sql = _sql.Text;
        _task.OutputPath = _output.Text.Trim();
        _task.ExportFormat = _format.SelectedItem?.ToString() ?? "CSV";
        _task.ScheduleType = _scheduleType.SelectedIndex == 1 ? "Daily" : "Interval";
        _task.IntervalMinutes = (int)_interval.Value;
        _task.DailyTime = _dailyTime.Text;
        _task.Enabled = _enabled.Checked;
        _task.NextRun = _task.Enabled ? NvTaskRunner.ComputeNextRun(_task) : null;
        DialogResult = DialogResult.OK;
        Close();
    }
}
