using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>SQL 查询标签页：编辑器 + 结果网格 + 消息，支持事务、导出、保存脚本。</summary>
internal sealed class QueryTab : UserControl, NvClosable
{
    private readonly DbSession _session;
    private readonly NvSqlEditor _editor;
    private readonly TabControl _resultTabs;
    private readonly RichTextBox _messages;
    private readonly ToolStrip _toolbar;
    private readonly ToolStripButton _runButton;
    private readonly ToolStripButton _runSelectionButton;
    private readonly ToolStripButton _stopButton;
    private readonly ToolStripButton _commitButton;
    private readonly ToolStripButton _rollbackButton;
    private readonly ToolStripButton _transactionButton;
    private readonly ToolStripComboBox _limitBox;
    private readonly Label _statusLeft;
    private readonly Label _statusRight;
    private readonly SplitContainer _split;
    private CancellationTokenSource? _cts;
    private string? _filePath;
    private bool _dirty;
    private bool _busy;
    private DataGridView? _currentGrid;
    private DataTable? _currentTable;

    public event EventHandler? TitleChanged;
    public event EventHandler? DirtyChanged;

    public QueryTab(DbSession session, string? initialSql = null)
    {
        _session = session;
        BackColor = NvTheme.Background;

        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 30,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.Professional,
            Padding = new Padding(4, 2, 4, 2)
        };
        NvTheme.ApplyToolStrip(_toolbar);

        _runButton = ToolButton("执行", NvIcon.Run, "执行全部 SQL (F5)");
        _runSelectionButton = ToolButton("执行选中", NvIcon.Run, "执行选中的 SQL (Ctrl+Enter)");
        _runSelectionButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _stopButton = ToolButton("停止", NvIcon.Stop, "停止执行");
        _stopButton.Enabled = false;
        var formatButton = ToolButton("格式化", NvIcon.Format, "格式化 SQL");
        var openButton = ToolButton("打开", NvIcon.Open, "打开 SQL 文件");
        openButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        var saveButton = ToolButton("保存", NvIcon.Save, "保存 SQL 文件 (Ctrl+S)");
        _transactionButton = ToolButton("开启事务", NvIcon.Lock, "开启手动事务");
        _transactionButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _commitButton = ToolButton("提交", NvIcon.Check, "提交事务");
        _commitButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _commitButton.Enabled = false;
        _rollbackButton = ToolButton("回滚", NvIcon.Refresh, "回滚事务");
        _rollbackButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _rollbackButton.Enabled = false;
        var exportButton = ToolButton("导出结果", NvIcon.Export, "导出当前结果集");
        exportButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        var clearButton = ToolButton("清空", NvIcon.Truncate, "清空编辑器");
        clearButton.DisplayStyle = ToolStripItemDisplayStyle.Text;

