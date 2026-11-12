using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>导入向导：CSV / TXT / XLSX / JSON 导入到现有表或新表。</summary>
internal sealed class NvImportDialog : Form
{
    private readonly DbSession _session;
    private readonly TableInfo? _presetTable;
    private readonly Func<Task>? _onCompleted;
    private readonly TextBox _fileBox;
    private readonly ComboBox _formatBox;
    private readonly CheckBox _headerCheck;
    private readonly ComboBox _tableBox;
    private readonly CheckBox _createCheck;
    private readonly TextBox _newTableName;
    private readonly CheckBox _truncateCheck;
    private readonly DataGridView _preview;
    private readonly DataGridView _mapping;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Button _importButton;
    private readonly List<TableInfo> _tables = new();
    private List<ColumnInfo> _targetColumns = new();
    private DataTable? _source;
    private bool _busy;

    public static void ShowForTable(IWin32Window? owner, DbSession session, TableInfo table, Func<Task> refresh)
    {
        using var dialog = new NvImportDialog(session, table, refresh);
        dialog.ShowDialog(owner);
    }

    public static void ShowForDatabase(IWin32Window? owner, DbSession session, Func<Task>? refresh = null)
    {
        using var dialog = new NvImportDialog(session, null, refresh);
        dialog.ShowDialog(owner);
    }

    private NvImportDialog(DbSession session, TableInfo? presetTable, Func<Task>? onCompleted)
    {
        _session = session;
        _presetTable = presetTable;
        _onCompleted = onCompleted;

        Text = "导入向导";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 660);
        MinimumSize = new Size(760, 560);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.Font;

