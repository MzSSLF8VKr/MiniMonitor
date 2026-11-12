using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace MiniMonitor.XShell;

/// <summary>Tabby 风格的 SFTP 文件浏览器：浏览、上传、下载、重命名、删除。</summary>
public sealed class XShellSftpPanel : UserControl
{
    /// <summary>远程文件拖放到本地窗格时使用的数据格式。</summary>
    public const string RemoteDataFormat = "XShell.Sftp.Remote";
    private readonly XSessionProfile _profile;
    private readonly XShellSettings _settings;
    private readonly Func<Task<SftpClient?>> _clientFactory;
    private readonly TextBox _pathBox;
    private readonly ListView _list;
    private readonly Label _status;
    private readonly ToolStrip _toolbar;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SftpClient? _client;
    private string _currentPath = "/";
    private bool _busy;

    public event Action<string, long>? UploadStarted;
    public event Action<string, long, long>? UploadProgress;
    public event Action<string>? UploadCompleted;
    public event Action<string, string>? UploadFailed;

    public XShellSftpPanel(XSessionProfile profile, XShellSettings settings,
        Func<Task<SftpClient?>> clientFactory)
    {
        _profile = profile;
        _settings = settings;
        _clientFactory = clientFactory;
        BackColor = XShellTheme.Panel;
        Font = XShellTheme.Ui(8.8f);

        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            Height = 33,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.Professional,
            BackColor = XShellTheme.PanelAlt,
            Padding = new Padding(4, 3, 4, 3),
            ImageScalingSize = new Size(16, 16)
        };
        XShellTheme.StyleMenu(_toolbar);
        _toolbar.Items.Add(Button("上传", XShellIcons.Upload(XShellTheme.Accent, 16), (_, _) => _ = UploadAsync()));
        _toolbar.Items.Add(Button("下载", XShellIcons.Download(XShellTheme.Green, 16), (_, _) => _ = DownloadAsync()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("新建文件夹", XShellIcons.Plus(XShellTheme.Accent, 16), (_, _) => _ = NewFolderAsync()));
        _toolbar.Items.Add(Button("重命名", XShellIcons.Edit(XShellTheme.Orange, 16), (_, _) => _ = RenameAsync()));
        _toolbar.Items.Add(Button("删除", XShellIcons.Delete(XShellTheme.Red, 16), (_, _) => _ = DeleteAsync()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Button("上级", XShellIcons.Up(XShellTheme.Muted, 16), (_, _) => _ = LoadDirectoryAsync(ParentPath(_currentPath))));
        _toolbar.Items.Add(Button("刷新", XShellIcons.Refresh(XShellTheme.Accent, 16), (_, _) => _ = LoadDirectoryAsync(_currentPath)));
        Controls.Add(_toolbar);

        var pathPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 31,
            BackColor = XShellTheme.Panel,
            Padding = new Padding(5, 3, 5, 3)
        };
        var pathLabel = new Label
        {
            Text = "远程",
            AutoSize = false,
            Width = 40,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = XShellTheme.Muted
        };
        pathPanel.Controls.Add(pathLabel);
        _pathBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = XShellTheme.Mono(9f),
            BackColor = XShellTheme.InputBack,
            ForeColor = XShellTheme.Text
        };
        _pathBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = LoadDirectoryAsync(_pathBox.Text.Trim());
            }
        };
        pathPanel.Controls.Add(_pathBox);
        Controls.Add(pathPanel);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            BackColor = XShellTheme.PanelAlt,
            ForeColor = XShellTheme.Muted,
            Text = "SFTP 就绪"
        };
        Controls.Add(_status);

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
            AllowDrop = true,
            HeaderStyle = ColumnHeaderStyle.None
        };
        _list.Columns.Add("名称", 260);
        _list.Columns.Add("大小", 100, HorizontalAlignment.Right);
        _list.Columns.Add("修改时间", 150);
        _list.Columns.Add("权限", 100);
        _list.Columns.Add("所有者", 90);
        _list.DoubleClick += (_, _) => _ = OpenSelectedAsync();
        _list.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                _ = UploadFilesAsync(files);
            }
        };
        _list.ItemDrag += (_, _) =>
        {
            var paths = SelectedRemotePaths();
            if (paths.Count == 0)
            {
                return;
            }
            var data = new DataObject();
            data.SetData(RemoteDataFormat, paths.ToArray());
            _list.DoDragDrop(data, DragDropEffects.Copy);
        };
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional };
        XShellTheme.StyleMenu(menu);
        menu.Items.Add("上传文件...", null, (_, _) => _ = UploadAsync());
        menu.Items.Add("下载", null, (_, _) => _ = DownloadAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("新建文件夹...", null, (_, _) => _ = NewFolderAsync());
        menu.Items.Add("重命名...", null, (_, _) => _ = RenameAsync());
        menu.Items.Add("删除", null, (_, _) => _ = DeleteAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("刷新", null, (_, _) => _ = LoadDirectoryAsync(_currentPath));
        _list.ContextMenuStrip = menu;
        Controls.Add(_list);

        var listHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = XShellTheme.PanelAlt
        };
        listHeader.Paint += (_, e) =>
        {
            using var background = new SolidBrush(XShellTheme.PanelAlt);
            e.Graphics.FillRectangle(background, listHeader.ClientRectangle);
            using var pen = new Pen(XShellTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, listHeader.Height - 1,
                listHeader.Width, listHeader.Height - 1);
            var font = XShellTheme.Ui(8.4f, FontStyle.Bold);
            int x = 8;
            string[] captions = { "名称", "大小", "修改时间", "权限", "所有者" };
            for (int i = 0; i < captions.Length && i < _list.Columns.Count; i++)
            {
                int width = _list.Columns[i].Width;
                var align = i == 1
                    ? TextFormatFlags.Right
                    : TextFormatFlags.Left;
                var rect = new Rectangle(x, 0, Math.Max(10, width - 12), listHeader.Height);
                TextRenderer.DrawText(e.Graphics, captions[i], font, rect,
                    XShellTheme.Muted,
                    align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                    | TextFormatFlags.EndEllipsis);
                x += width;
            }
        };
        Controls.Add(listHeader);

        // WinForms 按 z 序反向停靠：列表先占用剩余空间，再依次停靠状态栏、表头、路径栏和工具栏。
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_status, 1);
        Controls.SetChildIndex(listHeader, 2);
        Controls.SetChildIndex(pathPanel, 3);
        Controls.SetChildIndex(_toolbar, 4);
    }

    private static ToolStripButton Button(string text, Image image, EventHandler click)
    {
        var button = new ToolStripButton(text, image, click)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Font = XShellTheme.Ui(8.5f),
            ForeColor = XShellTheme.Text,
            Margin = new Padding(1, 0, 1, 0)
        };
        return button;
    }

    public async Task ActivateAsync(string? initialPath = null)
    {
        await LoadDirectoryAsync(string.IsNullOrWhiteSpace(initialPath)
            ? (_currentPath == "/" ? _profile.RemoteStartFolder : _currentPath)
            : initialPath);
    }

    private async Task<SftpClient?> EnsureClientAsync()
    {
        if (_client is { IsConnected: true })
        {
            return _client;
        }
        try
        {
            ResetClient();
            _client = await _clientFactory();
            if (_client == null)
            {
                SetStatus("当前会话不是 SSH，SFTP 不可用。", true);
                return null;
            }
            return _client;
        }
        catch (Exception ex)
        {
            SetStatus("SFTP 连接失败：" + ex.Message, true);
            return null;
        }
    }

    private async Task<bool> WithClientAsync(Func<SftpClient, Task> action)
    {
        if (_busy)
        {
            return false;
        }
        _busy = true;
        await _gate.WaitAsync();
        try
        {
            var client = await EnsureClientAsync();
            if (client != null)
            {
                await action(client);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            SetStatus("SFTP 操作失败：" + ex.Message, true);
            return false;
        }
        finally
        {
            _gate.Release();
            _busy = false;
        }
    }

    public async Task LoadDirectoryAsync(string path)
    {
        await WithClientAsync(async client =>
        {
            path = NormalizePath(path);
            var entries = await Task.Run(() => client.ListDirectory(path).ToList());
            if (IsDisposed)
            {
                return;
            }
            _currentPath = path;
            _pathBox.Text = path;

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var file in entries
                    .Where(f => f.Name != "." && f.Name != "..")
                    .OrderByDescending(f => f.IsDirectory)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ListViewItem(file.Name)
                    {
                        Tag = file,
                        ImageIndex = file.IsDirectory ? 0 : 1
                    };
                    item.SubItems.Add(file.IsDirectory ? "<DIR>" : FormatSize(file.Length));
                    item.SubItems.Add(file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    item.SubItems.Add(Permissions(file.Attributes));
                    item.SubItems.Add(file.Attributes.UserId.ToString(CultureInfo.InvariantCulture)
                        + ":" + file.Attributes.GroupId.ToString(CultureInfo.InvariantCulture));
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            SetStatus("已加载 " + path + " · " + _list.Items.Count + " 项");
        });
    }

    private async Task OpenSelectedAsync()
    {
        if (_list.SelectedItems.Count != 1)
        {
            return;
        }
        var file = _list.SelectedItems[0].Tag as SftpFile;
        if (file == null)
        {
            return;
        }
        if (file.IsDirectory)
        {
            await LoadDirectoryAsync(CombinePath(_currentPath, file.Name));
        }
        else if (_settings.SftpDoubleClickDownloads)
        {
            await DownloadAsync();
        }
    }

    private async Task UploadAsync()
    {
        if (!_list.Enabled)
        {
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "上传文件到 " + _currentPath,
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(_settings.LastLocalFolder)
                ? _settings.LastLocalFolder : ""
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        _settings.LastLocalFolder = Path.GetDirectoryName(dialog.FileNames[0]) ?? "";
        await UploadFilesAsync(dialog.FileNames);
    }

    public async Task UploadFilesAsync(IEnumerable<string> files)
    {
        var list = files.Where(File.Exists).ToArray();
        if (list.Length == 0)
        {
            return;
        }
        bool ok = await WithClientAsync(async client =>
        {
            int done = 0;
            foreach (string local in list)
            {
                string name = Path.GetFileName(local);
                long total = 0;
                try { total = new FileInfo(local).Length; } catch { }
                UploadStarted?.Invoke(local, total);
                try
                {
                    string remote = CombinePath(_currentPath, name);
                    SetStatus("正在上传 " + name + " ...");
                    using var stream = File.OpenRead(local);
                    await Task.Run(() => client.UploadFile(stream, remote, true,
                        uploaded => UploadProgress?.Invoke(local, (long)uploaded, total)));
                    done++;
                    UploadCompleted?.Invoke(local);
                }
                catch (Exception ex)
                {
                    UploadFailed?.Invoke(local, ex.Message);
                }
            }
            SetStatus("已上传 " + done + " 个文件。");
        });
        if (!ok)
        {
            foreach (string local in list)
            {
                long total = 0;
                try { total = new FileInfo(local).Length; } catch { }
                UploadStarted?.Invoke(local, total);
                UploadFailed?.Invoke(local, "SFTP 未连接或当前正忙");
            }
            return;
        }
        await LoadDirectoryAsync(_currentPath);
    }

    private List<string> SelectedRemotePaths()
    {
        return _list.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag as SftpFile)
            .Where(file => file != null)
            .Select(file => CombinePath(_currentPath, file!.Name))
            .ToList();
    }

    /// <summary>把远程文件 / 目录下载到指定本地目录（拖放远程文件到本地窗格时使用）。</summary>
    public async Task DownloadPathsToLocalAsync(IEnumerable<string> remotePaths,
        string localFolder)
    {
        var paths = remotePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (paths.Length == 0 || string.IsNullOrWhiteSpace(localFolder))
        {
            return;
        }
        Directory.CreateDirectory(localFolder);
        await WithClientAsync(async client =>
        {
            int done = 0;
            foreach (string remote in paths)
            {
                string name = remote.TrimEnd('/').Split('/').Last();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                string local = Path.Combine(localFolder, name);
                try
                {
                    var attributes = client.GetAttributes(remote);
                    if (attributes.IsDirectory)
                    {
                        await DownloadDirectoryAsync(client, remote, local);
                    }
                    else
                    {
                        await DownloadFileAsync(client, remote, local);
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    SetStatus("下载失败：" + name + " · " + ex.Message, true);
                }
            }
            SetStatus("已下载 " + done + " 项到 " + localFolder);
        });
    }

    private async Task DownloadAsync()
    {
        var selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag as SftpFile)
            .Where(f => f != null)
            .Cast<SftpFile>()
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }
        if (selected.Length == 1 && !selected[0].IsDirectory)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "下载 " + selected[0].Name,
                FileName = selected[0].Name,
                Filter = "所有文件 (*.*)|*.*",
                InitialDirectory = Directory.Exists(_settings.LastLocalFolder)
                    ? _settings.LastLocalFolder : ""
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }
            _settings.LastLocalFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
            await DownloadOneAsync(selected[0], dialog.FileName);
            return;
        }

        using var folderDialog = new FolderBrowserDialog
        {
            Description = "选择下载保存目录",
            SelectedPath = Directory.Exists(_settings.LastLocalFolder)
                ? _settings.LastLocalFolder : ""
        };
        if (folderDialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        _settings.LastLocalFolder = folderDialog.SelectedPath;
        await WithClientAsync(async client =>
        {
            foreach (var file in selected)
            {
                string local = Path.Combine(folderDialog.SelectedPath, file.Name);
                if (file.IsDirectory)
                {
                    await DownloadDirectoryAsync(client, CombinePath(_currentPath, file.Name), local);
                }
                else
                {
                    await DownloadFileAsync(client, CombinePath(_currentPath, file.Name), local);
                }
            }
        });
        SetStatus("下载完成。");
    }

    private async Task DownloadOneAsync(SftpFile file, string localPath)
    {
        await WithClientAsync(async client =>
        {
            await DownloadFileAsync(client, CombinePath(_currentPath, file.Name), localPath);
        });
        SetStatus("已下载 " + file.Name);
    }

    private async Task DownloadFileAsync(SftpClient client, string remote, string local)
    {
        string name = Path.GetFileName(local);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        SetStatus("正在下载 " + name + " ...");
        using var stream = File.Create(local);
        await Task.Run(() => client.DownloadFile(remote, stream,
            downloaded => SetStatus("正在下载 " + name + " · " + FormatSize((long)downloaded))));
    }

    private async Task DownloadDirectoryAsync(SftpClient client, string remoteDir, string local)
    {
        Directory.CreateDirectory(local);
        var entries = await Task.Run(() => client.ListDirectory(remoteDir).ToList());
        foreach (var entry in entries.Where(e => e.Name != "." && e.Name != ".."))
        {
            string child = Path.Combine(local, entry.Name);
            string remoteChild = CombinePath(remoteDir, entry.Name);
            if (entry.IsDirectory)
            {
                await DownloadDirectoryAsync(client, remoteChild, child);
            }
            else
            {
                await DownloadFileAsync(client, remoteChild, child);
            }
        }
    }

    private async Task NewFolderAsync()
    {
        string? name = XShellPrompt.Show(FindForm(), "新建文件夹", "文件夹名称：", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        await WithClientAsync(async client =>
        {
            string remote = CombinePath(_currentPath, name.Trim());
            await Task.Run(() => client.CreateDirectory(remote));
            SetStatus("已创建文件夹 " + name);
        });
        await LoadDirectoryAsync(_currentPath);
    }

    private async Task RenameAsync()
    {
        var file = _list.SelectedItems.Count == 1
            ? _list.SelectedItems[0].Tag as SftpFile : null;
        if (file == null)
        {
            return;
        }
        string? name = XShellPrompt.Show(FindForm(), "重命名", "新名称：", file.Name);
        if (string.IsNullOrWhiteSpace(name) || name == file.Name)
        {
            return;
        }
        await WithClientAsync(async client =>
        {
            await Task.Run(() => client.RenameFile(
                CombinePath(_currentPath, file.Name), CombinePath(_currentPath, name.Trim())));
            SetStatus("已重命名 " + file.Name + " → " + name);
        });
        await LoadDirectoryAsync(_currentPath);
    }

    private async Task DeleteAsync()
    {
        var selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag as SftpFile)
            .Where(f => f != null)
            .Cast<SftpFile>()
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }
        string message = selected.Length == 1
            ? "确定删除 " + selected[0].Name + " 吗？"
            : "确定删除选中的 " + selected.Length + " 项吗？";
        if (XShellMessageBox.Show(FindForm(), message, "XShell 复刻版",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        await WithClientAsync(async client =>
        {
            foreach (var file in selected)
            {
                string remote = CombinePath(_currentPath, file.Name);
                if (file.IsDirectory)
                {
                    await Task.Run(() => DeleteDirectoryRecursive(client, remote));
                }
                else
                {
                    await Task.Run(() => client.DeleteFile(remote));
                }
            }
            SetStatus("已删除 " + selected.Length + " 项。");
        });
        await LoadDirectoryAsync(_currentPath);
    }

    private static void DeleteDirectoryRecursive(SftpClient client, string path)
    {
        foreach (var entry in client.ListDirectory(path))
        {
            if (entry.Name is "." or "..")
            {
                continue;
            }
            string child = CombinePath(path, entry.Name);
            if (entry.IsDirectory)
            {
                DeleteDirectoryRecursive(client, child);
            }
            else
            {
                client.DeleteFile(child);
            }
        }
        client.DeleteDirectory(path);
    }

    public void ResetClient()
    {
        try
        {
            if (_client is { IsConnected: true })
            {
                _client.Disconnect();
            }
            _client?.Dispose();
        }
        catch
        {
            // 忽略关闭异常
        }
        _client = null;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        XShellTheme.ApplyNativeTheme(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ResetClient();
            _gate.Dispose();
        }
        base.Dispose(disposing);
    }

    private void SetStatus(string text, bool error = false)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(() => SetStatus(text, error)); } catch { }
            return;
        }
        _status.Text = text;
        _status.ForeColor = error ? XShellTheme.Red : XShellTheme.Muted;
    }

    private static string NormalizePath(string path)
    {
        path = (path ?? "").Trim().Replace('\\', '/');
        if (path.Length == 0)
        {
            return "/";
        }
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }
        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/");
        }
        return path;
    }

    public static string CombinePath(string basePath, string name)
    {
        basePath = NormalizePath(basePath);
        return basePath == "/" ? "/" + name : basePath.TrimEnd('/') + "/" + name;
    }

    public static string ParentPath(string path)
    {
        path = NormalizePath(path).TrimEnd('/');
        if (path.Length == 0 || path == "/")
        {
            return "/";
        }
        int index = path.LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    private static string Permissions(SftpFileAttributes attributes)
    {
        var sb = new StringBuilder(10);
        sb.Append(attributes.IsDirectory ? 'd' : attributes.IsSymbolicLink ? 'l' : '-');
        sb.Append(attributes.OwnerCanRead ? 'r' : '-');
        sb.Append(attributes.OwnerCanWrite ? 'w' : '-');
        sb.Append(attributes.OwnerCanExecute ? 'x' : '-');
        sb.Append(attributes.GroupCanRead ? 'r' : '-');
        sb.Append(attributes.GroupCanWrite ? 'w' : '-');
        sb.Append(attributes.GroupCanExecute ? 'x' : '-');
        sb.Append(attributes.OthersCanRead ? 'r' : '-');
        sb.Append(attributes.OthersCanWrite ? 'w' : '-');
        sb.Append(attributes.OthersCanExecute ? 'x' : '-');
        return sb.ToString();
    }
}

