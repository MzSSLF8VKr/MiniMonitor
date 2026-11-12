using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>表设计器：列 / 索引 / 外键编辑，生成并执行 ALTER TABLE / CREATE TABLE。</summary>
internal sealed class TableDesignerTab : UserControl, NvClosable
{
    private sealed class ColumnEdit
    {
        public string? OriginalName { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool NotNull { get; set; }
        public bool PrimaryKey { get; set; }
        public bool AutoIncrement { get; set; }
        public string? DefaultValue { get; set; }
        public string? Comment { get; set; }

        public ColumnEdit Clone() => (ColumnEdit)MemberwiseClone();
    }

    private sealed class IndexEdit
    {
        public string? OriginalName { get; set; }
        public string Name { get; set; } = "";
        public bool Unique { get; set; }
        public string Columns { get; set; } = "";

        public bool SameAs(IndexEdit other) => other.Name == Name && other.Unique == Unique
            && other.Columns.Replace(" ", "") == Columns.Replace(" ", "");
    }

    private sealed class FkEdit
    {
        public string? OriginalName { get; set; }
        public string Name { get; set; } = "";
        public string Columns { get; set; } = "";
        public string RefTable { get; set; } = "";
        public string RefColumns { get; set; } = "";
        public string OnDelete { get; set; } = "";
        public string OnUpdate { get; set; } = "";

        public bool SameAs(FkEdit other) => other.Name == Name
            && other.Columns.Replace(" ", "") == Columns.Replace(" ", "")
            && other.RefTable == RefTable && other.RefColumns.Replace(" ", "") == RefColumns.Replace(" ", "")
            && other.OnDelete == OnDelete && other.OnUpdate == OnUpdate;
    }

    private readonly DbSession _session;
    private readonly bool _isNew;
    private TableInfo _table;
    private readonly TextBox _tableName;
    private readonly DataGridView _columnsGrid;
    private readonly DataGridView _indexesGrid;
    private readonly DataGridView _foreignGrid;
    private readonly RichTextBox _ddlBox;
    private readonly Label _status;
    private readonly ToolStripButton _saveButton;
    private readonly ToolStripComboBox _typePicker;
    private List<ColumnEdit> _originalColumns = new();
    private List<IndexEdit> _originalIndexes = new();
    private List<FkEdit> _originalForeignKeys = new();
    private string _primaryKeyName = "";
    private bool _loading;
    private bool _dirty;

    public event EventHandler? TitleChanged;
    public event EventHandler? SchemaChanged;
    public event EventHandler<TableInfo>? TableCreated;

