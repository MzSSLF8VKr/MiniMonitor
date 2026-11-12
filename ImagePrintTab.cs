using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>照片打印处理：打印机 / 纸张 / 方向 / 每页张数 / 边距 / 排版方式 / 预览 / 打印。</summary>
internal sealed class ImagePrintTab : UserControl
{
    private sealed class PaperItem
    {
        public required PaperSize Size { get; init; }
        public override string ToString()
        {
            float w = Size.Width / 100f * 25.4f;
            float h = Size.Height / 100f * 25.4f;
            return Size.PaperName + "（" + Math.Round(w) + "×" + Math.Round(h) + " mm）";
        }
    }

    private readonly ListView _list;
    private readonly ComboBox _printerCombo;
    private readonly ComboBox _paperCombo;
    private readonly ComboBox _orientationCombo;
    private readonly ComboBox _perPageCombo;
    private readonly ComboBox _fitCombo;
    private readonly NumericUpDown _margin;
    private readonly NumericUpDown _spacing;
    private readonly NumericUpDown _copies;
    private readonly CheckBox _showNameCheck;
    private readonly PrintPreviewControl _preview;
    private readonly PrintDocument _document;
    private readonly Label _countLabel;
    private readonly Label _pageLabel;
    private readonly Label _statusLabel;
    private readonly PillButton _addButton;
    private readonly PillButton _removeButton;
    private readonly PillButton _upButton;
    private readonly PillButton _downButton;
    private readonly PillButton _clearButton;
    private readonly PillButton _printButton;
    private readonly PillButton _prevButton;
    private readonly PillButton _nextButton;
    private readonly Panel _footer;
    private readonly Dictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingPrinters;
    private bool _documentReady;
    private int _printPageIndex;

