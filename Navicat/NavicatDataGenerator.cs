using System.Data;
using System.Globalization;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>数据生成器：按列生成测试数据（Navicat Premium 数据生成功能的本地实现）。</summary>
internal static class NvFakeData
{
    private static readonly string[] FirstNames = { "张", "王", "李", "赵", "刘", "陈", "杨", "黄", "周", "吴", "徐", "孙" };
    private static readonly string[] GivenNames =
    {
        "伟", "芳", "娜", "敏", "静", "丽", "强", "磊", "军", "洋", "勇", "艳", "杰", "娟", "涛", "明",
        "超", "秀英", "霞", "平", "刚", "桂英", "文", "辉", "建华", "晓明", "雪", "婷", "鹏", "宇"
    };
    private static readonly string[] Domains = { "example.com", "test.com", "mail.com", "demo.cn", "corp.local" };
    private static readonly string[] Cities = { "北京", "上海", "广州", "深圳", "杭州", "成都", "武汉", "南京", "西安", "重庆" };
    private static readonly string[] Words =
    {
        "alpha", "beta", "gamma", "delta", "nova", "cloud", "data", "smart", "green", "fast",
        "light", "river", "stone", "ocean", "pixel", "logic", "matrix", "orbit", "prime", "wave"
    };

    public static readonly string[] Generators =
    {
        "自动", "随机整数", "随机小数", "随机字符串", "姓名", "邮箱", "手机号", "城市", "地址",
        "日期时间", "日期", "UUID", "布尔值", "顺序号", "固定值", "NULL"
    };

    public static object? Generate(string generator, ColumnInfo column, int rowIndex, string parameter,
        Random random)
    {
        switch (generator)
        {
            case "随机整数":
                return ConvertValue(random.Next(1, 100000), column);
            case "随机小数":
                return ConvertValue(Math.Round(random.NextDouble() * 10000, 2), column);
            case "随机字符串":
                int length = int.TryParse(parameter, out int parsed) ? Math.Clamp(parsed, 1, 200) : 8;
                return RandomString(length, random);
            case "姓名":
                return FirstNames[random.Next(FirstNames.Length)] + GivenNames[random.Next(GivenNames.Length)];
            case "邮箱":
                return RandomString(6, random).ToLowerInvariant() + "@" + Domains[random.Next(Domains.Length)];
            case "手机号":
                return "1" + random.Next(3, 10) + random.Next(100000000, 999999999).ToString();
            case "城市":
                return Cities[random.Next(Cities.Length)];
            case "地址":
                return Cities[random.Next(Cities.Length)] + "市" + RandomString(4, random) + "路"
                    + random.Next(1, 999) + "号";
            case "日期时间":
                return DateTime.Now.AddSeconds(-random.Next(0, 365 * 24 * 3600));
            case "日期":
                return DateTime.Today.AddDays(-random.Next(0, 3650));
            case "UUID":
                return Guid.NewGuid().ToString();
            case "布尔值":
                return random.Next(2) == 0;
            case "顺序号":
                return ConvertValue(rowIndex + 1, column);
            case "固定值":
                return ConvertValue(parameter, column);
            case "NULL":
                return DBNull.Value;
            default:
                return AutoValue(column, rowIndex, random);
        }
    }

    public static string DefaultGenerator(ColumnInfo column)
    {
        if (column.AutoIncrement)
        {
            return "自动";
        }
        string type = column.DataType.ToLowerInvariant();
        if (type.Contains("int")) return "随机整数";
        if (type.Contains("decimal") || type.Contains("numeric") || type.Contains("number")
            || type.Contains("float") || type.Contains("double")) return "随机小数";
        if (type.Contains("date") || type.Contains("time")) return "日期时间";
        if (type.Contains("bool") || type == "bit") return "布尔值";
        if (type.Contains("uuid")) return "UUID";
        if (!column.Nullable) return "随机字符串";
        return "随机字符串";
    }

