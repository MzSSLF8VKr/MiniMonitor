using System.Diagnostics;
using System.Windows.Forms;

namespace MiniMonitor.XFtp;

/// <summary>XFtp 左侧本地文件浏览器：驱动器、路径栏、文件列表与拖拽上传。</summary>
internal sealed class XFtpLocalPanel : UserControl
{
    private sealed record LocalEntry(string Name, bool IsDirectory, long Size,
        DateTime Modified, string FullPath);

    private readonly TextBox _path;
    private readonly ComboBox _drives;
    private readonly ListView _list;
    private readonly Label _status;
    private readonly ToolStrip _toolbar;
    private readonly ColumnHeader _nameColumn;
    private string _currentPath = "";
    private bool _suppressDriveEvent;

    public event Action<IReadOnlyList<string>>? UploadRequested;
    public event Action<IReadOnlyList<XFtpEntry>>? RemoteDropRequested;
    public event Action? SelectionChanged;
    public event Action? Activated;

    public string CurrentPath => _currentPath;

    public XFtpLocalPanel(string? initialPath = null)
    {
        BackColor = XFtpTheme.Panel;
        Font = XFtpTheme.Ui88;
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(XFtpRemotePanel.RemoteDataFormat) == true
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(XFtpRemotePanel.RemoteDataFormat) is XFtpEntry[] entries)
            {
                RemoteDropRequested?.Invoke(entries);
            }
        };

        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 33,
            GripStyle = ToolStripGripStyle.Hidden
        };
        XFtpTheme.StyleToolbar(_toolbar);
        _toolbar.Items.Add(Button("上级", "\uE74A", XFtpTheme.Muted,
            (_, _) => _ = NavigateAsync(ParentPath(_currentPath))));
        _toolbar.Items.Add(Button("刷新", "\uE72C", XFtpTheme.Accent,
            (_, _) => _ = RefreshAsync()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("新建文件夹", "\uE8F4", XFtpTheme.Accent,
            (_, _) => NewFolder()));
        _toolbar.Items.Add(Button("重命名", "\uE70F", XFtpTheme.Orange,
            (_, _) => RenameSelected()));
        _toolbar.Items.Add(Button("删除", "\uE74D", XFtpTheme.Red,
            (_, _) => DeleteSelected()));
        var upload = Button("上传到远程", "\uE898", XFtpTheme.Green,
            (_, _) => UploadSelected());
        upload.Alignment = ToolStripItemAlignment.Right;
        _toolbar.Items.Add(upload);

        var pathPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = XFtpTheme.Panel,
            Padding = new Padding(6, 4, 6, 4)
        };
        var localLabel = new Label
        {
            Text = "本地",
            Dock = DockStyle.Left,
            Width = 42,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = XFtpTheme.Accent,
            Font = XFtpTheme.UiBold,
            BackColor = Color.Transparent
        };
        _drives = XFtpTheme.Combo(86);
        _drives.Dock = DockStyle.Left;
        _drives.Margin = new Padding(0, 0, 6, 0);
        _drives.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressDriveEvent || _drives.SelectedItem == null)
            {
                return;
            }
            string drive = _drives.SelectedItem.ToString() ?? "";
            if (drive.Length > 0)
            {
                _ = NavigateAsync(drive);
            }
        };
        _path = XFtpTheme.Input(300);
        _path.Dock = DockStyle.Fill;
        _path.Font = XFtpTheme.Mono(8.8f);
        _path.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = NavigateAsync(_path.Text.Trim());
            }
        };
        pathPanel.Controls.Add(_path);
        pathPanel.Controls.Add(_drives);
        pathPanel.Controls.Add(localLabel);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = XFtpTheme.PanelAlt
        };
        header.Paint += (_, e) => DrawHeader(e.Graphics, header);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            BackColor = XFtpTheme.Panel,
            ForeColor = XFtpTheme.Text,
            Font = XFtpTheme.Ui88,
            HeaderStyle = ColumnHeaderStyle.None,
            AllowDrop = true
        };
        _nameColumn = _list.Columns.Add("名称", 280);
        _list.Columns.Add("大小", 90, HorizontalAlignment.Right);
        _list.Columns.Add("类型", 90);
        _list.Columns.Add("修改日期", 140);
        XFtpTheme.StyleList(_list);
        var images = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };
        images.Images.Add(XFtpTheme.Icon("\uE8B7", XFtpTheme.Accent, 16));
        images.Images.Add(XFtpTheme.Icon("\uE7C3", XFtpTheme.Muted, 16));
        _list.SmallImageList = images;
        _list.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke();
        _list.Enter += (_, _) => Activated?.Invoke();
        _list.Resize += (_, _) => ResizeColumns();
        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedItems.Count == 1
                && _list.SelectedItems[0].Tag is LocalEntry entry
                && entry.IsDirectory)
            {
                _ = NavigateAsync(entry.FullPath);
            }
        };
        _list.ItemDrag += (_, _) =>
        {
            var files = SelectedFilePaths();
            if (files.Count == 0)
            {
                return;
            }
            var data = new DataObject(DataFormats.FileDrop, files.ToArray());
            _list.DoDragDrop(data, DragDropEffects.Copy);
        };
        _list.DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(XFtpRemotePanel.RemoteDataFormat) == true
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(XFtpRemotePanel.RemoteDataFormat) is XFtpEntry[] entries)
            {
                RemoteDropRequested?.Invoke(entries);
            }
        };
        _list.ContextMenuStrip = BuildMenu();

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = XFtpTheme.PanelAlt,
            ForeColor = XFtpTheme.Muted,
            Text = "本地文件就绪"
        };

        Controls.Add(_list);
        Controls.Add(header);
        Controls.Add(pathPanel);
        Controls.Add(_toolbar);
        Controls.Add(_status);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_status, 1);
        Controls.SetChildIndex(header, 2);
        Controls.SetChildIndex(pathPanel, 3);
        Controls.SetChildIndex(_toolbar, 4);

        string start = initialPath ?? "";
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
        {
            // 默认本地目录：用户目录下的 Downloads（兼容旧系统的 Download）。
            string profile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            string downloads = Path.Combine(profile, "Downloads");
            string download = Path.Combine(profile, "Download");
            start = Directory.Exists(downloads) ? downloads
                : Directory.Exists(download) ? download
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
        {
            start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        _currentPath = start;
        _path.Text = start;
        LoadDrives();
        _ = NavigateAsync(start);
    }

    private static ToolStripButton Button(string text, string glyph, Color color,
        EventHandler click)
    {
        var button = new ToolStripButton(text, XFtpTheme.Icon(glyph, color, 16), click)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Font = XFtpTheme.Ui85,
            ForeColor = XFtpTheme.Text,
            Margin = new Padding(1, 0, 1, 0)
        };
        return button;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional };
        XFtpTheme.StyleMenu(menu);
        menu.Items.Add("上传选中文件", null, (_, _) => UploadSelected());
        menu.Items.Add("上传到远程", null, (_, _) => UploadSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("新建文件夹...", null, (_, _) => NewFolder());
        menu.Items.Add("重命名...", null, (_, _) => RenameSelected());
        menu.Items.Add("删除", null, (_, _) => DeleteSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开", null, (_, _) => OpenSelected());
        menu.Items.Add("刷新", null, (_, _) => _ = RefreshAsync());
        return menu;
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Activated?.Invoke();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        XFtpTheme.ApplyNative(this);
    }

    private void DrawHeader(Graphics g, Panel header)
    {
        using var background = new SolidBrush(XFtpTheme.PanelAlt);
        g.FillRectangle(background, header.ClientRectangle);
        using var pen = new Pen(XFtpTheme.BorderLight);
        g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        var font = XFtpTheme.Ui(8.4f, FontStyle.Bold);
        int x = 8;
        for (int i = 0; i < _list.Columns.Count; i++)
        {
            int width = _list.Columns[i].Width;
            var align = i == 1 ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(g, _list.Columns[i].Text, font,
                new Rectangle(x, 0, Math.Max(10, width - 12), header.Height),
                XFtpTheme.Muted,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                | TextFormatFlags.EndEllipsis);
            x += width;
        }
    }

    private void ResizeColumns()
    {
        if (_list.Columns.Count < 4)
        {
            return;
        }
        int fixedWidth = 90 + 90 + 140;
        _nameColumn.Width = Math.Max(180, _list.ClientSize.Width - fixedWidth - 20);
    }

    private void LoadDrives()
    {
        _suppressDriveEvent = true;
        try
        {
            _drives.Items.Clear();
            foreach (var drive in DriveEnumerator.GetDrives())
            {
                if (drive.IsReady)
                {
                    _drives.Items.Add(drive.Root);
                }
            }
        }
        catch
        {
            // 读取驱动器失败不影响浏览。
        }
        finally
        {
            _suppressDriveEvent = false;
        }
    }

    public async Task RefreshAsync() => await NavigateAsync(_currentPath);

    public async Task NavigateAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            string full = Path.GetFullPath(path);
            var entries = await Task.Run(() => Enumerate(full));
            if (IsDisposed)
            {
                return;
            }
            _currentPath = full;
            _path.Text = full;
            _suppressDriveEvent = true;
            try
            {
                string root = Path.GetPathRoot(full) ?? "";
                int index = _drives.Items.IndexOf(root);
                if (index >= 0)
                {
                    _drives.SelectedIndex = index;
                }
            }
            finally
            {
                _suppressDriveEvent = false;
            }
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var entry in entries)
                {
                    var item = new ListViewItem(entry.Name, entry.IsDirectory ? 0 : 1)
                    {
                        Tag = entry
                    };
                    item.SubItems.Add(entry.IsDirectory ? "<文件夹>" : XFtpEntry.FormatSize(entry.Size));
                    item.SubItems.Add(entry.IsDirectory ? "文件夹"
                        : string.IsNullOrWhiteSpace(Path.GetExtension(entry.Name)) ? "文件"
                        : Path.GetExtension(entry.Name).TrimStart('.').ToUpperInvariant() + " 文件");
                    item.SubItems.Add(entry.Modified.ToString("yyyy-MM-dd HH:mm"));
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            int folders = entries.Count(entry => entry.IsDirectory);
            _status.Text = $"{folders} 个文件夹，{entries.Count - folders} 个文件";
            ResizeColumns();
        }
        catch (Exception ex)
        {
            _status.Text = "无法打开本地目录：" + ex.Message;
        }
    }

    private static List<LocalEntry> Enumerate(string path)
    {
        var entries = new List<LocalEntry>();
        foreach (string dir in Directory.EnumerateDirectories(path))
        {
            try
            {
                var info = new DirectoryInfo(dir);
                entries.Add(new LocalEntry(info.Name, true, 0,
                    info.LastWriteTime, info.FullName));
            }
            catch
            {
                // 跳过无权限目录。
            }
        }
        foreach (string file in Directory.EnumerateFiles(path))
        {
            try
            {
                var info = new FileInfo(file);
                entries.Add(new LocalEntry(info.Name, false, info.Length,
                    info.LastWriteTime, info.FullName));
            }
            catch
            {
                // 跳过无权限文件。
            }
        }
        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SelectAllItems()
    {
        if (_list.Items.Count == 0)
        {
            return;
        }
        _list.BeginUpdate();
        try
        {
            foreach (ListViewItem item in _list.Items)
            {
                item.Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    public List<string> SelectedPaths()
    {
        return _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as LocalEntry)
            .Where(entry => entry != null)
            .Select(entry => entry!.FullPath)
            .ToList();
    }

    private List<string> SelectedFilePaths()
    {
        return _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as LocalEntry)
            .Where(entry => entry is { IsDirectory: false })
            .Select(entry => entry!.FullPath)
            .ToList();
    }

    private List<LocalEntry> SelectedEntries()
    {
        return _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as LocalEntry)
            .Where(entry => entry != null)
            .Cast<LocalEntry>()
            .ToList();
    }

    public void UploadSelected()
    {
        var paths = SelectedPaths();
        if (paths.Count == 0)
        {
            _status.Text = "请先选择要上传的文件或文件夹。";
            return;
        }
        UploadRequested?.Invoke(paths);
    }

    private void OpenSelected()
    {
        var entries = SelectedEntries();
        if (entries.Count != 1)
        {
            return;
        }
        var entry = entries[0];
        if (entry.IsDirectory)
        {
            _ = NavigateAsync(entry.FullPath);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch
        {
            // 打开文件失败不提示。
        }
    }

    public void NewFolder()
    {
        string? name = XFtpPrompt.Show(FindForm(), "新建文件夹", "请输入文件夹名称：");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.Combine(_currentPath, name));
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            XFtpMessageBox.Show(FindForm(), "新建文件夹失败：\r\n\r\n" + ex.Message,
                "XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void RenameSelected()
    {
        var entries = SelectedEntries();
        if (entries.Count != 1)
        {
            return;
        }
        var entry = entries[0];
        string? name = XFtpPrompt.Show(FindForm(), "重命名", "请输入新的名称：", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name)
        {
            return;
        }
        try
        {
            string target = Path.Combine(_currentPath, name);
            if (entry.IsDirectory)
            {
                Directory.Move(entry.FullPath, target);
            }
            else
            {
                File.Move(entry.FullPath, target);
            }
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            XFtpMessageBox.Show(FindForm(), "重命名失败：\r\n\r\n" + ex.Message,
                "XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void DeleteSelected()
    {
        var entries = SelectedEntries();
        if (entries.Count == 0)
        {
            return;
        }
        if (XFtpMessageBox.Show(FindForm(),
                "确定要删除选中的 " + entries.Count + " 项吗？\r\n\r\n此操作不可撤销。",
                "XFTP 复刻版", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            != DialogResult.Yes)
        {
            return;
        }
        int failed = 0;
        foreach (var entry in entries)
        {
            try
            {
                if (entry.IsDirectory)
                {
                    Directory.Delete(entry.FullPath, true);
                }
                else
                {
                    File.Delete(entry.FullPath);
                }
            }
            catch
            {
                failed++;
            }
        }
        if (failed > 0)
        {
            XFtpMessageBox.Show(FindForm(), "有 " + failed + " 项删除失败，可能正被其他程序占用。",
                "XFTP 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _ = RefreshAsync();
    }

    private static string ParentPath(string path)
    {
        try
        {
            return Directory.GetParent(path)?.FullName ?? path;
        }
        catch
        {
            return path;
        }
    }
}