        var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = NvTheme.HeaderBack };
        header.Controls.Add(new Label
        {
            Text = "导入数据",
            Location = new Point(20, 10),
            AutoSize = true,
            Font = NvTheme.Ui(13f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        });
        header.Controls.Add(new Label
        {
            Text = "支持 CSV / TXT / Excel (XLSX) / JSON，自动匹配列并可创建新表。",
            Location = new Point(22, 38),
            AutoSize = true,
            ForeColor = NvTheme.Muted
        });
        header.Controls.Add(new PictureBox
        {
            Image = NvIcons.Get(NvIcon.Import, 30),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(820, 16),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        });

        var top = new Panel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(16, 10, 16, 0) };
        top.Controls.Add(new Label { Text = "源文件", Location = new Point(4, 14), AutoSize = true, ForeColor = NvTheme.Text });
        _fileBox = new TextBox { Location = new Point(76, 10), Width = 660, Font = NvTheme.Ui(9.2f) };
        _fileBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var browse = NvTheme.FlatButton("浏览…", null, null, 74);
        browse.Location = new Point(744, 9);
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += (_, _) => BrowseFile();
        top.Controls.AddRange(new Control[] { _fileBox, browse });

        top.Controls.Add(new Label { Text = "格式", Location = new Point(4, 50), AutoSize = true, ForeColor = NvTheme.Text });
        _formatBox = new ComboBox
        {
            Location = new Point(76, 46),
            Width = 140,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        _formatBox.Items.AddRange(new[] { "CSV", "TXT (制表符)", "Excel XLSX", "JSON" });
        _formatBox.SelectedIndex = 0;
        _formatBox.SelectedIndexChanged += (_, _) => ReloadFile();
        top.Controls.Add(_formatBox);
        _headerCheck = new CheckBox
        {
            Text = "首行作为列名",
            Location = new Point(234, 48),
            AutoSize = true,
            Checked = true,
            ForeColor = NvTheme.Text
        };
        _headerCheck.CheckedChanged += (_, _) => ReloadFile();
        top.Controls.Add(_headerCheck);

        top.Controls.Add(new Label { Text = "目标表", Location = new Point(4, 86), AutoSize = true, ForeColor = NvTheme.Text });
        _tableBox = new ComboBox
        {
            Location = new Point(76, 82),
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        _tableBox.SelectedIndexChanged += async (_, _) => await LoadTargetColumnsAsync();
        top.Controls.Add(_tableBox);
        _createCheck = new CheckBox
        {
            Text = "创建新表",
            Location = new Point(390, 84),
            AutoSize = true,
            ForeColor = NvTheme.Text
        };
        _createCheck.CheckedChanged += (_, _) => ApplyCreateMode();
        top.Controls.Add(_createCheck);
        _newTableName = new TextBox
        {
            Location = new Point(478, 82),
            Width = 180,
            Font = NvTheme.Ui(9.2f),
            Enabled = false
        };
        top.Controls.Add(_newTableName);
        _truncateCheck = new CheckBox
        {
            Text = "导入前清空目标表",
            Location = new Point(676, 84),
            AutoSize = true,
            ForeColor = NvTheme.Text
        };
        top.Controls.Add(_truncateCheck);
        top.Controls.Add(new Label
        {
            Text = "提示：导入会按目标列类型自动转换数字、日期和布尔值；空字符串导入为 NULL。",
            Location = new Point(4, 118),
            Size = new Size(760, 24),
            ForeColor = NvTheme.Muted
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var previewPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        previewPanel.Controls.Add(new Label
        {
            Text = "源数据预览（前 100 行）",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _preview = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, ReadOnly = true };
        NvTheme.StyleGrid(_preview, true);
        previewPanel.Controls.Add(_preview);
        previewPanel.Controls.SetChildIndex(_preview, 0);

        var mapPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        mapPanel.Controls.Add(new Label
        {
            Text = "列映射（可修改目标列，选择“忽略”不导入该列）",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _mapping = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        NvTheme.StyleGrid(_mapping, false);
        _mapping.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Source",
            HeaderText = "源列",
            ReadOnly = true,
            FillWeight = 45
        });
        var targetColumn = new DataGridViewComboBoxColumn
        {
            Name = "Target",
            HeaderText = "目标列",
            FillWeight = 55,
            FlatStyle = FlatStyle.Flat
        };
        _mapping.Columns.Add(targetColumn);
        mapPanel.Controls.Add(_mapping);
        mapPanel.Controls.SetChildIndex(_mapping, 0);

        split.Panel1.Controls.Add(previewPanel);
        split.Panel2.Controls.Add(mapPanel);
        Load += (_, _) =>
        {
            try
            {
                split.SplitterDistance = Math.Max(150, (Height - 260) / 2);
            }
            catch
            {
                // ignore
            }
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 86, BackColor = NvTheme.Background };
        _progress = new ProgressBar
        {
            Location = new Point(16, 10),
            Width = 620,
            Height = 18,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _status = new Label
        {
            Text = "请选择源文件。",
            Location = new Point(16, 34),
            Size = new Size(700, 38),
            ForeColor = NvTheme.Muted,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _importButton = NvTheme.FlatButton("开始导入", NvTheme.Accent, Color.White, 104);
        _importButton.Location = new Point(666, 10);
        _importButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _importButton.Click += async (_, _) => await ImportAsync();
        var cancel = NvTheme.FlatButton("关闭", null, null, 88);
        cancel.Location = new Point(778, 10);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cancel.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _progress, _status, _importButton, cancel });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 4, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(top);
        Controls.Add(bottom);
        Controls.Add(header);
        CancelButton = cancel;

        LoadTables();
    }

    private void LoadTables()
    {
        try
        {
            _tables.Clear();
            _tables.AddRange(_session.GetTables(_session.CurrentDatabase));
            _tableBox.BeginUpdate();
            _tableBox.Items.Clear();
            foreach (var table in _tables)
            {
                _tableBox.Items.Add(table.DisplayName);
            }
            _tableBox.EndUpdate();
            if (_presetTable != null)
            {
                int index = _tables.FindIndex(t => t.Name.Equals(_presetTable.Name, StringComparison.OrdinalIgnoreCase)
                    && t.Schema.Equals(_presetTable.Schema, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _tableBox.SelectedIndex = index;
                }
            }
            else if (_tables.Count > 0)
            {
                _tableBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            _status.Text = "读取表列表失败：" + ex.Message;
        }
    }

    private void BrowseFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择导入文件",
            Filter = "数据文件 (*.csv;*.txt;*.xlsx;*.json)|*.csv;*.txt;*.xlsx;*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _fileBox.Text = dialog.FileName;
        string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        _formatBox.SelectedIndex = ext switch
        {
            ".xlsx" => 2,
            ".json" => 3,
            ".txt" => 1,
            _ => 0
        };
        ReloadFile();
    }

    private void ReloadFile()
    {
        string path = _fileBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }
        try
        {
            DataTable raw = _formatBox.SelectedIndex switch
            {
                1 => NvDataFile.ReadCsv(path, '\t', _headerCheck.Checked),
                2 => ReadExcel(path, _headerCheck.Checked),
                3 => ReadJson(path),
                _ => NvDataFile.ReadCsv(path, null, _headerCheck.Checked)
            };
            _source?.Dispose();
            _source = raw;
            _preview.DataSource = null;
            _preview.DataSource = raw;
            foreach (DataGridViewColumn column in _preview.Columns)
            {
                column.Width = 120;
            }
            _status.ForeColor = NvTheme.Muted;
            _status.Text = $"已读取 {raw.Rows.Count} 行 × {raw.Columns.Count} 列。";
            if (_createCheck.Checked && string.IsNullOrWhiteSpace(_newTableName.Text))
            {
                _newTableName.Text = Path.GetFileNameWithoutExtension(path);
            }
            BuildMapping();
        }
        catch (Exception ex)
        {
            _status.ForeColor = NvTheme.Red;
            _status.Text = "读取失败：" + ex.Message;
        }
    }

    private static DataTable ReadExcel(string path, bool header)
    {
        var raw = NvDataFile.ReadXlsx(path);
        if (!header || raw.Rows.Count == 0)
        {
            return raw;
        }
        var table = new DataTable();
        for (int c = 0; c < raw.Columns.Count; c++)
        {
            string name = Convert.ToString(raw.Rows[0][c]) ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Column" + (c + 1);
            }
            string unique = name;
            int n = 1;
            while (table.Columns.Contains(unique))
            {
                unique = name + "_" + n++;
            }
            table.Columns.Add(unique, typeof(string));
        }
        for (int r = 1; r < raw.Rows.Count; r++)
        {
            var values = new object?[raw.Columns.Count];
            for (int c = 0; c < raw.Columns.Count; c++)
            {
                values[c] = raw.Rows[r][c];
            }
            table.Rows.Add(values);
        }
        return table;
    }

    private static DataTable ReadJson(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        using var doc = JsonDocument.Parse(text);
        var table = new DataTable();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("JSON 顶层必须是数组，例如 [{\"id\":1}]");
        }
        var rows = new List<Dictionary<string, string>>();
        var columns = new List<string>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!columns.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    columns.Add(property.Name);
                }
                row[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => "",
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    _ => property.Value.GetRawText()
                };
            }
            rows.Add(row);
        }
        foreach (string column in columns)
        {
            table.Columns.Add(column, typeof(string));
        }
        foreach (var row in rows)
        {
            var values = new object?[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                values[i] = row.TryGetValue(columns[i], out string? value) ? value : "";
            }
            table.Rows.Add(values);
        }
        return table;
    }

    private void ApplyCreateMode()
    {
        bool create = _createCheck.Checked;
        _tableBox.Enabled = !create;
        _newTableName.Enabled = create;
        if (create && string.IsNullOrWhiteSpace(_newTableName.Text) && !string.IsNullOrWhiteSpace(_fileBox.Text))
        {
            _newTableName.Text = Path.GetFileNameWithoutExtension(_fileBox.Text);
        }
        BuildMapping();
    }

    private async Task LoadTargetColumnsAsync()
    {
        if (_createCheck.Checked || _tableBox.SelectedIndex < 0 || _tableBox.SelectedIndex >= _tables.Count)
        {
            BuildMapping();
            return;
        }
        var table = _tables[_tableBox.SelectedIndex];
        try
        {
            var columns = await Task.Run(() => _session.GetColumns(table));
            _targetColumns = columns;
        }
        catch
        {
            _targetColumns = new List<ColumnInfo>();
        }
        BuildMapping();
    }

    private void BuildMapping()
    {
        _mapping.Rows.Clear();
        if (_source == null)
        {
            return;
        }
        var combo = (DataGridViewComboBoxColumn)_mapping.Columns[1];
        combo.Items.Clear();
        combo.Items.Add("（忽略）");
        if (_createCheck.Checked)
        {
            foreach (DataColumn column in _source.Columns)
            {
                combo.Items.Add(column.ColumnName);
            }
        }
        else
        {
            foreach (var column in _targetColumns)
            {
                combo.Items.Add(column.Name);
            }
        }
        foreach (DataColumn sourceColumn in _source.Columns)
        {
            int index = _mapping.Rows.Add();
            _mapping.Rows[index].Cells[0].Value = sourceColumn.ColumnName;
            string match = _createCheck.Checked ? sourceColumn.ColumnName : _targetColumns
                .FirstOrDefault(c => c.Name.Equals(sourceColumn.ColumnName, StringComparison.OrdinalIgnoreCase))?.Name ?? "（忽略）";
            _mapping.Rows[index].Cells[1].Value = match;
        }
    }

    private async Task ImportAsync()
    {
        if (_busy || _source == null)
        {
            NvPrompt.Error(this, "导入", "请先选择有效的源文件。");
            return;
        }
        var mappings = new List<(string Source, string Target)>();
        foreach (DataGridViewRow row in _mapping.Rows)
        {
            string source = Convert.ToString(row.Cells[0].Value) ?? "";
            string target = Convert.ToString(row.Cells[1].Value) ?? "";
            if (!string.IsNullOrEmpty(target) && target != "（忽略）")
            {
                mappings.Add((source, target));
            }
        }
        if (mappings.Count == 0)
        {
            NvPrompt.Error(this, "导入", "请至少映射一列。");
            return;
        }

        TableInfo targetTable;
        bool create = _createCheck.Checked;
        if (create)
        {
            string name = _newTableName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                NvPrompt.Error(this, "导入", "请填写新表名称。");
                return;
            }
            targetTable = new TableInfo
            {
                Database = _session.CurrentDatabase,
                Schema = _session.Kind is DbKind.PostgreSQL ? "public" : "",
                Name = name
            };
        }
        else
        {
            if (_tableBox.SelectedIndex < 0 || _tableBox.SelectedIndex >= _tables.Count)
            {
                NvPrompt.Error(this, "导入", "请选择目标表。");
                return;
            }
            targetTable = _tables[_tableBox.SelectedIndex];
        }

        _busy = true;
        _importButton.Enabled = false;
        _progress.Maximum = Math.Max(1, _source.Rows.Count);
        _progress.Value = 0;
        _status.ForeColor = NvTheme.Muted;
        _status.Text = "正在导入…";
        try
        {
            var source = _source;
            int total = source.Rows.Count;
            var progress = new Progress<int>(value =>
            {
                if (IsDisposed)
                {
                    return;
                }
                _progress.Value = Math.Min(_progress.Maximum, value);
                _status.Text = $"正在导入 {value} / {total} 行…";
            });
            int imported = await Task.Run(() => DoImport(source, mappings, targetTable, create, progress));
            _status.ForeColor = NvTheme.Green;
            _status.Text = $"导入完成，共写入 {imported} 行。";
            _progress.Value = Math.Min(_progress.Maximum, Math.Max(1, imported));
            if (_onCompleted != null)
            {
                await _onCompleted();
            }
            NvPrompt.Info(this, "导入完成", $"成功导入 {imported} 行到 {targetTable.DisplayName}。");
        }
        catch (Exception ex)
        {
            _status.ForeColor = NvTheme.Red;
            _status.Text = "导入失败：" + ex.Message;
            NvLog.Error(ex, "工具异常：导入失败");
            NvPrompt.Error(this, "导入失败", ex.Message);
        }
        finally
        {
            _busy = false;
            _importButton.Enabled = true;
        }
    }

    private int DoImport(DataTable source, List<(string Source, string Target)> mappings,
        TableInfo targetTable, bool create, IProgress<int> progress)
    {
        if (create)
        {
            var columns = new List<ColumnInfo>();
            foreach (var mapping in mappings)
            {
                string code = InferType(source, mapping.Source);
                var (dataType, fullType) = MapCreateType(_session.Kind, code);
                columns.Add(new ColumnInfo
                {
                    Name = mapping.Target,
                    DataType = dataType,
                    FullType = fullType,
                    Nullable = true,
                    Ordinal = columns.Count + 1
                });
            }
            string ddl = _session.BuildCreateTable(targetTable, columns, new List<IndexInfo>(),
                new List<ForeignKeyInfo>());
            _session.ExecuteScript(ddl);
            _targetColumns = _session.GetColumns(targetTable);
        }
        else
        {
            _targetColumns = _session.GetColumns(targetTable);
        }

        var targetMap = mappings.Select(m => new
        {
            m.Source,
            Column = _targetColumns.FirstOrDefault(c =>
                c.Name.Equals(m.Target, StringComparison.OrdinalIgnoreCase))
        }).Where(m => m.Column != null).ToList();
        if (targetMap.Count == 0)
        {
            throw new InvalidOperationException("目标列不存在，请检查列映射。");
        }

        bool ownTransaction = !_session.HasTransaction;
        if (ownTransaction)
        {
            _session.BeginTransaction();
        }
        int imported = 0;
        try
        {
            if (_truncateCheck.Checked)
            {
                string sql = _session.Kind switch
                {
                    DbKind.SQLite => "DELETE FROM " + _session.Qualify(targetTable),
                    _ => "TRUNCATE TABLE " + _session.Qualify(targetTable)
                };
                _session.Execute(sql);
            }
            foreach (DataRow row in source.Rows)
            {
                var columns = new List<string>();
                var parameters = new Dictionary<string, object?>();
                int index = 0;
                foreach (var mapping in targetMap)
                {
                    var column = mapping.Column!;
                    if (column.AutoIncrement)
                    {
                        continue;
                    }
                    columns.Add(_session.Quote(column.Name));
                    string parameter = _session.Param("p" + index++);
                    string raw = Convert.ToString(row[mapping.Source], CultureInfo.InvariantCulture) ?? "";
                    parameters[parameter] = ConvertValue(raw, column, imported + 1);
                }
                if (columns.Count == 0)
                {
                    continue;
                }
                string sql = "INSERT INTO " + _session.Qualify(targetTable) + " ("
                    + string.Join(", ", columns) + ") VALUES (" + string.Join(", ", parameters.Keys) + ")";
                _session.ExecuteWithParams(sql, parameters);
                imported++;
                if (imported % 50 == 0 || imported == source.Rows.Count)
                {
                    progress.Report(imported);
                }
            }
            if (ownTransaction)
            {
                _session.Commit();
            }
            return imported;
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

    private static string InferType(DataTable table, string columnName)
    {
        bool any = false;
        bool allInt = true;
        bool allDecimal = true;
        bool allDate = true;
        foreach (DataRow row in table.Rows.Cast<DataRow>().Take(200))
        {
            string value = Convert.ToString(row[columnName]) ?? "";
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            any = true;
            if (!long.TryParse(value, out _))
            {
                allInt = false;
            }
            if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            {
                allDecimal = false;
            }
            if (!DateTime.TryParse(value, out _))
            {
                allDate = false;
            }
        }
        if (!any)
        {
            return "text";
        }
        if (allInt)
        {
            return "int";
        }
        if (allDecimal)
        {
            return "decimal";
        }
        if (allDate)
        {
            return "datetime";
        }
        return "text";
    }

    private static (string DataType, string FullType) MapCreateType(DbKind kind, string code)
    {
        return kind switch
        {
            DbKind.MySQL => code switch
            {
                "int" => ("bigint", "BIGINT"),
                "decimal" => ("decimal", "DECIMAL(24,6)"),
                "datetime" => ("datetime", "DATETIME"),
                _ => ("text", "LONGTEXT")
            },
            DbKind.PostgreSQL => code switch
            {
                "int" => ("bigint", "BIGINT"),
                "decimal" => ("numeric", "NUMERIC(24,6)"),
                "datetime" => ("timestamp", "TIMESTAMP"),
                _ => ("text", "TEXT")
            },
            DbKind.SqlServer => code switch
            {
                "int" => ("bigint", "BIGINT"),
                "decimal" => ("decimal", "DECIMAL(24,6)"),
                "datetime" => ("datetime2", "DATETIME2"),
                _ => ("nvarchar", "NVARCHAR(MAX)")
            },
            DbKind.Oracle => code switch
            {
                "int" => ("number", "NUMBER(19)"),
                "decimal" => ("number", "NUMBER(24,6)"),
                "datetime" => ("date", "DATE"),
                _ => ("clob", "CLOB")
            },
            _ => code switch
            {
                "int" => ("integer", "INTEGER"),
                "decimal" => ("numeric", "NUMERIC"),
                "datetime" => ("datetime", "DATETIME"),
                _ => ("text", "TEXT")
            }
        };
    }

    private static object ConvertValue(string raw, ColumnInfo column, int rowNumber)
    {
        if (raw.Length == 0)
        {
            return DBNull.Value;
        }
        string type = column.DataType.ToLowerInvariant();
        try
        {
            if (type.Contains("int") || type is "serial" or "bigserial" or "smallserial")
            {
                return long.Parse(raw, CultureInfo.InvariantCulture);
            }
            if (type.Contains("decimal") || type.Contains("numeric") || type == "number" || type.Contains("money"))
            {
                return decimal.Parse(raw, NumberStyles.Any, CultureInfo.InvariantCulture);
            }
            if (type.Contains("double") || type.Contains("float") || type == "real")
            {
                return double.Parse(raw, NumberStyles.Any, CultureInfo.InvariantCulture);
            }
            if (type.Contains("bool") || type == "bit")
            {
                return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || raw.Equals("y", StringComparison.OrdinalIgnoreCase);
            }
            if (type.Contains("date") || type.Contains("time"))
            {
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                {
                    return dt;
                }
            }
            if (column.IsBinary)
            {
                if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.FromHexString(raw[2..]);
                }
                try
                {
                    return Convert.FromBase64String(raw);
                }
                catch
                {
                    return Encoding.UTF8.GetBytes(raw);
                }
            }
        }
        catch (Exception)
        {
            throw new FormatException($"第 {rowNumber} 行，列 {column.Name} 的值“{raw}”无法转换为 {column.DisplayType}。");
        }
        return raw;
    }
}