    private static object? AutoValue(ColumnInfo column, int rowIndex, Random random)
    {
        if (column.AutoIncrement)
        {
            return DBNull.Value;
        }
        string type = column.DataType.ToLowerInvariant();
        if (type.Contains("int")) return random.Next(1, 100000);
        if (type.Contains("decimal") || type.Contains("numeric") || type.Contains("number"))
        {
            return Math.Round(random.NextDouble() * 10000, 2);
        }
        if (type.Contains("float") || type.Contains("double")) return Math.Round(random.NextDouble() * 10000, 2);
        if (type.Contains("bool") || type == "bit") return random.Next(2) == 0;
        if (type.Contains("date") || type.Contains("time")) return DateTime.Now.AddDays(-random.Next(0, 365));
        return RandomString(8, random) + rowIndex;
    }

    private static object? ConvertValue(object value, ColumnInfo column)
    {
        return NvSyncUtil.ConvertForTarget(value, column);
    }

    private static string RandomString(int length, Random random)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)(random.Next(2) == 0
                ? 'a' + random.Next(26)
                : '0' + random.Next(10));
        }
        return new string(chars);
    }
}

internal sealed class NvDataGeneratorForm : Form
{
    private readonly DbSession _session;
    private readonly DataGridView _columnsGrid;
    private readonly DataGridView _previewGrid;
    private readonly ComboBox _tableBox;
    private readonly NumericUpDown _rowCount;
    private readonly CheckBox _truncate;
    private readonly RichTextBox _log;
    private readonly ProgressBar _progress;
    private readonly Button _generateButton;
    private readonly Button _previewButton;
    private readonly Label _status;
    private List<TableInfo> _tables = new();
    private List<ColumnInfo> _columns = new();
    private readonly Random _random = new();

    public NvDataGeneratorForm(DbSession session, TableInfo? preset = null)
    {
        _session = session;
        Text = "数据生成 - " + session.Profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 680);
        MinimumSize = new Size(780, 580);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("数据生成", "按列生成测试数据；支持随机整数 / 字符串 / 姓名 / 邮箱 / 日期 / UUID 等",
            NvIcon.Add);

