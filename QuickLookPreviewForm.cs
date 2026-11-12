using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MiniMonitor.Video;

namespace MiniMonitor;

/// <summary>
/// QuickLook 复刻预览窗口：图片 / 文本 / 音频 / 视频 / PDF / HTML / Office / 压缩包 / 字体 / 文件夹（ls -lh 风格）。
/// 空格或 Esc 关闭；← / → 由服务层切换同文件夹文件。
/// </summary>
internal sealed class QuickLookPreviewForm : Form, IMessageFilter
{
    private const int WmMouseWheel = 0x020A;
    /// <summary>预览窗口隐藏后，最多 60 秒自动释放图片 / 音频 / 字体 / WebView 等资源，避免源文件被占用。</summary>
    private const int ReleaseDelayMs = 60_000;
    private readonly AppConfig _config;
    private readonly Panel _header;
    private readonly Panel _footer;
    private readonly Panel _contentHost;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Label _hintLabel;
    private readonly HeaderActionButton _openButton;
    private readonly HeaderCloseButton _closeButton;
    private readonly HeaderActionButton _copyButton;
    private readonly System.Windows.Forms.Timer _copyResetTimer;
    private readonly System.Windows.Forms.Timer _releaseTimer;
    private readonly ToolTip _toolTip;
    private readonly Font _titleFont;
    private readonly Font _subFont;
    private QuickLookAudioViewer? _audioViewer;
    private PhotoPreviewControl? _imageViewer;
    private Bitmap? _imageBitmap;
    private WebView2? _webView;
    private CoreWebView2Environment? _webEnvironment;
    private Control? _content;
    private Bitmap? _genericBitmap;
    private string? _tempPdfPath;
    private CancellationTokenSource? _videoProxyCts;
    private string _path = "";
    private string? _copyText;
    private string _loadedPath = "";
    private long _loadedLength = -1;
    private DateTime _loadedWriteTimeUtc;
    private int _loadVersion;
    private bool _disposed;
    private bool _messageFilterInstalled;

    public event Action<int>? NavigateRequested;
    public event Action? ClosedByUser;

