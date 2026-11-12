using System.Diagnostics;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Renci.SshNet;

namespace MiniMonitor.XShell;

/// <summary>
/// 基于 xterm.js 的终端控件：SSH / Telnet / 串口 / 本地 Shell 共用；
/// 复制粘贴只通过终端右键菜单完成，Ctrl+C 始终发送中断字符。
/// </summary>
public sealed class XShellTerminalControl : UserControl
{
    private readonly XSessionProfile _profile;
    private readonly XShellStore _store;
    private readonly XShellSettings _settings;
    private readonly WebView2 _web;
    private readonly Label _errorLabel;
    private readonly StringBuilder _pendingOutput = new();
    private readonly object _pendingLock = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IXShellTransport? _transport;
    private CancellationTokenSource? _connectCts;
    private CoreWebView2? _core;
    private bool _webReady;
    private bool _webInitialized;
    private bool _connecting;
    private bool _closedRaised;
    private int _columns;
    private int _rows;
    private StreamWriter? _logWriter;
    private readonly StringBuilder _logLine = new();
    private readonly object _logLock = new();
    private int _logCursor;
    private int _logSavedCursor;
    private string _logPendingEscape = "";
    private string _statusText = "未连接";
    private string _title;

    public XSessionProfile Profile => _profile;
    public string Title => _title;
    public string StatusText => _statusText;
    public bool IsConnected => _transport?.IsConnected == true;
    /// <summary>shell 通过 OSC 7 上报的当前工作目录（可能为空）。</summary>
    public string CurrentDirectory { get; private set; } = "";

    private string _titleDirectory = "";

    /// <summary>优先使用 OSC 7 目录，其次使用终端标题中的目录。</summary>
    public string EffectiveDirectory =>
        !string.IsNullOrWhiteSpace(CurrentDirectory) ? CurrentDirectory : _titleDirectory;
    public int Columns => _columns;
    public int Rows => _rows;

    public event Action<XShellTerminalControl>? StateChanged;
    public event Action<XShellTerminalControl, string>? TitleChanged;
    public event Action<XShellTerminalControl>? FullScreenRequested;

    public XShellTerminalControl(XSessionProfile profile, XShellStore store, XShellSettings settings)
    {
        _profile = profile;
        _store = store;
        _settings = settings;
        _columns = Math.Clamp(profile.Columns, 20, 500);
        _rows = Math.Clamp(profile.Rows, 5, 300);
        _title = profile.Name;
        BackColor = XShellTheme.Terminal;
        DoubleBuffered = true;

        _web = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = XShellTheme.Terminal
        };
        _web.CoreWebView2InitializationCompleted += OnWebInitialized;
        Controls.Add(_web);

