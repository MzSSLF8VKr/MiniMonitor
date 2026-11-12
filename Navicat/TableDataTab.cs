using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>表数据浏览/编辑标签页：分页、筛选、排序、增删改、导入导出。</summary>
internal sealed class TableDataTab : UserControl, NvClosable
{
    private readonly DbSession _session;
    private readonly TableInfo _table;
    private readonly bool _readOnly;
    private readonly DataGridView _grid;
    private readonly ToolStrip _toolbar;
    private readonly ToolStripTextBox _filterBox;
    private readonly ToolStripComboBox _pageSizeBox;
    private readonly Label _statusLeft;
    private readonly Label _statusRight;
    private readonly Label _pageLabel;
    private readonly Button _firstButton;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly Button _lastButton;
    private readonly ToolStripButton _saveButton;
    private readonly ToolStripButton _cancelButton;
    private readonly ToolStripButton _addButton;
    private readonly ToolStripButton _deleteButton;
    private DataTable? _data;
    private List<ColumnInfo> _columns = new();
    private HashSet<string> _primaryKeys = new(StringComparer.OrdinalIgnoreCase);
    private int _page;
    private int _pageSize;
    private long _totalRows;
    private string _filter = "";
    private string? _sortColumn;
    private bool _sortAscending = true;
    private bool _busy;
    private bool _loading;

    public event EventHandler? DesignRequested;
    public event EventHandler? GenerateDataRequested;

    public TableDataTab(DbSession session, TableInfo table, bool readOnly = false)
    {
        _session = session;
        _table = table;
        _readOnly = readOnly || table.IsView;
        _pageSize = Math.Max(50, NvSettingsStore.Current.PageSize);

        BackColor = NvTheme.Background;
        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 31,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.Professional
        };
        NvTheme.ApplyToolStrip(_toolbar);

