using System.Data;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

internal static class SyncUi
{
    public static Panel Header(string title, string subtitle, NvIcon icon)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = NvTheme.HeaderBack };
        header.Controls.Add(new Label
        {
            Text = title,
            Location = new Point(20, 8),
            AutoSize = true,
            Font = NvTheme.Ui(13f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Location = new Point(22, 36),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        var iconBox = new PictureBox
        {
            Image = NvIcons.Get(icon, 30),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(0, 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        header.Controls.Add(iconBox);
        header.Resize += (_, _) => iconBox.Location = new Point(Math.Max(8, header.Width - 56), 14);
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        return header;
    }

    public static RichTextBox LogBox()
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = NvTheme.Mono(9f),
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            WordWrap = false
        };
    }

    public static void Append(RichTextBox box, string text)
    {
        if (box.IsDisposed)
        {
            return;
        }
        if (box.InvokeRequired)
        {
            try
            {
                box.BeginInvoke(new Action(() => Append(box, text)));
            }
            catch
            {
                // ignore
            }
            return;
        }
        box.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
    }
}

/// <summary>数据传输：跨连接复制表结构与数据。</summary>
internal sealed class NvDataTransferForm : Form
{
    private readonly DbSession _source;
    private readonly CheckedListBox _tableList;
    private readonly NvEndpointSelector _target;
    private readonly RichTextBox _log;
    private readonly CheckBox _createTables;
    private readonly CheckBox _dropTables;
    private readonly CheckBox _truncate;
    private readonly CheckBox _copyStructure;
    private readonly CheckBox _copyData;
    private readonly Button _start;
    private readonly ProgressBar _progress;
    private List<TableInfo> _tables = new();

    public NvDataTransferForm(DbSession source, ConnectionStore store)
    {
        _source = source;
        Text = "数据传输 - " + source.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(820, 600);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("数据传输", "把源数据库的表结构和数据复制到另一个连接/数据库",
            NvIcon.Transfer);

        var targetPanel = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(14, 4, 14, 0) };
        _target = new NvEndpointSelector("目标连接", store) { Location = new Point(0, 4), Width = 430 };
        targetPanel.Controls.Add(_target);
        targetPanel.Controls.Add(new Label
        {
            Text = "源：" + source.Profile.Name + " · " + source.Kind.ShortName() + " · " + source.CurrentDatabase,
            Location = new Point(460, 26),
            AutoSize = true,
            ForeColor = NvTheme.Text,
            Font = NvTheme.Ui(9.2f, FontStyle.Bold)
        });
        targetPanel.Controls.Add(new Label
        {
            Text = "跨库传输会按目标数据库类型自动映射字段类型；外键与自增列会在目标端重建。",
            Location = new Point(460, 56),
            Size = new Size(440, 40),
            ForeColor = NvTheme.Muted
        });

        var options = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = NvTheme.Panel };
        _copyStructure = new CheckBox
        {
            Text = "复制表结构",
            Location = new Point(16, 10),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _copyData = new CheckBox
        {
            Text = "复制表数据",
            Location = new Point(126, 10),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _createTables = new CheckBox
        {
            Text = "目标表不存在时创建",
            Location = new Point(236, 10),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _dropTables = new CheckBox
        {
            Text = "先删除目标表",
            Location = new Point(386, 10),
            AutoSize = true,
            ForeColor = NvTheme.Text
        };
        _truncate = new CheckBox
        {
            Text = "先清空目标表",
            Location = new Point(506, 10),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        options.Controls.AddRange(new Control[] { _copyStructure, _copyData, _createTables, _dropTables, _truncate });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var tablePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 6, 6) };
        tablePanel.Controls.Add(new Label
        {
            Text = "选择要传输的表",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _tableList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = NvTheme.Ui(9f),
            BorderStyle = BorderStyle.FixedSingle
        };
        tablePanel.Controls.Add(_tableList);
        tablePanel.Controls.SetChildIndex(_tableList, 0);
        split.Panel1.Controls.Add(tablePanel);

        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 12, 6) };
        logPanel.Controls.Add(new Label
        {
            Text = "执行日志",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _log = SyncUi.LogBox();
        var logHost = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        logHost.Controls.Add(_log);
        logPanel.Controls.Add(logHost);
        logPanel.Controls.SetChildIndex(logHost, 0);
        split.Panel2.Controls.Add(logPanel);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = NvTheme.Background };
        _progress = new ProgressBar
        {
            Location = new Point(16, 16),
            Width = 420,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false
        };
        _start = NvTheme.FlatButton("开始传输", NvTheme.Accent, Color.White, 110);
        _start.Location = new Point(700, 12);
        _start.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _start.Click += async (_, _) => await StartAsync();
        var close = NvTheme.FlatButton("关闭", null, null, 88);
        close.Location = new Point(818, 12);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _progress, _start, close });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(options);
        Controls.Add(targetPanel);
        Controls.Add(header);
        CancelButton = close;
        Load += async (_, _) => await LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            _tables = await Task.Run(() => _source.GetTables(_source.CurrentDatabase));
            _tableList.Items.Clear();
            foreach (var table in _tables)
            {
                _tableList.Items.Add(table.DisplayName, true);
            }
        }
        catch (Exception ex)
        {
            SyncUi.Append(_log, "读取源表失败：" + ex.Message);
        }
    }

    private async Task StartAsync()
    {
        var target = _target.SelectedSession;
        if (target == null || !_target.IsReady)
        {
            NvPrompt.Error(this, "数据传输", "请先选择并连接目标数据库。");
            return;
        }
        var selected = _tableList.CheckedItems.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        if (selected.Count == 0)
        {
            NvPrompt.Error(this, "数据传输", "请至少选择一张表。");
            return;
        }
        if (!_copyStructure.Checked && !_copyData.Checked)
        {
            NvPrompt.Error(this, "数据传输", "请至少勾选“复制表结构”或“复制表数据”。");
            return;
        }
        _start.Enabled = false;
        _progress.Visible = true;
        _log.Clear();
        SyncUi.Append(_log, "开始传输：" + _source.Profile.Name + " → " + target.Profile.Name);
        try
        {
            int total = await Task.Run(() => Transfer(target, selected));
            SyncUi.Append(_log, "传输完成，共 " + total + " 行。");
            NvPrompt.Info(this, "数据传输", "传输完成，共复制 " + total + " 行。");
        }
        catch (Exception ex)
        {
            SyncUi.Append(_log, "传输失败：" + ex.Message);
            NvLog.Error(ex, "工具异常：数据传输失败");
            NvPrompt.Error(this, "数据传输失败", ex.Message);
        }
        finally
        {
            _start.Enabled = true;
            _progress.Visible = false;
        }
    }

    private int Transfer(DbSession target, List<string> selected)
    {
        string targetDb = _target.SelectedDatabase ?? target.CurrentDatabase;
        int totalRows = 0;
        foreach (string name in selected)
        {
            var sourceTable = _tables.FirstOrDefault(t =>
                t.DisplayName == name || t.Name == name);
            if (sourceTable == null)
            {
                continue;
            }
            SyncUi.Append(_log, "处理表：" + name);
            var targetTable = NvSyncUtil.TargetTable(target, sourceTable);
            var targetTables = target.GetTables(targetDb);
            var existing = targetTables.FirstOrDefault(t =>
                t.Name.Equals(targetTable.Name, StringComparison.OrdinalIgnoreCase)
                && (target.Kind is DbKind.MySQL or DbKind.Oracle
                    || string.Equals(t.Schema, targetTable.Schema, StringComparison.OrdinalIgnoreCase)));
            if (_dropTables.Checked && existing != null)
            {
                target.Execute("DROP TABLE " + target.Qualify(existing), 600);
                existing = null;
                SyncUi.Append(_log, "  已删除目标表 " + name);
            }
            if (_copyStructure.Checked)
            {
                if (existing == null && _createTables.Checked)
                {
                    string ddl = NvSyncUtil.CreateTableOnTarget(_source, target, sourceTable, out targetTable);
                    target.ExecuteScript(ddl, 600);
                    SyncUi.Append(_log, "  已创建目标表 " + name);
                    existing = target.GetTables(targetDb).FirstOrDefault(t =>
                        t.Name.Equals(targetTable.Name, StringComparison.OrdinalIgnoreCase));
                }
                else if (existing != null)
                {
                    targetTable = existing;
                }
            }
            if (!_copyData.Checked)
            {
                continue;
            }
            if (existing == null && _createTables.Checked)
            {
                // 仅复制数据时也确保目标表存在
                string ddl = NvSyncUtil.CreateTableOnTarget(_source, target, sourceTable, out targetTable);
                target.ExecuteScript(ddl, 600);
                existing = target.GetTables(targetDb).FirstOrDefault(t =>
                    t.Name.Equals(targetTable.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    targetTable = existing;
                }
            }
            if (_truncate.Checked && existing != null)
            {
                string sql = target.Kind == DbKind.SQLite
                    ? "DELETE FROM " + target.Qualify(targetTable)
                    : "TRUNCATE TABLE " + target.Qualify(targetTable);
                try
                {
                    target.Execute(sql, 600);
                }
                catch
                {
                    target.Execute("DELETE FROM " + target.Qualify(targetTable), 600);
                }
            }

            var sourceColumns = _source.GetColumns(sourceTable);
            var targetColumns = target.GetColumns(targetTable);
            var targetMap = new List<(int SourceIndex, ColumnInfo Column)>();
            foreach (var column in targetColumns)
            {
                int index = sourceColumns.FindIndex(c =>
                    c.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    targetMap.Add((index, column));
                }
            }
            if (targetMap.Count == 0)
            {
                SyncUi.Append(_log, "  跳过：源表与目标表没有同名可映射列。");
                continue;
            }
            var insertColumns = targetMap;
            if (insertColumns.Count == 0)
            {
                continue;
            }
            bool identityInsert = target.Kind == DbKind.SqlServer
                && insertColumns.Any(m => m.Column.AutoIncrement);
            if (identityInsert)
            {
                target.Execute("SET IDENTITY_INSERT " + NvSyncUtil.IdentityInsertTable(target, targetTable) + " ON", 600);
            }
            string columnList = string.Join(", ", insertColumns.Select(m => target.Quote(m.Column.Name)));
            string parameterList = string.Join(", ", insertColumns.Select((_, i) => target.Param("p" + i)));
            string insertSql = "INSERT INTO " + target.Qualify(targetTable) + " (" + columnList
                + ") VALUES (" + parameterList + ")";
            bool ownTransaction = !target.HasTransaction;
            if (ownTransaction)
            {
                target.BeginTransaction();
            }
            int tableRows = 0;
            try
            {
                _source.StreamRows("SELECT * FROM " + _source.Qualify(sourceTable), reader =>
                {
                    var parameters = new Dictionary<string, object?>();
                    for (int i = 0; i < insertColumns.Count; i++)
                    {
                        var (sourceIndex, column) = insertColumns[i];
                        object value = reader.GetValue(sourceIndex);
                        parameters[target.Param("p" + i)] = NvSyncUtil.ConvertForTarget(
                            value is DBNull ? null : value, column);
                    }
                    target.ExecuteWithParams(insertSql, parameters, 600);
                    tableRows++;
                    totalRows++;
                    if (tableRows % 1000 == 0)
                    {
                        SyncUi.Append(_log, "  " + name + " 已复制 " + tableRows + " 行…");
                    }
                }, 900);
                if (ownTransaction)
                {
                    target.Commit();
                }
                SyncUi.Append(_log, "  " + name + " 完成，共 " + tableRows + " 行。");
            }
            catch
            {
                if (ownTransaction)
                {
                    try
                    {
                        target.Rollback();
                    }
                    catch
                    {
                        // ignore
                    }
                }
                throw;
            }
            finally
            {
                if (identityInsert)
                {
                    try
                    {
                        target.Execute("SET IDENTITY_INSERT " + NvSyncUtil.IdentityInsertTable(target, targetTable) + " OFF", 600);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
        return totalRows;
    }
}

/// <summary>数据同步：按主键比较并插入 / 更新 / 删除。</summary>
internal sealed class NvDataSyncForm : Form
{
    private readonly DbSession _source;
    private readonly CheckedListBox _tableList;
    private readonly NvEndpointSelector _target;
    private readonly RichTextBox _log;
    private readonly CheckBox _insert;
    private readonly CheckBox _update;
    private readonly CheckBox _delete;
    private readonly Button _syncButton;
    private readonly ProgressBar _progress;
    private List<TableInfo> _tables = new();

    public NvDataSyncForm(DbSession source, ConnectionStore store)
    {
        _source = source;
        Text = "数据同步 - " + source.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(820, 600);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("数据同步", "按主键比较两个数据库中的表数据，生成 INSERT / UPDATE / DELETE",
            NvIcon.Sync);
        var targetPanel = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(14, 4, 14, 0) };
        _target = new NvEndpointSelector("目标连接", store) { Location = new Point(0, 4), Width = 430 };
        targetPanel.Controls.Add(_target);
        targetPanel.Controls.Add(new Label
        {
            Text = "源：" + source.Profile.Name + " · " + source.Kind.ShortName() + " · " + source.CurrentDatabase,
            Location = new Point(460, 26),
            AutoSize = true,
            Font = NvTheme.Ui(9.2f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        targetPanel.Controls.Add(new Label
        {
            Text = "数据同步要求源表与目标表具有相同主键；没有主键的表会被跳过。",
            Location = new Point(460, 56),
            Size = new Size(440, 40),
            ForeColor = NvTheme.Muted
        });

        var options = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = NvTheme.Panel };
        _insert = new CheckBox { Text = "插入目标缺失的行", Location = new Point(16, 10), AutoSize = true, Checked = true, ForeColor = NvTheme.Text };
        _update = new CheckBox { Text = "更新有差异的行", Location = new Point(176, 10), AutoSize = true, Checked = true, ForeColor = NvTheme.Text };
        _delete = new CheckBox { Text = "删除目标多余的行", Location = new Point(326, 10), AutoSize = true, ForeColor = NvTheme.Text };
        options.Controls.AddRange(new Control[] { _insert, _update, _delete });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var tablePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 6, 6) };
        tablePanel.Controls.Add(new Label
        {
            Text = "选择要同步的表",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _tableList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = NvTheme.Ui(9f),
            BorderStyle = BorderStyle.FixedSingle
        };
        tablePanel.Controls.Add(_tableList);
        tablePanel.Controls.SetChildIndex(_tableList, 0);
        split.Panel1.Controls.Add(tablePanel);
        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 12, 6) };
        logPanel.Controls.Add(new Label
        {
            Text = "同步日志",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _log = SyncUi.LogBox();
        var logHost = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        logHost.Controls.Add(_log);
        logPanel.Controls.Add(logHost);
        logPanel.Controls.SetChildIndex(logHost, 0);
        split.Panel2.Controls.Add(logPanel);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = NvTheme.Background };
        _progress = new ProgressBar
        {
            Location = new Point(16, 16),
            Width = 420,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false
        };
        _syncButton = NvTheme.FlatButton("开始同步", NvTheme.Accent, Color.White, 110);
        _syncButton.Location = new Point(700, 12);
        _syncButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _syncButton.Click += async (_, _) => await SyncAsync();
        var close = NvTheme.FlatButton("关闭", null, null, 88);
        close.Location = new Point(818, 12);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _progress, _syncButton, close });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(options);
        Controls.Add(targetPanel);
        Controls.Add(header);
        CancelButton = close;
        Load += async (_, _) => await LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            _tables = await Task.Run(() => _source.GetTables(_source.CurrentDatabase));
            _tableList.Items.Clear();
            foreach (var table in _tables)
            {
                _tableList.Items.Add(table.DisplayName, true);
            }
        }
        catch (Exception ex)
        {
            SyncUi.Append(_log, "读取源表失败：" + ex.Message);
        }
    }

    private async Task SyncAsync()
    {
        var target = _target.SelectedSession;
        if (target == null || !_target.IsReady)
        {
            NvPrompt.Error(this, "数据同步", "请先选择并连接目标数据库。");
            return;
        }
        var selected = _tableList.CheckedItems.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        if (selected.Count == 0)
        {
            NvPrompt.Error(this, "数据同步", "请至少选择一张表。");
            return;
        }
        _syncButton.Enabled = false;
        _progress.Visible = true;
        _log.Clear();
        SyncUi.Append(_log, "开始同步：" + _source.Profile.Name + " → " + target.Profile.Name);
        try
        {
            var (ins, upd, del) = await Task.Run(() => Sync(target, selected));
            SyncUi.Append(_log, $"同步完成：插入 {ins} 行，更新 {upd} 行，删除 {del} 行。");
            NvPrompt.Info(this, "数据同步", $"同步完成：插入 {ins} 行，更新 {upd} 行，删除 {del} 行。");
        }
        catch (Exception ex)
        {
            SyncUi.Append(_log, "同步失败：" + ex.Message);
            NvLog.Error(ex, "工具异常：数据同步失败");
            NvPrompt.Error(this, "数据同步失败", ex.Message);
        }
        finally
        {
            _syncButton.Enabled = true;
            _progress.Visible = false;
        }
    }

    private (int Inserted, int Updated, int Deleted) Sync(DbSession target, List<string> selected)
    {
        string targetDb = _target.SelectedDatabase ?? target.CurrentDatabase;
        var targetTables = target.GetTables(targetDb);
        int inserted = 0, updated = 0, deleted = 0;
        foreach (string name in selected)
        {
            var sourceTable = _tables.FirstOrDefault(t => t.DisplayName == name || t.Name == name);
            if (sourceTable == null)
            {
                continue;
            }
            var targetTable = targetTables.FirstOrDefault(t => t.Name.Equals(sourceTable.Name, StringComparison.OrdinalIgnoreCase));
            if (targetTable == null)
            {
                SyncUi.Append(_log, "跳过 " + name + "：目标表不存在。");
                continue;
            }
            var sourcePk = _source.GetPrimaryKeyColumns(sourceTable);
            var targetPk = target.GetPrimaryKeyColumns(targetTable);
            if (sourcePk.Count == 0 || targetPk.Count == 0)
            {
                SyncUi.Append(_log, "跳过 " + name + "：没有主键。");
                continue;
            }
            var pk = sourcePk.Where(p => targetPk.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            if (pk.Count == 0)
            {
                SyncUi.Append(_log, "跳过 " + name + "：主键列不一致。");
                continue;
            }
            SyncUi.Append(_log, "同步表 " + name + "，主键：" + string.Join(", ", pk));
            var sourceRows = _source.Query("SELECT * FROM " + _source.Qualify(sourceTable), null, 900);
            var targetRows = target.Query("SELECT * FROM " + target.Qualify(targetTable), null, 900);
            var sourceColumns = _source.GetColumns(sourceTable);
            var targetColumns = target.GetColumns(targetTable);
            var targetLookup = targetRows.Rows.Cast<DataRow>()
                .ToDictionary(r => NvSyncUtil.KeyOf(r, pk), r => r);
            var sourceKeys = new HashSet<string>();
            bool identityInsert = target.Kind == DbKind.SqlServer
                && targetColumns.Any(c => c.AutoIncrement);
            if (identityInsert)
            {
                target.Execute("SET IDENTITY_INSERT " + NvSyncUtil.IdentityInsertTable(target, targetTable) + " ON", 600);
            }
            bool ownTransaction = !target.HasTransaction;
            if (ownTransaction)
            {
                target.BeginTransaction();
            }
            try
            {
                foreach (DataRow sourceRow in sourceRows.Rows)
                {
                    string key = NvSyncUtil.KeyOf(sourceRow, pk);
                    sourceKeys.Add(key);
                    if (!targetLookup.TryGetValue(key, out var targetRow))
                    {
                        if (_insert.Checked)
                        {
                            InsertRow(target, targetTable, sourceRow, targetColumns);
                            inserted++;
                        }
                        continue;
                    }
                    var changes = new List<(ColumnInfo Column, object? Value)>();
                    foreach (var column in targetColumns)
                    {
                        if (pk.Contains(column.Name, StringComparer.OrdinalIgnoreCase)
                            || column.AutoIncrement)
                        {
                            continue;
                        }
                        if (!sourceRows.Columns.Contains(column.Name))
                        {
                            continue;
                        }
                        object? sourceValue = sourceRow[column.Name];
                        object? targetValue = targetRow[column.Name];
                        if (!NvSyncUtil.ValuesEqual(sourceValue, targetValue))
                        {
                            changes.Add((column, NvSyncUtil.ConvertForTarget(
                                sourceValue is DBNull ? null : sourceValue, column)));
                        }
                    }
                    if (changes.Count > 0 && _update.Checked)
                    {
                        UpdateRow(target, targetTable, targetRow, pk, changes);
                        updated++;
                    }
                }
                if (_delete.Checked)
                {
                    foreach (var pair in targetLookup)
                    {
                        if (sourceKeys.Contains(pair.Key))
                        {
                            continue;
                        }
                        DeleteRow(target, targetTable, pair.Value, pk);
                        deleted++;
                    }
                }
                if (ownTransaction)
                {
                    target.Commit();
                }
            }
            catch
            {
                if (ownTransaction)
                {
                    try
                    {
                        target.Rollback();
                    }
                    catch
                    {
                        // ignore
                    }
                }
                throw;
            }
            finally
            {
                if (identityInsert)
                {
                    try
                    {
                        target.Execute("SET IDENTITY_INSERT " + NvSyncUtil.IdentityInsertTable(target, targetTable) + " OFF", 600);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
        return (inserted, updated, deleted);
    }

    private static void InsertRow(DbSession target, TableInfo table, DataRow sourceRow,
        List<ColumnInfo> targetColumns)
    {
        var columns = new List<string>();
        var parameters = new Dictionary<string, object?>();
        int index = 0;
        foreach (var column in targetColumns)
        {
            if (!sourceRow.Table.Columns.Contains(column.Name))
            {
                continue;
            }
            columns.Add(target.Quote(column.Name));
            string name = target.Param("p" + index++);
            parameters[name] = NvSyncUtil.ConvertForTarget(
                sourceRow[column.Name] is DBNull ? null : sourceRow[column.Name], column);
        }
        if (columns.Count == 0)
        {
            return;
        }
        string sql = "INSERT INTO " + target.Qualify(table) + " (" + string.Join(", ", columns)
            + ") VALUES (" + string.Join(", ", parameters.Keys) + ")";
        target.ExecuteWithParams(sql, parameters, 600);
    }

    private static void UpdateRow(DbSession target, TableInfo table, DataRow targetRow,
        List<string> pk, List<(ColumnInfo Column, object? Value)> changes)
    {
        var sets = new List<string>();
        var where = new List<string>();
        var parameters = new Dictionary<string, object?>();
        int index = 0;
        foreach (var (column, value) in changes)
        {
            string name = target.Param("p" + index++);
            sets.Add(target.Quote(column.Name) + " = " + name);
            parameters[name] = value;
        }
        foreach (string key in pk)
        {
            string name = target.Param("k" + index++);
            where.Add(target.Quote(key) + " = " + name);
            parameters[name] = targetRow[key];
        }
        string sql = "UPDATE " + target.Qualify(table) + " SET " + string.Join(", ", sets)
            + " WHERE " + string.Join(" AND ", where);
        target.ExecuteWithParams(sql, parameters, 600);
    }

    private static void DeleteRow(DbSession target, TableInfo table, DataRow targetRow, List<string> pk)
    {
        var where = new List<string>();
        var parameters = new Dictionary<string, object?>();
        int index = 0;
        foreach (string key in pk)
        {
            string name = target.Param("k" + index++);
            where.Add(target.Quote(key) + " = " + name);
            parameters[name] = targetRow[key];
        }
        target.ExecuteWithParams("DELETE FROM " + target.Qualify(table) + " WHERE " + string.Join(" AND ", where),
            parameters, 600);
    }
}

/// <summary>结构同步：比较表 / 列并生成 ALTER 脚本。</summary>
internal sealed class NvStructureSyncForm : Form
{
    private readonly DbSession _source;
    private readonly CheckedListBox _tableList;
    private readonly NvEndpointSelector _target;
    private readonly RichTextBox _sqlBox;
    private readonly Label _summary;
    private readonly Button _compareButton;
    private readonly Button _executeButton;
    private List<TableInfo> _tables = new();
    private string _generatedSql = "";

    public NvStructureSyncForm(DbSession source, ConnectionStore store)
    {
        _source = source;
        Text = "结构同步 - " + source.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(860, 620);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("结构同步", "比较两个数据库的表结构，生成并执行 CREATE / ALTER 脚本",
            NvIcon.Structure);
        var targetPanel = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(14, 4, 14, 0) };
        _target = new NvEndpointSelector("目标连接", store) { Location = new Point(0, 4), Width = 430 };
        targetPanel.Controls.Add(_target);
        targetPanel.Controls.Add(new Label
        {
            Text = "源：" + source.Profile.Name + " · " + source.Kind.ShortName() + " · " + source.CurrentDatabase,
            Location = new Point(460, 26),
            AutoSize = true,
            Font = NvTheme.Ui(9.2f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        targetPanel.Controls.Add(new Label
        {
            Text = "默认只生成“新增表 / 新增列 / 修改列类型”，不会删除目标端多余对象。",
            Location = new Point(460, 56),
            Size = new Size(480, 40),
            ForeColor = NvTheme.Muted
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var tablePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 6, 6) };
        tablePanel.Controls.Add(new Label
        {
            Text = "选择要比较的表",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _tableList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = NvTheme.Ui(9f),
            BorderStyle = BorderStyle.FixedSingle
        };
        tablePanel.Controls.Add(_tableList);
        tablePanel.Controls.SetChildIndex(_tableList, 0);
        split.Panel1.Controls.Add(tablePanel);

        var sqlPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 12, 6) };
        sqlPanel.Controls.Add(new Label
        {
            Text = "同步脚本",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _sqlBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Mono(9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = NvTheme.Text,
            WordWrap = false,
            ReadOnly = true
        };
        sqlPanel.Controls.Add(_sqlBox);
        sqlPanel.Controls.SetChildIndex(_sqlBox, 0);
        split.Panel2.Controls.Add(sqlPanel);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = NvTheme.Background };
        _summary = new Label
        {
            Text = "请选择目标连接后点击“比较结构”。",
            Location = new Point(16, 18),
            Size = new Size(500, 22),
            ForeColor = NvTheme.Muted
        };
        _compareButton = NvTheme.FlatButton("比较结构", null, null, 100);
        _compareButton.Location = new Point(650, 12);
        _compareButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _compareButton.Click += async (_, _) => await CompareAsync();
        _executeButton = NvTheme.FlatButton("执行同步", NvTheme.Accent, Color.White, 100);
        _executeButton.Location = new Point(758, 12);
        _executeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _executeButton.Enabled = false;
        _executeButton.Click += async (_, _) => await ExecuteAsync();
        var close = NvTheme.FlatButton("关闭", null, null, 88);
        close.Location = new Point(866, 12);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _summary, _compareButton, _executeButton, close });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(targetPanel);
        Controls.Add(header);
        CancelButton = close;
        Load += async (_, _) => await LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            _tables = await Task.Run(() => _source.GetTables(_source.CurrentDatabase));
            _tableList.Items.Clear();
            foreach (var table in _tables)
            {
                _tableList.Items.Add(table.DisplayName, true);
            }
        }
        catch (Exception ex)
        {
            _summary.Text = "读取源表失败：" + ex.Message;
        }
    }

    private async Task CompareAsync()
    {
        var target = _target.SelectedSession;
        if (target == null || !_target.IsReady)
        {
            NvPrompt.Error(this, "结构同步", "请先选择并连接目标数据库。");
            return;
        }
        var selected = _tableList.CheckedItems.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        if (selected.Count == 0)
        {
            NvPrompt.Error(this, "结构同步", "请至少选择一张表。");
            return;
        }
        _compareButton.Enabled = false;
        _summary.Text = "正在比较结构…";
        try
        {
            var result = await Task.Run(() => BuildDiff(target, selected));
            _generatedSql = result.Sql;
            _sqlBox.Text = result.Sql;
            _summary.Text = result.Summary;
            _executeButton.Enabled = !string.IsNullOrWhiteSpace(result.Sql);
        }
        catch (Exception ex)
        {
            _summary.Text = "比较失败：" + ex.Message;
            NvLog.Error(ex, "工具异常：结构同步");
            NvPrompt.Error(this, "结构同步", ex.Message);
        }
        finally
        {
            _compareButton.Enabled = true;
        }
    }

    private (string Sql, string Summary) BuildDiff(DbSession target, List<string> selected)
    {
        string targetDb = _target.SelectedDatabase ?? target.CurrentDatabase;
        var targetTables = target.GetTables(targetDb);
        var statements = new List<string>();
        int newTables = 0, newColumns = 0, changedColumns = 0;
        foreach (string name in selected)
        {
            var sourceTable = _tables.FirstOrDefault(t => t.DisplayName == name || t.Name == name);
            if (sourceTable == null)
            {
                continue;
            }
            var targetTable = targetTables.FirstOrDefault(t =>
                t.Name.Equals(sourceTable.Name, StringComparison.OrdinalIgnoreCase));
            if (targetTable == null)
            {
                string ddl = NvSyncUtil.CreateTableOnTarget(_source, target, sourceTable, out _);
                statements.Add("-- 新建表 " + name + Environment.NewLine + ddl.TrimEnd().TrimEnd(';') + ";");
                newTables++;
                continue;
            }
            var sourceColumns = _source.GetColumns(sourceTable);
            var targetColumns = target.GetColumns(targetTable);
            var targetMap = targetColumns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
            var sourceMap = sourceColumns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
            foreach (var sourceColumn in sourceColumns)
            {
                if (!targetMap.ContainsKey(sourceColumn.Name))
                {
                    statements.Add("-- " + name + " 新增列 " + sourceColumn.Name + Environment.NewLine
                        + NvSyncUtil.AddColumnSql(target, targetTable, sourceColumn, _source.Kind) + ";");
                    newColumns++;
                }
                else
                {
                    var targetColumn = targetMap[sourceColumn.Name];
                    string sourceType = NvSqlUtil.MapType(_source.Kind, target.Kind, sourceColumn);
                    if (NvSyncUtil.NormalizeType(sourceType) != NvSyncUtil.NormalizeType(targetColumn.FullType))
                    {
                        string alterSql = NvSyncUtil.AlterColumnTypeSql(target, targetTable, targetColumn.Name,
                            sourceType, !sourceColumn.Nullable, sourceColumn.DefaultValue, sourceColumn.Comment);
                        if (!string.IsNullOrWhiteSpace(alterSql))
                        {
                            statements.Add("-- " + name + " 修改列 " + sourceColumn.Name + "："
                                + targetColumn.FullType + " → " + sourceType + Environment.NewLine + alterSql + ";");
                            changedColumns++;
                        }
                    }
                }
            }
            foreach (var targetColumn in targetColumns)
            {
                if (!sourceMap.ContainsKey(targetColumn.Name))
                {
                    statements.Add("-- 注意：" + name + " 的目标列 " + targetColumn.Name
                        + " 在源表中不存在（未自动删除）");
                }
            }
        }
        string sql = statements.Count == 0
            ? "-- 两个数据库结构一致，无需同步。"
            : string.Join(Environment.NewLine + Environment.NewLine, statements);
        string summary = $"比较完成：新增表 {newTables}，新增列 {newColumns}，修改列 {changedColumns}。";
        return (sql, summary);
    }

    private async Task ExecuteAsync()
    {
        var target = _target.SelectedSession;
        if (target == null || string.IsNullOrWhiteSpace(_generatedSql))
        {
            return;
        }
        if (!NvPrompt.Confirm(this, "执行结构同步", "即将执行生成的 SQL 脚本，是否继续？", true))
        {
            return;
        }
        _executeButton.Enabled = false;
        _summary.Text = "正在执行同步脚本…";
        try
        {
            var statements = NvSqlUtil.SplitScript(_generatedSql);
            int executed = 0;
            await Task.Run(() =>
            {
                foreach (string statement in statements)
                {
                    if (!NvSqlUtil.HasExecutableSql(statement))
                    {
                        continue;
                    }
                    target.Execute(statement, 900);
                    executed++;
                }
            });
            _summary.Text = "同步完成，共执行 " + executed + " 条语句。";
            NvPrompt.Info(this, "结构同步", "同步完成，共执行 " + executed + " 条语句。");
        }
        catch (Exception ex)
        {
            _summary.Text = "执行失败：" + ex.Message;
            NvLog.Error(ex, "工具异常：结构同步失败");
            NvPrompt.Error(this, "结构同步失败", ex.Message);
        }
        finally
        {
            _executeButton.Enabled = true;
        }
    }
}