    public ImagePrintTab()
    {
        BackColor = Color.White;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(root);

        // ---------- 打印参数 ----------
        var settingsCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            CornerRadius = 14
        };
        var printerTitle = MakeLabel("打印机", 16, 14, 54, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _printerCombo = new ComboBox
        {
            Location = new Point(72, 12),
            Size = new Size(236, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _printerCombo.SelectedIndexChanged += (_, _) => OnPrinterChanged();
        var paperTitle = MakeLabel("纸张", 322, 14, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _paperCombo = new ComboBox
        {
            Location = new Point(364, 12),
            Size = new Size(160, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _paperCombo.SelectedIndexChanged += (_, _) => OnPaperChanged();
        var orientationTitle = MakeLabel("方向", 538, 14, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _orientationCombo = new ComboBox
        {
            Location = new Point(580, 12),
            Size = new Size(92, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _orientationCombo.Items.AddRange(new object[] { "纵向", "横向" });
        _orientationCombo.SelectedIndex = 0;
        _orientationCombo.SelectedIndexChanged += (_, _) => OnOrientationChanged();
        var copiesTitle = MakeLabel("份数", 686, 14, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _copies = new NumericUpDown
        {
            Location = new Point(728, 12),
            Size = new Size(60, 28),
            Minimum = 1,
            Maximum = 99,
            Value = 1,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };

        var perPageTitle = MakeLabel("每页照片", 16, 56, 68, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _perPageCombo = new ComboBox
        {
            Location = new Point(90, 54),
            Size = new Size(82, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _perPageCombo.Items.AddRange(new object[] { "1 张", "2 张", "4 张", "6 张", "9 张" });
        _perPageCombo.SelectedIndex = 2;
        _perPageCombo.SelectedIndexChanged += (_, _) => OnLayoutChanged();
        var marginTitle = MakeLabel("页边距", 186, 56, 56, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _margin = new NumericUpDown
        {
            Location = new Point(246, 54),
            Size = new Size(58, 28),
            Minimum = 0,
            Maximum = 50,
            Value = 10,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _margin.ValueChanged += (_, _) => OnLayoutChanged();
        var mm1 = MakeLabel("mm", 308, 56, 30, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var spacingTitle = MakeLabel("间距", 344, 56, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _spacing = new NumericUpDown
        {
            Location = new Point(386, 54),
            Size = new Size(58, 28),
            Minimum = 0,
            Maximum = 30,
            Value = 5,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _spacing.ValueChanged += (_, _) => OnLayoutChanged();
        var mm2 = MakeLabel("mm", 448, 56, 30, 26,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var fitTitle = MakeLabel("排版", 490, 56, 40, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _fitCombo = new ComboBox
        {
            Location = new Point(532, 54),
            Size = new Size(146, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        _fitCombo.Items.AddRange(new object[] { "适应页面", "裁剪填满", "拉伸填满" });
        _fitCombo.SelectedIndex = 0;
        _fitCombo.SelectedIndexChanged += (_, _) => OnLayoutChanged();
        _showNameCheck = new CheckBox
        {
            Text = "显示文件名",
            Location = new Point(690, 56),
            Size = new Size(120, 24),
            Font = new Font("Microsoft YaHei UI", 8.2f)
        };
        _showNameCheck.CheckedChanged += (_, _) => OnLayoutChanged();
        var settingsHint = MakeLabel(
            "每页张数会按纸张方向自动排列；打印前可在右侧预览每一页。",
            16, 94, 760, 22,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        settingsCard.Controls.AddRange(new Control[]
        {
            printerTitle, _printerCombo, paperTitle, _paperCombo,
            orientationTitle, _orientationCombo, copiesTitle, _copies,
            perPageTitle, _perPageCombo, marginTitle, _margin, mm1,
            spacingTitle, _spacing, mm2, fitTitle, _fitCombo, _showNameCheck, settingsHint
        });
        root.Controls.Add(settingsCard, 0, 0);

        // ---------- 照片列表 + 预览 ----------
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        var listCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(10)
        };
        var listLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White
        };
        listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var listToolbar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        _addButton = MakeButton("添加照片", 0, 2, 100, 32, PillButtonStyle.Primary);
        _addButton.Click += (_, _) => AddPhotosDialog();
        _removeButton = MakeButton("移除选中", 108, 2, 100, 32, PillButtonStyle.Light);
        _removeButton.Click += (_, _) => RemoveSelected();
        _upButton = MakeButton("上移", 0, 42, 72, 28, PillButtonStyle.Light);
        _upButton.Click += (_, _) => MoveSelected(-1);
        _downButton = MakeButton("下移", 80, 42, 72, 28, PillButtonStyle.Light);
        _downButton.Click += (_, _) => MoveSelected(1);
        _clearButton = MakeButton("清空", 160, 42, 72, 28, PillButtonStyle.Light);
        _clearButton.Click += (_, _) => ClearList();
        _countLabel = MakeLabel("0 张", 242, 42, 100, 28,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _countLabel.TextAlign = ContentAlignment.MiddleRight;
        _countLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        listToolbar.Controls.AddRange(new Control[]
        {
            _addButton, _removeButton, _upButton, _downButton, _clearButton, _countLabel
        });
        listToolbar.Resize += (_, _) =>
        {
            _countLabel.Width = Math.Max(40, listToolbar.ClientSize.Width - _countLabel.Left);
        };
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            AllowDrop = true
        };
        _list.Columns.Add("文件名", 220);
        _list.Columns.Add("尺寸", 90);
        _list.Resize += (_, _) =>
        {
            if (_list.Columns.Count >= 2)
            {
                int width = Math.Max(200, _list.ClientSize.Width - 4);
                _list.Columns[0].Width = width - 94;
                _list.Columns[1].Width = 90;
            }
        };
        _list.SelectedIndexChanged += (_, _) => UpdateListState();
        _list.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                AddPaths(paths);
            }
        };
        listLayout.Controls.Add(listToolbar, 0, 0);
        listLayout.Controls.Add(_list, 0, 1);
        listCard.Controls.Add(listLayout);
        body.Controls.Add(listCard, 0, 0);

        var previewCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 0),
            Padding = new Padding(8)
        };
        _preview = new PrintPreviewControl
        {
            Dock = DockStyle.Fill,
            AutoZoom = true,
            UseAntiAlias = true,
            Rows = 1,
            Columns = 1,
            BackColor = Color.FromArgb(226, 232, 240)
        };
        previewCard.Controls.Add(_preview);
        body.Controls.Add(previewCard, 1, 0);

        // ---------- 底部 ----------
        _footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        _statusLabel = MakeLabel("添加照片后即可预览打印效果", 14, 15, 400, 28,
            new Font("Microsoft YaHei UI", 8.3f), ShutdownTheme.Muted, Color.White);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _pageLabel = MakeLabel("第 0 / 0 页", 0, 15, 130, 28,
            new Font("Microsoft YaHei UI", 8.3f), ShutdownTheme.Muted, Color.White);
        _pageLabel.TextAlign = ContentAlignment.MiddleCenter;
        _prevButton = MakeButton("上一页", 0, 14, 86, 34, PillButtonStyle.Light);
        _prevButton.Click += (_, _) => ChangePreviewPage(-1);
        _nextButton = MakeButton("下一页", 0, 14, 86, 34, PillButtonStyle.Light);
        _nextButton.Click += (_, _) => ChangePreviewPage(1);
        _printButton = MakeButton("打印…", 0, 14, 110, 34, PillButtonStyle.Primary);
        _printButton.Click += (_, _) => PrintPhotos();
        _footer.Controls.AddRange(new Control[]
        {
            _statusLabel, _pageLabel, _prevButton, _nextButton, _printButton
        });
        _footer.Resize += (_, _) => LayoutFooter();
        root.Controls.Add(_footer, 0, 2);

        _document = new PrintDocument();
        _document.DocumentName = "MiniMonitor 照片打印";
        _document.BeginPrint += (_, _) => _printPageIndex = 0;
        _document.PrintPage += OnPrintPage;

        LoadPrinters();
        Load += (_, _) => InitializePreview();
        LayoutFooter();
        UpdateListState();
    }

    // ================= 布局 =================

    private void LayoutFooter()
    {
        int right = _footer.ClientSize.Width - 14;
        _printButton.Location = new Point(right - _printButton.Width, 14);
        _nextButton.Location = new Point(_printButton.Left - 10 - _nextButton.Width, 14);
        _prevButton.Location = new Point(_nextButton.Left - 6 - _prevButton.Width, 14);
        _pageLabel.Location = new Point(_prevButton.Left - 6 - _pageLabel.Width, 15);
        _statusLabel.Location = new Point(14, 15);
        _statusLabel.Width = Math.Max(120, _pageLabel.Left - 16 - _statusLabel.Left);
    }

    private void InitializePreview()
    {
        if (_documentReady)
        {
            return;
        }
        _documentReady = true;
        try
        {
            _preview.Document = _document;
            ApplyPrintSettings();
            RefreshPreview();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "打印预览不可用：" + ex.Message;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
    }

    // ================= 打印机 / 纸张 =================

    private void LoadPrinters()
    {
        _loadingPrinters = true;
        _printerCombo.BeginUpdate();
        _printerCombo.Items.Clear();
        try
        {
            var names = PrinterSettings.InstalledPrinters
                .Cast<string>()
                .ToArray();
            _printerCombo.Items.AddRange(names);
            var defaultPrinter = new PrinterSettings();
            if (names.Length > 0)
            {
                int index = Array.IndexOf(names, defaultPrinter.PrinterName);
                _printerCombo.SelectedIndex = index >= 0 ? index : 0;
            }
        }
        catch
        {
            // 未安装打印机
        }
        _printerCombo.EndUpdate();
        if (_printerCombo.Items.Count == 0)
        {
            _printerCombo.Items.Add("未安装打印机");
            _printerCombo.SelectedIndex = 0;
            _printerCombo.Enabled = false;
            _printButton.Enabled = false;
        }
        _loadingPrinters = false;
        if (_printerCombo.SelectedItem != null && _printerCombo.Enabled)
        {
            LoadPaperSizes(_printerCombo.SelectedItem.ToString() ?? "");
        }
    }

    private void OnPrinterChanged()
    {
        if (_loadingPrinters || !_printerCombo.Enabled)
        {
            return;
        }
        string name = _printerCombo.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return;
        }
        LoadPaperSizes(name);
        ApplyPrintSettings();
        RefreshPreview();
    }

    private void LoadPaperSizes(string printer)
    {
        _loadingPrinters = true;
        _paperCombo.BeginUpdate();
        _paperCombo.Items.Clear();
        try
        {
            _document.PrinterSettings.PrinterName = printer;
            var sizes = _document.PrinterSettings.PaperSizes;
            var items = new List<PaperItem>();
            foreach (PaperSize size in sizes)
            {
                items.Add(new PaperItem { Size = size });
            }
            _paperCombo.Items.AddRange(items.ToArray());
            int index = items.FindIndex(item =>
                item.Size.Kind == PaperKind.A4 || item.Size.PaperName.Contains("A4", StringComparison.OrdinalIgnoreCase));
            _paperCombo.SelectedIndex = index >= 0 ? index : (items.Count > 0 ? 0 : -1);
        }
        catch
        {
            _paperCombo.Items.Clear();
        }
        _paperCombo.EndUpdate();
        _loadingPrinters = false;
    }

    private void OnPaperChanged()
    {
        if (_loadingPrinters)
        {
            return;
        }
        ApplyPrintSettings();
        RefreshPreview();
    }

    private void OnOrientationChanged()
    {
        ApplyPrintSettings();
        RefreshPreview();
    }

    private void OnLayoutChanged()
    {
        ApplyPrintSettings();
        RefreshPreview();
    }

    private void ApplyPrintSettings()
    {
        try
        {
            if (_paperCombo.SelectedItem is PaperItem paper)
            {
                _document.DefaultPageSettings.PaperSize = paper.Size;
            }
            _document.DefaultPageSettings.Landscape = _orientationCombo.SelectedIndex == 1;
            int margin = (int)MmToHundredths((float)_margin.Value);
            _document.DefaultPageSettings.Margins = new Margins(margin, margin, margin, margin);
        }
        catch
        {
            // 参数不合法时保持上一次设置
        }
    }

    private static float MmToHundredths(float mm)
    {
        return mm / 25.4f * 100f;
    }

    // ================= 列表 =================

    private void AddPhotosDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "添加要打印的照片",
            Filter = PhotoToolkit.OpenFileFilter,
            Multiselect = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is string path)
            {
                existing.Add(path);
            }
        }
        _list.BeginUpdate();
        try
        {
            foreach (string path in paths)
            {
                if (!File.Exists(path) || !PhotoToolkit.IsSupported(path) || !existing.Add(path))
                {
                    continue;
                }
                var item = new ListViewItem(Path.GetFileName(path)) { Tag = path };
                try
                {
                    var info = PhotoToolkit.ReadFileInfo(path);
                    item.SubItems.Add(info.Width + " × " + info.Height);
                }
                catch
                {
                    item.SubItems.Add("—");
                }
                item.ToolTipText = path;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateListState();
        RefreshPreview();
    }

    private void RemoveSelected()
    {
        var item = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;
        if (item == null)
        {
            return;
        }
        string? path = item.Tag as string;
        _list.Items.Remove(item);
        if (path != null)
        {
            RemoveCache(path);
        }
        UpdateListState();
        RefreshPreview();
    }

    private void ClearList()
    {
        _list.Items.Clear();
        DisposeCache();
        UpdateListState();
        RefreshPreview();
    }

    private void MoveSelected(int delta)
    {
        var item = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;
        if (item == null)
        {
            return;
        }
        int index = item.Index;
        int target = index + delta;
        if (target < 0 || target >= _list.Items.Count)
        {
            return;
        }
        _list.Items.RemoveAt(index);
        _list.Items.Insert(target, item);
        item.Selected = true;
        item.Focused = true;
        _list.EnsureVisible(target);
        RefreshPreview();
    }

    private void UpdateListState()
    {
        int count = _list.Items.Count;
        _countLabel.Text = count + " 张";
        _removeButton.Enabled = _list.SelectedItems.Count > 0;
        _upButton.Enabled = _list.SelectedItems.Count > 0
            && _list.SelectedItems[0].Index > 0;
        _downButton.Enabled = _list.SelectedItems.Count > 0
            && _list.SelectedItems[0].Index < count - 1;
        _clearButton.Enabled = count > 0;
        _printButton.Enabled = count > 0 && _printerCombo.Enabled;
        if (count > 0)
        {
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            _statusLabel.Text = "共 " + count + " 张照片 · 每页 "
                + PerPageCount() + " 张 · 共 " + TotalPages() + " 页";
        }
        else
        {
            _statusLabel.Text = "添加照片后即可预览打印效果";
        }
        UpdatePageLabel();
    }

    // ================= 预览 / 打印 =================

    private int PerPageCount()
    {
        return _perPageCombo.SelectedIndex switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            3 => 6,
            4 => 9,
            _ => 4
        };
    }

    private int TotalPages()
    {
        int count = _list.Items.Count;
        int perPage = PerPageCount();
        return count == 0 ? 0 : (int)Math.Ceiling(count / (double)perPage);
    }

    private void RefreshPreview()
    {
        UpdateListState();
        if (!_documentReady)
        {
            return;
        }
        try
        {
            _preview.InvalidatePreview();
            int total = TotalPages();
            if (total > 0)
            {
                _preview.StartPage = Math.Clamp(_preview.StartPage, 0, total - 1);
            }
            UpdatePageLabel();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "打印预览不可用：" + ex.Message;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
    }

    private void UpdatePageLabel()
    {
        int total = TotalPages();
        int current = total == 0 ? 0 : Math.Clamp(_preview.StartPage + 1, 1, total);
        _pageLabel.Text = "第 " + current + " / " + total + " 页";
        _prevButton.Enabled = total > 0 && _preview.StartPage > 0;
        _nextButton.Enabled = total > 0 && _preview.StartPage < total - 1;
    }

    private void ChangePreviewPage(int delta)
    {
        int total = TotalPages();
        if (total <= 0)
        {
            return;
        }
        int page = Math.Clamp(_preview.StartPage + delta, 0, total - 1);
        _preview.StartPage = page;
        UpdatePageLabel();
    }

    private void OnPrintPage(object sender, PrintPageEventArgs e)
    {
        var paths = _list.Items
            .Cast<ListViewItem>()
            .Select(item => item.Tag as string)
            .Where(path => !string.IsNullOrEmpty(path))
            .Cast<string>()
            .ToList();
        int perPage = PerPageCount();
        int start = _printPageIndex * perPage;
        if (start >= paths.Count)
        {
            e.HasMorePages = false;
            return;
        }

        Rectangle bounds = e.MarginBounds;
        if (bounds.Width <= 20 || bounds.Height <= 20)
        {
            bounds = e.PageBounds;
        }
        int cols = 1;
        int rows = 1;
        switch (perPage)
        {
            case 2:
                cols = e.PageSettings.Landscape ? 2 : 1;
                rows = e.PageSettings.Landscape ? 1 : 2;
                break;
            case 4:
                cols = 2;
                rows = 2;
                break;
            case 6:
                cols = e.PageSettings.Landscape ? 3 : 2;
                rows = e.PageSettings.Landscape ? 2 : 3;
                break;
            case 9:
                cols = 3;
                rows = 3;
                break;
        }

        float gap = MmToHundredths((float)_spacing.Value);
        float cellWidth = (bounds.Width - gap * (cols - 1)) / cols;
        float cellHeight = (bounds.Height - gap * (rows - 1)) / rows;
        if (cellWidth <= 1 || cellHeight <= 1)
        {
            e.HasMorePages = false;
            return;
        }

        var fit = _fitCombo.SelectedIndex switch
        {
            1 => PrintFitMode.Fill,
            2 => PrintFitMode.Stretch,
            _ => PrintFitMode.Fit
        };
        bool showName = _showNameCheck.Checked;
        var graphics = e.Graphics;
        if (graphics == null)
        {
            e.HasMorePages = false;
            return;
        }
        using var background = new SolidBrush(Color.White);
        graphics.FillRectangle(background, e.PageBounds);

        for (int i = 0; i < perPage && start + i < paths.Count; i++)
        {
            var photo = GetPhoto(paths[start + i]);
            if (photo == null)
            {
                continue;
            }
            int col = i % cols;
            int row = i / cols;
            var cell = new RectangleF(
                bounds.Left + col * (cellWidth + gap),
                bounds.Top + row * (cellHeight + gap),
                cellWidth, cellHeight);
            PhotoPrintRenderer.DrawPhoto(graphics, photo, cell, fit, showName);
        }

        _printPageIndex++;
        e.HasMorePages = start + perPage < paths.Count;
    }

    private Bitmap? GetPhoto(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        try
        {
            var bitmap = PhotoToolkit.LoadBitmap(path, autoOrient: true);
            bitmap.Tag = path;
            _cache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void RemoveCache(string path)
    {
        if (_cache.Remove(path, out var bitmap))
        {
            bitmap.Dispose();
        }
    }

    private void DisposeCache()
    {
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }
        _cache.Clear();
    }

    private void PrintPhotos()
    {
        if (_list.Items.Count == 0)
        {
            MessageBox.Show(FindForm(), "请先添加要打印的照片。", "照片打印",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_printerCombo.Enabled || _printerCombo.SelectedItem == null)
        {
            MessageBox.Show(FindForm(), "未检测到可用的打印机。", "照片打印",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            ApplyPrintSettings();
            using var dialog = new PrintDialog
            {
                Document = _document,
                UseEXDialog = true,
                AllowSomePages = false,
                AllowSelection = false
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }
            _document.PrinterSettings.Copies =
                (short)Math.Clamp((int)_copies.Value, 1, 99);
            _document.PrintController = new StandardPrintController();
            _printPageIndex = 0;
            _document.Print();
            _statusLabel.ForeColor = ShutdownTheme.Success;
            _statusLabel.Text = "已发送到打印机：" + _document.PrinterSettings.PrinterName
                + " · " + TotalPages() + " 页 × " + _copies.Value + " 份";
            RefreshPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "打印失败：\n" + ex.Message, "照片打印",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ================= 工具 =================

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

    private static PillButton MakeButton(string text, int x, int y, int width, int height,
        PillButtonStyle style)
    {
        return new PillButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            ButtonStyle = style
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCache();
            _preview.Document = null;
            _document.Dispose();
        }
        base.Dispose(disposing);
    }
}
