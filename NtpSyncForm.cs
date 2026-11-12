using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// NTP 同步管理窗口：显示自动同步开关、服务器、上次同步结果和最近 100 条记录。
/// 默认不打开；仅在右键菜单点击“NTP 同步管理”时显示，后台同步服务始终独立运行。
/// </summary>
public sealed class NtpSyncForm : Form
{
    private readonly NtpSyncManager _manager;
    private readonly NtpHistoryStore _historyStore;
    private readonly AppConfig _config;
    private readonly Panel _header;
    private readonly ToggleSwitch _autoToggle;
    private readonly Label _autoStatus;
    private readonly Label _autoHint;
    private readonly Label _lastSyncLabel;
    private readonly Label _detailLabel;
    private readonly Label _serverLabel;
    private readonly Label _currentTimeLabel;
    private readonly Label _statusLabel;
    private readonly PillButton _syncNowButton;
    private readonly DataGridView _grid;
    private readonly Label _emptyLabel;
    private readonly Label _historyCountLabel;
    private readonly System.Windows.Forms.Timer _clockTimer;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private readonly List<NtpSyncRecord> _records = new();
    private readonly Action<NtpSyncRecord> _onSyncCompleted;
    private bool _closed;
    private bool _syncing;

    public NtpSyncForm(NtpSyncManager manager, NtpHistoryStore historyStore, AppConfig config)
    {
        _manager = manager;
        _historyStore = historyStore;
        _config = config;
        _onSyncCompleted = OnSyncCompleted;

        Text = "NTP 同步管理 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 700);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(900, 104),
            BackColor = Color.FromArgb(15, 118, 110)
        };
        _header.Paint += PaintHeader;
        _header.MouseMove += (_, e) =>
        {
            bool hover = CloseRect.Contains(e.Location);
            if (hover != _closeHover)
            {
                _closeHover = hover;
                _header.Cursor = hover ? Cursors.Hand : Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseLeave += (_, _) =>
        {
            if (_closeHover)
            {
                _closeHover = false;
                _header.Cursor = Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseClick += (_, e) =>
        {
            if (CloseRect.Contains(e.Location))
            {
                Close();
            }
        };
        NativeWindowDrag.Attach(this, _header, p => CloseRect.Contains(p));
        Controls.Add(_header);

        // ---------- 自动同步卡片 ----------
        var autoCard = new CardPanel
        {
            Location = new Point(24, 116),
            Size = new Size(852, 74)
        };
        _autoToggle = new ToggleSwitch
        {
            Location = new Point(20, 22),
            Size = new Size(56, 30),
            OnColor = ShutdownTheme.Accent,
            Checked = _config.NtpAutoSync
        };
        var autoTitle = MakeLabel("自动同步（每 5 分钟）", 90, 12, 460, 26,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _autoHint = MakeLabel(
            _config.NtpAutoSync
                ? "默认开启；每 5 分钟自动校准本机时间"
                : "已关闭；仅可手动点击“立即同步”",
            92, 40, 560, 22,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _autoStatus = new Label
        {
            Text = _config.NtpAutoSync ? "已开启" : "已关闭",
            Location = new Point(autoCard.Width - 112, 22),
            Size = new Size(92, 28),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = _config.NtpAutoSync ? ShutdownTheme.Success : ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        autoCard.Controls.AddRange(new Control[] { _autoToggle, autoTitle, _autoHint, _autoStatus });
        autoCard.BorderColor = _config.NtpAutoSync ? ShutdownTheme.Accent : ShutdownTheme.CardBorder;
        Controls.Add(autoCard);

        // ---------- 同步状态卡片 ----------
        var statusCard = new CardPanel
        {
            Location = new Point(24, 202),
            Size = new Size(852, 150)
        };
        var statusTitle = MakeLabel("同步状态", 18, 12, 300, 24,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _lastSyncLabel = MakeLabel("暂无同步记录", 18, 36, 530, 22,
            new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _detailLabel = MakeLabel("后台自动同步服务已启动", 18, 60, 530, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _serverLabel = MakeLabel(NtpSyncManager.ServerSummary, 18, 84, 530, 20,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _currentTimeLabel = new Label
        {
            Text = "本机时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Location = new Point(560, 12),
            Size = new Size(274, 24),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Accent,
            BackColor = Color.White,
            AutoSize = false
        };
        _syncNowButton = new PillButton
        {
            Text = "立即同步",
            ButtonStyle = PillButtonStyle.Primary,
            Location = new Point(560, 46),
            Size = new Size(274, 40),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            BackColor = Color.White
        };
        _syncNowButton.Click += (_, _) => _ = SyncNowFromUi();
        _statusLabel = new Label
        {
            Text = _config.NtpAutoSync ? "自动同步已开启" : "自动同步已关闭",
            Location = new Point(560, 94),
            Size = new Size(274, 20),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        statusCard.Controls.AddRange(new Control[]
        {
            statusTitle, _lastSyncLabel, _detailLabel, _serverLabel,
            _currentTimeLabel, _syncNowButton, _statusLabel
        });
        Controls.Add(statusCard);

        _autoToggle.CheckedChanged += (_, _) =>
        {
            bool enabled = _autoToggle.Checked;
            _config.NtpAutoSync = enabled;
            _config.Save();
            _manager.SetAutoSync(enabled);
            _autoStatus.Text = enabled ? "已开启" : "已关闭";
            _autoStatus.ForeColor = enabled ? ShutdownTheme.Success : ShutdownTheme.Muted;
            _autoHint.Text = enabled
                ? "默认开启；每 5 分钟自动校准本机时间"
                : "已关闭；仅可手动点击“立即同步”";
            autoCard.BorderColor = enabled ? ShutdownTheme.Accent : ShutdownTheme.CardBorder;
            autoCard.Invalidate();
            _statusLabel.Text = enabled ? "自动同步已开启，等待下一次同步…" : "自动同步已关闭";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
        };

        // ---------- 历史记录卡片 ----------
        var historyCard = new CardPanel
        {
            Location = new Point(24, 364),
            Size = new Size(852, 300)
        };
        var historyTitle = MakeLabel("同步记录", 18, 12, 300, 24,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _historyCountLabel = new Label
        {
            Text = "共 0 条",
            Location = new Point(600, 14),
            Size = new Size(234, 20),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };

        _grid = new DataGridView
        {
            Location = new Point(14, 44),
            Size = new Size(824, 242),
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Color.White,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(226, 230, 234),
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            VirtualMode = true,
            RowTemplate = { Height = 26 },
            ColumnHeadersHeight = 34,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            DefaultCellStyle =
            {
                Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = Color.FromArgb(40, 46, 58),
                SelectionBackColor = Color.FromArgb(204, 251, 241),
                SelectionForeColor = Color.FromArgb(15, 70, 60),
                Padding = new Padding(6, 0, 6, 0)
            },
            AlternatingRowsDefaultCellStyle =
            {
                BackColor = Color.FromArgb(247, 249, 252)
            },
            ColumnHeadersDefaultCellStyle =
            {
                BackColor = Color.FromArgb(20, 120, 112),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                SelectionBackColor = Color.FromArgb(20, 120, 112),
                Padding = new Padding(6, 0, 0, 0)
            }
        };
        EnableDoubleBuffer(_grid);
        AddGridColumns();
        _grid.CellValueNeeded += GridCellValueNeeded;
        _grid.CellFormatting += GridCellFormatting;
        _grid.CellToolTipTextNeeded += GridCellToolTip;

        _emptyLabel = new Label
        {
            Text = "暂无同步记录\n自动同步开启后，每次校时结果会显示在这里。",
            Location = new Point(14, 84),
            Size = new Size(824, 190),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White
        };
        historyCard.Controls.AddRange(new Control[]
        {
            historyTitle, _historyCountLabel, _grid, _emptyLabel
        });
        Controls.Add(historyCard);
        _emptyLabel.BringToFront();

        _records.AddRange(_historyStore.Snapshot());
        _grid.RowCount = _records.Count;
        UpdateHistoryMeta();
        UpdateSummaryFromLatest();

        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) => OnClockTick();
        _clockTimer.Start();

        _manager.SyncCompleted += _onSyncCompleted;
        Shown += (_, _) => ReloadHistoryFromStore();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    private bool _closeHover;

    private static void EnableDoubleBuffer(DataGridView grid)
    {
        typeof(DataGridView)
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(grid, true);
    }

    private static Label MakeLabel(string text, int x, int y, int width, int height,
        Font font, Color color, Color backColor)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = font,
            ForeColor = color,
            BackColor = backColor,
            AutoSize = false
        };
    }

    private void AddGridColumns()
    {
        var columns = new[]
        {
            ("时间", 150),
            ("结果", 62),
            ("NTP 服务器", 180),
            ("偏差 (ms)", 90),
            ("延时 (ms)", 90)
        };
        foreach (var (header, width) in columns)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
        var messageColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "说明",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 200,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _grid.Columns.Add(messageColumn);
    }

    private void GridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _records.Count)
        {
            return;
        }
        NtpSyncRecord record = _records[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            0 => record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            1 => record.Success ? "成功" : "失败",
            2 => record.Server,
            3 => record.Success ? FormatOffset(record.OffsetMs) : "—",
            4 => record.Success ? record.DelayMs.ToString("0.0", CultureInfo.InvariantCulture) : "—",
            5 => record.Message.Replace('\n', ' '),
            _ => ""
        };
    }

    private void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _records.Count)
        {
            return;
        }
        if (e.ColumnIndex == 1 && e.CellStyle != null)
        {
            e.CellStyle.ForeColor = _records[e.RowIndex].Success
                ? ShutdownTheme.Success
                : ShutdownTheme.Danger;
        }
    }

    private void GridCellToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex >= 0 && e.RowIndex < _records.Count)
        {
            e.ToolTipText = _records[e.RowIndex].Message;
        }
    }

    private void OnSyncCompleted(NtpSyncRecord record)
    {
        if (_closed || IsDisposed || !IsHandleCreated)
        {
            return;
        }
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_closed || IsDisposed)
                {
                    return;
                }
                ApplyNewRecord(record);
            }));
        }
        catch (InvalidOperationException)
        {
            // 窗口正在关闭，忽略已在途的同步通知
        }
    }

    private void ReloadHistoryFromStore()
    {
        _records.Clear();
        _records.AddRange(_historyStore.Snapshot());
        _grid.RowCount = _records.Count;
        _grid.Invalidate();
        UpdateHistoryMeta();
        UpdateSummaryFromLatest();
    }

    private void ApplyNewRecord(NtpSyncRecord record)
    {
        if (_records.Count > 0 && _records[0].Equals(record))
        {
            return;
        }
        _records.Insert(0, record);
        if (_records.Count > NtpHistoryStore.MaxRecords)
        {
            _records.RemoveRange(NtpHistoryStore.MaxRecords,
                _records.Count - NtpHistoryStore.MaxRecords);
        }
        _grid.RowCount = _records.Count;
        _grid.Invalidate();
        UpdateHistoryMeta();
        UpdateSummaryFromLatest();
        _syncing = false;
        _syncNowButton.Enabled = true;
    }

    private void UpdateHistoryMeta()
    {
        _historyCountLabel.Text = "共 " + _records.Count + " 条 · 最多 "
            + NtpHistoryStore.MaxRecords.ToString("N0", CultureInfo.InvariantCulture) + " 条";
        _emptyLabel.Visible = _records.Count == 0;
        _emptyLabel.BringToFront();
    }

    private void UpdateSummaryFromLatest()
    {
        if (_records.Count == 0)
        {
            _lastSyncLabel.Text = "暂无同步记录";
            _lastSyncLabel.ForeColor = ShutdownTheme.Text;
            _detailLabel.Text = _manager.AutoSyncEnabled
                ? "后台自动同步服务已启动，首次同步将在启动约 10 秒后执行"
                : "自动同步已关闭；点击“立即同步”可手动校准时间";
            _detailLabel.ForeColor = ShutdownTheme.Muted;
            _statusLabel.Text = _manager.AutoSyncEnabled ? "自动同步已开启" : "自动同步已关闭";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            return;
        }

        NtpSyncRecord record = _records[0];
        _lastSyncLabel.Text = "上次同步："
            + record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            + " · " + record.Server;
        _lastSyncLabel.ForeColor = record.Success ? ShutdownTheme.Text : ShutdownTheme.Danger;
        if (record.Success)
        {
            _detailLabel.Text = "偏差 " + FormatOffset(record.OffsetMs) + " ms · 延时 "
                + record.DelayMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms · "
                + (record.TimeAdjusted ? "已校准系统时间" : "无需调整");
            _detailLabel.ForeColor = ShutdownTheme.Muted;
            _statusLabel.Text = "同步成功";
            _statusLabel.ForeColor = ShutdownTheme.Success;
        }
        else
        {
            _detailLabel.Text = record.Message.Replace('\n', ' ');
            _detailLabel.ForeColor = ShutdownTheme.Danger;
            _statusLabel.Text = "同步失败";
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
    }

    private void OnClockTick()
    {
        _currentTimeLabel.Text = "本机时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (_manager.IsBusy && !_syncing)
        {
            _statusLabel.Text = "正在自动同步…";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
        }
    }

    private async Task SyncNowFromUi()
    {
        if (_syncing)
        {
            return;
        }
        if (_manager.IsBusy)
        {
            _statusLabel.Text = "同步已在进行中…";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            return;
        }
        _syncing = true;
        _syncNowButton.Enabled = false;
        _statusLabel.Text = "正在同步…";
        _statusLabel.ForeColor = ShutdownTheme.Muted;
        try
        {
            NtpSyncRecord? record = await _manager.SyncNowAsync();
            if (!_closed && record == null && !_manager.IsBusy)
            {
                _statusLabel.Text = "同步未能启动";
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                _statusLabel.Text = "同步异常：" + ex.Message;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
        }
        finally
        {
            _syncing = false;
            if (!_closed && !IsDisposed)
            {
                _syncNowButton.Enabled = true;
            }
        }
    }

    private static string FormatOffset(double offset)
    {
        string text = offset.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        return text;
    }

    private Rectangle CloseRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 52f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 104f;

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(15, 118, 110), Color.FromArgb(8, 145, 178),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 230, -100, 310, 310);
            g.FillEllipse(glow, _header.Width - 96, 34, 130, 130);
        }

        TextRenderer.DrawText(g, "NTP 同步管理", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s), (int)(_header.Width - 140f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "自动校准本机时间 · 每 5 分钟同步一次", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s), (int)(_header.Width - 160f * s), (int)(26f * s)),
            Color.FromArgb(204, 251, 241), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f * s))
        {
            int inset = (int)(12f * s);
            g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closed = true;
        _clockTimer.Stop();
        _manager.SyncCompleted -= _onSyncCompleted;
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float scale = Height / 700f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), 18f * scale);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockTimer.Stop();
            _clockTimer.Dispose();
            _manager.SyncCompleted -= _onSyncCompleted;
            _headerTitleFont.Dispose();
            _headerSubFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }
}