        var refresh = ToolButton("刷新", NvIcon.Refresh);
        refresh.Click += async (_, _) => await LoadPageAsync();
        _addButton = ToolButton("添加记录", NvIcon.Add);
        _addButton.Click += (_, _) => AddRow();
        _deleteButton = ToolButton("删除记录", NvIcon.Delete);
        _deleteButton.Click += (_, _) => DeleteRows();
        _saveButton = ToolButton("保存", NvIcon.Save);
        _saveButton.Click += async (_, _) => await SaveAsync();
        _cancelButton = ToolButton("取消", NvIcon.Stop);
        _cancelButton.Click += (_, _) => CancelChanges();
        var design = ToolButton("设计表", NvIcon.Table);
        design.Click += (_, _) => DesignRequested?.Invoke(this, EventArgs.Empty);
        var ddl = ToolButton("DDL", NvIcon.File);
        ddl.DisplayStyle = ToolStripItemDisplayStyle.Text;
        ddl.Click += (_, _) =>
        {
            string sql = _session.GetTableDdl(_table);
            using var dialog = new NvSqlPreviewDialog("表 DDL - " + _table.DisplayName, sql);
            dialog.ShowDialog(FindForm());
        };
        var export = ToolButton("导出", NvIcon.Export);
        export.Click += (_, _) => ExportCurrent();
        var exportAll = ToolButton("导出全部", NvIcon.Export);
        exportAll.DisplayStyle = ToolStripItemDisplayStyle.Text;
        exportAll.Click += (_, _) => ExportAll();
        var import = ToolButton("导入", NvIcon.Import);
        import.Click += (_, _) => Import();
        var generate = ToolButton("生成数据", NvIcon.Add);
        generate.DisplayStyle = ToolStripItemDisplayStyle.Text;
        generate.Enabled = !_readOnly;
        generate.Click += (_, _) => GenerateDataRequested?.Invoke(this, EventArgs.Empty);

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            refresh, new ToolStripSeparator(),
            _addButton, _deleteButton, _saveButton, _cancelButton, new ToolStripSeparator(),
            design, ddl, new ToolStripSeparator(),
            export, exportAll, import, generate, new ToolStripSeparator(),
            new ToolStripLabel("筛选") { ForeColor = NvTheme.Muted },
            new ToolStripLabel("WHERE") { ForeColor = NvTheme.Muted }
        });
        _filterBox = new ToolStripTextBox
        {
            Width = 300,
            Font = NvTheme.Mono(9.2f),
            ToolTipText = "输入 WHERE 条件，例如 id > 100 AND name LIKE '%abc%'"
        };
        _filterBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                _filter = _filterBox.Text.Trim();
                _page = 0;
                await LoadPageAsync();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        _toolbar.Items.Add(_filterBox);
        var apply = new ToolStripButton("应用")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = NvTheme.Ui(9f)
        };
        apply.Click += async (_, _) =>
        {
            _filter = _filterBox.Text.Trim();
            _page = 0;
            await LoadPageAsync();
        };
        var clear = new ToolStripButton("清除")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = NvTheme.Ui(9f)
        };
        clear.Click += async (_, _) =>
        {
            _filterBox.Text = "";
            _filter = "";
            _page = 0;
            await LoadPageAsync();
        };
        _toolbar.Items.Add(apply);
        _toolbar.Items.Add(clear);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(new ToolStripLabel("每页") { ForeColor = NvTheme.Muted });
        _pageSizeBox = new ToolStripComboBox
        {
            Width = 76,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9f)
        };
        _pageSizeBox.Items.AddRange(new object[] { "100", "500", "1000", "5000", "10000" });
        _pageSizeBox.SelectedItem = _pageSize.ToString();
        if (_pageSizeBox.SelectedIndex < 0)
        {
            _pageSizeBox.SelectedIndex = 1;
        }
        _pageSizeBox.SelectedIndexChanged += async (_, _) =>
        {
            if (int.TryParse(_pageSizeBox.SelectedItem?.ToString(), out int size) && size > 0)
            {
                _pageSize = size;
                _page = 0;
                await LoadPageAsync();
            }
        };
        _toolbar.Items.Add(_pageSizeBox);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            EditMode = _readOnly ? DataGridViewEditMode.EditProgrammatically : DataGridViewEditMode.EditOnKeystrokeOrF2,
            ReadOnly = _readOnly
        };
        NvTheme.StyleGrid(_grid, _readOnly);
        _grid.RowHeadersVisible = true;
        _grid.RowHeadersWidth = 46;
        _grid.SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
        _grid.MultiSelect = true;
        _grid.RowPostPaint += (_, e) =>
        {
            string text = ((_page * _pageSize) + e.RowIndex + 1).ToString();
            TextRenderer.DrawText(e.Graphics, text, NvTheme.Ui(8.5f),
                new Rectangle(e.RowBounds.Left, e.RowBounds.Top, _grid.RowHeadersWidth - 4, e.RowBounds.Height),
                NvTheme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.Value == null || e.Value == DBNull.Value)
            {
                e.Value = "NULL";
                e.CellStyle.ForeColor = Color.FromArgb(160, 170, 180);
                e.FormattingApplied = true;
            }
            else if (e.Value is byte[] bytes)
            {
                e.Value = bytes.Length <= 24
                    ? "0x" + Convert.ToHexString(bytes)
                    : "0x" + Convert.ToHexString(bytes.AsSpan(0, 24)) + $"… ({bytes.Length}B)";
                e.CellStyle.ForeColor = NvTheme.Text;
                e.FormattingApplied = true;
            }
            else if (e.Value is DateTime dt)
            {
                e.Value = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                e.CellStyle.ForeColor = NvTheme.Text;
                e.FormattingApplied = true;
            }
            else
            {
                e.CellStyle.ForeColor = NvTheme.Text;
            }
        };
        _grid.CellBeginEdit += (_, e) =>
        {
            if (_readOnly)
            {
                e.Cancel = true;
                return;
            }
            string name = _grid.Columns[e.ColumnIndex].DataPropertyName;
            var column = _columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (column?.IsBinary == true)
            {
                e.Cancel = true;
            }
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _readOnly)
            {
                string? value = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].FormattedValue?.ToString();
                if (!string.IsNullOrEmpty(value) && value != "NULL")
                {
                    Clipboard.SetText(value);
                }
            }
        };
        _grid.ColumnHeaderMouseClick += async (_, e) =>
        {
            string name = _grid.Columns[e.ColumnIndex].DataPropertyName;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            if (_sortColumn == name)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = name;
                _sortAscending = true;
            }
            await LoadPageAsync();
        };
        _grid.KeyDown += GridKeyDown;
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            _statusLeft.Text = "数据错误：" + e.Exception?.Message;
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("添加记录", null, (_, _) => AddRow());
        menu.Items.Add("删除选中记录", null, (_, _) => DeleteRows());
        menu.Items.Add("保存修改", null, async (_, _) => await SaveAsync());
        menu.Items.Add("取消修改", null, (_, _) => CancelChanges());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("复制单元格", null, (_, _) => CopySelected());
        menu.Items.Add("粘贴", null, (_, _) => PasteClipboard());
        menu.Items.Add("设置为 NULL", null, (_, _) => SetNull());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("刷新", null, async (_, _) => await LoadPageAsync());
        menu.Items.Add("设计表", null, (_, _) => DesignRequested?.Invoke(this, EventArgs.Empty));
        _grid.ContextMenuStrip = menu;

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            BackColor = NvTheme.HeaderBack
        };
        _statusLeft = new Label
        {
            Text = "就绪",
            Location = new Point(10, 6),
            Size = new Size(360, 20),
            ForeColor = NvTheme.Muted,
            AutoSize = false
        };
        _pageLabel = new Label
        {
            Text = "第 1 / 1 页",
            Size = new Size(150, 20),
            Location = new Point(ClientSize.Width - 430, 6),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = NvTheme.Text,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _firstButton = PageButton("|<", (_, _) => GoPage(0));
        _prevButton = PageButton("<", (_, _) => GoPage(_page - 1));
        _nextButton = PageButton(">", (_, _) => GoPage(_page + 1));
        _lastButton = PageButton(">|", (_, _) => GoPage(int.MaxValue));
        int buttonWidth = 34;
        _firstButton.Width = _prevButton.Width = _nextButton.Width = _lastButton.Width = buttonWidth;
        _firstButton.Location = new Point(ClientSize.Width - 274, 4);
        _prevButton.Location = new Point(ClientSize.Width - 238, 4);
        _nextButton.Location = new Point(ClientSize.Width - 202, 4);
        _lastButton.Location = new Point(ClientSize.Width - 166, 4);
        foreach (var button in new[] { _firstButton, _prevButton, _nextButton, _lastButton })
        {
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        }
        _statusRight = new Label
        {
            Text = _session.Profile.Name + " · " + _table.DisplayName,
            Size = new Size(300, 20),
            Location = new Point(ClientSize.Width - 600, 6),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleRight
        };
        bottom.Controls.AddRange(new Control[]
        {
            _statusLeft, _statusRight, _pageLabel, _firstButton, _prevButton, _nextButton, _lastButton
        });
        bottom.Resize += (_, _) =>
        {
            _pageLabel.Location = new Point(bottom.Width - 420, 6);
            _firstButton.Location = new Point(bottom.Width - 264, 4);
            _prevButton.Location = new Point(bottom.Width - 228, 4);
            _nextButton.Location = new Point(bottom.Width - 192, 4);
            _lastButton.Location = new Point(bottom.Width - 156, 4);
            _statusRight.Location = new Point(bottom.Width - 720, 6);
        };

        content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 0, 2, 2) };
        content.Controls.Add(_grid);
        Controls.Add(content);
        Controls.Add(_toolbar);
        Controls.Add(bottom);
        UpdateEditButtons();
        Load += async (_, _) => await LoadPageAsync();
    }

    private Panel content;

    private ToolStripButton ToolButton(string text, NvIcon icon)
    {
        return new ToolStripButton(text, NvIcons.Get(icon, 16))
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Font = NvTheme.Ui(9f),
            Padding = new Padding(3, 0, 3, 0),
            AutoSize = true
        };
    }

    private Button PageButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(34, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = NvTheme.Panel,
            ForeColor = NvTheme.Text,
            Font = NvTheme.Ui(8.2f),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            TabStop = false
        };
        button.FlatAppearance.BorderColor = NvTheme.Border;
        button.Click += handler;
        return button;
    }

    public TableInfo Table => _table;
    public DbSession Session => _session;

    private string Qualified => _session.Qualify(_table);

    private string BuildWhere()
    {
        return string.IsNullOrWhiteSpace(_filter) ? "" : " WHERE " + _filter;
    }

    private string BuildOrder()
    {
        if (!string.IsNullOrWhiteSpace(_sortColumn))
        {
            return " ORDER BY " + _session.Quote(_sortColumn) + (_sortAscending ? " ASC" : " DESC");
        }
        if (_primaryKeys.Count > 0)
        {
            return " ORDER BY " + string.Join(", ", _primaryKeys.Select(_session.Quote));
        }
        if (_session.Kind == DbKind.SQLite)
        {
            return " ORDER BY rowid";
        }
        if (_session.Kind == DbKind.SqlServer)
        {
            return " ORDER BY (SELECT NULL)";
        }
        return "";
    }

    private string BuildPageSql()
    {
        int offset = _page * _pageSize;
        string baseSql = "SELECT * FROM " + Qualified + BuildWhere() + BuildOrder();
        return _session.Kind switch
        {
            DbKind.SqlServer => baseSql + $" OFFSET {offset} ROWS FETCH NEXT {_pageSize} ROWS ONLY",
            DbKind.Oracle => baseSql + $" OFFSET {offset} ROWS FETCH NEXT {_pageSize} ROWS ONLY",
            _ => baseSql + $" LIMIT {_pageSize} OFFSET {offset}"
        };
    }

    private async Task LoadPageAsync(bool force = false)
    {
        if (_loading || (_busy && !force))
        {
            return;
        }
        _loading = true;
        SetBusy(true, "正在加载数据…");
        try
        {
            string countSql = "SELECT COUNT(*) FROM " + Qualified + BuildWhere();
            string pageSql = BuildPageSql();
            var result = await Task.Run(() =>
            {
                object? count = _session.Scalar(countSql);
                long total = 0;
                if (count != null)
                {
                    long.TryParse(Convert.ToString(count, CultureInfo.InvariantCulture), out total);
                }
                var table = _session.Query(pageSql);
                var columns = _session.GetColumns(_table);
                var pks = _session.GetPrimaryKeyColumns(_table);
                return (total, table, columns, pks);
            });

            _totalRows = result.total;
            _columns = result.columns;
            _primaryKeys = result.pks;
            int pages = _totalRows == 0 ? 1 : (int)Math.Ceiling(_totalRows / (double)_pageSize);
            if (_page >= pages)
            {
                _page = Math.Max(0, pages - 1);
                result = await Task.Run(() =>
                {
                    object? count = _session.Scalar(countSql);
                    long total = 0;
                    if (count != null)
                    {
                        long.TryParse(Convert.ToString(count, CultureInfo.InvariantCulture), out total);
                    }
                    return (total, _session.Query(BuildPageSql()), _session.GetColumns(_table),
                        _session.GetPrimaryKeyColumns(_table));
                });
                _totalRows = result.Item1;
            }

            _grid.DataSource = null;
            _data?.Dispose();
            _data = result.Item2;
            _grid.DataSource = _data;
            ConfigureGrid();
            UpdateStatus(pages);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "加载表数据失败：" + _table.DisplayName + " · 筛选=" + _filter);
            _statusLeft.ForeColor = NvTheme.Red;
            _statusLeft.Text = "加载失败：" + ex.Message;
            NvPrompt.Error(FindForm(), "加载数据", ex.Message);
        }
        finally
        {
            SetBusy(false, null);
            _loading = false;
        }
    }

    private void ConfigureGrid()
    {
        if (_data == null)
        {
            return;
        }
        _grid.ReadOnly = _readOnly;
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            string name = column.DataPropertyName;
            var info = _columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            string header = name;
            if (_primaryKeys.Contains(name))
            {
                header = "PK  " + name;
            }
            column.HeaderText = header;
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
            column.ReadOnly = _readOnly || info?.AutoIncrement == true || info?.IsBinary == true;
            int desired = Math.Max(110, (info?.FullType.Length ?? 10) * 8 + 40);
            column.Width = Math.Clamp(desired, 90, 260);
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase) || _primaryKeys.Contains(name))
            {
                column.Width = 110;
            }
        }
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        UpdateEditButtons();
    }

    private void UpdateEditButtons()
    {
        bool editable = !_readOnly && !_busy;
        _addButton.Enabled = editable;
        _deleteButton.Enabled = editable;
        _saveButton.Enabled = editable;
        _cancelButton.Enabled = editable;
    }

    private void UpdateStatus(int pages)
    {
        _statusLeft.ForeColor = NvTheme.Muted;
        _statusLeft.Text = $"共 {_totalRows} 行 · 本页 {_data?.Rows.Count ?? 0} 行 · " +
            (_readOnly ? "只读" : "可编辑") + (string.IsNullOrEmpty(_filter) ? "" : " · 已筛选");
        _pageLabel.Text = $"第 {_page + 1} / {Math.Max(1, pages)} 页";
        _firstButton.Enabled = _prevButton.Enabled = _page > 0 && !_busy;
        _nextButton.Enabled = _lastButton.Enabled = _page + 1 < Math.Max(1, pages) && !_busy;
    }

    private void GoPage(int page)
    {
        int pages = _totalRows == 0 ? 1 : (int)Math.Ceiling(_totalRows / (double)_pageSize);
        _page = Math.Clamp(page, 0, Math.Max(0, pages - 1));
        _ = LoadPageAsync();
    }

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _grid.Enabled = !busy;
        if (message != null)
        {
            _statusLeft.ForeColor = NvTheme.Muted;
            _statusLeft.Text = message;
        }
        UpdateEditButtons();
    }

    private void AddRow()
    {
        if (_readOnly || _data == null)
        {
            return;
        }
        var row = _data.NewRow();
        _data.Rows.Add(row);
        int index = _grid.Rows.Count - 1;
        if (index >= 0)
        {
            _grid.CurrentCell = _grid.Rows[index].Cells.Cast<DataGridViewCell>()
                .FirstOrDefault(c => !c.ReadOnly) ?? _grid.Rows[index].Cells[0];
            _grid.BeginEdit(true);
        }
        _statusLeft.Text = "已添加新行，编辑后点击“保存”。";
    }

    private void DeleteRows()
    {
        if (_readOnly || _data == null)
        {
            return;
        }
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.Index)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();
        if (selected.Count == 0 && _grid.CurrentRow != null)
        {
            selected.Add(_grid.CurrentRow.Index);
        }
        if (selected.Count == 0)
        {
            return;
        }
        if (!NvPrompt.Confirm(FindForm(), "删除记录", $"确定删除选中的 {selected.Count} 条记录吗？", true))
        {
            return;
        }
        foreach (int index in selected)
        {
            if (index >= 0 && index < _data.Rows.Count)
            {
                _data.Rows[index].Delete();
            }
        }
        _statusLeft.Text = "已标记删除，点击“保存”写入数据库。";
    }

    private void CancelChanges()
    {
        if (_data == null)
        {
            return;
        }
        _data.RejectChanges();
        _grid.Refresh();
        _statusLeft.Text = "已取消未保存的修改。";
    }

    private async Task SaveAsync()
    {
        if (_readOnly || _data == null)
        {
            return;
        }
        _grid.EndEdit();
        var changed = _data.GetChanges();
        if (changed == null || changed.Rows.Count == 0)
        {
            _statusLeft.Text = "没有需要保存的修改。";
            return;
        }

        if (!NvPrompt.Confirm(FindForm(), "保存修改",
            $"即将写入 {changed.Rows.Count} 条记录变更，是否继续？", false))
        {
            return;
        }

        SetBusy(true, "正在保存…");
        try
        {
            var pkNames = _primaryKeys.Count > 0
                ? _columns.Where(c => _primaryKeys.Contains(c.Name)).Select(c => c.Name).ToList()
                : _columns.Select(c => c.Name).ToList();
            bool hasPrimaryKey = _primaryKeys.Count > 0;
            int affected = await Task.Run(() => SaveChanges(changed, pkNames, hasPrimaryKey));
            _statusLeft.ForeColor = NvTheme.Green;
            _statusLeft.Text = $"已保存，影响 {affected} 行。";
            await LoadPageAsync(true);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "保存表数据失败：" + _table.DisplayName);
            _statusLeft.ForeColor = NvTheme.Red;
            _statusLeft.Text = "保存失败";
            NvPrompt.Error(FindForm(), "保存失败", ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private int SaveChanges(DataTable changed, List<string> pkNames, bool hasPrimaryKey)
    {
        bool ownTransaction = !_session.HasTransaction;
        if (ownTransaction)
        {
            _session.BeginTransaction();
        }
        int affected = 0;
        try
        {
            foreach (DataRow row in changed.Rows)
            {
                if (row.RowState == DataRowState.Deleted)
                {
                    affected += DeleteRow(row, pkNames);
                }
                else if (row.RowState == DataRowState.Added)
                {
                    affected += InsertRow(row);
                }
                else if (row.RowState == DataRowState.Modified)
                {
                    affected += UpdateRow(row, pkNames, hasPrimaryKey);
                }
            }
            if (ownTransaction)
            {
                _session.Commit();
            }
            return affected;
        }
        catch
        {
            if (ownTransaction)
            {
                try
                {
                    _session.Rollback();
                }
                catch
                {
                    // ignore
                }
            }
            throw;
        }
    }

    private int InsertRow(DataRow row)
    {
        var columns = new List<string>();
        var parameters = new Dictionary<string, object?>();
        int index = 0;
        foreach (DataColumn column in row.Table.Columns)
        {
            var info = _columns.FirstOrDefault(c => c.Name.Equals(column.ColumnName, StringComparison.OrdinalIgnoreCase));
            object? value = row[column];
            if (info?.AutoIncrement == true && (value == null || value == DBNull.Value))
            {
                continue;
            }
            columns.Add(_session.Quote(column.ColumnName));
            string name = _session.Param("p" + index++);
            parameters[name] = value;
        }

        string sql;
        if (columns.Count == 0)
        {
            sql = _session.Kind switch
            {
                DbKind.MySQL => "INSERT INTO " + Qualified + " () VALUES ()",
                DbKind.Oracle => "INSERT INTO " + Qualified + " DEFAULT VALUES",
                _ => "INSERT INTO " + Qualified + " DEFAULT VALUES"
            };
            return _session.Execute(sql);
        }
        sql = "INSERT INTO " + Qualified + " (" + string.Join(", ", columns) + ") VALUES ("
            + string.Join(", ", parameters.Keys) + ")";
        return _session.ExecuteWithParams(sql, parameters);
    }

    private int UpdateRow(DataRow row, List<string> pkNames, bool hasPrimaryKey)
    {
        var sets = new List<string>();
        var where = new List<string>();
        var parameters = new Dictionary<string, object?>();
        int index = 0;

        var pkSet = new HashSet<string>(pkNames, StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in row.Table.Columns)
        {
            var info = _columns.FirstOrDefault(c => c.Name.Equals(column.ColumnName, StringComparison.OrdinalIgnoreCase));
            if ((hasPrimaryKey && pkSet.Contains(column.ColumnName)) || info?.AutoIncrement == true)
            {
                continue;
            }
            string name = _session.Param("p" + index++);
            sets.Add(_session.Quote(column.ColumnName) + " = " + name);
            parameters[name] = row[column];
        }
        if (sets.Count == 0)
        {
            return 0;
        }
        foreach (string pk in pkNames)
        {
            string name = _session.Param("k" + index++);
            object? value = row[pk, DataRowVersion.Original];
            if (value == null || value == DBNull.Value)
            {
                where.Add(_session.Quote(pk) + " IS NULL");
            }
            else
            {
                where.Add(_session.Quote(pk) + " = " + name);
                parameters[name] = value;
            }
        }
        string sql = "UPDATE " + Qualified + " SET " + string.Join(", ", sets)
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "");
        return _session.ExecuteWithParams(sql, parameters);
    }

    private int DeleteRow(DataRow row, List<string> pkNames)
    {
        var where = new List<string>();
        var parameters = new Dictionary<string, object?>();
        int index = 0;
        foreach (string pk in pkNames)
        {
            string name = _session.Param("k" + index++);
            object? value = row[pk, DataRowVersion.Original];
            if (value == null || value == DBNull.Value)
            {
                where.Add(_session.Quote(pk) + " IS NULL");
            }
            else
            {
                where.Add(_session.Quote(pk) + " = " + name);
                parameters[name] = value;
            }
        }
        string sql = "DELETE FROM " + Qualified
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "");
        if (where.Count == 0)
        {
            throw new InvalidOperationException("该表没有主键，无法安全删除记录。");
        }
        return _session.ExecuteWithParams(sql, parameters);
    }

    private void CopySelected()
    {
        if (_grid.GetCellCount(DataGridViewElementStates.Selected) == 0)
        {
            return;
        }
        var sb = new StringBuilder();
        foreach (DataGridViewCell cell in _grid.SelectedCells)
        {
            sb.Append(cell.FormattedValue?.ToString() ?? "");
            sb.Append('\t');
        }
        Clipboard.SetText(sb.ToString().TrimEnd('\t'));
        _statusLeft.Text = "已复制到剪贴板。";
    }

    private void PasteClipboard()
    {
        if (_readOnly || _data == null || !Clipboard.ContainsText())
        {
            return;
        }
        string text = Clipboard.GetText();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0)
        {
            return;
        }
        int startRow = _grid.CurrentCell?.RowIndex ?? Math.Max(0, _grid.Rows.Count - 1);
        int startColumn = _grid.CurrentCell?.ColumnIndex ?? 0;
        int added = 0;
        for (int r = 0; r < lines.Length; r++)
        {
            if (string.IsNullOrEmpty(lines[r]) && r == lines.Length - 1)
            {
                continue;
            }
            var cells = lines[r].Split('\t');
            int targetRow = startRow + r;
            while (targetRow >= _data.Rows.Count)
            {
                _data.Rows.Add(_data.NewRow());
                added++;
            }
            for (int c = 0; c < cells.Length; c++)
            {
                int targetColumn = startColumn + c;
                if (targetColumn >= _grid.Columns.Count)
                {
                    break;
                }
                if (_grid.Columns[targetColumn].ReadOnly)
                {
                    continue;
                }
                string value = cells[c];
                try
                {
                    _data.Rows[targetRow][_grid.Columns[targetColumn].DataPropertyName] =
                        value.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? DBNull.Value : value;
                }
                catch (Exception)
                {
                    _statusLeft.ForeColor = NvTheme.Red;
                    _statusLeft.Text = "第 " + (targetRow + 1) + " 行的值“" + value + "”与列类型不匹配，已跳过。";
                }
            }
        }
        _statusLeft.Text = added > 0 ? $"已粘贴并新增 {added} 行，编辑后请保存。" : "已粘贴数据，请保存。";
    }

    private void SetNull()
    {
        if (_readOnly)
        {
            return;
        }
        foreach (DataGridViewCell cell in _grid.SelectedCells)
        {
            if (!cell.ReadOnly)
            {
                cell.Value = DBNull.Value;
            }
        }
    }

    private void ExportCurrent()
    {
        if (_data == null)
        {
            return;
        }
        NvExportService.ExportTableWithDialog(FindForm(), _data, _table.Name + "_page" + (_page + 1),
            _session.Kind, true);
    }

    private async void ExportAll()
    {
        if (_busy)
        {
            return;
        }
        SetBusy(true, "正在读取全部数据…");
        try
        {
            string sql = "SELECT * FROM " + Qualified + BuildWhere() + BuildOrder();
            var table = await Task.Run(() => _session.Query(sql, null, 600));
            SetBusy(false, null);
            if (table.Rows.Count > 100000)
            {
                if (!NvPrompt.Confirm(FindForm(), "导出全部",
                    $"该表返回 {table.Rows.Count} 行，导出可能耗时较长，是否继续？", true))
                {
                    return;
                }
            }
            NvExportService.ExportTableWithDialog(FindForm(), table, _table.Name, _session.Kind, true);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "导出全部数据失败：" + _table.DisplayName);
            NvPrompt.Error(FindForm(), "导出全部", ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void Import()
    {
        NvImportDialog.ShowForTable(FindForm(), _session, _table, () => LoadPageAsync());
    }

    public bool TryClose()
    {
        if (_readOnly || _data == null)
        {
            return true;
        }
        var changes = _data.GetChanges();
        if (changes == null || changes.Rows.Count == 0)
        {
            return true;
        }
        return MessageBox.Show(FindForm(),
            "当前表有未保存的修改，关闭将丢弃这些修改。\n确定关闭吗？",
            "关闭表", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void GridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            _ = SaveAsync();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            _ = LoadPageAsync();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.V)
        {
            PasteClipboard();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete && !_readOnly)
        {
            foreach (DataGridViewCell cell in _grid.SelectedCells)
            {
                if (!cell.ReadOnly)
                {
                    cell.Value = DBNull.Value;
                }
            }
            e.Handled = true;
        }
    }
}
