using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>Navicat 复刻版日志：按天写入 %APPDATA%\MiniMonitor\navicat\logs，便于排错。</summary>
internal static class NvLog
{
    private static readonly object Gate = new();
    private const long MaxFileBytes = 8 * 1024 * 1024;
    private const int KeepDays = 14;
    private static bool _startupLogged;

    public static string Folder => NvPaths.LogsDir;

    public static string CurrentFile =>
        Path.Combine(Folder, "navicat-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

    public static void Startup()
    {
        if (_startupLogged)
        {
            return;
        }
        _startupLogged = true;
        Info("========== Navicat 复刻版启动 ==========");
        Info("版本：" + Application.ProductVersion);
        Info("操作系统：" + Environment.OSVersion + "，64 位进程：" + Environment.Is64BitProcess);
        Info("运行时：" + Environment.Version);
        Info("程序目录：" + AppContext.BaseDirectory);
        Info("日志目录：" + Folder);
        Info("配置目录：" + NvPaths.Root);
    }

    public static void Debug(string message) => Write("DEBUG", message, null);
    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    public static void Error(Exception exception, string context) =>
        Write("ERROR", context, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append(']')
                .Append(" [T").Append(Environment.CurrentManagedThreadId).Append("] ")
                .Append(message ?? "");
            if (exception != null)
            {
                sb.AppendLine();
                sb.Append(exception);
            }
            sb.AppendLine();
            sb.AppendLine("----------------------------------------");

            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                string file = CurrentFile;
                FileInfo info = new(file);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    string rotated = Path.Combine(Folder,
                        Path.GetFileNameWithoutExtension(file) + "-"
                        + DateTime.Now.ToString("HHmmssfff") + ".log");
                    try
                    {
                        File.Move(file, rotated);
                    }
                    catch
                    {
                        // 被其他进程占用时继续追加
                    }
                }
                File.AppendAllText(file, sb.ToString(), new UTF8Encoding(false));
                CleanupOldFiles();
            }
        }
        catch
        {
            // 日志失败不能影响主功能
        }
    }

    private static void CleanupOldFiles()
    {
        try
        {
            foreach (string file in Directory.GetFiles(Folder, "navicat-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-KeepDays))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    public static IReadOnlyList<string> Files()
    {
        try
        {
            return Directory.GetFiles(Folder, "navicat-*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string ReadFile(string file, int maxLines = 6000)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
                if (lines.Count > maxLines * 2)
                {
                    lines.RemoveRange(0, maxLines);
                }
            }
            if (lines.Count > maxLines)
            {
                lines = lines.Skip(lines.Count - maxLines).ToList();
            }
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return "读取日志失败：" + ex.Message;
        }
    }

    public static string ReadCurrent(int maxLines = 6000) => ReadFile(CurrentFile, maxLines);

    public static void ClearCurrent()
    {
        try
        {
            lock (Gate)
            {
                File.WriteAllText(CurrentFile, "", new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            Error(ex, "清空日志失败");
        }
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开日志目录：\r\n" + ex.Message + "\r\n\r\n" + Folder,
                "Navicat 复刻版日志", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text ?? "";
        }
        return text[..max] + " …";
    }
}

/// <summary>日志查看器。</summary>
internal sealed class NvLogViewerForm : Form
{
    private readonly ComboBox _fileBox;
    private readonly RichTextBox _box;
    private readonly Label _status;
    private readonly CheckBox _autoRefresh;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Button _refresh;
    private bool _loading;

    public NvLogViewerForm()
    {
        Text = "Navicat 复刻版日志";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(760, 520);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("Navicat 复刻版日志",
            "记录连接、SQL 执行、导入导出、备份、同步等操作与异常堆栈", NvIcon.File);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = NvTheme.Panel };
        toolbar.Controls.Add(new Label
        {
            Text = "日志文件",
            Location = new Point(10, 13),
            AutoSize = true,
            ForeColor = NvTheme.Text
        });
        _fileBox = new ComboBox
        {
            Location = new Point(76, 9),
            Width = 360,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = NvTheme.Ui(9f)
        };
        _fileBox.SelectedIndexChanged += (_, _) => LoadFile();
        toolbar.Controls.Add(_fileBox);
        _refresh = NvTheme.FlatButton("刷新", null, null, 76);
        _refresh.Location = new Point(446, 8);
        _refresh.Click += (_, _) => LoadFile();
        var openFolder = NvTheme.FlatButton("打开日志目录", null, null, 116);
        openFolder.Location = new Point(528, 8);
        openFolder.Click += (_, _) => NvLog.OpenFolder();
        var copy = NvTheme.FlatButton("复制全部", null, null, 96);
        copy.Location = new Point(650, 8);
        copy.Click += (_, _) =>
        {
            if (_box.TextLength > 0)
            {
                Clipboard.SetText(_box.Text);
                _status.Text = "日志已复制到剪贴板。";
            }
        };
        var clear = NvTheme.FlatButton("清空当天日志", null, null, 116);
        clear.Location = new Point(752, 8);
        clear.Click += (_, _) =>
        {
            if (NvPrompt.Confirm(this, "清空日志", "确定清空当前正在写入的日志吗？"))
            {
                NvLog.ClearCurrent();
                LoadFile();
            }
        };
        _autoRefresh = new CheckBox
        {
            Text = "每 2 秒自动刷新",
            Location = new Point(880, 12),
            AutoSize = true,
            Checked = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = NvTheme.Text
        };
        toolbar.Controls.AddRange(new Control[] { _refresh, openFolder, copy, clear, _autoRefresh });

        _box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = NvTheme.Mono(9f),
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            WordWrap = false,
            DetectUrls = false
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        body.Controls.Add(_box);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Text = "就绪"
        };
        bottom.Controls.Add(_status);

        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(header);

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) =>
        {
            if (_autoRefresh.Checked && !_loading)
            {
                LoadFile(true);
            }
        };
        Load += (_, _) =>
        {
            RefreshFileList();
            _timer.Start();
        };
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
        };
    }

    private void RefreshFileList()
    {
        var files = NvLog.Files();
        _fileBox.BeginUpdate();
        _fileBox.Items.Clear();
        foreach (string file in files)
        {
            _fileBox.Items.Add(Path.GetFileName(file));
        }
        _fileBox.EndUpdate();
        if (_fileBox.Items.Count > 0)
        {
            _fileBox.SelectedIndex = 0;
        }
        else
        {
            _status.Text = "暂无日志文件：" + NvLog.Folder;
        }
    }

    private void LoadFile(bool silent = false)
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        try
        {
            var files = NvLog.Files();
            string file;
            if (_fileBox.SelectedIndex >= 0 && _fileBox.SelectedIndex < files.Count
                && string.Equals(Path.GetFileName(files[_fileBox.SelectedIndex]),
                    _fileBox.SelectedItem?.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                file = files[_fileBox.SelectedIndex];
            }
            else
            {
                file = NvLog.CurrentFile;
            }
            string text = NvLog.ReadFile(file);
            _box.Text = text;
            _box.SelectionStart = _box.TextLength;
            _box.ScrollToCaret();
            _status.Text = file + "  ·  " + text.Split('\n').Length + " 行  ·  "
                + new FileInfo(file).Length / 1024 + " KB";
            if (!silent)
            {
                _box.SelectionStart = _box.TextLength;
                _box.ScrollToCaret();
            }
        }
        catch (Exception ex)
        {
            _status.Text = "读取日志失败：" + ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }
}