        var top = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(14, 8, 14, 0) };
        top.Controls.Add(new Label
        {
            Text = "目标表",
            Location = new Point(4, 18),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _tableBox = new ComboBox
        {
            Location = new Point(68, 14),
            Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9.2f)
        };
        _tableBox.SelectedIndexChanged += async (_, _) => await LoadColumnsAsync();
        top.Controls.Add(_tableBox);
        top.Controls.Add(new Label
        {
            Text = "生成行数",
            Location = new Point(350, 18),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _rowCount = new NumericUpDown
        {
            Location = new Point(420, 14),
            Width = 100,
            Minimum = 1,
            Maximum = 1000000,
            Value = 100,
            Font = NvTheme.Ui(9.2f)
        };
        top.Controls.Add(_rowCount);
        _truncate = new CheckBox
        {
            Text = "生成前清空表",
            Location = new Point(540, 16),
            AutoSize = true,
            ForeColor = NvTheme.Text
        };
        top.Controls.Add(_truncate);
        _status = new Label
        {
            Text = "正在读取表…",
            Location = new Point(4, 52),
            Size = new Size(840, 22),
            ForeColor = NvTheme.Muted
        };
        top.Controls.Add(_status);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = NvTheme.BorderLight
        };
        var columnPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        columnPanel.Controls.Add(new Label
        {
            Text = "列生成规则",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _columnsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        NvTheme.StyleGrid(_columnsGrid, false);
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "列名",
            ReadOnly = true,
            FillWeight = 90
        });
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Type",
            HeaderText = "类型",
            ReadOnly = true,
            FillWeight = 100
        });
        var generatorColumn = new DataGridViewComboBoxColumn
        {
            Name = "Generator",
            HeaderText = "生成器",
            FillWeight = 120,
            FlatStyle = FlatStyle.Flat
        };
        generatorColumn.Items.AddRange(NvFakeData.Generators);
        _columnsGrid.Columns.Add(generatorColumn);
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Parameter",
            HeaderText = "参数 / 固定值",
            FillWeight = 130
        });
        columnPanel.Controls.Add(_columnsGrid);
        columnPanel.Controls.SetChildIndex(_columnsGrid, 0);
        split.Panel1.Controls.Add(columnPanel);

        var previewPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 8) };
        previewPanel.Controls.Add(new Label
        {
            Text = "数据预览（前 50 行，红色仅为预览）",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = NvTheme.Text
        });
        _previewGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            ReadOnly = true,
            AutoGenerateColumns = true
        };
        NvTheme.StyleGrid(_previewGrid, true);
        _previewGrid.DataError += (_, e) => e.ThrowException = false;
        previewPanel.Controls.Add(_previewGrid);
        previewPanel.Controls.SetChildIndex(_previewGrid, 0);
        split.Panel2.Controls.Add(previewPanel);
        Load += (_, _) =>
        {
            try
            {
                split.Panel1MinSize = 160;
                split.Panel2MinSize = 160;
                split.SplitterDistance = Math.Max(160, (Height - 200) / 2);
            }
            catch
            {
                // ignore
            }
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = NvTheme.Background };
        _progress = new ProgressBar
        {
            Location = new Point(14, 12),
            Width = 560,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false
        };
        _log = new RichTextBox
        {
            Location = new Point(14, 36),
            Size = new Size(560, 46),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = NvTheme.Mono(8.6f),
            BackColor = NvTheme.Background,
            ForeColor = NvTheme.Muted,
            WordWrap = false
        };
        _previewButton = NvTheme.FlatButton("预览", null, null, 88);
        _previewButton.Location = new Point(600, 12);
        _previewButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _previewButton.Click += (_, _) => PreviewData();
        _generateButton = NvTheme.FlatButton("生成数据", NvTheme.Accent, Color.White, 100);
        _generateButton.Location = new Point(696, 12);
        _generateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _generateButton.Click += async (_, _) => await GenerateAsync();
        var close = NvTheme.FlatButton("关闭", null, null, 88);
        close.Location = new Point(804, 12);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _progress, _log, _previewButton, _generateButton, close });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        body.Controls.Add(split);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(header);
        CancelButton = close;
        Load += async (_, _) => await LoadTablesAsync(preset);
    }

    private async Task LoadTablesAsync(TableInfo? preset)
    {
        try
        {
            _tables = await Task.Run(() => _session.GetTables(_session.CurrentDatabase));
            _tableBox.Items.Clear();
            int selected = 0;
            for (int i = 0; i < _tables.Count; i++)
            {
                _tableBox.Items.Add(_tables[i].DisplayName);
                if (preset != null && _tables[i].Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                }
            }
            if (_tableBox.Items.Count > 0)
            {
                _tableBox.SelectedIndex = selected;
            }
        }
        catch (Exception ex)
        {
            _status.Text = "读取表失败：" + ex.Message;
        }
    }

    private async Task LoadColumnsAsync()
    {
        if (_tableBox.SelectedIndex < 0 || _tableBox.SelectedIndex >= _tables.Count)
        {
            return;
        }
        var table = _tables[_tableBox.SelectedIndex];
        _status.Text = "正在读取列…";
        try
        {
            _columns = await Task.Run(() => _session.GetColumns(table));
            _columnsGrid.Rows.Clear();
            foreach (var column in _columns)
            {
                int index = _columnsGrid.Rows.Add(column.Name, column.DisplayType,
                    NvFakeData.DefaultGenerator(column), "");
                _columnsGrid.Rows[index].Tag = column;
            }
            _status.Text = table.DisplayName + " · " + _columns.Count + " 列";
        }
        catch (Exception ex)
        {
            _status.Text = "读取列失败：" + ex.Message;
        }
    }

    private List<(ColumnInfo Column, string Generator, string Parameter)> ReadRules()
    {
        var list = new List<(ColumnInfo, string, string)>();
        foreach (DataGridViewRow row in _columnsGrid.Rows)
        {
            if (row.Tag is not ColumnInfo column)
            {
                continue;
            }
            string generator = Convert.ToString(row.Cells["Generator"].Value) ?? "自动";
            string parameter = Convert.ToString(row.Cells["Parameter"].Value) ?? "";
            if (generator == "自动" && column.AutoIncrement)
            {
                continue;
            }
            if (generator == "NULL" && !column.Nullable && !column.AutoIncrement)
            {
                continue;
            }
            list.Add((column, generator, parameter));
        }
        return list;
    }

    private DataTable BuildPreview(int count)
    {
        var table = new DataTable();
        var rules = ReadRules();
        foreach (var rule in rules)
        {
            table.Columns.Add(rule.Column.Name, typeof(object));
        }
        for (int r = 0; r < count; r++)
        {
            var values = new object?[rules.Count];
            for (int c = 0; c < rules.Count; c++)
            {
                values[c] = NvFakeData.Generate(rules[c].Generator, rules[c].Column, r, rules[c].Parameter, _random);
            }
            table.Rows.Add(values);
        }
        return table;
    }

    private void PreviewData()
    {
        try
        {
            var preview = BuildPreview(50);
            _previewGrid.DataSource = null;
            _previewGrid.DataSource = preview;
            foreach (DataGridViewColumn column in _previewGrid.Columns)
            {
                column.Width = 120;
            }
            _log.Text = "已生成 50 行预览（未写入数据库）。";
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：数据生成");
            NvPrompt.Error(this, "数据生成", ex.Message);
        }
    }

    private async Task GenerateAsync()
    {
        if (_tableBox.SelectedIndex < 0 || _tableBox.SelectedIndex >= _tables.Count)
        {
            NvPrompt.Error(this, "数据生成", "请选择目标表。");
            return;
        }
        var target = _tables[_tableBox.SelectedIndex];
        var rules = ReadRules();
        if (rules.Count == 0)
        {
            NvPrompt.Error(this, "数据生成", "没有可生成的列。");
            return;
        }
        int count = (int)_rowCount.Value;
        if (!NvPrompt.Confirm(this, "生成数据",
            $"将向 {target.DisplayName} 插入 {count} 行测试数据，是否继续？", false))
        {
            return;
        }
        _generateButton.Enabled = false;
        _previewButton.Enabled = false;
        _progress.Visible = true;
        _log.Clear();
        _log.AppendText("开始生成 " + count + " 行…" + Environment.NewLine);
        try
        {
            string tableName = _session.Qualify(target);
            var columns = rules;
            string columnList = string.Join(", ", columns.Select(c => _session.Quote(c.Column.Name)));
            string parameterList = string.Join(", ", columns.Select((_, i) => _session.Param("p" + i)));
            string insertSql = "INSERT INTO " + tableName + " (" + columnList + ") VALUES ("
                + parameterList + ")";
            bool truncate = _truncate.Checked;
            int inserted = 0;
            await Task.Run(() =>
            {
                bool ownTransaction = !_session.HasTransaction;
                if (ownTransaction)
                {
                    _session.BeginTransaction();
                }
                try
                {
                    if (truncate)
                    {
                        string sql = _session.Kind == DbKind.SQLite
                            ? "DELETE FROM " + tableName
                            : "TRUNCATE TABLE " + tableName;
                        try
                        {
                            _session.Execute(sql, 900);
                        }
                        catch
                        {
                            _session.Execute("DELETE FROM " + tableName, 900);
                        }
                    }
                    for (int r = 0; r < count; r++)
                    {
                        var parameters = new Dictionary<string, object?>();
                        for (int i = 0; i < columns.Count; i++)
                        {
                            parameters[_session.Param("p" + i)] = NvFakeData.Generate(
                                columns[i].Generator, columns[i].Column, r, columns[i].Parameter, _random);
                        }
                        _session.ExecuteWithParams(insertSql, parameters, 900);
                        inserted++;
                        if (inserted % 500 == 0)
                        {
                            int snapshot = inserted;
                            try
                            {
                                BeginInvoke(new Action(() =>
                                    _log.AppendText("已生成 " + snapshot + " 行…" + Environment.NewLine)));
                            }
                            catch
                            {
                                // 窗口已关闭
                            }
                        }
                    }
                    if (ownTransaction)
                    {
                        _session.Commit();
                    }
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
            });
            _log.AppendText("完成，共写入 " + inserted + " 行。" + Environment.NewLine);
            _status.Text = "生成完成，共 " + inserted + " 行。";
            NvPrompt.Info(this, "数据生成", "成功生成 " + inserted + " 行测试数据。");
        }
        catch (Exception ex)
        {
            _log.AppendText("失败：" + ex.Message + Environment.NewLine);
            NvLog.Error(ex, "工具异常：数据生成");
            NvPrompt.Error(this, "数据生成", ex.Message);
        }
        finally
        {
            _generateButton.Enabled = true;
            _previewButton.Enabled = true;
            _progress.Visible = false;
        }
    }
}