        _errorLabel = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = XShellTheme.Terminal,
            ForeColor = Color.FromArgb(232, 168, 124),
            Font = XShellTheme.Ui(10f),
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false,
            Padding = new Padding(30)
        };
        Controls.Add(_errorLabel);
        _errorLabel.BringToFront();

        HandleCreated += (_, _) => _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        if (_webInitialized || IsDisposed)
        {
            return;
        }
        _webInitialized = true;
        try
        {
            string root = XShellWebAssets.EnsureExtracted();
            var environment = await XShellWebAssets.GetEnvironmentAsync();
            if (IsDisposed)
            {
                return;
            }
            await _web.EnsureCoreWebView2Async(environment);
            if (IsDisposed || _web.CoreWebView2 == null)
            {
                return;
            }
            _core = _web.CoreWebView2;
            var settings = _core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            settings.AreHostObjectsAllowed = false;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NewWindowRequested += OnNewWindowRequested;
            _core.NavigationStarting += OnNavigationStarting;
            _core.SetVirtualHostNameToFolderMapping(XShellWebAssets.HostName, root,
                CoreWebView2HostResourceAccessKind.Allow);
            _core.Navigate($"https://{XShellWebAssets.HostName}/terminal.html");
        }
        catch (Exception ex)
        {
            ShowWebError(
                "终端渲染组件加载失败。\r\n\r\n"
                + "XShell 复刻版使用 Microsoft Edge WebView2 渲染终端（Windows 10/11 通常已自带）。\r\n"
                + "请安装 WebView2 Runtime 后重新打开本窗口。\r\n\r\n"
                + ex.Message);
        }
    }

    private void OnWebInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowWebError("终端渲染组件初始化失败：\r\n\r\n" + e.InitializationException?.Message);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Uri.StartsWith($"https://{XShellWebAssets.HostName}/",
                StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
        }
        catch
        {
            // 链接打开失败不影响终端
        }
    }

    private void ShowWebError(string text)
    {
        if (IsDisposed)
        {
            return;
        }
        _errorLabel.Text = text;
        _errorLabel.Visible = true;
        _errorLabel.BringToFront();
        SetStatus("终端组件不可用");
    }

    public async Task ConnectAsync()
    {
        if (_connecting || IsConnected)
        {
            return;
        }
        _connecting = true;
        _closedRaised = false;
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _connectCts.Token;
        CurrentDirectory = "";
        _titleDirectory = "";
        SetStatus("正在连接...");
        AppendSystem("\r\n\x1b[36m正在连接 " + _profile.DisplayAddress + " ...\x1b[0m\r\n");

        try
        {
            _transport = XShellTransportFactory.Create(_profile, _store, VerifyHostKey);
            _transport.Output += OnTransportOutput;
            _transport.Closed += OnTransportClosed;
            await _transport.ConnectAsync(_columns, _rows, token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            SetStatus("已连接");
            if (_profile.LogSession)
            {
                try { StartLogging(); } catch { }
            }
            AppendSystem("\x1b[32m连接成功。\x1b[0m\r\n");
            FocusTerminal();
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            SetStatus("连接失败");
            AppendSystem("\r\n\x1b[31m连接失败：" + EscapeAnsi(ex.Message) + "\x1b[0m\r\n");
        }
        finally
        {
            _connecting = false;
            StateChanged?.Invoke(this);
        }
    }

    public async Task ReconnectAsync()
    {
        Disconnect("正在重新连接...");
        await Task.Delay(180);
        await ConnectAsync();
    }

    public void Disconnect(string reason = "已断开连接。")
    {
        _closedRaised = true;
        try { _connectCts?.Cancel(); } catch { }
        var transport = _transport;
        _transport = null;
        if (transport != null)
        {
            try { transport.Disconnect(reason); } catch { }
            _ = transport.DisposeAsync().AsTask();
        }
        CloseLog();
        SetStatus("已断开");
        AppendSystem("\r\n\x1b[33m" + EscapeAnsi(reason) + "\x1b[0m\r\n");
        StateChanged?.Invoke(this);
    }

    private void OnTransportOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        PostOutput(text);
    }

    private void OnTransportClosed(string reason)
    {
        if (_closedRaised || IsDisposed)
        {
            return;
        }
        _closedRaised = true;
        CloseLog();
        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnTransportClosed(reason)); } catch { }
            return;
        }
        SetStatus("已断开");
        AppendSystem("\r\n\x1b[33m" + EscapeAnsi(reason) + "\x1b[0m\r\n");
        StateChanged?.Invoke(this);
    }

    private bool VerifyHostKey(string host, string fingerprint)
    {
        if (IsDisposed)
        {
            return false;
        }
        if (InvokeRequired)
        {
            try
            {
                return (bool)Invoke(new Func<bool>(() => VerifyHostKey(host, fingerprint)));
            }
            catch
            {
                return false;
            }
        }
        var result = XShellMessageBox.Show(
            "无法验证主机 " + host + " 的真实性。\r\n\r\n"
            + "指纹：" + fingerprint + "\r\n\r\n"
            + "请确认该指纹与服务端一致。是否信任并继续连接？",
            "XShell 复刻版 - 主机密钥验证",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return result == DialogResult.Yes;
    }

    private void UpdateTitleDirectory(string title)
    {
        int colon = title.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0 || colon + 2 >= title.Length)
        {
            return;
        }
        string dir = title[(colon + 2)..].Trim();
        if (dir.StartsWith("/", StringComparison.Ordinal))
        {
            _titleDirectory = dir;
            return;
        }
        if (dir == "~" || dir.StartsWith("~/", StringComparison.Ordinal))
        {
            string home = string.Equals(_profile.UserName, "root",
                StringComparison.OrdinalIgnoreCase)
                ? "/root" : "/home/" + _profile.UserName;
            _titleDirectory = dir == "~" ? home : home + dir[1..];
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            string type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString() ?? "" : "";
            switch (type)
            {
                case "ready":
                    _webReady = true;
                    SendInit();
                    FlushPendingOutput();
                    FocusTerminal();
                    break;
                case "input":
                    if (root.TryGetProperty("data", out var input))
                    {
                        _ = SendInputAsync(input.GetString() ?? "");
                    }
                    break;
                case "resize":
                    if (root.TryGetProperty("cols", out var cols)
                        && root.TryGetProperty("rows", out var rows)
                        && cols.TryGetInt32(out int c) && rows.TryGetInt32(out int r))
                    {
                        _columns = Math.Clamp(c, 20, 500);
                        _rows = Math.Clamp(r, 5, 300);
                        _transport?.Resize(_columns, _rows);
                    }
                    break;
                case "copy":
                    if (root.TryGetProperty("text", out var textElement))
                    {
                        string text = textElement.GetString() ?? "";
                        if (text.Length > 0)
                        {
                            CopyToClipboard(text);
                        }
                    }
                    break;
                case "pasteRequest":
                    PasteFromClipboard();
                    break;
                case "fontDelta":
                    if (root.TryGetProperty("delta", out var deltaElement)
                        && deltaElement.TryGetInt32(out int delta))
                    {
                        int size = Math.Clamp(_profile.FontSize + delta, 8, 32);
                        _profile.FontSize = size;
                        SetFontSize(size);
                    }
                    break;
                case "fontReset":
                    _profile.FontSize = Math.Clamp(_settings.FontSize, 8, 32);
                    SetFontSize(_profile.FontSize);
                    break;
                case "fullscreen":
                    FullScreenRequested?.Invoke(this);
                    break;
                case "logAction":
                    if (root.TryGetProperty("action", out var actionElement))
                    {
                        try
                        {
                            switch (actionElement.GetString())
                            {
                                case "start":
                                    StartLogging();
                                    break;
                                case "stop":
                                    StopLogging();
                                    break;
                                case "open":
                                    OpenLogFolder();
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            XShellMessageBox.Show(FindForm(),
                                "日志操作失败：\r\n\r\n" + ex.Message, "XShell 复刻版",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    break;
                case "cwd":
                    if (root.TryGetProperty("path", out var cwdElement))
                    {
                        string cwd = cwdElement.GetString() ?? "";
                        if (cwd.Length > 0)
                        {
                            CurrentDirectory = cwd;
                        }
                    }
                    break;
                case "title":
                    if (root.TryGetProperty("title", out var titleElement))
                    {
                        string title = titleElement.GetString() ?? "";
                        if (title.Length > 0 && title != _title)
                        {
                            _title = title;
                            UpdateTitleDirectory(title);
                            TitleChanged?.Invoke(this, title);
                        }
                    }
                    break;
            }
        }
        catch
        {
            // 忽略格式异常消息。
        }
    }

    private void SendInit()
    {
        var palette = XShellPalettes.Get(
            string.IsNullOrWhiteSpace(_profile.ColorScheme)
                ? _settings.TerminalTheme : _profile.ColorScheme);
        Post(new
        {
            type = "init",
            fontSize = _profile.FontSize > 0 ? _profile.FontSize : _settings.FontSize,
            fontFamily = string.IsNullOrWhiteSpace(_settings.FontFamily)
                ? "Consolas" : _settings.FontFamily,
            scrollback = _profile.Scrollback > 0 ? _profile.Scrollback : _settings.Scrollback,
            theme = palette.ToWebTheme(),
            uiDark = XShellTheme.IsDark
        });
    }

    public void ApplySettings()
    {
        var palette = XShellPalettes.Get(_settings.TerminalTheme);
        Post(new
        {
            type = "init",
            fontSize = _settings.FontSize,
            fontFamily = _settings.FontFamily,
            scrollback = _settings.Scrollback,
            theme = palette.ToWebTheme(),
            uiDark = XShellTheme.IsDark
        });
    }

    /// <summary>界面主题（深/浅色）变化时同步终端右键菜单与查找栏的配色。</summary>
    public void ApplyUiTheme() =>
        Post(new { type = "uiTheme", dark = XShellTheme.IsDark });

    public void FocusTerminal() => Post(new { type = "focus" });
    public void Search() => Post(new { type = "search" });
    public void SelectAll() => Post(new { type = "selectAll" });
    public void ClearScreen() => Post(new { type = "clear" });
    public void Refit() => Post(new { type = "refit" });
    public void RequestCopy() => Post(new { type = "requestCopy" });
    public void Paste() => PasteFromClipboard();

    public async Task SendComposeLineAsync(string text)
    {
        var transport = _transport;
        if (transport == null || !transport.IsConnected || string.IsNullOrEmpty(text))
        {
            return;
        }
        try
        {
            await transport.WriteAsync(text + "\r", _lifetime.Token);
        }
        catch
        {
            // 断开状态由 Closed 事件处理。
        }
    }

    public void SetFontSize(int size)
    {
        Post(new { type = "fontSize", value = Math.Clamp(size, 8, 32) });
    }

    private async Task SendInputAsync(string data)
    {
        var transport = _transport;
        if (transport == null || !transport.IsConnected || data.Length == 0)
        {
            return;
        }
        try
        {
            await transport.WriteAsync(data, _lifetime.Token);
        }
        catch
        {
            // 连接断开时由 Closed 事件反馈。
        }
    }

    /// <summary>通过当前 SSH 连接探测交互式 shell 的当前目录（不写入终端）。</summary>
    public async Task<string?> ProbeShellDirectoryAsync()
    {
        if (_transport is not SshXShellTransport ssh)
        {
            return null;
        }
        try
        {
            return await ssh.GetShellDirectoryAsync(_lifetime.Token);
        }
        catch
        {
            return null;
        }
    }

    public async Task<SftpClient?> CreateSftpClientAsync()
    {
        var transport = _transport;
        return transport == null
            ? null
            : await transport.CreateSftpClientAsync(_lifetime.Token);
    }

    private void CopyToClipboard(string text)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch
            {
                Thread.Sleep(30);
            }
        }
    }

    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    Post(new { type = "paste", data = text });
                }
            }
            else
            {
                SystemSounds.Beep.Play();
            }
        }
        catch
        {
            SystemSounds.Beep.Play();
        }
        FocusTerminal();
    }

    private void PostOutput(string text)
    {
        if (IsDisposed)
        {
            return;
        }
        WriteLog(text);
        if (!_webReady)
        {
            lock (_pendingLock)
            {
                if (_pendingOutput.Length < 2_000_000)
                {
                    _pendingOutput.Append(text);
                }
            }
            return;
        }
        Post(new { type = "output", data = text });
    }

    private void FlushPendingOutput()
    {
        string pending;
        lock (_pendingLock)
        {
            pending = _pendingOutput.ToString();
            _pendingOutput.Clear();
        }
        if (pending.Length > 0)
        {
            Post(new { type = "output", data = pending });
        }
    }

    private void AppendSystem(string text)
    {
        PostOutput(text);
    }

    private void Post(object message)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Post(message)); } catch { }
            return;
        }
        if (!_webReady || _core == null)
        {
            return;
        }
        try
        {
            _core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
        }
        catch
        {
            // WebView 正在销毁时忽略。
        }
    }

    private void SetStatus(string status)
    {
        _statusText = status;
        if (StateChanged != null && !IsDisposed)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(() => StateChanged?.Invoke(this)); } catch { }
            }
            else
            {
                StateChanged?.Invoke(this);
            }
        }
    }

    public bool IsLogging
    {
        get
        {
            lock (_logLock)
            {
                return _logWriter != null;
            }
        }
    }

    public string? CurrentLogFile { get; private set; }

    /// <summary>开始把当前终端的所有可见输出写入日志，返回日志文件路径。</summary>
    public string StartLogging()
    {
        string folder = ResolveLogFolder();
        Directory.CreateDirectory(folder);
        string safe = SanitizeFileName(_profile.Name);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string? file = null;

        lock (_logLock)
        {
            if (_logWriter != null)
            {
                return CurrentLogFile ?? "";
            }
            for (int attempt = 0; attempt < 12; attempt++)
            {
                string suffix = Random.Shared.Next(1000, 10000).ToString("D4");
                string candidate = Path.Combine(folder,
                    safe + "_" + stamp + "_" + suffix + ".logs.txt");
                try
                {
                    var stream = new FileStream(candidate, FileMode.CreateNew,
                        FileAccess.Write, FileShare.Read);
                    // 带 BOM 的 UTF-8：Windows 记事本、VS Code 等编辑器
                    // 能自动识别编码，中文日志不会显示为乱码。
                    _logWriter = new StreamWriter(stream, new UTF8Encoding(true))
                    {
                        AutoFlush = true
                    };
                    _logLine.Clear();
                    _logCursor = 0;
                    _logPendingEscape = "";
                    CurrentLogFile = candidate;
                    file = candidate;
                    _logWriter.WriteLine("=== XShell 复刻版 会话日志 ===");
                    _logWriter.WriteLine("编码: UTF-8 (BOM)");
                    _logWriter.WriteLine("会话: " + _profile.Name);
                    _logWriter.WriteLine("地址: " + _profile.DisplayAddress);
                    _logWriter.WriteLine("开始: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    _logWriter.WriteLine(new string('-', 64));
                    break;
                }
                catch (IOException)
                {
                    // 同名文件已存在时换一个随机数重试。
                }
            }
            if (_logWriter == null)
            {
                throw new IOException("无法创建日志文件，请检查日志目录权限。");
            }
        }

        NotifyLogStateChanged();
        return file ?? "";
    }

    /// <summary>停止当前终端日志记录。</summary>
    public void StopLogging()
    {
        StreamWriter? writer;
        lock (_logLock)
        {
            FlushLogLineLocked();
            writer = _logWriter;
            _logWriter = null;
            CurrentLogFile = null;
        }
        if (writer == null)
        {
            return;
        }
        try
        {
            writer.WriteLine(new string('-', 64));
            writer.WriteLine("结束: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.Dispose();
        }
        catch
        {
            // 日志关闭失败不影响终端。
        }
        NotifyLogStateChanged();
    }

    public void OpenLogFolder()
    {
        string folder = ResolveLogFolder();
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + folder + "\"",
            UseShellExecute = true
        });
    }

    public string LogFolder => ResolveLogFolder();

    private string ResolveLogFolder() => string.IsNullOrWhiteSpace(_profile.LogFolder)
        ? XShellPaths.LogsDir : _profile.LogFolder;

    private static string SanitizeFileName(string name)
    {
        string safe = string.Join("_", (name ?? "").Split(
            Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        safe = safe.Trim('.', ' ');
        if (safe.Length == 0)
        {
            safe = "session";
        }
        return safe.Length > 60 ? safe[..60] : safe;
    }

    private void NotifyLogStateChanged()
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(() => StateChanged?.Invoke(this)); } catch { }
        }
        else
        {
            StateChanged?.Invoke(this);
        }
    }

    private void WriteLog(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        lock (_logLock)
        {
            if (_logWriter == null)
            {
                return;
            }
            try
            {
                string combined = _logPendingEscape + text;
                _logPendingEscape = "";
                AppendVisibleLogText(combined);
            }
            catch
            {
                // 日志失败不影响终端。
            }
        }
    }

    /// <summary>
    /// 用一个轻量的行内光标模型还原终端可见内容：退格、回车覆盖、
    /// 光标横向移动与整行擦除都按终端效果处理，避免 yum / dnf 进度条
    /// 在日志里堆叠成一行。
    /// </summary>
    private void AppendVisibleLogText(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\x1b')
            {
                if (!TrySkipEscapeSequence(text, i, out int escapeEnd))
                {
                    // ESC 序列被拆到下一次输出块：缓存到下一块一起处理。
                    _logPendingEscape = text[i..];
                    if (_logPendingEscape.Length > 256)
                    {
                        _logPendingEscape = "";
                    }
                    break;
                }
                i = escapeEnd;
                continue;
            }
            if (c == '\b')
            {
                if (_logCursor > 0)
                {
                    _logCursor--;
                }
                continue;
            }
            if (c == '\r')
            {
                _logCursor = 0;
                continue;
            }
            if (c == '\n')
            {
                WriteLogLineLocked();
                continue;
            }
            if (c == '\t')
            {
                int next = ((_logCursor / 8) + 1) * 8;
                while (_logLine.Length < next)
                {
                    _logLine.Append(' ');
                }
                _logCursor = next;
                continue;
            }
            if (c < 0x20 || c == 0x7f)
            {
                continue;
            }
            AppendLogChar(c);
        }
    }

    private void AppendLogChar(char c)
    {
        if (_logCursor < _logLine.Length)
        {
            _logLine[_logCursor] = c;
        }
        else
        {
            while (_logLine.Length < _logCursor)
            {
                _logLine.Append(' ');
            }
            _logLine.Append(c);
        }
        _logCursor++;
    }

    private void ClearLogLine()
    {
        _logLine.Clear();
        _logCursor = 0;
    }

    private void WriteLogLineLocked()
    {
        _logWriter?.WriteLine(_logLine.ToString().TrimEnd());
        ClearLogLine();
    }

    /// <summary>尝试跳过 ESC 序列；序列不完整时返回 false，交由下一次写入拼接。</summary>
    private bool TrySkipEscapeSequence(string text, int start, out int end)
    {
        end = start;
        if (start + 1 >= text.Length)
        {
            return false;
        }
        char kind = text[start + 1];
        if (kind == '7')
        {
            _logSavedCursor = _logCursor;
            end = start + 1;
            return true;
        }
        if (kind == '8')
        {
            _logCursor = Math.Max(0, _logSavedCursor);
            end = start + 1;
            return true;
        }
        if (kind == '[')
        {
            int i = start + 2;
            while (i < text.Length && (text[i] < 0x40 || text[i] > 0x7E))
            {
                i++;
            }
            if (i >= text.Length)
            {
                return false;
            }
            string parameter = text.Substring(start + 2, i - start - 2);
            switch (text[i])
            {
                case 'A':
                    // 光标上移：进度条开始重绘。
                    ClearLogLine();
                    break;
                case 'K':
                {
                    int mode = ParseFirstParameter(parameter, 0);
                    if (mode == 2)
                    {
                        ClearLogLine();
                    }
                    else if (mode == 0 && _logCursor < _logLine.Length)
                    {
                        _logLine.Length = _logCursor;
                    }
                    else if (mode == 1 && _logCursor > 0)
                    {
                        int count = Math.Min(_logCursor, _logLine.Length);
                        _logLine.Remove(0, count);
                        _logCursor = 0;
                    }
                    break;
                }
                case 'J':
                {
                    int mode = ParseFirstParameter(parameter, 0);
                    if (mode == 2)
                    {
                        ClearLogLine();
                    }
                    else if (mode == 0 && _logCursor < _logLine.Length)
                    {
                        _logLine.Length = _logCursor;
                    }
                    break;
                }
                case 'G':
                    _logCursor = Math.Max(0, ParseFirstParameter(parameter, 1) - 1);
                    break;
                case 'C':
                    _logCursor += Math.Max(1, ParseFirstParameter(parameter, 1));
                    break;
                case 'D':
                    _logCursor = Math.Max(0,
                        _logCursor - Math.Max(1, ParseFirstParameter(parameter, 1)));
                    break;
                case 'H':
                case 'f':
                {
                    string[] parts = parameter.Split(';');
                    int column = parts.Length >= 2
                        ? ParseInt(parts[1], 1) : ParseInt(parts[0], 1);
                    _logCursor = Math.Max(0, column - 1);
                    break;
                }
                case 's':
                    _logSavedCursor = _logCursor;
                    break;
                case 'u':
                    _logCursor = Math.Max(0, _logSavedCursor);
                    break;
                case 'P':
                {
                    int count = Math.Max(1, ParseFirstParameter(parameter, 1));
                    if (_logCursor < _logLine.Length)
                    {
                        _logLine.Remove(_logCursor,
                            Math.Min(count, _logLine.Length - _logCursor));
                    }
                    break;
                }
                case 'X':
                {
                    int count = Math.Max(1, ParseFirstParameter(parameter, 1));
                    int eraseEnd = Math.Min(_logLine.Length, _logCursor + count);
                    for (int k = _logCursor; k < eraseEnd; k++)
                    {
                        _logLine[k] = ' ';
                    }
                    break;
                }
            }
            end = i;
            return true;
        }
        if (kind == ']')
        {
            int i = start + 2;
            while (i < text.Length)
            {
                if (text[i] == '\x07')
                {
                    end = i;
                    return true;
                }
                if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\')
                {
                    end = i + 1;
                    return true;
                }
                i++;
            }
            return false;
        }
        int j = start + 1;
        while (j < text.Length && text[j] >= 0x20 && text[j] <= 0x2F)
        {
            j++;
        }
        if (j >= text.Length)
        {
            return false;
        }
        end = j;
        return true;
    }

    private static int ParseFirstParameter(string parameter, int fallback)
    {
        if (string.IsNullOrEmpty(parameter))
        {
            return fallback;
        }
        int end = 0;
        while (end < parameter.Length && char.IsDigit(parameter[end]))
        {
            end++;
        }
        return end > 0 && int.TryParse(parameter[..end], out int value)
            ? value : fallback;
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out int value) ? value : fallback;

    private void FlushLogLineLocked()
    {
        if (_logWriter == null)
        {
            ClearLogLine();
            return;
        }
        if (_logLine.Length > 0)
        {
            try
            {
                _logWriter.WriteLine(_logLine.ToString().TrimEnd());
            }
            catch
            {
                // 忽略写入失败。
            }
        }
        ClearLogLine();
    }

    private void CloseLog()
    {
        StreamWriter? writer;
        lock (_logLock)
        {
            FlushLogLineLocked();
            writer = _logWriter;
            _logWriter = null;
            CurrentLogFile = null;
        }
        try { writer?.Dispose(); } catch { }
    }

    private static string EscapeAnsi(string text)
    {
        return (text ?? "").Replace("\x1b", "");
    }

    public void RefreshStatus() => StateChanged?.Invoke(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _lifetime.Cancel(); } catch { }
            try { _connectCts?.Cancel(); } catch { }
            try { _transport?.Disconnect("终端窗口已关闭。"); } catch { }
            try { _transport?.DisposeAsync().AsTask().Wait(400); } catch { }
            _transport = null;
            CloseLog();
            try { _web.Dispose(); } catch { }
            try { _lifetime.Dispose(); } catch { }
            try { _connectCts?.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