    public QuickLookPreviewForm(AppConfig config)
    {
        _config = config;
        Text = "QuickLook复刻 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = config.QuickLookAlwaysOnTop;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(15, 23, 42);

        _titleFont = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold);
        _subFont = new Font("Microsoft YaHei UI", 8.3f);

        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        int width = Math.Clamp(config.QuickLookWidth, 420, Math.Max(420, screen.Width - 20));
        int height = Math.Clamp(config.QuickLookHeight, 320, Math.Max(320, screen.Height - 20));
        ClientSize = new Size(width, height);
        if (config.QuickLookX is int x && config.QuickLookY is int y)
        {
            Location = new Point(x, y);
            EnsureOnScreen();
        }
        else
        {
            Location = new Point(screen.Left + (screen.Width - width) / 2,
                screen.Top + (screen.Height - height) / 2);
        }

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 66,
            BackColor = Color.FromArgb(30, 41, 59)
        };
        _header.Paint += PaintHeader;

        _titleLabel = new Label
        {
            Location = new Point(24, 10),
            Size = new Size(500, 26),
            Font = _titleFont,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            Text = "QuickLook复刻"
        };
        _subtitleLabel = new Label
        {
            Location = new Point(25, 37),
            Size = new Size(600, 20),
            Font = _subFont,
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };
        _openButton = new HeaderActionButton
        {
            Size = new Size(132, 32),
            Text = "系统程序打开"
        };
        _openButton.Click += (_, _) => OpenExternal();
        _closeButton = new HeaderCloseButton { Size = new Size(32, 32) };
        _closeButton.Click += (_, _) => HidePreview();
        _copyButton = new HeaderActionButton
        {
            Size = new Size(104, 32),
            Text = "复制全部",
            Icon = HeaderIcon.Copy,
            Visible = false
        };
        _copyButton.Click += (_, _) => CopyAllText();
        _copyResetTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _copyResetTimer.Tick += (_, _) =>
        {
            _copyResetTimer.Stop();
            _copyButton.Text = "复制全部";
        };
        _releaseTimer = new System.Windows.Forms.Timer { Interval = ReleaseDelayMs };
        _releaseTimer.Tick += (_, _) => ReleaseLoadedContent();
        _toolTip = new ToolTip
        {
            InitialDelay = 400,
            ReshowDelay = 200,
            ShowAlways = true
        };
        _toolTip.SetToolTip(_openButton, "用系统默认程序打开当前文件");
        _toolTip.SetToolTip(_closeButton, "关闭预览（空格 / Esc）");
        _toolTip.SetToolTip(_copyButton, "复制全部文本内容到剪贴板");
        _header.Controls.AddRange(new Control[]
        {
            _titleLabel, _subtitleLabel, _openButton, _closeButton, _copyButton
        });
        _header.Resize += (_, _) => LayoutHeader();
        NativeWindowDrag.Attach(this, _header, p => _openButton.Bounds.Contains(p)
            || _closeButton.Bounds.Contains(p) || _copyButton.Bounds.Contains(p));
        // 预览窗口可以随意拖动：标题、副标题、底部提示与内容空白区域都支持左键拖动移动。
        NativeWindowDrag.Attach(this, _titleLabel);
        NativeWindowDrag.Attach(this, _subtitleLabel);

        _footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        _hintLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 7.8f),
            ForeColor = Color.FromArgb(100, 116, 139),
            BackColor = Color.FromArgb(15, 23, 42),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "空格 / Esc 关闭预览   ·   ← / → 切换同文件夹文件   ·   滚轮缩放   ·   右键拖动图片   ·   左键拖动窗口任意位置移动"
        };
        _footer.Controls.Add(_hintLabel);
        NativeWindowDrag.Attach(this, _footer);
        NativeWindowDrag.Attach(this, _hintLabel);

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(6, 0, 6, 6)
        };
        NativeWindowDrag.Attach(this, _contentHost);
        Controls.Add(_contentHost);
        Controls.Add(_footer);
        Controls.Add(_header);

        FormClosed += (_, _) => SaveWindowState();
        LayoutHeader();
        ShowMessage("等待预览…");
    }

    public string CurrentPath => _path;

    public void LoadFile(string path)
    {
        if (_disposed)
        {
            return;
        }
        _loadVersion++;
        int version = _loadVersion;
        _releaseTimer.Stop();
        CancelVideoProxy();
        _path = path;
        SetCopyText(null);
        DisposeContent();
        CleanupTempPdf();
        UpdateHeader(path);
        // 记录加载时的文件状态，供同一路径再次预览时判断内容是否已变化。
        _loadedPath = path;
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                _loadedLength = info.Length;
                _loadedWriteTimeUtc = info.LastWriteTimeUtc;
            }
            else
            {
                _loadedLength = -1;
            }
        }
        catch
        {
            _loadedLength = -1;
        }
        var kind = QuickLookFormats.Detect(path);
        if (!QuickLookFormats.PluginEnabled(_config, kind))
        {
            // 插件被关闭时不算“已加载内容”，重新开启后需要重新加载。
            _loadedPath = "";
            ShowMessage("该文件类型的预览插件已在设置中关闭。\n\n" + QuickLookFormats.KindName(kind));
            return;
        }
        try
        {
            switch (kind)
            {
                case QuickLookKind.Image:
                    ShowImage(path, version);
                    break;
                case QuickLookKind.ImageWeb:
                case QuickLookKind.Pdf:
                case QuickLookKind.Html:
                    ShowWebFile(path, version);
                    break;
                case QuickLookKind.Video:
                    ShowVideo(path, version);
                    break;
                case QuickLookKind.Markdown:
                    ShowMarkdown(path, version);
                    break;
                case QuickLookKind.Text:
                    if (QuickLookFormats.IsCodeFile(path))
                    {
                        ShowCode(path, version);
                    }
                    else
                    {
                        ShowTextFile(path, null);
                    }
                    break;
                case QuickLookKind.Audio:
                    ShowAudio(path);
                    break;
                case QuickLookKind.Office:
                    ShowOffice(path);
                    break;
                case QuickLookKind.Archive:
                    ShowArchive(path);
                    break;
                case QuickLookKind.Font:
                    ShowFont(path);
                    break;
                case QuickLookKind.Folder:
                    ShowFolder(path);
                    break;
                default:
                    ShowGenericFile(path);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowMessage("预览失败：\n" + ex.Message);
        }
    }

    private void UpdateHeader(string path)
    {
        string name = Directory.Exists(path)
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) + (path.EndsWith("\\") ? "" : "")
            : Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = path;
        }
        _titleLabel.Text = name;
        string kind = QuickLookFormats.KindName(QuickLookFormats.Detect(path));
        string size = "";
        try
        {
            if (File.Exists(path))
            {
                size = QuickLookFormats.FormatSize(new FileInfo(path).Length);
            }
            else if (Directory.Exists(path))
            {
                size = "文件夹";
            }
        }
        catch
        {
            // 忽略
        }
        _subtitleLabel.Text = path + (size.Length > 0 ? "  ·  " + kind + "  ·  " + size : "");
    }

    private async void ShowImage(string path, int version)
    {
        ShowMessage("正在加载图片…");
        Bitmap bitmap;
        try
        {
            bitmap = await Task.Run(() => AdvancedImageSupport.IsAdvanced(path)
                ? AdvancedImageSupport.LoadBitmap(path)
                : PhotoToolkit.LoadBitmap(path, autoOrient: true));
        }
        catch (Exception ex)
        {
            if (!_disposed && version == _loadVersion)
            {
                if (AdvancedImageSupport.IsAdvanced(path) && AdvancedImageSupport.IsPdfBased(path))
                {
                    ShowPdfCopy(path, version);
                }
                else
                {
                    ShowMessage("图片加载失败：\n" + ex.Message);
                }
            }
            return;
        }
        if (_disposed || version != _loadVersion)
        {
            bitmap.Dispose();
            return;
        }

        // 在后台先缩放到 2 倍显示尺寸：UI 线程不再对原始大图做重采样，
        // 同时保留足够细节，滚轮放大时依然清晰。
        int maxWidth = Math.Max(640, (_contentHost.ClientSize.Width - 40) * 2);
        int maxHeight = Math.Max(480, (_contentHost.ClientSize.Height - 40) * 2);
        Bitmap display = bitmap;
        if (bitmap.Width > maxWidth || bitmap.Height > maxHeight)
        {
            try
            {
                display = await Task.Run(() => PhotoToolkit.ScaleToFit(bitmap, maxWidth, maxHeight));
            }
            catch
            {
                display = bitmap;
            }
            if (display != bitmap)
            {
                bitmap.Dispose();
            }
        }
        if (_disposed || version != _loadVersion)
        {
            display.Dispose();
            return;
        }
        var viewer = new PhotoPreviewControl
        {
            Dock = DockStyle.Fill,
            SelectionEnabled = false,
            ZoomEnabled = true,
            Photo = display,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        SetContent(viewer);
        // 图片区域也可用左键拖动窗口（右键用于平移图片，互不冲突）。
        NativeWindowDrag.Attach(this, viewer);
        _imageViewer = viewer;
        _imageBitmap = display;
    }

    private void ShowTextFile(string path, string? note)
    {
        var text = QuickLookFormats.ReadText(path, out bool truncated);
        if (text == null)
        {
            ShowGenericFile(path, "该文件不是文本文件，无法按文本预览。");
            return;
        }
        ShowText(text, note);
    }

    /// <summary>代码文件：整页语法高亮预览（浅色主题，顶部可一键复制全文）。</summary>
    private void ShowCode(string path, int version)
    {
        var text = QuickLookFormats.ReadText(path, out _);
        if (text == null)
        {
            ShowTextFile(path, null);
            return;
        }
        SetCopyText(text);
        ShowWebHtml(QuickLookFormats.CodeToHtml(text, path), version);
    }

    private void ShowMarkdown(string path, int version)
    {
        var text = QuickLookFormats.ReadText(path, out _);
        if (text == null)
        {
            ShowGenericFile(path, "无法读取 Markdown 文件。");
            return;
        }
        SetCopyText(text);
        string html = QuickLookFormats.MarkdownToHtml(text);
        ShowWebHtml(html, version);
    }

    private void ShowOffice(string path)
    {
        string? text = QuickLookFormats.ExtractOfficeText(path);
        if (text == null)
        {
            ShowMessage("旧版 Office 二进制格式（.doc / .xls / .ppt）暂不支持内置渲染。\n"
                + "请点击右上角“用系统程序打开”。\n\n"
                + "docx / xlsx / pptx 可提取文字内容预览。");
            return;
        }
        ShowText(text,
            "Office 文档内容提取预览（不渲染原始版式）；复杂排版请用系统程序打开。");
    }

    private void ShowAudio(string path)
    {
        var viewer = new QuickLookAudioViewer { Dock = DockStyle.Fill };
        viewer.LoadFile(path);
        SetContent(viewer);
        _audioViewer = viewer;
    }

    private void ShowArchive(string path)
    {
        var entries = QuickLookFormats.ListArchive(path, out string? error);
        if (entries == null)
        {
            ShowMessage(error ?? "无法读取压缩包。");
            return;
        }
        var list = CreateDarkList();
        list.Columns.Add("名称", 360);
        list.Columns.Add("大小", 100);
        list.Columns.Add("压缩后", 100);
        foreach (var entry in entries)
        {
            var item = new ListViewItem(entry.Name);
            item.SubItems.Add(QuickLookFormats.FormatSize(entry.Size));
            item.SubItems.Add(entry.CompressedSize > 0
                ? QuickLookFormats.FormatSize(entry.CompressedSize) : "—");
            list.Items.Add(item);
        }
        var wrapper = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(18) };
        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "压缩包共 " + entries.Count + " 项（内置预览仅列出目录，不自动解压）",
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.FromArgb(15, 23, 42)
        };
        list.Dock = DockStyle.Fill;
        wrapper.Controls.Add(list);
        wrapper.Controls.Add(info);
        SetContent(wrapper);
    }

    private void ShowFolder(string path)
    {
        const int MaxItems = 2000;
        var rows = new StringBuilder();
        long totalBytes = 0;
        int count = 0;
        bool truncated = false;

        try
        {
            var entries = new DirectoryInfo(path)
                .EnumerateFileSystemInfos()
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxItems + 1)
                .ToArray();
            if (entries.Length > MaxItems)
            {
                truncated = true;
                entries = entries.Take(MaxItems).ToArray();
            }

            string owner = Environment.UserName;
            if (string.IsNullOrWhiteSpace(owner))
            {
                owner = "user";
            }
            owner = owner.Replace(' ', '_');
            const string group = "users";

            foreach (FileSystemInfo entry in entries)
            {
                bool isDirectory = entry is DirectoryInfo;
                FileAttributes attributes = FileAttributes.Normal;
                long size = 0;
                DateTime modified = DateTime.MinValue;
                string linkTarget = "";
                try
                {
                    attributes = entry.Attributes;
                }
                catch
                {
                    // 属性不可读时按普通文件展示
                }
                try
                {
                    if (!isDirectory && entry is FileInfo file)
                    {
                        size = file.Length;
                        totalBytes += size;
                    }
                    modified = entry.LastWriteTime;
                }
                catch
                {
                    // 单个项目读取失败不中断整个文件夹预览
                }
                try
                {
                    linkTarget = entry.LinkTarget ?? "";
                }
                catch
                {
                    // 链接目标不可用时只显示名称
                }

                string mode = BuildLsMode(entry.Name, isDirectory, attributes, linkTarget);
                long displaySize = isDirectory ? 4096 : size;
                string displayName = string.IsNullOrEmpty(linkTarget)
                    ? entry.Name
                    : entry.Name + " -> " + linkTarget;

                // 与 ls -lh 类似的列：权限、链接数、所有者、组、大小、修改时间、名称
                rows.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-10} {1,2} {2,-12} {3,-8} {4,8} {5,-13} {6}",
                    mode, 1, FitLsText(owner, 12), FitLsText(group, 8),
                    FormatLsSize(displaySize), FormatLsDate(modified), displayName));
                count++;
            }
        }
        catch (Exception ex)
        {
            ShowMessage("读取文件夹失败：\n" + ex.Message);
            return;
        }

        var listing = new StringBuilder();
        listing.Append("total ").AppendLine(FormatLsSize(totalBytes));
        listing.Append(rows);
        if (truncated)
        {
            listing.AppendLine("... 仅显示前 " + MaxItems + " 项（文件夹内还有更多项目）");
        }
        if (count == 0)
        {
            listing.AppendLine("（空文件夹）");
        }
        string text = listing.ToString();

        var wrapper = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(18)
        };
        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Text = "文件夹 · " + count + " 项 · ls -lh 风格预览"
                + (truncated ? "（超出 2000 项仅显示前 2000 项）" : "")
                + "\n权限 / 所有者 / 组按当前 Windows 用户简化显示；目录大小按 4.0K 显示",
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.FromArgb(15, 23, 42),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(226, 232, 240),
            Font = new Font("Consolas", 9.5f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            DetectUrls = false,
            HideSelection = false,
            ShortcutsEnabled = true,
            Text = text
        };
        wrapper.Controls.Add(box);
        wrapper.Controls.Add(info);
        SetContent(wrapper);
        SetCopyText(text);
    }

    private static string BuildLsMode(
        string name, bool isDirectory, FileAttributes attributes, string linkTarget)
    {
        bool readOnly = (attributes & FileAttributes.ReadOnly) != 0;
        // 仅把能解析出目标路径的重解析点当作符号链接；OneDrive 占位文件不会误标为链接。
        if (!string.IsNullOrEmpty(linkTarget))
        {
            return "lrwxrwxrwx";
        }
        if (isDirectory)
        {
            return readOnly ? "dr-xr-xr-x" : "drwxr-xr-x";
        }
        bool executable = IsExecutableName(name);
        if (readOnly)
        {
            return executable ? "-r-xr-xr-x" : "-r--r--r--";
        }
        return executable ? "-rwxr-xr-x" : "-rw-r--r--";
    }

    private static bool IsExecutableName(string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        return extension is ".exe" or ".com" or ".bat" or ".cmd" or ".msi" or ".ps1" or ".vbs";
    }

    private static string FitLsText(string text, int width)
    {
        return text.Length <= width ? text : text[..width];
    }

    /// <summary>ls -lh 风格的大小：1024 进制，单位 K/M/G/T。</summary>
    private static string FormatLsSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture);
        }
        double value = bytes / 1024.0;
        string unit = "K";
        if (value >= 1024)
        {
            value /= 1024;
            unit = "M";
        }
        if (value >= 1024)
        {
            value /= 1024;
            unit = "G";
        }
        if (value >= 1024)
        {
            value /= 1024;
            unit = "T";
        }
        string number = value >= 10
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
        return number + unit;
    }

    /// <summary>ls 风格日期：近半年显示月日时分，更早显示年份。</summary>
    private static string FormatLsDate(DateTime time)
    {
        if (time == DateTime.MinValue)
        {
            return "?";
        }
        string month = time.ToString("MMM", CultureInfo.InvariantCulture);
        if (DateTime.Now - time > TimeSpan.FromDays(180))
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} {1,2}  {2}", month, time.Day, time.Year);
        }
        return string.Format(CultureInfo.InvariantCulture,
            "{0} {1,2} {2:HH:mm}", month, time.Day, time);
    }

    /// <summary>处理页面内的复制请求（Markdown 代码块右上角“复制”）。</summary>
    private void HandleWebMessage(CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type)
                && type.GetString() == "copy"
                && root.TryGetProperty("text", out var textElement))
            {
                string text = textElement.GetString() ?? "";
                if (text.Length > 0)
                {
                    Clipboard.SetText(text);
                }
            }
        }
        catch
        {
            // 忽略非法消息
        }
    }

    private void ShowFont(string path)
    {
        var viewer = new QuickLookFontViewer { Dock = DockStyle.Fill };
        viewer.LoadFont(path);
        SetContent(viewer);
    }

    private void ShowText(string text, string? note)
    {
        var wrapper = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42) };
        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(226, 232, 240),
            Font = new Font("Consolas", 10.5f),
            WordWrap = true,
            DetectUrls = true,
            Text = text,
            ScrollBars = RichTextBoxScrollBars.ForcedVertical,
            HideSelection = true
        };
        if (note != null)
        {
            var noteLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Text = "  " + note,
                Font = new Font("Microsoft YaHei UI", 8f),
                ForeColor = Color.FromArgb(252, 211, 77),
                BackColor = Color.FromArgb(30, 41, 59)
            };
            wrapper.Controls.Add(box);
            wrapper.Controls.Add(noteLabel);
        }
        else
        {
            wrapper.Controls.Add(box);
        }
        SetContent(wrapper);
        SetCopyText(text);
    }

    private void ShowGenericFile(string path, string? message = null)
    {
        if (File.Exists(path))
        {
            var text = QuickLookFormats.ReadText(path, out _);
            if (text != null && text.Length > 0)
            {
                ShowText(text, (message ?? "未知文件类型") + "；已按文本内容预览。");
                return;
            }
        }
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42) };
        Icon? icon = null;
        try
        {
            icon = Icon.ExtractAssociatedIcon(path);
        }
        catch
        {
            // 忽略
        }
        Bitmap? iconBitmap = null;
        try
        {
            iconBitmap = icon?.ToBitmap();
        }
        catch
        {
            // 忽略
        }
        var picture = new PictureBox
        {
            Location = new Point(30, 30),
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = iconBitmap,
            BackColor = Color.Transparent
        };
        var title = new Label
        {
            Location = new Point(112, 34),
            Size = new Size(500, 30),
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Text = Path.GetFileName(path),
            AutoEllipsis = true
        };
        string info = message ?? ("此文件类型暂无专用预览。");
        try
        {
            if (File.Exists(path))
            {
                info += "\n\n大小：" + QuickLookFormats.FormatSize(new FileInfo(path).Length)
                    + "\n修改时间：" + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        catch
        {
            // 忽略
        }
        var detail = new Label
        {
            Location = new Point(112, 68),
            Size = new Size(600, 150),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.Transparent,
            Text = info
        };
        panel.Controls.AddRange(new Control[] { picture, title, detail });
        panel.Resize += (_, _) =>
        {
            title.Width = Math.Max(120, panel.ClientSize.Width - title.Left - 30);
            detail.Width = Math.Max(120, panel.ClientSize.Width - detail.Left - 30);
        };
        SetContent(panel);
        _genericBitmap = iconBitmap;
        if (icon != null)
        {
            icon.Dispose();
        }
    }

    private async void ShowWebFile(string path, int version)
    {
        await ShowWebCoreAsync(null, new Uri(path), version, path);
    }

    /// <summary>
    /// 浏览器不能直接播放的 TS / RMVB / FLV / AVI / MKV 等，
    /// 用 FFmpeg 生成前 2 分钟的 H.264 预览代理后交给 WebView2。
    /// </summary>
    private async void ShowVideo(string path, int version)
    {
        if (!NeedsVideoProxy(path))
        {
            ShowWebFile(path, version);
            return;
        }
        ShowMessage("正在生成视频预览…\n\n"
            + "TS / RMVB / FLV / AVI 等格式需要 FFmpeg 转成浏览器可播放的预览副本。\n"
            + "首次预览会稍慢，生成后会缓存。\n\n"
            + Path.GetFileName(path));
        try
        {
            string? proxy = await BuildVideoProxyAsync(path, version);
            if (_disposed || version != _loadVersion)
            {
                return;
            }
            if (proxy == null)
            {
                ShowMessage("视频预览失败：无法生成预览文件。\n\n"
                    + "请确认 ffmpeg.exe 可用（帮助 → 设置 FFmpeg），"
                    + "或点击右上角“系统程序打开”。\n\n" + path);
                return;
            }
            ShowWebFile(proxy, version);
        }
        catch (OperationCanceledException)
        {
            // 切换文件或关闭窗口时忽略
        }
        catch (Exception ex)
        {
            if (!_disposed && version == _loadVersion)
            {
                ShowMessage("视频预览失败：\n" + ex.Message + "\n\n" + path);
            }
            VideoLog.Warn("QuickLook 视频预览失败：" + path + " · " + ex.Message);
        }
    }

    private static bool NeedsVideoProxy(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".ts" or ".m2ts" or ".mts" or ".rm" or ".rmvb"
            or ".flv" or ".avi" or ".wmv" or ".vob" or ".asf" or ".f4v"
            or ".mkv" or ".3gp" or ".mpg" or ".mpeg";
    }

    private async Task<string?> BuildVideoProxyAsync(string path, int version)
    {
        string? ffmpeg = VideoToolkit.Ffmpeg(_config.FfmpegPath);
        if (ffmpeg == null)
        {
            return null;
        }
        string folder = Path.Combine(AppPaths.Temp, "QuickLookVideo");
        Directory.CreateDirectory(folder);
        CleanupVideoProxyCache(folder);
        string fullPath = Path.GetFullPath(path);
        string key = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(fullPath + "|"
                    + new FileInfo(fullPath).Length + "|"
                    + File.GetLastWriteTimeUtc(fullPath).Ticks)))[..16];
        string output = Path.Combine(folder, key + ".mp4");
        if (File.Exists(output) && new FileInfo(output).Length > 1024)
        {
            return output;
        }
        string temp = Path.Combine(folder,
            key + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp.mp4");
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _videoProxyCts = cts;
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y", "-i", fullPath,
            "-t", "120",
            "-map", "0:v:0", "-map", "0:a:0?",
            "-vf", "scale=1280:720:force_original_aspect_ratio=decrease,"
                + "pad=1280:720:(ow-iw)/2:(oh-ih)/2:black",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k",
            "-sn", "-dn", "-movflags", "+faststart", temp
        };
        VideoLog.Info("QuickLook 视频预览代理：" + fullPath);
        var result = await VideoToolkit.RunFfmpegAsync(_config.FfmpegPath, args, 120,
            null, cts.Token);
        if (_disposed || version != _loadVersion || cts.IsCancellationRequested)
        {
            TryDelete(temp);
            return null;
        }
        long size = File.Exists(temp) ? new FileInfo(temp).Length : 0;
        if (result.ExitCode != 0 || size < 1024)
        {
            TryDelete(temp);
            VideoLog.Error("QuickLook 视频预览代理失败：exit=" + result.ExitCode
                + " size=" + size + " · " + fullPath + "\n" + Tail(result.StdErr, 1000));
            return null;
        }
        try
        {
            File.Move(temp, output, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            return File.Exists(output) ? output : null;
        }
        return output;
    }

    private static void CleanupVideoProxyCache(string folder)
    {
        try
        {
            foreach (string pattern in new[] { "*.mp4", "*.tmp.mp4" })
            {
                foreach (string file in Directory.GetFiles(folder, pattern))
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                    {
                        File.Delete(file);
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private void CancelVideoProxy()
    {
        try
        {
            _videoProxyCts?.Cancel();
        }
        catch
        {
            // CTS 可能已随上一次预览结束而释放
        }
        _videoProxyCts = null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string Tail(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        text = text.Trim();
        return text.Length <= maxLength ? text : text[^maxLength..];
    }

    /// <summary>AI（PDF 兼容文件）没有位图预览时，复制为临时 PDF 交给 WebView2 显示。</summary>
    private async void ShowPdfCopy(string source, int version)
    {
        string temp = "";
        try
        {
            string folder = Path.Combine(AppPaths.Temp, "Preview");
            Directory.CreateDirectory(folder);
            temp = Path.Combine(folder, "preview-" + Guid.NewGuid().ToString("N") + ".pdf");
            string from = source;
            string target = temp;
            await Task.Run(() => File.Copy(from, target, true));
            if (_disposed || version != _loadVersion)
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // ignore
                }
                return;
            }
            CleanupTempPdf();
            _tempPdfPath = temp;
            ShowWebFile(temp, version);
        }
        catch (Exception ex)
        {
            if (!_disposed && version == _loadVersion)
            {
                ShowMessage("AI / PDF 预览失败：\n" + ex.Message);
            }
        }
    }

    private void CleanupTempPdf()
    {
        if (string.IsNullOrEmpty(_tempPdfPath))
        {
            return;
        }
        try
        {
            File.Delete(_tempPdfPath);
        }
        catch
        {
            // 临时文件被占用时交给系统清理
        }
        _tempPdfPath = null;
    }

    private async void ShowWebHtml(string html, int version)
    {
        await ShowWebCoreAsync(html, null, version, "");
    }

    private async Task ShowWebCoreAsync(string? html, Uri? source, int version, string path)
    {
        var web = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(15, 23, 42)
        };
        DisposeContent();
        _webView = web;
        _content = web;
        _contentHost.Controls.Add(web);
        try
        {
            string userData = AppPaths.Sub("WebView2");
            Directory.CreateDirectory(userData);
            if (_webEnvironment == null)
            {
                _webEnvironment = await CoreWebView2Environment.CreateAsync(null, userData);
            }
            await web.EnsureCoreWebView2Async(_webEnvironment);
            if (_disposed || version != _loadVersion)
            {
                web.Dispose();
                return;
            }
            var settings = web.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            web.CoreWebView2.WebMessageReceived += (_, args) => HandleWebMessage(args);
            if (html != null)
            {
                web.CoreWebView2.NavigateToString(html);
            }
            else if (source != null)
            {
                web.Source = source;
            }
        }
        catch (Exception ex)
        {
            if (_disposed || version != _loadVersion)
            {
                web.Dispose();
                return;
            }
            ShowMessage("网页 / 视频 / PDF 预览需要 Microsoft Edge WebView2 Runtime。\n"
                + "请安装 WebView2 Runtime 后重试。\n\n" + ex.Message + (path.Length > 0 ? "\n\n" + path : ""));
        }
    }

    private ListView CreateDarkList()
    {
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(226, 232, 240),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        // 用 16×24 的透明占位图控制行高，避免列表行挤在一起。
        // 注意：ImageList 会持有该 Bitmap 直到句柄创建/绘制，不能在这里提前 Dispose，
        // 否则列表绘制时会抛出 GDI+ “Parameter is not valid”。这里整体做兜底，
        // 即使行高设置失败也保证列表本身可用。
        ImageList? rowImages = null;
        Bitmap? spacer = null;
        try
        {
            rowImages = new ImageList
            {
                ImageSize = new Size(16, 24),
                ColorDepth = ColorDepth.Depth32Bit
            };
            spacer = new Bitmap(16, 24);
            rowImages.Images.Add(spacer);
            list.SmallImageList = rowImages;
            var images = rowImages;
            var image = spacer;
            list.Disposed += (_, _) =>
            {
                images.Dispose();
                image.Dispose();
            };
        }
        catch
        {
            rowImages?.Dispose();
            spacer?.Dispose();
        }
        return list;
    }

    private void ShowMessage(string text)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42) };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 10f),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(40)
        };
        panel.Controls.Add(label);
        SetContent(panel);
    }

    private void SetContent(Control content)
    {
        DisposeContent();
        _content = content;
        content.Dock = DockStyle.Fill;
        _contentHost.Controls.Add(content);
    }

    private void DisposeContent()
    {
        if (_audioViewer != null)
        {
            _audioViewer.Dispose();
            _audioViewer = null;
        }
        if (_webView != null)
        {
            _webView.Dispose();
            _webView = null;
        }
        if (_content != null)
        {
            _content.Dispose();
            _content = null;
        }
        _imageViewer = null;
        _imageBitmap?.Dispose();
        _imageBitmap = null;
        if (_genericBitmap != null)
        {
            _genericBitmap.Dispose();
            _genericBitmap = null;
        }
        _contentHost.Controls.Clear();
    }

    private void OpenExternal()
    {
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开文件：\n" + ex.Message,
                "QuickLook复刻", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LayoutHeader()
    {
        if (_openButton == null || _closeButton == null || _copyButton == null)
        {
            return;
        }
        int right = _header.ClientSize.Width - 14;
        _closeButton.Location = new Point(right - _closeButton.Width, 17);
        _openButton.Location = new Point(_closeButton.Left - _openButton.Width - 10, 17);
        int leftLimit = _openButton.Left;
        if (_copyButton.Visible)
        {
            _copyButton.Location = new Point(leftLimit - _copyButton.Width - 8, 17);
            leftLimit = _copyButton.Left;
        }
        _titleLabel.Width = Math.Max(80, leftLimit - _titleLabel.Left - 16);
        _subtitleLabel.Width = Math.Max(80, leftLimit - _subtitleLabel.Left - 16);
    }

    /// <summary>设置顶部“复制全部”按钮的可见性与待复制文本。</summary>
    private void SetCopyText(string? text)
    {
        _copyText = text;
        _copyResetTimer.Stop();
        _copyButton.Text = "复制全部";
        _copyButton.Visible = !string.IsNullOrEmpty(text);
        LayoutHeader();
    }

    private void CopyAllText()
    {
        if (string.IsNullOrEmpty(_copyText))
        {
            return;
        }
        try
        {
            Clipboard.SetText(_copyText);
            _copyButton.Text = "已复制";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        catch
        {
            _copyButton.Text = "复制失败";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
    }

    /// <summary>当前文件自上次加载后是否未被修改（用于同一文件再次空格时避免旧内容）。</summary>
    public bool ContentUpToDate(string path)
    {
        if (!string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            if (Directory.Exists(path))
            {
                return false;
            }
            var info = new FileInfo(path);
            return info.Length == _loadedLength
                && info.LastWriteTimeUtc == _loadedWriteTimeUtc;
        }
        catch
        {
            return false;
        }
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(24, 33, 51), Color.FromArgb(38, 52, 76),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var accent = new Pen(Color.FromArgb(56, 189, 248), 2f))
        {
            g.DrawLine(accent, 0, _header.Height - 1, _header.Width * 0.28f, _header.Height - 1);
        }
        using (var line = new Pen(Color.FromArgb(51, 65, 85), 1f))
        {
            g.DrawLine(line, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        }
    }

    private void EnsureOnScreen()
    {
        var area = Screen.FromPoint(Location).WorkingArea;
        int x = Math.Clamp(Location.X, area.Left, Math.Max(area.Left, area.Right - 100));
        int y = Math.Clamp(Location.Y, area.Top, Math.Max(area.Top, area.Bottom - 80));
        Location = new Point(x, y);
    }

    private void SaveWindowState()
    {
        _config.QuickLookX = Location.X;
        _config.QuickLookY = Location.Y;
        _config.QuickLookWidth = ClientSize.Width;
        _config.QuickLookHeight = ClientSize.Height;
        _config.Save();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 预览窗口不做透明度动画：分层窗口会显著拖慢图片首帧与拖动/缩放。
        Opacity = 1f;
        if (!_messageFilterInstalled)
        {
            Application.AddMessageFilter(this);
            _messageFilterInstalled = true;
        }
    }

    /// <summary>把屏幕坐标的滚轮事件转发给图片控件；命中图片区域时返回 true（吞掉滚轮）。</summary>
    public bool TryZoomImageAtScreenPoint(Point screenPoint, int delta)
    {
        if (_disposed || !Visible || _imageViewer == null || _imageViewer.IsDisposed)
        {
            return false;
        }
        var clientPoint = _imageViewer.PointToClient(screenPoint);
        if (!_imageViewer.ClientRectangle.Contains(clientPoint))
        {
            return false;
        }
        _imageViewer.ApplyWheelZoom(clientPoint, delta);
        return true;
    }

    /// <summary>
    /// 预览窗口显示时不抢焦点，滚轮消息会发给资源管理器而不是本窗口；
    /// 窗口被激活时再由这里兜底，保证光标在图片上时滚轮始终用于缩放。
    /// </summary>
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseWheel || !Visible || _imageViewer == null
            || _imageViewer.IsDisposed)
        {
            return false;
        }
        try
        {
            long lParam = m.LParam.ToInt64();
            int screenX = (short)(lParam & 0xFFFF);
            int screenY = (short)((lParam >> 16) & 0xFFFF);
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            return TryZoomImageAtScreenPoint(new Point(screenX, screenY), delta);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>隐藏而不销毁窗口，下一次空格可以直接复用，避免重复创建窗体和控件。</summary>
    /// <remarks>
    /// 隐藏时立即停止视频转码代理并暂停音频，同时启动 60 秒倒计时：
    /// 若期间未再次预览，则自动释放图片 / 音频 / 字体 / WebView 等内容，
    /// 确保预览过的源文件最多 1 分钟后不再被本程序占用（可删除 / 移动）。
    /// </remarks>
    public void HidePreview()
    {
        if (_disposed)
        {
            return;
        }
        _audioViewer?.PausePlayback();
        CancelVideoProxy();
        _releaseTimer.Stop();
        _releaseTimer.Start();
        SaveWindowState();
        Hide();
    }

    /// <summary>预览隐藏超过释放延迟后释放全部内容资源，解除源文件占用。</summary>
    private void ReleaseLoadedContent()
    {
        _releaseTimer.Stop();
        if (_disposed || Visible)
        {
            return;
        }
        CancelVideoProxy();
        DisposeContent();
        CleanupTempPdf();
        _loadedPath = "";
        _loadedLength = -1;
        ShowMessage("等待预览…");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space)
        {
            HidePreview();
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Left or Keys.Up or Keys.Right or Keys.Down)
        {
            NavigateRequested?.Invoke(e.KeyCode is Keys.Left or Keys.Up ? -1 : 1);
            e.Handled = true;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float radius = 14f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        if (_header != null)
        {
            _header.Invalidate();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ClosedByUser?.Invoke();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            int screenX = (short)(m.LParam.ToInt64() & 0xFFFF);
            int screenY = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
            var point = PointToClient(new Point(screenX, screenY));
            const int grip = 6;
            bool left = point.X <= grip;
            bool right = point.X >= ClientSize.Width - grip;
            bool top = point.Y <= grip;
            bool bottom = point.Y >= ClientSize.Height - grip;
            if (top && left) { m.Result = (IntPtr)13; return; }
            if (top && right) { m.Result = (IntPtr)14; return; }
            if (bottom && left) { m.Result = (IntPtr)16; return; }
            if (bottom && right) { m.Result = (IntPtr)17; return; }
            if (left) { m.Result = (IntPtr)10; return; }
            if (right) { m.Result = (IntPtr)11; return; }
            if (top) { m.Result = (IntPtr)12; return; }
            if (bottom) { m.Result = (IntPtr)15; return; }
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            if (_messageFilterInstalled)
            {
                Application.RemoveMessageFilter(this);
                _messageFilterInstalled = false;
            }
            CancelVideoProxy();
            DisposeContent();
            CleanupTempPdf();
            _releaseTimer.Stop();
            _releaseTimer.Dispose();
            _toolTip.Dispose();
            _copyResetTimer.Stop();
            _copyResetTimer.Dispose();
            _titleFont.Dispose();
            _subFont.Dispose();
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

/// <summary>预览窗口右上角操作按钮的图标类型。</summary>
internal enum HeaderIcon
{
    OpenExternal,
    Copy
}

/// <summary>预览窗口右上角的深色玻璃质感操作按钮。</summary>
internal sealed class HeaderActionButton : Control
{
    private bool _hover;
    private bool _pressed;

    /// <summary>按钮图标：外链打开或复制。</summary>
    public HeaderIcon Icon { get; set; } = HeaderIcon.OpenExternal;

    public HeaderActionButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
        ForeColor = Color.White;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color fill = _pressed
            ? Color.FromArgb(96, 56, 189, 248)
            : _hover ? Color.FromArgb(58, 56, 189, 248) : Color.FromArgb(26, 255, 255, 255);
        Color border = _pressed
            ? Color.FromArgb(200, 125, 211, 252)
            : _hover ? Color.FromArgb(130, 125, 211, 252) : Color.FromArgb(48, 255, 255, 255);
        using (var path = ShutdownTheme.RoundRect(rect, 9))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
        }

        // 图标：外链 / 复制，按按钮高度垂直居中。
        float iconX = 12f;
        float iconTop = (Height - 14f) / 2f;
        Color iconColor = _hover ? Color.White : Color.FromArgb(203, 213, 225);
        using (var iconPen = new Pen(iconColor, 1.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            if (Icon == HeaderIcon.Copy)
            {
                using var backPath = ShutdownTheme.RoundRect(
                    new RectangleF(iconX + 4f, iconTop + 1f, 8.5f, 9.5f), 2f);
                using var frontPath = ShutdownTheme.RoundRect(
                    new RectangleF(iconX, iconTop + 4.5f, 8.5f, 9.5f), 2f);
                g.DrawPath(iconPen, backPath);
                g.DrawPath(iconPen, frontPath);
            }
            else
            {
                using var boxPath = ShutdownTheme.RoundRect(
                    new RectangleF(iconX, iconTop + 4f, 9f, 9f), 2f);
                g.DrawPath(iconPen, boxPath);
                g.DrawLine(iconPen, iconX + 6f, iconTop + 8f, iconX + 13f, iconTop + 1f);
                g.DrawLine(iconPen, iconX + 8.5f, iconTop + 1f, iconX + 13f, iconTop + 1f);
                g.DrawLine(iconPen, iconX + 13f, iconTop + 1f, iconX + 13f, iconTop + 5.5f);
            }
        }

        var textRect = new Rectangle(34, 0, Math.Max(1, Width - 40), Height);
        TextRenderer.DrawText(g, Text, Font, textRect,
            _hover ? Color.White : Color.FromArgb(226, 232, 240),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }
}

/// <summary>预览窗口右上角的关闭按钮，悬停时显示红色圆角底。</summary>
internal sealed class HeaderCloseButton : Control
{
    private bool _hover;
    private bool _pressed;

    public HeaderCloseButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        if (_hover || _pressed)
        {
            using var path = ShutdownTheme.RoundRect(rect, 9);
            using var brush = new SolidBrush(_pressed
                ? Color.FromArgb(185, 28, 28)
                : Color.FromArgb(239, 68, 68));
            g.FillPath(brush, path);
        }
        using var pen = new Pen(_hover
            ? Color.White
            : Color.FromArgb(203, 213, 225), 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        float inset = 10.5f;
        g.DrawLine(pen, inset, inset, Width - inset, Height - inset);
        g.DrawLine(pen, Width - inset, inset, inset, Height - inset);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }
}
