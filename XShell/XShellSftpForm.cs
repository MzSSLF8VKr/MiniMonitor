using System.Windows.Forms;
using Renci.SshNet;

namespace MiniMonitor.XShell;

/// <summary>独立弹出的 SFTP 文件传输窗口（Xftp 风格），与 XShell 复刻版共享主题。</summary>
internal sealed class XShellSftpForm : Form
{
    private readonly XShellSftpPanel _panel;
    private readonly XShellLocalFilePanel _local;
    private readonly XShellUploadQueue _queue;
    private readonly SplitContainer _split;

    public XShellSftpPanel Panel => _panel;

    public XShellSftpForm(XSessionProfile profile, XShellSettings settings,
        Func<Task<SftpClient?>> clientFactory)
    {
        Text = "SFTP - " + profile.Name;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(900, 560);
        ClientSize = new Size(1180, 720);
        BackColor = XShellTheme.Background;
        ForeColor = XShellTheme.Text;
        Font = XShellTheme.Ui(9f);
        ShowInTaskbar = true;
        DoubleBuffered = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 图标读取失败不影响窗口。
        }
        HandleCreated += (_, _) => XShellWindowTheme.Apply(this);

        var header = new XShellHeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 64,
            Title = "SFTP 文件传输",
            Subtitle = profile.DisplayAddress + "  ·  Xftp 风格双栏窗口（无需 Xftp）"
        };
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            BackColor = XShellTheme.Background
        };

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = XShellTheme.Border
        };
        _split.Panel1.BackColor = XShellTheme.Panel;
        _split.Panel2.BackColor = XShellTheme.Panel;
        string localStart = !string.IsNullOrWhiteSpace(profile.LocalStartFolder)
            && Directory.Exists(profile.LocalStartFolder)
                ? profile.LocalStartFolder : settings.LastLocalFolder;
        _local = new XShellLocalFilePanel(localStart) { Dock = DockStyle.Fill };
        _panel = new XShellSftpPanel(profile, settings, clientFactory) { Dock = DockStyle.Fill };

        var remoteHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = XShellTheme.Panel
        };
        var remoteTitle = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = XShellTheme.PanelAlt
        };
        remoteTitle.Paint += (_, e) =>
        {
            using var background = new SolidBrush(XShellTheme.PanelAlt);
            e.Graphics.FillRectangle(background, remoteTitle.ClientRectangle);
            using var accent = new SolidBrush(XShellTheme.Accent);
            e.Graphics.FillRectangle(accent, 10, 8, 3, 16);
            TextRenderer.DrawText(e.Graphics, "远程文件  ·  " + profile.DisplayAddress,
                XShellTheme.Ui(9f, FontStyle.Bold),
                new Rectangle(20, 0, remoteTitle.Width - 30, remoteTitle.Height),
                XShellTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            using var pen = new Pen(XShellTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, remoteTitle.Height - 1,
                remoteTitle.Width, remoteTitle.Height - 1);
        };
        remoteHost.Controls.Add(_panel);
        remoteHost.Controls.Add(remoteTitle);
        remoteHost.Controls.SetChildIndex(_panel, 0);
        remoteHost.Controls.SetChildIndex(remoteTitle, 1);

        _split.Panel1.Controls.Add(_local);
        _split.Panel2.Controls.Add(remoteHost);
        _queue = new XShellUploadQueue
        {
            Dock = DockStyle.Bottom,
            Height = 176
        };
        frame.Controls.Add(_split);
        frame.Controls.Add(_queue);
        Controls.Add(frame);
        Controls.Add(header);
        Controls.SetChildIndex(frame, 0);
        Controls.SetChildIndex(header, 1);

        _local.UploadRequested += files => _ = _panel.UploadFilesAsync(files);
        _local.DownloadRequested += paths => _ = DownloadToLocalAsync(paths);
        _panel.UploadStarted += (file, total) => _queue.StartTask(file, total);
        _panel.UploadProgress += (file, uploaded, total) =>
            _queue.UpdateProgress(file, uploaded, total);
        _panel.UploadCompleted += file => _queue.Complete(file);
        _panel.UploadFailed += (file, error) => _queue.Fail(file, error);

        Shown += async (_, _) =>
        {
            XShellTheme.ApplyNativeTheme(this);
            try
            {
                int width = _split.Width;
                int distance = Math.Max(300, (int)(width * 0.36));
                distance = Math.Min(distance, width - 320 - _split.SplitterWidth);
                if (distance >= 200)
                {
                    _split.SplitterDistance = distance;
                }
                _split.Panel1MinSize = 280;
                _split.Panel2MinSize = 320;
            }
            catch
            {
                // 窗口过窄时保持默认分隔位置。
            }
            try
            {
                await _panel.ActivateAsync();
            }
            catch
            {
                // 面板内部会显示错误状态。
            }
        };
    }

    public void ApplyTheme(XShellUiPalette oldPalette)
    {
        XShellTheme.Refresh(this, oldPalette);
        XShellWindowTheme.Apply(this);
        XShellTheme.ApplyNativeTheme(this);
    }

    private async Task DownloadToLocalAsync(IReadOnlyList<string> remotePaths)
    {
        try
        {
            await _panel.DownloadPathsToLocalAsync(remotePaths, _local.CurrentPath);
            await _local.RefreshAsync();
        }
        catch
        {
            // 面板内部会显示下载状态。
        }
    }
}
