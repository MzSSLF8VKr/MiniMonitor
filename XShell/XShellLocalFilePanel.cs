using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>SFTP 独立窗口左侧的本地文件浏览器，形成类似 Xftp 的双栏布局。</summary>
internal sealed class XShellLocalFilePanel : UserControl
{
    private sealed record LocalEntry(string Name, bool IsDirectory, long Size,
        DateTime Modified, string FullPath);

    private readonly TextBox _path;
    private readonly ListView _list;
    private readonly Label _status;
    private string _currentPath = "";

    public event Action<IReadOnlyList<string>>? UploadRequested;
    public event Action<IReadOnlyList<string>>? DownloadRequested;

    public string CurrentPath => _currentPath;

    public XShellLocalFilePanel(string? initialPath = null)
    {
        BackColor = XShellTheme.Panel;
        Font = XShellTheme.Ui(8.8f);
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(XShellSftpPanel.RemoteDataFormat) == true
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(XShellSftpPanel.RemoteDataFormat) is string[] remotePaths)
            {
                DownloadRequested?.Invoke(remotePaths);
            }
        };

        var top = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = XShellTheme.PanelAlt,
            Padding = new Padding(8, 6, 8, 6)
        };
        var title = new Label
        {
            Text = "本地文件",
            Dock = DockStyle.Left,
            Width = 72,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(9f, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        _path = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = XShellTheme.InputBack,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(8.8f)
        };
        _path.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = NavigateAsync(_path.Text.Trim());
            }
        };
        var up = XShellTheme.FlatButton("上级", 54, 26);
        up.Dock = DockStyle.Right;
        up.Margin = new Padding(4, 0, 0, 0);
        up.Click += (_, _) => _ = NavigateAsync(ParentPath(_currentPath));
        var upload = XShellTheme.PrimaryButton("上传到远程", 92, 26);
        upload.Dock = DockStyle.Right;
        upload.Margin = new Padding(6, 0, 0, 0);
        upload.Click += (_, _) => UploadSelected();
        top.Controls.Add(_path);
        top.Controls.Add(upload);
        top.Controls.Add(up);
        top.Controls.Add(title);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = XShellTheme.PanelAlt
        };
        header.Paint += (_, e) =>
        {
            var list = _list;
            if (list == null)
            {
                return;
            }
            using var background = new SolidBrush(XShellTheme.PanelAlt);
            e.Graphics.FillRectangle(background, header.ClientRectangle);
            using var pen = new Pen(XShellTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            var font = XShellTheme.Ui(8.4f, FontStyle.Bold);
            int x = 8;
            string[] captions = { "名称", "大小", "修改时间" };
            for (int i = 0; i < captions.Length && i < list.Columns.Count; i++)
            {
                int width = list.Columns[i].Width;
                TextRenderer.DrawText(e.Graphics, captions[i], font,
                    new Rectangle(x, 0, Math.Max(10, width - 12), header.Height),
                    XShellTheme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                x += width;
            }
        };

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = XShellTheme.PanelAlt,
            ForeColor = XShellTheme.Muted,
            Text = "本地文件就绪"
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            BackColor = XShellTheme.Panel,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(8.8f),
            HeaderStyle = ColumnHeaderStyle.None,
            AllowDrop = true
        };
        _list.Columns.Add("名称", 250);
        _list.Columns.Add("大小", 90, HorizontalAlignment.Right);
        _list.Columns.Add("修改时间", 140);
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
            var files = SelectedFiles();
            if (files.Count == 0)
            {
                return;
            }
            var data = new DataObject(DataFormats.FileDrop, files.ToArray());
            _list.DoDragDrop(data, DragDropEffects.Copy);
        };
        _list.DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(XShellSftpPanel.RemoteDataFormat) == true
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(XShellSftpPanel.RemoteDataFormat) is string[] remotePaths)
            {
                DownloadRequested?.Invoke(remotePaths);
            }
        };
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional };
        XShellTheme.StyleMenu(menu);
        menu.Items.Add("打开", null, (_, _) =>
        {
            if (_list.SelectedItems.Count == 1
                && _list.SelectedItems[0].Tag is LocalEntry entry)
            {
                if (entry.IsDirectory)
                {
                    _ = NavigateAsync(entry.FullPath);
                }
                else
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "\"" + entry.FullPath + "\""); }
                    catch { }
                }
            }
        });
        menu.Items.Add("上传选中文件", null, (_, _) => UploadSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("上级", null, (_, _) => _ = NavigateAsync(ParentPath(_currentPath)));
        menu.Items.Add("刷新", null, (_, _) => _ = NavigateAsync(_currentPath));
        _list.ContextMenuStrip = menu;

        Controls.Add(_list);
        Controls.Add(header);
        Controls.Add(_status);
        Controls.Add(top);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_status, 1);
        Controls.SetChildIndex(header, 2);
        Controls.SetChildIndex(top, 3);

        string start = initialPath ?? "";
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
        {
            start = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
        {
            start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        _currentPath = start;
        _path.Text = start;
        _ = NavigateAsync(start);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        XShellTheme.ApplyNativeTheme(this);
    }

    private void UploadSelected()
    {
        var files = SelectedFiles();
        if (files.Count == 0)
        {
            _status.Text = "请先选择要上传的本地文件。";
            return;
        }
        UploadRequested?.Invoke(files);
    }

    private List<string> SelectedFiles()
    {
        return _list.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag as LocalEntry)
            .Where(entry => entry is { IsDirectory: false })
            .Select(entry => entry!.FullPath)
            .ToList();
    }

    public Task RefreshAsync() => NavigateAsync(_currentPath);

    private async Task NavigateAsync(string path)
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
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var entry in entries)
                {
                    var item = new ListViewItem(entry.Name)
                    {
                        Tag = entry
                    };
                    item.SubItems.Add(entry.IsDirectory ? "<文件夹>" : FormatSize(entry.Size));
                    item.SubItems.Add(entry.Modified.ToString("yyyy-MM-dd HH:mm"));
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            int folders = entries.Count(x => x.IsDirectory);
            _status.Text = $"{folders} 个文件夹，{entries.Count - folders} 个文件";
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
                entries.Add(new LocalEntry(info.Name, true, 0, info.LastWriteTime, info.FullName));
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

    private static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = size;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return index == 0 ? value.ToString("0") + " " + units[index]
            : value.ToString("0.##") + " " + units[index];
    }
}
