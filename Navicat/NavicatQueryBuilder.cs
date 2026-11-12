using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>可视化查询构建器：选择表、字段、条件和排序，生成 SELECT 语句。</summary>
internal sealed class NvQueryBuilderForm : Form
{
    private sealed class BuilderTable
    {
        public TableInfo Table { get; init; } = new();
        public List<ColumnInfo> Columns { get; set; } = new();
        public List<ForeignKeyInfo> ForeignKeys { get; set; } = new();
        public string Alias { get; set; } = "";
    }

    private readonly DbSession _session;
    private readonly ListBox _tableList;
    private readonly CheckedListBox _columnList;
    private readonly DataGridView _selectedGrid;
    private readonly DataGridView _whereGrid;
    private readonly DataGridView _orderGrid;
    private readonly RichTextBox _sqlBox;
    private readonly Label _status;
    private readonly Dictionary<string, BuilderTable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private List<TableInfo> _tableInfos = new();
    private BuilderTable? _currentTable;

    public string GeneratedSql { get; private set; } = "";

    public NvQueryBuilderForm(DbSession session)
    {
        _session = session;
        Text = "查询构建器 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 720);
        MinimumSize = new Size(900, 620);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("查询构建器", "勾选字段、条件与排序，自动生成 SELECT 语句", NvIcon.Chart);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };

        var tablePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 6, 6) };
        tablePanel.Controls.Add(new Label
        {
            Text = "数据表",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _tableList = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Ui(9f),
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false
        };
        _tableList.SelectedIndexChanged += async (_, _) => await LoadColumnsAsync();
        tablePanel.Controls.Add(_tableList);
        tablePanel.Controls.SetChildIndex(_tableList, 0);

        var columnPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 6, 6) };
        columnPanel.Controls.Add(new Label
        {
            Text = "字段（勾选加入查询）",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _columnList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = NvTheme.Ui(9f),
            BorderStyle = BorderStyle.FixedSingle
        };
        _columnList.ItemCheck += (_, e) => BeginInvoke(new Action(() => OnColumnChecked(e.Index, e.NewValue)));
        columnPanel.Controls.Add(_columnList);
        columnPanel.Controls.SetChildIndex(_columnList, 0);

        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = NvTheme.Panel };
        tablePanel.Dock = DockStyle.Top;
        tablePanel.Height = 250;
        leftPanel.Controls.Add(columnPanel);
        leftPanel.Controls.Add(tablePanel);
        split.Panel1.Controls.Add(leftPanel);

        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = NvTheme.Ui(9f), Padding = new Point(14, 4) };
        var fieldPage = new TabPage("已选字段") { BackColor = NvTheme.Panel };
        _selectedGrid = CreateGrid();
        _selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "表", Width = 150, ReadOnly = true });
        _selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "字段", Width = 180, ReadOnly = true });
        _selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Alias", HeaderText = "别名", Width = 140 });
        var agg = new DataGridViewComboBoxColumn
        {
            Name = "Aggregate",
            HeaderText = "聚合",
            Width = 100,
            FlatStyle = FlatStyle.Flat
        };
        agg.Items.AddRange(new object[] { "无", "COUNT", "SUM", "AVG", "MIN", "MAX" });
        _selectedGrid.Columns.Add(agg);
        _selectedGrid.CellValueChanged += (_, _) => BuildSql();
        _selectedGrid.RowsRemoved += (_, _) => BuildSql();
        _selectedGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_selectedGrid.IsCurrentCellDirty)
            {
                _selectedGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        var fieldToolbar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = NvTheme.HeaderBack };
        var removeField = NvTheme.FlatButton("移除字段", null, null, 88);
        removeField.Location = new Point(4, 4);
        removeField.Height = 24;
        removeField.Click += (_, _) =>
        {
            if (_selectedGrid.CurrentRow != null)
            {
                _selectedGrid.Rows.Remove(_selectedGrid.CurrentRow);
            }
        };
        fieldToolbar.Controls.Add(removeField);
        fieldPage.Controls.Add(_selectedGrid);
        fieldPage.Controls.Add(fieldToolbar);

        var wherePage = new TabPage("条件") { BackColor = NvTheme.Panel };
        _whereGrid = CreateGrid();
        _whereGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "表/别名", Width = 120 });
        _whereGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "字段", Width = 150 });
        var op = new DataGridViewComboBoxColumn
        {
            Name = "Operator",
            HeaderText = "运算符",
            Width = 100,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        op.Items.AddRange(new object[] { "=", "<>", ">", ">=", "<", "<=", "LIKE", "NOT LIKE", "IN", "IS NULL", "IS NOT NULL" });
        _whereGrid.Columns.Add(op);
        _whereGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "值", Width = 180 });
        var logic = new DataGridViewComboBoxColumn
        {
            Name = "Logic",
            HeaderText = "连接",
            Width = 80,
            FlatStyle = FlatStyle.Flat
        };
        logic.Items.AddRange(new object[] { "AND", "OR" });
        _whereGrid.Columns.Add(logic);
        _whereGrid.CellValueChanged += (_, _) => BuildSql();
        _whereGrid.RowsAdded += (_, e) =>
        {
            _whereGrid.Rows[e.RowIndex].Cells["Operator"].Value ??= "=";
            _whereGrid.Rows[e.RowIndex].Cells["Logic"].Value ??= "AND";
        };
        var whereToolbar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = NvTheme.HeaderBack };
        var addWhere = NvTheme.FlatButton("添加条件", null, null, 88);
        addWhere.Location = new Point(4, 4);
        addWhere.Height = 24;
        addWhere.Click += (_, _) => _whereGrid.Rows.Add();
        whereToolbar.Controls.Add(addWhere);
        wherePage.Controls.Add(_whereGrid);
        wherePage.Controls.Add(whereToolbar);

        var orderPage = new TabPage("排序") { BackColor = NvTheme.Panel };
        _orderGrid = CreateGrid();
        _orderGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "字段 / 别名", Width = 240 });
        var dir = new DataGridViewComboBoxColumn
        {
            Name = "Direction",
            HeaderText = "方向",
            Width = 110,
            FlatStyle = FlatStyle.Flat
        };
        dir.Items.AddRange(new object[] { "ASC", "DESC" });
        _orderGrid.Columns.Add(dir);
        _orderGrid.CellValueChanged += (_, _) => BuildSql();
        _orderGrid.RowsAdded += (_, e) => _orderGrid.Rows[e.RowIndex].Cells["Direction"].Value ??= "ASC";
        var orderToolbar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = NvTheme.HeaderBack };
        var addOrder = NvTheme.FlatButton("添加排序", null, null, 88);
        addOrder.Location = new Point(4, 4);
        addOrder.Height = 24;
        addOrder.Click += (_, _) => _orderGrid.Rows.Add();
        orderToolbar.Controls.Add(addOrder);
        orderPage.Controls.Add(_orderGrid);
        orderPage.Controls.Add(orderToolbar);

        var sqlPage = new TabPage("SQL") { BackColor = NvTheme.Panel };
        _sqlBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Mono(10f),
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(253, 253, 253),
            ReadOnly = true,
            WordWrap = false
        };
        sqlPage.Controls.Add(_sqlBox);

        tabs.TabPages.Add(fieldPage);
        tabs.TabPages.Add(wherePage);
        tabs.TabPages.Add(orderPage);
        tabs.TabPages.Add(sqlPage);
        rightSplit.Panel1.Controls.Add(tabs);

        var preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 6, 6) };
        preview.Controls.Add(new Label
        {
            Text = "SQL 预览",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        var previewBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Mono(10f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ReadOnly = true,
            WordWrap = false
        };
        preview.Controls.Add(previewBox);
        preview.Controls.SetChildIndex(previewBox, 0);
        previewBox.TextChanged += (_, _) =>
        {
            GeneratedSql = previewBox.Text;
            _sqlBox.Text = previewBox.Text;
        };
        _previewBox = previewBox;
        rightSplit.Panel2.Controls.Add(preview);
        split.Panel2.Controls.Add(rightSplit);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = NvTheme.Background };
        _status = new Label
        {
            Text = "请选择数据表和字段。",
            Location = new Point(16, 18),
            Size = new Size(560, 22),
            ForeColor = NvTheme.Muted
        };
        var open = NvTheme.FlatButton("在查询窗口打开", NvTheme.Accent, Color.White, 130);
        open.Location = new Point(780, 12);
        open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        open.Click += (_, _) =>
        {
            BuildSql();
            if (string.IsNullOrWhiteSpace(GeneratedSql))
            {
                NvPrompt.Error(this, "查询构建器", "请至少选择一个字段。");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = NvTheme.FlatButton("取消", null, null, 88);
        cancel.Location = new Point(918, 12);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cancel.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _status, open, cancel });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(header);
        CancelButton = cancel;
        Load += async (_, _) =>
        {
            try
            {
                split.SplitterDistance = 260;
            }
            catch
            {
                // ignore
            }
            await LoadTablesAsync();
        };
    }

    private RichTextBox _previewBox;

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        NvTheme.StyleGrid(grid, false);
        return grid;
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            _tableInfos = await Task.Run(() => _session.GetTables(_session.CurrentDatabase));
            _tableList.Items.Clear();
            foreach (var table in _tableInfos)
            {
                _tableList.Items.Add(table.DisplayName);
            }
            if (_tableList.Items.Count > 0)
            {
                _tableList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            _status.Text = "读取表失败：" + ex.Message;
        }
    }

    private async Task LoadColumnsAsync()
    {
        if (_tableList.SelectedIndex < 0 || _tableList.SelectedIndex >= _tableInfos.Count)
        {
            return;
        }
        var table = _tableInfos[_tableList.SelectedIndex];
        if (!_tables.TryGetValue(table.DisplayName, out var builder))
        {
            builder = new BuilderTable { Table = table };
            _tables[table.DisplayName] = builder;
        }
        _currentTable = builder;
        _columnList.Items.Clear();
        try
        {
            if (builder.Columns.Count == 0)
            {
                builder.Columns = await Task.Run(() => _session.GetColumns(table));
                builder.ForeignKeys = await Task.Run(() => _session.GetForeignKeys(table));
            }
            foreach (var column in builder.Columns)
            {
                _columnList.Items.Add(column.Name + "    " + column.DisplayType, false);
            }
        }
        catch (Exception ex)
        {
            _status.Text = "读取字段失败：" + ex.Message;
        }
    }

    private void OnColumnChecked(int index, CheckState state)
    {
        if (_currentTable == null || index < 0 || index >= _currentTable.Columns.Count)
        {
            return;
        }
        string name = _currentTable.Columns[index].Name;
        if (state == CheckState.Checked)
        {
            var existing = _selectedGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r =>
                Convert.ToString(r.Cells["Table"].Value) == _currentTable.Table.DisplayName
                && Convert.ToString(r.Cells["Column"].Value) == name);
            if (existing != null)
            {
                return;
            }
            int rowIndex = _selectedGrid.Rows.Add(_currentTable.Table.DisplayName, name, "", "无");
            _selectedGrid.Rows[rowIndex].Tag = _currentTable;
        }
        else
        {
            for (int i = _selectedGrid.Rows.Count - 1; i >= 0; i--)
            {
                var row = _selectedGrid.Rows[i];
                if (Convert.ToString(row.Cells["Table"].Value) == _currentTable.Table.DisplayName
                    && Convert.ToString(row.Cells["Column"].Value) == name)
                {
                    _selectedGrid.Rows.RemoveAt(i);
                }
            }
        }
        BuildSql();
    }

    private void BuildSql()
    {
        var selectedRows = _selectedGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow && r.Cells["Column"].Value != null)
            .ToList();
        if (selectedRows.Count == 0)
        {
            SetPreview("");
            _status.Text = "请至少选择一个字段。";
            return;
        }
        var involved = selectedRows.Select(r => Convert.ToString(r.Cells["Table"].Value) ?? "")
            .Where(x => _tables.ContainsKey(x))
            .Distinct()
            .ToList();
        int aliasIndex = 1;
        foreach (string key in involved)
        {
            _tables[key].Alias = "t" + aliasIndex++;
        }
        var select = new List<string>();
        foreach (var row in selectedRows)
        {
            string key = Convert.ToString(row.Cells["Table"].Value) ?? "";
            if (!_tables.TryGetValue(key, out var table))
            {
                continue;
            }
            string column = Convert.ToString(row.Cells["Column"].Value) ?? "";
            string alias = Convert.ToString(row.Cells["Alias"].Value) ?? "";
            string aggregate = Convert.ToString(row.Cells["Aggregate"].Value) ?? "无";
            string expr = table.Alias + "." + _session.Quote(column);
            if (aggregate != "无" && aggregate.Length > 0)
            {
                expr = aggregate + "(" + expr + ")";
            }
            if (!string.IsNullOrWhiteSpace(alias))
            {
                expr += " AS " + _session.Quote(alias);
            }
            select.Add(expr);
        }
        var sb = new StringBuilder();
        sb.AppendLine("SELECT");
        sb.AppendLine("  " + string.Join("," + Environment.NewLine + "  ", select));
        string firstKey = involved[0];
        var first = _tables[firstKey];
        sb.AppendLine("FROM " + _session.Qualify(first.Table) + " " + first.Alias);
        for (int i = 1; i < involved.Count; i++)
        {
            var table = _tables[involved[i]];
            var joinInfo = FindJoin(table, involved.Take(i).Select(k => _tables[k]).ToList());
            if (joinInfo.Other != null)
            {
                sb.AppendLine("INNER JOIN " + _session.Qualify(table.Table) + " " + table.Alias
                    + " ON " + table.Alias + "." + _session.Quote(joinInfo.CurrentColumn)
                    + " = " + joinInfo.Other.Alias + "." + _session.Quote(joinInfo.OtherColumn));
            }
            else
            {
                sb.AppendLine("CROSS JOIN " + _session.Qualify(table.Table) + " " + table.Alias);
            }
        }
        var conditions = new List<string>();
        foreach (DataGridViewRow row in _whereGrid.Rows)
        {
            string tableKey = Convert.ToString(row.Cells["Table"].Value) ?? "";
            string column = Convert.ToString(row.Cells["Column"].Value) ?? "";
            string op = Convert.ToString(row.Cells["Operator"].Value) ?? "=";
            string value = Convert.ToString(row.Cells["Value"].Value) ?? "";
            string logic = Convert.ToString(row.Cells["Logic"].Value) ?? "AND";
            if (string.IsNullOrWhiteSpace(column))
            {
                continue;
            }
            string expr;
            if (_tables.TryGetValue(tableKey, out var table) && !string.IsNullOrWhiteSpace(table.Alias))
            {
                expr = table.Alias + "." + _session.Quote(column);
            }
            else if (involved.Count == 1)
            {
                expr = _tables[involved[0]].Alias + "." + _session.Quote(column);
            }
            else
            {
                continue;
            }
            string condition;
            if (op is "IS NULL" or "IS NOT NULL")
            {
                condition = expr + " " + op;
            }
            else
            {
                condition = expr + " " + op + " " + FormatValue(value);
            }
            conditions.Add((conditions.Count == 0 ? "" : logic + " ") + condition);
        }
        if (conditions.Count > 0)
        {
            sb.AppendLine("WHERE " + string.Join(Environment.NewLine + "  ", conditions));
        }
        var orders = new List<string>();
        foreach (DataGridViewRow row in _orderGrid.Rows)
        {
            string column = Convert.ToString(row.Cells["Column"].Value) ?? "";
            string dir = Convert.ToString(row.Cells["Direction"].Value) ?? "ASC";
            if (!string.IsNullOrWhiteSpace(column))
            {
                orders.Add(column + " " + dir);
            }
        }
        if (orders.Count > 0)
        {
            sb.AppendLine("ORDER BY " + string.Join(", ", orders));
        }
        if (involved.Count == 1 && _tables.TryGetValue(involved[0], out var single)
            && _session.Kind is DbKind.MySQL or DbKind.PostgreSQL or DbKind.SQLite)
        {
            sb.AppendLine("LIMIT 1000");
        }
        string sql = sb.ToString().TrimEnd();
        SetPreview(sql);
        _status.Text = "已生成 SQL · " + involved.Count + " 张表 · " + selectedRows.Count + " 个字段";
    }

    private void SetPreview(string sql)
    {
        GeneratedSql = sql;
        _previewBox.Text = sql;
        _sqlBox.Text = sql;
    }

    private static string FormatValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "''";
        }
        if (decimal.TryParse(value, out _) || value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }
        if (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))
        {
            return value;
        }
        return "'" + value.Replace("'", "''") + "'";
    }

    private readonly record struct JoinInfo(BuilderTable? Other, string CurrentColumn, string OtherColumn);

    private static JoinInfo FindJoin(BuilderTable table, List<BuilderTable> previous)
    {
        foreach (var fk in table.ForeignKeys)
        {
            var other = previous.FirstOrDefault(t => fk.RefTable.EndsWith(t.Table.Name, StringComparison.OrdinalIgnoreCase)
                || fk.RefTable.Equals(t.Table.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (other != null)
            {
                return new JoinInfo(other, fk.Columns.FirstOrDefault() ?? "",
                    fk.RefColumns.FirstOrDefault() ?? "");
            }
        }
        foreach (var other in previous)
        {
            foreach (var fk in other.ForeignKeys)
            {
                if (fk.RefTable.EndsWith(table.Table.Name, StringComparison.OrdinalIgnoreCase)
                    || fk.RefTable.Equals(table.Table.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    return new JoinInfo(other, fk.RefColumns.FirstOrDefault() ?? "",
                        fk.Columns.FirstOrDefault() ?? "");
                }
            }
        }
        return new JoinInfo(null, "", "");
    }
}