internal static class XShellPrompt
{
    public static string? Show(IWin32Window? owner, string title, string label, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(390, 130),
            BackColor = XShellTheme.Background,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(9f)
        };
        form.HandleCreated += (_, _) => XShellWindowTheme.Apply(form);
        var caption = new Label
        {
            Text = label,
            Left = 16,
            Top = 16,
            Width = 350,
            Height = 20,
            ForeColor = XShellTheme.Text,
            BackColor = Color.Transparent
        };
        var input = new TextBox
        {
            Left = 16,
            Top = 42,
            Width = 356,
            Text = initial,
            Font = XShellTheme.Ui(9f),
            BackColor = XShellTheme.InputBack,
            ForeColor = XShellTheme.Text
        };
        var ok = XShellTheme.PrimaryButton("确定", 78, 27);
        ok.Left = 212;
        ok.Top = 86;
        ok.DialogResult = DialogResult.OK;
        var cancel = XShellTheme.FlatButton("取消", 78, 27);
        cancel.Left = 296;
        cancel.Top = 86;
        cancel.DialogResult = DialogResult.Cancel;
        form.Controls.AddRange(new Control[] { caption, input, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) =>
        {
            XShellTheme.ApplyNativeTheme(form);
            input.Focus();
            input.SelectAll();
        };
        return form.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }
}