        _limitBox = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 88,
            Font = NvTheme.Ui(9f)
        };
        _limitBox.Items.AddRange(new object[] { "1000 行", "5000 行", "10000 行", "50000 行", "全部" });
        _limitBox.SelectedIndex = 2;

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            _runButton, _runSelectionButton, _stopButton, new ToolStripSeparator(),
            formatButton, new ToolStripSeparator(),
            openButton, saveButton, new ToolStripSeparator(),
            _transactionButton, _commitButton, _rollbackButton, new ToolStripSeparator(),
            exportButton, clearButton, new ToolStripSeparator(),
            new ToolStripLabel("最大结果") { ForeColor = NvTheme.Muted }, _limitBox
        });

        _runButton.Click += (_, _) => Execute(false);
        _runSelectionButton.Click += (_, _) => Execute(true);
        _stopButton.Click += (_, _) => CancelExecution();
        formatButton.Click += (_, _) => FormatSql();
        openButton.Click += (_, _) => OpenFile();
        saveButton.Click += (_, _) => SaveFile();
        _transactionButton.Click += (_, _) => ToggleTransaction();
        _commitButton.Click += (_, _) => CommitTransaction();
        _rollbackButton.Click += (_, _) => RollbackTransaction();
        exportButton.Click += (_, _) => ExportCurrent();
        clearButton.Click += (_, _) =>
        {
            if (_editor.Text.Length > 0 && NvPrompt.Confirm(this, "清空", "确定清空当前 SQL 编辑器吗？"))
            {
                _editor.Clear();
            }
        };

        _editor = new NvSqlEditor { Dock = DockStyle.Fill };
        if (!string.IsNullOrEmpty(initialSql))
        {
            _editor.Text = initialSql;
        }
        _editor.TextChangedByUser += (_, _) => MarkDirty(true);

        _resultTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = NvTheme.Ui(9f),
            Padding = new Point(14, 4)
        };

        var resultPage = new TabPage("结果") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        _currentGrid = CreateGrid();
        resultPage.Controls.Add(_currentGrid);
        _resultTabs.TabPages.Add(resultPage);

        var messagePage = new TabPage("消息") { BackColor = NvTheme.Panel, Padding = new Padding(0) };
        _messages = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Font = NvTheme.Mono(9.5f),
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            DetectUrls = false,
            WordWrap = false
        };
        messagePage.Controls.Add(_messages);
        _resultTabs.TabPages.Add(messagePage);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 5,
            BackColor = NvTheme.BorderLight
        };
        _split.Panel1.Controls.Add(_editor);
        _split.Panel2.Controls.Add(_resultTabs);

        var status = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            BackColor = NvTheme.HeaderBack
        };
        status.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, 0, status.Width, 0);
        };
        _statusLeft = new Label
        {
            Text = "就绪",
            AutoSize = false,
            Location = new Point(8, 2),
            Size = new Size(520, 20),
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _statusRight = new Label
        {
            Text = _session.Profile.Name + " · " + _session.CurrentDatabase,
            AutoSize = false,
            Location = new Point(ClientSize.Width - 360, 2),
            Size = new Size(350, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleRight
        };
        status.Controls.Add(_statusLeft);
        status.Controls.Add(_statusRight);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 0, 2, 2) };
        content.Controls.Add(_split);
        Controls.Add(content);
        Controls.Add(_toolbar);
        Controls.Add(status);

        Load += (_, _) =>
        {
            try
            {
                _split.Panel1MinSize = 100;
                _split.Panel2MinSize = 120;
                _split.SplitterDistance = Math.Max(120, Height / 3);
            }
            catch
            {
                // ignore
            }
        };
        _editor.InnerBox.KeyDown += EditorKeyDown;
        LoadSuggestions();
    }

    public string TabTitle
    {
        get
        {
            string name = _filePath != null ? Path.GetFileName(_filePath) : "查询";
            return name;
        }
    }

    public bool IsDirty => _dirty;
    public DbSession Session => _session;

    public void FormatSqlFromMenu() => FormatSql();

    public void OpenFileFromMenu() => OpenFile();

    public void FocusEditor() => _editor.FocusEditor();

    public bool FindText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        string content = _editor.Text;
        int start = _editor.SelectionStart + _editor.SelectionLength;
        int index = content.IndexOf(text, Math.Min(start, content.Length), StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = content.IndexOf(text, 0, StringComparison.OrdinalIgnoreCase);
        }
        if (index < 0)
        {
            return false;
        }
        _editor.Select(index, text.Length);
        _editor.FocusEditor();
        return true;
    }

    private static ToolStripButton ToolButton(string text, NvIcon icon, string tip)
    {
        return new ToolStripButton(text, NvIcons.Get(icon, 16))
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ToolTipText = tip,
            Font = NvTheme.Ui(9f),
            Padding = new Padding(3, 0, 3, 0),
            AutoSize = true
        };
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            VirtualMode = false,
            RowHeadersWidth = 52
        };
        NvTheme.StyleGrid(grid, true);
        grid.RowHeadersVisible = true;
        grid.RowHeadersWidth = 52;
        grid.RowPostPaint += (_, e) =>
        {
            if (e.RowIndex < 0)
            {
                return;
            }
            var text = (e.RowIndex + 1).ToString();
            TextRenderer.DrawText(e.Graphics, text, NvTheme.Ui(8.5f),
                new Rectangle(e.RowBounds.Left, e.RowBounds.Top, grid.RowHeadersWidth - 4, e.RowBounds.Height),
                NvTheme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        };
        grid.CellFormatting += (_, e) =>
        {
            if (e.Value is byte[] bytes)
            {
                e.Value = bytes.Length <= 32
                    ? "0x" + Convert.ToHexString(bytes)
                    : "0x" + Convert.ToHexString(bytes.AsSpan(0, 32)) + $"… ({bytes.Length} 字节)";
                e.FormattingApplied = true;
            }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("复制单元格", null, (_, _) => CopySelection(grid, false));
        menu.Items.Add("复制选中行", null, (_, _) => CopySelection(grid, true));
        menu.Items.Add("复制全部", null, (_, _) => CopyAll(grid));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导出结果…", null, (_, _) =>
        {
            if (grid.DataSource is DataTable table)
            {
                NvExportService.ExportTableWithDialog(grid.FindForm(), table, "query_result",
                    DbKind.MySQL, false);
            }
        });
        grid.ContextMenuStrip = menu;
        return grid;
    }

    private static void CopySelection(DataGridView grid, bool rows)
    {
        if (grid.CurrentCell == null)
        {
            return;
        }
        var sb = new StringBuilder();
        if (rows)
        {
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }
                var values = new List<string>();
                foreach (DataGridViewCell cell in row.Cells)
                {
                    values.Add(cell.FormattedValue?.ToString() ?? "");
                }
                sb.AppendLine(string.Join("\t", values));
            }
        }
        else
        {
            foreach (DataGridViewCell cell in grid.SelectedCells)
            {
                sb.AppendLine(cell.FormattedValue?.ToString() ?? "");
            }
        }
        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString().TrimEnd());
        }
    }

    private static void CopyAll(DataGridView grid)
    {
        if (grid.DataSource is not DataTable table)
        {
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in table.Rows)
        {
            sb.AppendLine(string.Join("\t", row.ItemArray.Select(v => v == DBNull.Value ? "NULL" : Convert.ToString(v))));
        }
        Clipboard.SetText(sb.ToString());
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            Execute(false);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Enter)
        {
            Execute(true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            SaveFile();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.O)
        {
            OpenFile();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private async void LoadSuggestions()
    {
        try
        {
            var words = await Task.Run(() =>
            {
                var result = new List<string>();
                try
                {
                    foreach (var table in _session.GetTables(_session.CurrentDatabase))
                    {
                        result.Add(table.Name);
                        foreach (var column in _session.GetColumns(table).Take(80))
                        {
                            result.Add(column.Name);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
                return result;
            });
            if (!IsDisposed)
            {
                _editor.SetDynamicWords(words);
            }
        }
        catch
        {
            // ignore
        }
    }

    private int MaxRows
    {
        get
        {
            return _limitBox.SelectedIndex switch
            {
                0 => 1000,
                1 => 5000,
                2 => 10000,
                3 => 50000,
                _ => 200000
            };
        }
    }

    private async void Execute(bool selectedOnly)
    {
        if (_busy)
        {
            return;
        }
        string sql = selectedOnly ? _editor.SelectedText : _editor.Text;
        if (string.IsNullOrWhiteSpace(sql))
        {
            sql = _editor.Text;
        }
        if (string.IsNullOrWhiteSpace(sql))
        {
            NvPrompt.Info(this, "执行 SQL", "请先输入要执行的 SQL 语句。");
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        NvLog.Debug("执行 SQL：" + NvLog.Truncate(sql, 1600));
        AppendMessage("-- " + DateTime.Now.ToString("HH:mm:ss") + " 开始执行 --");
        AppendMessage(sql.Length > 2000 ? sql[..2000] + "\n…" : sql);
        try
        {
            var token = _cts.Token;
            var result = await Task.Run(() => _session.ExecuteBatch(sql, 300, MaxRows, token), token);
            Populate(result);
        }
        catch (OperationCanceledException)
        {
            _statusLeft.Text = "已停止执行";
            AppendMessage("-- 用户已停止执行 --");
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "查询执行失败：" + NvLog.Truncate(sql, 1600));
            _statusLeft.ForeColor = NvTheme.Red;
            _statusLeft.Text = "执行失败";
            AppendMessage("错误：" + ex.Message);
            _resultTabs.SelectedIndex = Math.Min(1, _resultTabs.TabCount - 1);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Populate(BatchResult result)
    {
        while (_resultTabs.TabPages.Count > 2)
        {
            var page = _resultTabs.TabPages[^1];
            _resultTabs.TabPages.Remove(page);
            page.Dispose();
        }

        int gridIndex = 0;
        long totalRows = 0;
        int selectCount = 0;
        foreach (var set in result.ResultSets)
        {
            if (set.HasGrid)
            {
                selectCount++;
                totalRows += set.Rows.Count;
                var table = NvDataFile.ToDataTable(set);
                var grid = gridIndex == 0 ? _currentGrid! : CreateGrid();
                if (gridIndex == 0)
                {
                    _currentGrid!.DataSource = null;
                    _currentGrid.DataSource = table;
                    ConfigureGrid(_currentGrid);
                    _currentTable = table;
                    _resultTabs.TabPages[0].Text = "结果";
                }
                else
                {
                    grid.DataSource = table;
                    ConfigureGrid(grid);
                    var page = new TabPage("结果 " + (gridIndex + 1)) { BackColor = NvTheme.Panel };
                    page.Controls.Add(grid);
                    _resultTabs.TabPages.Add(page);
                }
                AppendMessage($"结果集 {selectCount}：{set.Columns.Count} 列，{set.Rows.Count} 行");
                gridIndex++;
            }
            else
            {
                AppendMessage(set.Message ?? ("影响 " + set.AffectedRows + " 行"));
            }
        }

        if (gridIndex == 0)
        {
            _currentGrid!.DataSource = null;
            _currentTable = null;
            _resultTabs.TabPages[0].Text = "结果";
        }

        _resultTabs.SelectedIndex = gridIndex > 0 ? 0 : Math.Min(1, _resultTabs.TabCount - 1);
        _statusLeft.ForeColor = NvTheme.Muted;
        _statusLeft.Text = $"完成 · {selectCount} 个结果集 · {totalRows} 行 · {result.Elapsed.TotalMilliseconds:0} ms";
        AppendMessage($"-- 执行完成，用时 {result.Elapsed.TotalMilliseconds:0} ms --");
        _dirty = true;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.Width > 320)
            {
                column.Width = 320;
            }
            if (column.Width < 70)
            {
                column.Width = 70;
            }
        }
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }

    private void AppendMessage(string text)
    {
        _messages.AppendText(text + Environment.NewLine);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _runButton.Enabled = !busy;
        _runSelectionButton.Enabled = !busy;
        _stopButton.Enabled = busy;
        _toolbar.Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        if (busy)
        {
            _statusLeft.ForeColor = NvTheme.Muted;
            _statusLeft.Text = "正在执行…";
        }
        UpdateTransactionButtons();
    }

    private void CancelExecution()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }
    }

    private void FormatSql()
    {
        string text = _editor.SelectedText.Length > 0 ? _editor.SelectedText : _editor.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        string formatted = NvSqlUtil.Format(text);
        if (_editor.SelectedText.Length > 0)
        {
            _editor.InnerBox.SelectedText = formatted;
        }
        else
        {
            int start = _editor.SelectionStart;
            _editor.Text = formatted;
            _editor.Select(Math.Min(start, formatted.Length), 0);
        }
    }

    private void MarkDirty(bool dirty)
    {
        if (_dirty == dirty)
        {
            return;
        }
        _dirty = dirty;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveFile()
    {
        string path = _filePath ?? "";
        if (string.IsNullOrEmpty(path))
        {
            using var dialog = new SaveFileDialog
            {
                Title = "保存 SQL 文件",
                Filter = "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*",
                FileName = "query_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".sql"
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }
            path = dialog.FileName;
        }
        try
        {
            File.WriteAllText(path, _editor.Text, new UTF8Encoding(false));
            _filePath = path;
            MarkDirty(false);
            TitleChanged?.Invoke(this, EventArgs.Empty);
            _statusLeft.Text = "已保存：" + path;
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：保存失败");
            NvPrompt.Error(FindForm(), "保存失败", ex.Message);
        }
    }

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开 SQL 文件",
            Filter = "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
            _editor.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            _filePath = dialog.FileName;
            MarkDirty(false);
            TitleChanged?.Invoke(this, EventArgs.Empty);
            _statusLeft.Text = "已打开：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：打开失败");
            NvPrompt.Error(FindForm(), "打开失败", ex.Message);
        }
    }

    private void ToggleTransaction()
    {
        if (_session.HasTransaction)
        {
            RollbackTransaction();
            return;
        }
        try
        {
            _session.BeginTransaction();
            _transactionButton.Text = "事务中";
            AppendMessage("-- 已开启手动事务 --");
            UpdateTransactionButtons();
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：事务");
            NvPrompt.Error(FindForm(), "事务", ex.Message);
        }
    }

    private void CommitTransaction()
    {
        try
        {
            _session.Commit();
            AppendMessage("-- 事务已提交 --");
            _statusLeft.Text = "事务已提交";
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：提交失败");
            NvPrompt.Error(FindForm(), "提交失败", ex.Message);
        }
        _transactionButton.Text = "开启事务";
        UpdateTransactionButtons();
    }

    private void RollbackTransaction()
    {
        try
        {
            _session.Rollback();
            AppendMessage("-- 事务已回滚 --");
            _statusLeft.Text = "事务已回滚";
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：回滚失败");
            NvPrompt.Error(FindForm(), "回滚失败", ex.Message);
        }
        _transactionButton.Text = "开启事务";
        UpdateTransactionButtons();
    }

    private void UpdateTransactionButtons()
    {
        bool has = _session.HasTransaction;
        _commitButton.Enabled = has && !_busy;
        _rollbackButton.Enabled = has && !_busy;
        _transactionButton.Text = has ? "回滚事务" : "开启事务";
    }

    private void ExportCurrent()
    {
        if (_currentTable == null)
        {
            NvPrompt.Info(FindForm(), "导出结果", "当前没有可导出的结果集。");
            return;
        }
        NvExportService.ExportTableWithDialog(FindForm(), _currentTable, "query_result", _session.Kind, true);
    }

    public bool TryClose()
    {
        if (_busy)
        {
            if (!NvPrompt.Confirm(FindForm(), "关闭查询", "查询正在执行，确定停止并关闭吗？", true))
            {
                return false;
            }
            CancelExecution();
        }
        if (_session.HasTransaction)
        {
            var choice = MessageBox.Show(FindForm(),
                "当前查询存在未提交的事务，关闭前是否回滚？",
                "关闭查询", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel)
            {
                return false;
            }
            if (choice == DialogResult.Yes)
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
        }
        if (_dirty)
        {
            var choice = MessageBox.Show(FindForm(),
                "SQL 脚本已修改，是否保存？",
                "关闭查询", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel)
            {
                return false;
            }
            if (choice == DialogResult.Yes)
            {
                SaveFile();
                if (_dirty)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