    public TableDesignerTab(DbSession session, TableInfo? existing)
    {
        _session = session;
        _isNew = existing == null;
        _table = existing ?? new TableInfo
        {
            Database = session.CurrentDatabase,
            Schema = DefaultSchema(),
            Name = "new_table"
        };

        BackColor = NvTheme.Background;

        var header = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = NvTheme.HeaderBack };
        header.Controls.Add(new Label
        {
            Text = _isNew ? "新建表" : "设计表",
            Location = new Point(12, 6),
            AutoSize = true,
            Font = NvTheme.Ui(9.6f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        header.Controls.Add(new Label
        {
            Text = "表名",
            Location = new Point(12, 25),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        _tableName = new TextBox
        {
            Location = new Point(54, 21),
            Width = 300,
            Font = NvTheme.Ui(9.2f),
            Text = _isNew ? "new_table" : _table.Name
        };
        header.Controls.Add(_tableName);
        header.Controls.Add(new Label
        {
            Text = _session.Kind.ShortName() + " · " + _session.CurrentDatabase,
            Location = new Point(370, 24),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 31,
            GripStyle = ToolStripGripStyle.Hidden
        };
        NvTheme.ApplyToolStrip(toolbar);
        var addColumn = ToolButton("添加列", NvIcon.Add);
        addColumn.Click += (_, _) => AddColumnRow();
        var removeColumn = ToolButton("删除列", NvIcon.Delete);
        removeColumn.Click += (_, _) => RemoveColumnRows();
        var up = ToolButton("上移", NvIcon.Chart);
        up.DisplayStyle = ToolStripItemDisplayStyle.Text;
        up.Click += (_, _) => MoveRow(-1);
        var down = ToolButton("下移", NvIcon.Chart);
        down.DisplayStyle = ToolStripItemDisplayStyle.Text;
        down.Click += (_, _) => MoveRow(1);
        var reload = ToolButton("重新加载", NvIcon.Refresh);
        reload.Click += async (_, _) => await LoadMetadataAsync();
        var preview = ToolButton("预览变更 SQL", NvIcon.File);
        preview.DisplayStyle = ToolStripItemDisplayStyle.Text;
        preview.Click += (_, _) => PreviewChanges();
        _saveButton = ToolButton(_isNew ? "创建表" : "保存结构", NvIcon.Save);
        _saveButton.Click += async (_, _) => await SaveAsync();

        _typePicker = new ToolStripComboBox
        {
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9f)
        };
        foreach (string type in TypeList())
        {
            _typePicker.Items.Add(type);
        }
        var applyType = new ToolStripButton("应用类型")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = NvTheme.Ui(9f)
        };
        applyType.Click += (_, _) => ApplyType();

        toolbar.Items.AddRange(new ToolStripItem[]
        {
            _saveButton, new ToolStripSeparator(),
            addColumn, removeColumn, up, down, new ToolStripSeparator(),
            reload, preview, new ToolStripSeparator(),
            new ToolStripLabel("常用类型") { ForeColor = NvTheme.Muted }, _typePicker, applyType
        });

        _columnsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = true,
            RowHeadersWidth = 34,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        NvTheme.StyleGrid(_columnsGrid, false);
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "列名",
            FillWeight = 110
        });
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Type",
            HeaderText = "类型（含长度）",
            FillWeight = 150
        });
        _columnsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "NotNull",
            HeaderText = "非空",
            FillWeight = 45
        });
        _columnsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Primary",
            HeaderText = "主键",
            FillWeight = 45
        });
        _columnsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Auto",
            HeaderText = "自增",
            FillWeight = 45
        });
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Default",
            HeaderText = "默认值",
            FillWeight = 90
        });
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Comment",
            HeaderText = "注释",
            FillWeight = 110
        });
        _columnsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_columnsGrid.IsCurrentCellDirty && _columnsGrid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _columnsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _columnsGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _columnsGrid.Columns["Type"].Index)
            {
                ApplyType();
            }
        };

        _indexesGrid = CreateIndexGrid();
        _foreignGrid = CreateForeignGrid();
        _columnsGrid.CellValueChanged += (_, _) => _dirty = true;
        _columnsGrid.RowsAdded += (_, _) => _dirty = true;
        _columnsGrid.RowsRemoved += (_, _) => _dirty = true;
        _indexesGrid.CellValueChanged += (_, _) => _dirty = true;
        _indexesGrid.RowsAdded += (_, _) => _dirty = true;
        _indexesGrid.RowsRemoved += (_, _) => _dirty = true;
        _foreignGrid.CellValueChanged += (_, _) => _dirty = true;
        _foreignGrid.RowsAdded += (_, _) => _dirty = true;
        _foreignGrid.RowsRemoved += (_, _) => _dirty = true;
        _tableName.TextChanged += (_, _) => _dirty = true;
        _ddlBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Mono(9.5f),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            WordWrap = false
        };

        var bottomTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Ui(9f),
            Padding = new Point(14, 4)
        };
        var indexPage = new TabPage("索引") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        var indexToolbar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = NvTheme.HeaderBack };
        var addIndex = NvTheme.FlatButton("添加索引", null, null, 84);
        addIndex.Location = new Point(4, 3);
        addIndex.Height = 24;
        addIndex.Click += (_, _) => _indexesGrid.Rows.Add();
        var removeIndex = NvTheme.FlatButton("删除索引", null, null, 84);
        removeIndex.Location = new Point(92, 3);
        removeIndex.Height = 24;
        removeIndex.Click += (_, _) => RemoveSelected(_indexesGrid);
        indexToolbar.Controls.AddRange(new Control[] { addIndex, removeIndex });
        indexPage.Controls.Add(_indexesGrid);
        indexPage.Controls.Add(indexToolbar);

        var fkPage = new TabPage("外键") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        var fkToolbar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = NvTheme.HeaderBack };
        var addFk = NvTheme.FlatButton("添加外键", null, null, 84);
        addFk.Location = new Point(4, 3);
        addFk.Height = 24;
        addFk.Click += (_, _) => _foreignGrid.Rows.Add();
        var removeFk = NvTheme.FlatButton("删除外键", null, null, 84);
        removeFk.Location = new Point(92, 3);
        removeFk.Height = 24;
        removeFk.Click += (_, _) => RemoveSelected(_foreignGrid);
        fkToolbar.Controls.AddRange(new Control[] { addFk, removeFk });
        fkPage.Controls.Add(_foreignGrid);
        fkPage.Controls.Add(fkToolbar);

        var ddlPage = new TabPage("DDL 预览") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        ddlPage.Controls.Add(_ddlBox);
        bottomTabs.TabPages.Add(indexPage);
        bottomTabs.TabPages.Add(fkPage);
        bottomTabs.TabPages.Add(ddlPage);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 5,
            BackColor = NvTheme.BorderLight
        };
        split.Panel1.Controls.Add(_columnsGrid);
        split.Panel2.Controls.Add(bottomTabs);
        Load += (_, _) =>
        {
            try
            {
                split.SplitterDistance = Math.Max(160, (Height - 46 - 31 - 30) * 2 / 3);
            }
            catch
            {
                // ignore
            }
        };

        var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Text = "就绪",
            Location = new Point(8, 2),
            Size = new Size(700, 20),
            ForeColor = NvTheme.Muted
        };
        statusPanel.Controls.Add(_status);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 0, 2, 2) };
        content.Controls.Add(split);
        Controls.Add(content);
        Controls.Add(toolbar);
        Controls.Add(header);
        Controls.Add(statusPanel);
        Load += async (_, _) => await LoadMetadataAsync();
    }

    public TableInfo Table => _table;
    public DbSession Session => _session;
    public string TabTitle => _isNew ? "新建表" : "设计表 - " + _table.DisplayName;

    private string DefaultSchema()
    {
        return _session.Kind switch
        {
            DbKind.PostgreSQL => "public",
            DbKind.SqlServer => "dbo",
            DbKind.Oracle => _session.CurrentDatabase,
            _ => ""
        };
    }

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

    private IEnumerable<string> TypeList()
    {
        return _session.Kind switch
        {
            DbKind.MySQL => new[]
            {
                "INT", "BIGINT", "SMALLINT", "TINYINT", "DECIMAL(18,4)", "FLOAT", "DOUBLE",
                "VARCHAR(255)", "CHAR(36)", "TEXT", "LONGTEXT", "DATE", "DATETIME", "TIMESTAMP",
                "TIME", "BLOB", "LONGBLOB", "JSON", "BOOLEAN", "ENUM('A','B')"
            },
            DbKind.PostgreSQL => new[]
            {
                "INTEGER", "BIGINT", "SMALLINT", "NUMERIC(18,4)", "REAL", "DOUBLE PRECISION",
                "VARCHAR(255)", "CHAR(36)", "TEXT", "DATE", "TIMESTAMP", "TIMESTAMPTZ",
                "TIME", "BOOLEAN", "BYTEA", "JSON", "JSONB", "UUID"
            },
            DbKind.SqlServer => new[]
            {
                "INT", "BIGINT", "SMALLINT", "TINYINT", "DECIMAL(18,4)", "FLOAT", "REAL",
                "NVARCHAR(255)", "VARCHAR(255)", "NCHAR(36)", "NVARCHAR(MAX)", "DATE",
                "DATETIME2", "DATETIME", "TIME", "BIT", "VARBINARY(MAX)"
            },
            DbKind.Oracle => new[]
            {
                "NUMBER(19)", "NUMBER(18,4)", "BINARY_DOUBLE", "VARCHAR2(255)", "NVARCHAR2(255)",
                "CHAR(36)", "NCHAR(36)", "CLOB", "NCLOB", "BLOB", "DATE", "TIMESTAMP", "RAW(2000)"
            },
            _ => new[] { "INTEGER", "REAL", "TEXT", "BLOB", "NUMERIC" }
        };
    }

    private DataGridView CreateIndexGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        NvTheme.StyleGrid(grid, false);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "索引名", FillWeight = 100 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Unique", HeaderText = "唯一", FillWeight = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cols", HeaderText = "列（逗号分隔）", FillWeight = 180 });
        return grid;
    }

    private DataGridView CreateForeignGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        NvTheme.StyleGrid(grid, false);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "外键名", FillWeight = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cols", HeaderText = "本表列", FillWeight = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RefTable", HeaderText = "引用表", FillWeight = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RefCols", HeaderText = "引用列", FillWeight = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OnDelete", HeaderText = "删除规则", FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OnUpdate", HeaderText = "更新规则", FillWeight = 60 });
        return grid;
    }

    private void AddColumnRow()
    {
        int index = _columnsGrid.Rows.Add();
        var row = _columnsGrid.Rows[index];
        row.Cells["Name"].Value = "new_column";
        row.Cells["Type"].Value = TypeList().FirstOrDefault() ?? "TEXT";
        row.Cells["NotNull"].Value = false;
        row.Cells["Primary"].Value = false;
        row.Cells["Auto"].Value = false;
        row.Tag = null;
        _columnsGrid.CurrentCell = row.Cells["Name"];
        _columnsGrid.BeginEdit(true);
    }

    private void RemoveColumnRows()
    {
        var rows = _columnsGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.Index)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();
        if (rows.Count == 0 && _columnsGrid.CurrentRow != null)
        {
            rows.Add(_columnsGrid.CurrentRow.Index);
        }
        foreach (int index in rows)
        {
            _columnsGrid.Rows.RemoveAt(index);
        }
    }

    private static void RemoveSelected(DataGridView grid)
    {
        var rows = grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.Index)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();
        if (rows.Count == 0 && grid.CurrentRow != null)
        {
            rows.Add(grid.CurrentRow.Index);
        }
        foreach (int index in rows)
        {
            grid.Rows.RemoveAt(index);
        }
    }

    private void MoveRow(int delta)
    {
        if (_columnsGrid.CurrentRow == null)
        {
            return;
        }
        int index = _columnsGrid.CurrentRow.Index;
        int target = index + delta;
        if (target < 0 || target >= _columnsGrid.Rows.Count)
        {
            return;
        }
        var source = _columnsGrid.Rows[index];
        var values = source.Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray();
        object? tag = source.Tag;
        _columnsGrid.Rows.RemoveAt(index);
        _columnsGrid.Rows.Insert(target, 1);
        var row = _columnsGrid.Rows[target];
        for (int i = 0; i < values.Length; i++)
        {
            row.Cells[i].Value = values[i];
        }
        row.Tag = tag;
        _columnsGrid.CurrentCell = row.Cells["Name"];
    }

    private void ApplyType()
    {
        if (_columnsGrid.CurrentRow == null || _typePicker.SelectedItem == null)
        {
            return;
        }
        _columnsGrid.CurrentRow.Cells["Type"].Value = _typePicker.SelectedItem.ToString();
    }

    private async Task LoadMetadataAsync()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _status.Text = "正在读取表结构…";
        try
        {
            if (_isNew)
            {
                if (_columnsGrid.Rows.Count == 0)
                {
                    AddInitialColumns();
                }
                _loading = false;
                _status.Text = "填写列定义后点击“创建表”。";
                return;
            }
            var table = new TableInfo
            {
                Database = _table.Database,
                Schema = _table.Schema,
                Name = _table.Name,
                IsView = _table.IsView
            };
            var data = await Task.Run(() =>
            {
                var columns = _session.GetColumns(table);
                var indexes = _session.GetIndexes(table);
                var fks = _session.GetForeignKeys(table);
                string pkName = _session.GetPrimaryKeyName(table);
                return (columns, indexes, fks, pkName);
            });
            _primaryKeyName = data.pkName;
            _originalColumns = data.columns.Select(c => new ColumnEdit
            {
                OriginalName = c.Name,
                Name = c.Name,
                Type = c.FullType,
                NotNull = !c.Nullable,
                PrimaryKey = c.PrimaryKey,
                AutoIncrement = c.AutoIncrement,
                DefaultValue = c.DefaultValue,
                Comment = c.Comment
            }).ToList();
            _originalIndexes = data.indexes.Where(i => !i.Primary).Select(i => new IndexEdit
            {
                OriginalName = i.Name,
                Name = i.Name,
                Unique = i.Unique,
                Columns = string.Join(", ", i.Columns)
            }).ToList();
            _originalForeignKeys = data.fks.Select(f => new FkEdit
            {
                OriginalName = f.Name,
                Name = f.Name,
                Columns = string.Join(", ", f.Columns),
                RefTable = f.RefTable,
                RefColumns = string.Join(", ", f.RefColumns),
                OnDelete = f.OnDelete ?? "",
                OnUpdate = f.OnUpdate ?? ""
            }).ToList();
            FillGrids();
            _tableName.Text = _table.Name;
            _dirty = false;
            _status.ForeColor = NvTheme.Muted;
            _status.Text = $"已加载 {_originalColumns.Count} 列、{_originalIndexes.Count} 个索引、{_originalForeignKeys.Count} 个外键。";
            UpdateDdlPreviewFromDatabase();
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "加载表结构失败：" + _table.DisplayName);
            _status.ForeColor = NvTheme.Red;
            _status.Text = "加载表结构失败：" + ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void AddInitialColumns()
    {
        string intType = _session.Kind switch
        {
            DbKind.MySQL => "BIGINT",
            DbKind.PostgreSQL => "BIGINT",
            DbKind.SqlServer => "BIGINT",
            DbKind.Oracle => "NUMBER(19)",
            _ => "INTEGER"
        };
        int index = _columnsGrid.Rows.Add();
        var row = _columnsGrid.Rows[index];
        row.Cells["Name"].Value = "id";
        row.Cells["Type"].Value = intType;
        row.Cells["NotNull"].Value = true;
        row.Cells["Primary"].Value = true;
        row.Cells["Auto"].Value = true;
        row.Tag = null;

        index = _columnsGrid.Rows.Add();
        row = _columnsGrid.Rows[index];
        row.Cells["Name"].Value = "name";
        row.Cells["Type"].Value = _session.Kind == DbKind.SqlServer ? "NVARCHAR(255)"
            : _session.Kind == DbKind.Oracle ? "VARCHAR2(255)" : "VARCHAR(255)";
        row.Cells["NotNull"].Value = false;
        row.Cells["Primary"].Value = false;
        row.Cells["Auto"].Value = false;
        row.Tag = null;
    }

    private void FillGrids()
    {
        _columnsGrid.Rows.Clear();
        foreach (var column in _originalColumns)
        {
            int index = _columnsGrid.Rows.Add();
            var row = _columnsGrid.Rows[index];
            row.Cells["Name"].Value = column.Name;
            row.Cells["Type"].Value = column.Type;
            row.Cells["NotNull"].Value = column.NotNull;
            row.Cells["Primary"].Value = column.PrimaryKey;
            row.Cells["Auto"].Value = column.AutoIncrement;
            row.Cells["Default"].Value = column.DefaultValue;
            row.Cells["Comment"].Value = column.Comment;
            row.Tag = column.OriginalName;
        }
        _indexesGrid.Rows.Clear();
        foreach (var index in _originalIndexes)
        {
            int rowIndex = _indexesGrid.Rows.Add();
            var row = _indexesGrid.Rows[rowIndex];
            row.Cells["Name"].Value = index.Name;
            row.Cells["Unique"].Value = index.Unique;
            row.Cells["Cols"].Value = index.Columns;
            row.Tag = index.OriginalName;
        }
        _foreignGrid.Rows.Clear();
        foreach (var fk in _originalForeignKeys)
        {
            int rowIndex = _foreignGrid.Rows.Add();
            var row = _foreignGrid.Rows[rowIndex];
            row.Cells["Name"].Value = fk.Name;
            row.Cells["Cols"].Value = fk.Columns;
            row.Cells["RefTable"].Value = fk.RefTable;
            row.Cells["RefCols"].Value = fk.RefColumns;
            row.Cells["OnDelete"].Value = fk.OnDelete;
            row.Cells["OnUpdate"].Value = fk.OnUpdate;
            row.Tag = fk.OriginalName;
        }
    }

    private List<ColumnEdit> ReadColumns()
    {
        var list = new List<ColumnEdit>();
        foreach (DataGridViewRow row in _columnsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }
            string name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            list.Add(new ColumnEdit
            {
                OriginalName = row.Tag as string,
                Name = name,
                Type = Convert.ToString(row.Cells["Type"].Value)?.Trim() ?? "TEXT",
                NotNull = Convert.ToBoolean(row.Cells["NotNull"].Value ?? false),
                PrimaryKey = Convert.ToBoolean(row.Cells["Primary"].Value ?? false),
                AutoIncrement = Convert.ToBoolean(row.Cells["Auto"].Value ?? false),
                DefaultValue = Convert.ToString(row.Cells["Default"].Value),
                Comment = Convert.ToString(row.Cells["Comment"].Value)
            });
        }
        return list;
    }

    private List<IndexEdit> ReadIndexes()
    {
        var list = new List<IndexEdit>();
        foreach (DataGridViewRow row in _indexesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }
            string name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            list.Add(new IndexEdit
            {
                OriginalName = row.Tag as string,
                Name = name,
                Unique = Convert.ToBoolean(row.Cells["Unique"].Value ?? false),
                Columns = Convert.ToString(row.Cells["Cols"].Value)?.Trim() ?? ""
            });
        }
        return list;
    }

    private List<FkEdit> ReadForeignKeys()
    {
        var list = new List<FkEdit>();
        foreach (DataGridViewRow row in _foreignGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }
            string name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            list.Add(new FkEdit
            {
                OriginalName = row.Tag as string,
                Name = name,
                Columns = Convert.ToString(row.Cells["Cols"].Value)?.Trim() ?? "",
                RefTable = Convert.ToString(row.Cells["RefTable"].Value)?.Trim() ?? "",
                RefColumns = Convert.ToString(row.Cells["RefCols"].Value)?.Trim() ?? "",
                OnDelete = Convert.ToString(row.Cells["OnDelete"].Value)?.Trim() ?? "",
                OnUpdate = Convert.ToString(row.Cells["OnUpdate"].Value)?.Trim() ?? ""
            });
        }
        return list;
    }

    private string ValidateEdits(List<ColumnEdit> columns, List<IndexEdit> indexes, List<FkEdit> fks)
    {
        if (columns.Count == 0)
        {
            return "表至少需要一列。";
        }
        var duplicate = columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            return "列名重复：" + duplicate.Key;
        }
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column.Type))
            {
                return "列 " + column.Name + " 未填写类型。";
            }
        }
        foreach (var index in indexes)
        {
            if (string.IsNullOrWhiteSpace(index.Columns))
            {
                return "索引 " + index.Name + " 未填写列。";
            }
        }
        foreach (var fk in fks)
        {
            if (string.IsNullOrWhiteSpace(fk.Columns) || string.IsNullOrWhiteSpace(fk.RefTable)
                || string.IsNullOrWhiteSpace(fk.RefColumns))
            {
                return "外键 " + fk.Name + " 的列/引用表/引用列不能为空。";
            }
        }
        return "";
    }

    private void PreviewChanges()
    {
        try
        {
            string sql = BuildSql(out _, out _);
            _ddlBox.Text = sql;
            if (string.IsNullOrWhiteSpace(sql))
            {
                NvPrompt.Info(FindForm(), "预览", "没有需要执行的变更。");
                return;
            }
            if (string.Equals(_tableName.Text.Trim(), _table.Name, StringComparison.Ordinal) && !_isNew)
            {
                sql = "-- 结构变更预览\n" + sql;
            }
            using var dialog = new NvSqlPreviewDialog("SQL 预览", sql);
            dialog.ShowDialog(FindForm());
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "生成表结构 SQL 失败：" + _table.DisplayName);
            NvPrompt.Error(FindForm(), "生成 SQL", ex.Message);
        }
    }

    private async Task SaveAsync()
    {
        var columns = ReadColumns();
        var indexes = ReadIndexes();
        var fks = ReadForeignKeys();
        string error = ValidateEdits(columns, indexes, fks);
        if (!string.IsNullOrEmpty(error))
        {
            NvPrompt.Error(FindForm(), "表结构", error);
            return;
        }
        string newName = _tableName.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            NvPrompt.Error(FindForm(), "表结构", "表名不能为空。");
            return;
        }

        List<string> statements;
        string preview;
        try
        {
            if (_isNew)
            {
                _table.Name = newName;
                preview = BuildCreateStatement(columns, indexes, fks);
                statements = new List<string> { preview };
            }
            else
            {
                statements = BuildAlterStatements(columns, indexes, fks, newName);
                preview = string.Join(";" + Environment.NewLine + Environment.NewLine, statements);
            }
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "保存表结构生成 SQL 失败：" + _table.DisplayName);
            NvPrompt.Error(FindForm(), "生成 SQL", ex.Message);
            return;
        }

        if (statements.Count == 0)
        {
            NvPrompt.Info(FindForm(), "保存结构", "没有检测到任何结构变更。");
            return;
        }

        using (var dialog = new NvSqlPreviewDialog(_isNew ? "确认创建表" : "确认修改表结构",
            preview, "确认后将执行以下 SQL，请检查无误后点击“执行”。", true))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }
        }

        _saveButton.Enabled = false;
        _status.ForeColor = NvTheme.Muted;
        _status.Text = "正在执行结构变更…";
        try
        {
            await Task.Run(() =>
            {
                foreach (string statement in statements)
                {
                    _session.ExecuteScript(statement, 600);
                }
            });
            _status.ForeColor = NvTheme.Green;
            if (_isNew)
            {
                _status.Text = "表创建成功。";
                _dirty = false;
                var created = new TableInfo
                {
                    Database = _table.Database,
                    Schema = _table.Schema,
                    Name = newName
                };
                TableCreated?.Invoke(this, created);
                SchemaChanged?.Invoke(this, EventArgs.Empty);
                TitleChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _status.Text = "表结构保存成功。";
                _table = new TableInfo
                {
                    Database = _table.Database,
                    Schema = _table.Schema,
                    Name = newName
                };
                await LoadMetadataAsync();
                SchemaChanged?.Invoke(this, EventArgs.Empty);
                TitleChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "执行表结构变更失败：" + _table.DisplayName);
            _status.ForeColor = NvTheme.Red;
            _status.Text = "执行失败";
            NvPrompt.Error(FindForm(), "执行失败", ex.Message);
        }
        finally
        {
            _saveButton.Enabled = true;
        }
    }

    private string BuildSql(out List<ColumnEdit> columns, out List<IndexEdit> indexes)
    {
        columns = ReadColumns();
        indexes = ReadIndexes();
        var fks = ReadForeignKeys();
        string error = ValidateEdits(columns, indexes, fks);
        if (!string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException(error);
        }
        string newName = _tableName.Text.Trim();
        if (_isNew)
        {
            _table.Name = newName;
            return BuildCreateStatement(columns, indexes, fks);
        }
        var statements = BuildAlterStatements(columns, indexes, fks, newName);
        return string.Join(";" + Environment.NewLine + Environment.NewLine, statements);
    }

    private ColumnEdit? FindOld(string? originalName)
    {
        return _originalColumns.FirstOrDefault(c => c.OriginalName == originalName);
    }

    private IndexEdit? FindOldIndex(string? originalName)
    {
        return _originalIndexes.FirstOrDefault(c => c.OriginalName == originalName);
    }

    private FkEdit? FindOldFk(string? originalName)
    {
        return _originalForeignKeys.FirstOrDefault(c => c.OriginalName == originalName);
    }

    private string BuildCreateStatement(List<ColumnEdit> columns, List<IndexEdit> indexes, List<FkEdit> fks)
    {
        var table = new TableInfo
        {
            Database = _table.Database,
            Schema = _table.Schema,
            Name = _tableName.Text.Trim()
        };
        var columnInfos = columns.Select(c => new ColumnInfo
        {
            Name = c.Name,
            DataType = c.Type,
            FullType = c.Type,
            Nullable = !c.NotNull,
            DefaultValue = string.IsNullOrWhiteSpace(c.DefaultValue) ? null : c.DefaultValue,
            PrimaryKey = c.PrimaryKey,
            AutoIncrement = c.AutoIncrement,
            Comment = c.Comment,
            Ordinal = columns.IndexOf(c) + 1
        }).ToList();
        var indexInfos = indexes.Select(i => new IndexInfo
        {
            Name = i.Name,
            Unique = i.Unique,
            Columns = SplitColumns(i.Columns)
        }).ToList();
        var fkInfos = fks.Select(f => new ForeignKeyInfo
        {
            Name = f.Name,
            Columns = SplitColumns(f.Columns),
            RefTable = f.RefTable,
            RefColumns = SplitColumns(f.RefColumns),
            OnDelete = string.IsNullOrWhiteSpace(f.OnDelete) ? null : f.OnDelete,
            OnUpdate = string.IsNullOrWhiteSpace(f.OnUpdate) ? null : f.OnUpdate
        }).ToList();
        return _session.BuildCreateTable(table, columnInfos, indexInfos, fkInfos);
    }

    private static List<string> SplitColumns(string text)
    {
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private string ColumnDefinition(ColumnEdit column, bool includeName)
    {
        var sb = new StringBuilder();
        if (includeName)
        {
            sb.Append(_session.Quote(column.Name)).Append(' ');
        }
        string type = column.Type.Trim();
        sb.Append(type);
        if (column.AutoIncrement && _session.Kind != DbKind.MySQL)
        {
            switch (_session.Kind)
            {
                case DbKind.PostgreSQL:
                    if (!type.Contains("serial", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(" GENERATED BY DEFAULT AS IDENTITY");
                    }
                    break;
                case DbKind.SqlServer:
                    sb.Append(" IDENTITY(1,1)");
                    break;
                case DbKind.Oracle:
                    sb.Append(" GENERATED BY DEFAULT AS IDENTITY");
                    break;
            }
        }
        if (column.NotNull)
        {
            sb.Append(" NOT NULL");
        }
        if (!string.IsNullOrWhiteSpace(column.DefaultValue) && !column.AutoIncrement)
        {
            sb.Append(" DEFAULT ").Append(column.DefaultValue.Trim());
        }
        if (column.AutoIncrement && _session.Kind == DbKind.MySQL)
        {
            sb.Append(" AUTO_INCREMENT");
        }
        if (_session.Kind == DbKind.MySQL && !string.IsNullOrWhiteSpace(column.Comment))
        {
            sb.Append(" COMMENT '").Append(column.Comment!.Replace("'", "''")).Append('\'');
        }
        return sb.ToString();
    }

    private string AddColumnSql(ColumnEdit column)
    {
        string table = _session.Qualify(_table);
        string definition = ColumnDefinition(column, false);
        return _session.Kind switch
        {
            DbKind.Oracle => $"ALTER TABLE {table} ADD ({_session.Quote(column.Name)} {definition})",
            DbKind.SqlServer => $"ALTER TABLE {table} ADD {_session.Quote(column.Name)} {definition}",
            _ => $"ALTER TABLE {table} ADD COLUMN {_session.Quote(column.Name)} {definition}"
        };
    }

    private string DropColumnSql(string name)
    {
        return _session.Kind == DbKind.Oracle
            ? $"ALTER TABLE {_session.Qualify(_table)} DROP COLUMN {_session.Quote(name)}"
            : $"ALTER TABLE {_session.Qualify(_table)} DROP COLUMN {_session.Quote(name)}";
    }

    private string RenameColumnSql(string oldName, string newName)
    {
        string table = _session.Qualify(_table);
        return _session.Kind switch
        {
            DbKind.MySQL => $"ALTER TABLE {table} RENAME COLUMN {_session.Quote(oldName)} TO {_session.Quote(newName)}",
            DbKind.SqlServer => $"EXEC sp_rename N'{EscapeSql(SqlServerObjectName())}.{EscapeSql(oldName)}', N'{EscapeSql(newName)}', 'COLUMN'",
            _ => $"ALTER TABLE {table} RENAME COLUMN {_session.Quote(oldName)} TO {_session.Quote(newName)}"
        };
    }

    private string ModifyColumnSql(ColumnEdit column, ColumnEdit old)
    {
        string table = _session.Qualify(_table);
        switch (_session.Kind)
        {
            case DbKind.MySQL:
                return $"ALTER TABLE {table} MODIFY COLUMN {ColumnDefinition(column, true)}";
            case DbKind.PostgreSQL:
            {
                var list = new List<string>();
                if (!string.Equals(column.Type, old.Type, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add($"ALTER TABLE {table} ALTER COLUMN {_session.Quote(column.Name)} TYPE {column.Type}");
                }
                if (column.NotNull != old.NotNull)
                {
                    list.Add($"ALTER TABLE {table} ALTER COLUMN {_session.Quote(column.Name)} "
                        + (column.NotNull ? "SET NOT NULL" : "DROP NOT NULL"));
                }
                string newDefault = column.DefaultValue?.Trim() ?? "";
                string oldDefault = old.DefaultValue?.Trim() ?? "";
                if (!string.Equals(newDefault, oldDefault, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add($"ALTER TABLE {table} ALTER COLUMN {_session.Quote(column.Name)} "
                        + (newDefault.Length == 0 ? "DROP DEFAULT" : "SET DEFAULT " + newDefault));
                }
                if (column.AutoIncrement != old.AutoIncrement)
                {
                    list.Add(column.AutoIncrement
                        ? $"ALTER TABLE {table} ALTER COLUMN {_session.Quote(column.Name)} ADD GENERATED BY DEFAULT AS IDENTITY"
                        : $"ALTER TABLE {table} ALTER COLUMN {_session.Quote(column.Name)} DROP IDENTITY");
                }
                if (!string.Equals(column.Comment ?? "", old.Comment ?? "", StringComparison.Ordinal))
                {
                    list.Add($"COMMENT ON COLUMN {table}.{_session.Quote(column.Name)} IS "
                        + (string.IsNullOrEmpty(column.Comment) ? "NULL" : "'" + column.Comment.Replace("'", "''") + "'"));
                }
                return string.Join(";" + Environment.NewLine, list);
            }
            case DbKind.SqlServer:
            {
                string nullText = column.NotNull ? " NOT NULL" : " NULL";
                string sql = $"ALTER TABLE {table} ALTER COLUMN {_session.Quote(column.Name)} {column.Type}{nullText}";
                string newDefault = column.DefaultValue?.Trim() ?? "";
                string oldDefault = old.DefaultValue?.Trim() ?? "";
                if (!string.Equals(newDefault, oldDefault, StringComparison.OrdinalIgnoreCase))
                {
                    sql += Environment.NewLine + "-- 注意：SQL Server 修改默认值需先删除原默认约束，此处未自动执行";
                }
                return sql;
            }
            case DbKind.Oracle:
            {
                var sb = new StringBuilder($"ALTER TABLE {table} MODIFY ({_session.Quote(column.Name)} {column.Type}");
                if (!string.IsNullOrWhiteSpace(column.DefaultValue) && !column.AutoIncrement)
                {
                    sb.Append(" DEFAULT ").Append(column.DefaultValue.Trim());
                }
                if (column.AutoIncrement != old.AutoIncrement)
                {
                    sb.Append(column.AutoIncrement
                        ? " GENERATED BY DEFAULT AS IDENTITY"
                        : " DROP IDENTITY");
                }
                if (column.NotNull)
                {
                    sb.Append(" NOT NULL");
                }
                else if (old.NotNull)
                {
                    sb.Append(" NULL");
                }
                sb.Append(')');
                return sb.ToString();
            }
            default:
                throw new NotSupportedException("SQLite 不支持直接修改列类型/约束，请新建表后复制数据。");
        }
    }

    private string DropPrimaryKeySql()
    {
        string table = _session.Qualify(_table);
        return _session.Kind switch
        {
            DbKind.MySQL => $"ALTER TABLE {table} DROP PRIMARY KEY",
            DbKind.SqlServer or DbKind.PostgreSQL or DbKind.Oracle
                => string.IsNullOrEmpty(_primaryKeyName)
                    ? $"ALTER TABLE {table} DROP PRIMARY KEY"
                    : $"ALTER TABLE {table} DROP CONSTRAINT {_session.Quote(_primaryKeyName)}",
            _ => throw new NotSupportedException("SQLite 不支持直接删除主键，请重建表。")
        };
    }

    private string AddPrimaryKeySql(List<ColumnEdit> columns)
    {
        string table = _session.Qualify(_table);
        var cols = columns.Where(c => c.PrimaryKey).Select(c => _session.Quote(c.Name)).ToList();
        return $"ALTER TABLE {table} ADD PRIMARY KEY ({string.Join(", ", cols)})";
    }

    private string CreateIndexSql(IndexEdit index)
    {
        string table = _session.Qualify(_table);
        var cols = SplitColumns(index.Columns).Select(_session.Quote);
        return $"CREATE {(index.Unique ? "UNIQUE " : "")}INDEX {_session.Quote(index.Name)} ON {table} ("
            + string.Join(", ", cols) + ")";
    }

    private string DropIndexSql(string name)
    {
        string table = _session.Qualify(_table);
        return _session.Kind switch
        {
            DbKind.MySQL => $"DROP INDEX {_session.Quote(name)} ON {table}",
            DbKind.Oracle => $"DROP INDEX {_session.Quote(name)}",
            _ => $"DROP INDEX {_session.Quote(name)}"
        };
    }

    private string AddForeignKeySql(FkEdit fk)
    {
        string table = _session.Qualify(_table);
        var cols = SplitColumns(fk.Columns).Select(_session.Quote);
        var refCols = SplitColumns(fk.RefColumns).Select(_session.Quote);
        string sql = $"ALTER TABLE {table} ADD CONSTRAINT {_session.Quote(fk.Name)} FOREIGN KEY ("
            + string.Join(", ", cols) + ") REFERENCES " + fk.RefTable + " (" + string.Join(", ", refCols) + ")";
        if (!string.IsNullOrWhiteSpace(fk.OnDelete))
        {
            sql += " ON DELETE " + fk.OnDelete;
        }
        if (!string.IsNullOrWhiteSpace(fk.OnUpdate))
        {
            sql += " ON UPDATE " + fk.OnUpdate;
        }
        return sql;
    }

    private string DropForeignKeySql(string name)
    {
        string table = _session.Qualify(_table);
        return _session.Kind switch
        {
            DbKind.MySQL => $"ALTER TABLE {table} DROP FOREIGN KEY {_session.Quote(name)}",
            _ => $"ALTER TABLE {table} DROP CONSTRAINT {_session.Quote(name)}"
        };
    }

    private string SqlServerObjectName()
    {
        return string.IsNullOrWhiteSpace(_table.Schema)
            ? _table.Name
            : _table.Schema + "." + _table.Name;
    }

    private string RenameTableSql(string newName)
    {
        string oldTable = _session.Qualify(_table);
        return _session.Kind switch
        {
            DbKind.MySQL => "RENAME TABLE " + oldTable + " TO " + _session.Quote(newName),
            DbKind.SqlServer => $"EXEC sp_rename N'{EscapeSql(SqlServerObjectName())}', N'{EscapeSql(newName)}'",
            _ => $"ALTER TABLE {oldTable} RENAME TO {_session.Quote(newName)}"
        };
    }

    private static string EscapeSql(string text) => text.Replace("'", "''");

    private List<string> BuildAlterStatements(List<ColumnEdit> columns, List<IndexEdit> indexes,
        List<FkEdit> fks, string newName)
    {
        if (_session.Kind == DbKind.SQLite)
        {
            bool modifies = columns.Any(c =>
            {
                var old = FindOld(c.OriginalName);
                if (old == null)
                {
                    return false;
                }
                return !string.Equals(old.Type, c.Type, StringComparison.OrdinalIgnoreCase)
                    || old.NotNull != c.NotNull
                    || !string.Equals(old.DefaultValue ?? "", c.DefaultValue ?? "", StringComparison.OrdinalIgnoreCase)
                    || old.AutoIncrement != c.AutoIncrement;
            });
            if (modifies)
            {
                throw new NotSupportedException(
                    "SQLite 不支持直接修改列类型/非空/默认值。请新建表，使用“数据传输”复制数据后重命名。");
            }
        }

        var statements = new List<string>();
        string table = _session.Qualify(_table);
        var newByOriginal = columns.Where(c => !string.IsNullOrEmpty(c.OriginalName))
            .GroupBy(c => c.OriginalName!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 删除外键 / 索引
        foreach (var oldFk in _originalForeignKeys)
        {
            var current = fks.FirstOrDefault(f => f.OriginalName == oldFk.OriginalName);
            if (current == null || !current.SameAs(oldFk))
            {
                if (_session.Kind == DbKind.SQLite)
                {
                    throw new NotSupportedException("SQLite 不支持直接修改外键，请重建表。");
                }
                statements.Add(DropForeignKeySql(oldFk.Name));
            }
        }
        foreach (var oldIndex in _originalIndexes)
        {
            var current = indexes.FirstOrDefault(i => i.OriginalName == oldIndex.OriginalName);
            if (current == null || !current.SameAs(oldIndex))
            {
                statements.Add(DropIndexSql(oldIndex.Name));
            }
        }

        // 重命名列
        foreach (var column in columns)
        {
            var old = FindOld(column.OriginalName);
            if (old != null && !string.Equals(old.Name, column.Name, StringComparison.Ordinal))
            {
                statements.Add(RenameColumnSql(old.Name, column.Name));
            }
        }

        // 删除列
        foreach (var old in _originalColumns)
        {
            if (!newByOriginal.ContainsKey(old.OriginalName ?? ""))
            {
                statements.Add(DropColumnSql(old.Name));
            }
        }

        // 主键变化
        var oldPk = _originalColumns.Where(c => c.PrimaryKey).Select(c => c.Name).ToList();
        var newPk = columns.Where(c => c.PrimaryKey).Select(c => c.Name).ToList();
        bool pkChanged = !oldPk.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(newPk.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        if (pkChanged && oldPk.Count > 0)
        {
            statements.Add(DropPrimaryKeySql());
        }

        // 新增列
        foreach (var column in columns.Where(c => string.IsNullOrEmpty(c.OriginalName)))
        {
            statements.Add(AddColumnSql(column));
        }

        // 修改列
        foreach (var column in columns)
        {
            var old = FindOld(column.OriginalName);
            if (old == null)
            {
                continue;
            }
            bool changed = !string.Equals(old.Type, column.Type, StringComparison.OrdinalIgnoreCase)
                || old.NotNull != column.NotNull
                || old.AutoIncrement != column.AutoIncrement
                || !string.Equals(old.DefaultValue ?? "", column.DefaultValue ?? "", StringComparison.OrdinalIgnoreCase)
                || (_session.Kind != DbKind.SQLite
                    && !string.Equals(old.Comment ?? "", column.Comment ?? "", StringComparison.Ordinal));
            if (changed)
            {
                statements.Add(ModifyColumnSql(column, old));
            }
        }

        if (pkChanged && newPk.Count > 0)
        {
            statements.Add(AddPrimaryKeySql(columns));
        }

        // 新增索引
        foreach (var index in indexes)
        {
            var old = FindOldIndex(index.OriginalName);
            if (old == null || !index.SameAs(old))
            {
                statements.Add(CreateIndexSql(index));
            }
        }

        // 新增外键
        foreach (var fk in fks)
        {
            var old = FindOldFk(fk.OriginalName);
            if (old == null || !fk.SameAs(old))
            {
                if (_session.Kind == DbKind.SQLite)
                {
                    throw new NotSupportedException("SQLite 不支持直接新增外键，请重建表。");
                }
                statements.Add(AddForeignKeySql(fk));
            }
        }

        if (!string.Equals(_table.Name, newName, StringComparison.Ordinal))
        {
            statements.Add(RenameTableSql(newName));
        }
        return statements.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private void UpdateDdlPreviewFromDatabase()
    {
        try
        {
            _ddlBox.Text = _session.GetTableDdl(_table);
        }
        catch (Exception ex)
        {
            _ddlBox.Text = "-- " + ex.Message;
        }
    }

    public bool TryClose()
    {
        if (!_dirty)
        {
            return true;
        }
        return MessageBox.Show(FindForm(),
            "表结构已修改但尚未保存，关闭将丢弃这些修改。\n确定关闭吗？",
            "关闭表设计器", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
}
