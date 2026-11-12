using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// QuickLook 复刻后台服务：全局空格键监听、读取资源管理器选中项、
/// 打开/关闭预览窗口并在同文件夹内切换文件。默认开启，管理窗口仅用于设置。
/// </summary>
public sealed class QuickLookService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int VkSpace = 0x20;
    private const int VkEscape = 0x1B;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const uint GaRoot = 2;
    private const uint KeyEventKeyUp = 0x0002;

    private readonly AppConfig _config;
    private QuickLookKeyboardHook? _hook;
    private QuickLookMouseHook? _mouseHook;
    private QuickLookPreviewForm? _previewForm;
    private object? _shellApplication;
    private SynchronizationContext? _uiContext;
    private bool _disposed;
    private bool _spaceBlockedUntilRelease;
    private readonly List<string> _navigation = new();
    private int _navigationIndex = -1;
    private string? _currentPath;
    private bool _navigationReady;

    public QuickLookService(AppConfig config)
    {
        _config = config;
        _uiContext = SynchronizationContext.Current;
        RemoveLegacyLog();
    }

    /// <summary>删除旧版本遗留的 quicklook.log；新版本不再记录该功能日志。</summary>
    private static void RemoveLegacyLog()
    {
        try
        {
            string file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniMonitor", "quicklook.log");
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // 日志文件被占用时忽略，不影响预览功能
        }
    }

    public bool IsRunning => _hook != null;
    public string? LastError { get; private set; }

    public void Start()
    {
        _uiContext ??= SynchronizationContext.Current;
        if (_disposed || !_config.QuickLookEnabled)
        {
            return;
        }
        if (_hook != null)
        {
            return;
        }
        var hook = new QuickLookKeyboardHook(OnHookKeyDown, OnHookKeyUp);
        if (!hook.Install())
        {
            LastError = "全局键盘监听安装失败（错误码 " + hook.ErrorCode
                + "），可能被安全软件拦截。";
            hook.Dispose();
            return;
        }
        _hook = hook;
        LastError = null;
    }

    public void Stop()
    {
        _hook?.Dispose();
        _hook = null;
        ClosePreview();
    }

    public void ApplySettings()
    {
        if (_config.QuickLookEnabled)
        {
            Start();
            if (_previewForm != null)
            {
                _previewForm.TopMost = _config.QuickLookAlwaysOnTop;
            }
        }
        else
        {
            Stop();
        }
    }

    public void ShowPreviewForPath(string path)
    {
        if (_disposed || !_config.QuickLookEnabled)
        {
            return;
        }
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }
        _currentPath = path;
        _navigationReady = false;
        var form = EnsurePreviewForm();
        // 只有文件未发生变化时才复用旧内容；否则重新加载，避免 Markdown 等文件改了内容仍显示旧版本。
        bool sameContent = form.ContentUpToDate(path);
        // 先让窗口立即出现，再加载内容：首帧响应不再受文件类型与目录规模影响。
        form.TopMost = _config.QuickLookAlwaysOnTop;
        if (!form.Visible)
        {
            form.Show();
        }
        if (!sameContent)
        {
            form.LoadFile(path);
        }
        // 预览显示期间临时挂鼠标滚轮钩子：窗口不激活也能滚轮缩放，关闭时立即卸载。
        EnsureMouseHook();
    }

    /// <summary>在启动后预创建预览窗口，避免第一次按空格时临时创建控件造成卡顿。</summary>
    public void Prewarm()
    {
        if (_disposed || !_config.QuickLookEnabled)
        {
            return;
        }
        EnsurePreviewForm();
        // 提前创建原生窗口句柄，避免第一次按空格时才创建窗口造成明显停顿。
        _ = _previewForm!.Handle;
        EnsureShellApplication();
    }

    private QuickLookPreviewForm EnsurePreviewForm()
    {
        if (_previewForm != null && !_previewForm.IsDisposed)
        {
            return _previewForm;
        }
        var form = new QuickLookPreviewForm(_config);
        form.NavigateRequested += Navigate;
        form.ClosedByUser += OnPreviewClosed;
        form.VisibleChanged += (_, _) =>
        {
            if (!form.Visible)
            {
                ReleaseMouseHook();
            }
        };
        _previewForm = form;
        return form;
    }

    public void ClosePreview()
    {
        var form = _previewForm;
        if (form != null && !form.IsDisposed)
        {
            form.HidePreview();
        }
        ReleaseMouseHook();
    }

    /// <summary>仅在预览窗口显示期间安装鼠标滚轮钩子，避免全程拦截系统鼠标事件。</summary>
    private void EnsureMouseHook()
    {
        if (_disposed || _mouseHook != null)
        {
            return;
        }
        var hook = new QuickLookMouseHook(OnMouseWheelHook);
        if (hook.Install())
        {
            _mouseHook = hook;
        }
        else
        {
            hook.Dispose();
        }
    }

    private void ReleaseMouseHook()
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
    }

    private bool OnMouseWheelHook(int delta, Point screenPoint)
    {
        var form = _previewForm;
        if (form == null || form.IsDisposed || !form.Visible)
        {
            return false;
        }
        try
        {
            return form.TryZoomImageAtScreenPoint(screenPoint, delta);
        }
        catch
        {
            return false;
        }
    }

    private void OnPreviewClosed()
    {
        ReleaseMouseHook();
        _previewForm = null;
        _navigation.Clear();
        _navigationIndex = -1;
        _navigationReady = false;
        _currentPath = null;
    }

    private bool OnHookKeyDown(int vk)
    {
        try
        {
            if (_previewForm != null && !_previewForm.IsDisposed && _previewForm.Visible)
            {
                if (!IsPreviewContextActive())
                {
                    return false;
                }
                if (vk == VkSpace || vk == VkEscape)
                {
                    if (vk == VkSpace)
                    {
                        _spaceBlockedUntilRelease = true;
                    }
                    // 关闭同样投递到消息循环：保存窗口位置会写配置文件，不能在钩子里同步执行。
                    PostToUi(ClosePreview);
                    return true;
                }
                if (vk is VkLeft or VkUp or VkRight or VkDown)
                {
                    int delta = vk is VkLeft or VkUp ? -1 : 1;
                    PostToUi(() => Navigate(delta));
                    return true;
                }
                return false;
            }

            if (vk != VkSpace)
            {
                return false;
            }
            if (_spaceBlockedUntilRelease)
            {
                return true;
            }
            if (HasModifierDown())
            {
                return false;
            }
            IntPtr foreground = GetForegroundWindow();
            IntPtr root = GetAncestor(foreground, GaRoot);
            if (!IsExplorerWindow(root))
            {
                return false;
            }
            if (IsEditingControlFocused(foreground))
            {
                return false;
            }

            // 关键：低级键盘钩子处于“输入同步呼叫”上下文，此时调用 Shell COM 会抛出
            // RPC_E_CANTCALLOUT_ININPUTSYNCCALL，只能退化到剪贴板回退（阻塞且易失败）。
            // 因此钩子只负责吞掉空格，真正的取值与打开投递到 UI 消息循环里异步执行。
            _spaceBlockedUntilRelease = true;
            PostToUi(() => ResolveAndShowPreview(root));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>在 UI 消息循环中读取选中项并打开预览（此时 COM 调用合法且很快）。</summary>
    private void ResolveAndShowPreview(IntPtr root)
    {
        try
        {
            string? path = GetExplorerSelectedPath(root);
            if (path == null)
            {
                // 管理员权限下 Shell 自动化仍可能受限，再退回剪贴板读取。
                path = GetSelectedPathViaClipboard(root);
            }
            if (path == null)
            {
                return;
            }
            ShowPreviewForPath(path);
        }
        catch
        {
            // 读取选中项失败时保持静默，不影响其它窗口的空格键
        }
    }

    /// <summary>把耗时操作投递到 UI 消息循环；没有 UI 上下文时退化为同步执行。</summary>
    private void PostToUi(Action action)
    {
        var context = _uiContext;
        if (context != null)
        {
            try
            {
                context.Post(_ => action(), null);
                return;
            }
            catch
            {
                // 界面正在关闭时投递失败，直接忽略。
                return;
            }
        }
        action();
    }

    private bool OnHookKeyUp(int vk)
    {
        if (vk == VkSpace && _spaceBlockedUntilRelease)
        {
            _spaceBlockedUntilRelease = false;
            return true;
        }
        return false;
    }

    private bool IsPreviewContextActive()
    {
        IntPtr foreground = GetForegroundWindow();
        IntPtr root = GetAncestor(foreground, GaRoot);
        if (_previewForm != null && !_previewForm.IsDisposed
            && (foreground == _previewForm.Handle || root == _previewForm.Handle))
        {
            return true;
        }
        return IsExplorerWindow(root);
    }

    private void Navigate(int delta)
    {
        if (_previewForm == null || _previewForm.IsDisposed)
        {
            return;
        }
        // 目录列表改为按需构建：打开预览不再等待整个文件夹枚举完成。
        EnsureNavigation();
        if (_navigation.Count == 0)
        {
            return;
        }
        int index = Math.Clamp(_navigationIndex + delta, 0, _navigation.Count - 1);
        if (index == _navigationIndex)
        {
            return;
        }
        string next = _navigation[index];
        if (!File.Exists(next) && !Directory.Exists(next))
        {
            return;
        }
        _navigationIndex = index;
        _currentPath = next;
        _previewForm.LoadFile(next);
    }

    private void EnsureNavigation()
    {
        if (_navigationReady)
        {
            return;
        }
        _navigationReady = true;
        if (_currentPath != null)
        {
            BuildNavigation(_currentPath);
        }
    }

    private void BuildNavigation(string path)
    {
        _navigation.Clear();
        _navigationIndex = -1;
        string? folder;
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            folder = info.Parent?.FullName;
        }
        else
        {
            folder = Path.GetDirectoryName(path);
        }
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0
                            && !string.Equals(entry, path, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        _navigation.Add(entry);
                    }
                    catch
                    {
                        // 忽略无法访问的项
                    }
                }
            }
            catch
            {
                // 忽略
            }
        }
        _navigation.Sort(StringComparer.CurrentCultureIgnoreCase);
        _navigationIndex = _navigation.FindIndex(entry =>
            string.Equals(Path.GetFullPath(entry), Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase));
        if (_navigationIndex < 0)
        {
            _navigation.Add(path);
            _navigation.Sort(StringComparer.CurrentCultureIgnoreCase);
            _navigationIndex = _navigation.FindIndex(entry =>
                string.Equals(Path.GetFullPath(entry), Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool HasModifierDown()
    {
        return (GetAsyncKeyState(VkControl) & 0x8000) != 0
            || (GetAsyncKeyState(VkShift) & 0x8000) != 0
            || (GetAsyncKeyState(VkMenu) & 0x8000) != 0
            || (GetAsyncKeyState(VkLWin) & 0x8000) != 0
            || (GetAsyncKeyState(VkRWin) & 0x8000) != 0;
    }

    private static string GetFocusDescription(IntPtr foreground)
    {
        try
        {
            uint threadId = GetWindowThreadProcessId(foreground, out _);
            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
            {
                return "焦点类=" + GetWindowClassName(info.hwndFocus)
                    + "，flags=" + info.flags + "，caret=" + info.hwndCaret;
            }
        }
        catch
        {
            // 忽略
        }
        return "焦点=未知";
    }

    private static bool IsEditingControlFocused(IntPtr foreground)
    {
        try
        {
            uint threadId = GetWindowThreadProcessId(foreground, out _);
            var info = new GuiThreadInfo
            {
                cbSize = Marshal.SizeOf<GuiThreadInfo>()
            };
            if (!GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
            {
                return false;
            }
            string className = GetWindowClassName(info.hwndFocus);
            // 与 QuickLook 一致：光标/文本插入符处于活动状态时（地址栏、搜索框、重命名等）不触发。
            if (info.flags != 0 || info.hwndCaret != IntPtr.Zero)
            {
                return true;
            }
            return className.Equals("Windows.UI.Core.CoreWindow",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        try
        {
            var builder = new System.Text.StringBuilder(256);
            return GetClassName(hwnd, builder, 256) > 0 ? builder.ToString() : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsExplorerWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }
        string className = GetWindowClassName(hwnd);
        return className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase)
            || className.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>供设置界面检测当前资源管理器选中项，帮助排查空格预览未触发的问题。</summary>
    public string? GetExplorerSelectionForDiagnostics(out string detail)
    {
        IntPtr foreground = GetForegroundWindow();
        IntPtr root = GetAncestor(foreground, GaRoot);
        string foregroundClass = GetWindowClassName(foreground);
        string rootClass = GetWindowClassName(root);
        detail = "前台窗口：" + foregroundClass + "  /  根窗口：" + rootClass
            + "\r\n" + GetFocusDescription(foreground)
            + "\r\n键盘监听：" + (IsRunning ? "运行中" : "未运行（" + (LastError ?? "未知原因") + "）");
        if (!IsExplorerWindow(root))
        {
            string? anyPath = GetExplorerSelectedPath(null);
            detail += anyPath == null
                ? "\r\n前台不是资源管理器，且未找到带选中项的资源管理器窗口。"
                : "\r\n前台不是资源管理器；从其它资源管理器窗口读取到选中项：" + anyPath;
            return anyPath;
        }
        if (IsEditingControlFocused(foreground))
        {
            detail += "\r\n资源管理器焦点在输入框（地址栏/搜索框/重命名），请先点选文件。";
            return null;
        }
        string? path = GetExplorerSelectedPath(root);
        if (path == null)
        {
            path = GetSelectedPathViaClipboard(root);
        }
        detail += path == null
            ? "\r\n未读取到选中项，请在资源管理器中单击选中一个文件后再试。"
            : "\r\n已读取选中项：" + path;
        return path;
    }

    private string? GetExplorerSelectedPath(IntPtr? matchWindow)
    {
        object? windowsObject = null;
        try
        {
            object? shellObject = EnsureShellApplication();
            if (shellObject == null)
            {
                return null;
            }
            windowsObject = ComCall(shellObject, "Windows");
            if (windowsObject == null)
            {
                return null;
            }
            int count = Convert.ToInt32(ComGet(windowsObject, "Count") ?? 0);
            bool matchedWindow = false;
            for (int i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = ComCall(windowsObject, "Item", i);
                    if (window == null)
                    {
                        continue;
                    }
                    long rawHwnd = Convert.ToInt64(ComGet(window, "HWND") ?? 0);
                    IntPtr hwnd = new IntPtr(rawHwnd);
                    IntPtr windowRoot = GetAncestor(hwnd, GaRoot);
                    if (matchWindow.HasValue && hwnd != matchWindow.Value
                        && windowRoot != matchWindow.Value)
                    {
                        continue;
                    }
                    matchedWindow = true;
                    object? document = ComGet(window, "Document");
                    if (document == null)
                    {
                        continue;
                    }
                    object? selectedItem = null;
                    try
                    {
                        object? selected = ComCall(document, "SelectedItems");
                        int selectedCount = selected == null
                            ? 0
                            : Convert.ToInt32(ComGet(selected, "Count") ?? 0);
                        if (selectedCount > 0)
                        {
                            selectedItem = ComCall(selected!, "Item", 0);
                        }
                    }
                    catch
                    {
                        // Shell 自动化在部分权限/安全策略下不可用，继续尝试 FocusedItem
                    }
                    if (selectedItem == null)
                    {
                        try
                        {
                            selectedItem = ComGet(document, "FocusedItem");
                        }
                        catch
                        {
                            // 忽略
                        }
                    }
                    if (selectedItem == null)
                    {
                        continue;
                    }
                    string path = Convert.ToString(ComGet(selectedItem, "Path")) ?? "";
                    if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                    {
                        return path;
                    }
                }
                catch
                {
                    // 单个 ShellWindow 读取失败不影响其它窗口
                }
                finally
                {
                    if (window != null)
                    {
                        Marshal.FinalReleaseComObject(window);
                    }
                }
            }
            // 兼容部分 Windows 11 标签页窗口：ShellWindow 的 HWND 与顶层窗口不一致。
            // 只有一个资源管理器窗口时，直接回退读取该窗口的选中项。
            if (matchWindow.HasValue && !matchedWindow && count == 1)
            {
                return GetExplorerSelectedPath(null);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (windowsObject != null)
            {
                Marshal.FinalReleaseComObject(windowsObject);
            }
        }
        return null;
    }

    private object? EnsureShellApplication()
    {
        if (_shellApplication != null)
        {
            return _shellApplication;
        }
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
            {
                return null;
            }
            _shellApplication = Activator.CreateInstance(shellType);
        }
        catch
        {
            _shellApplication = null;
        }
        return _shellApplication;
    }

    private static object? ComCall(object target, string name, params object?[] args)
    {
        return target.GetType().InvokeMember(
            name,
            System.Reflection.BindingFlags.InvokeMethod,
            null,
            target,
            args);
    }

    private static object? ComGet(object target, string name)
    {
        return target.GetType().InvokeMember(
            name,
            System.Reflection.BindingFlags.GetProperty,
            null,
            target,
            null);
    }

    /// <summary>
    /// 管理员权限进程读取资源管理器 Shell 自动化可能失败。
    /// 最后兜底：向当前资源管理器发送 Ctrl+C，从剪贴板读取复制的文件路径，然后恢复原剪贴板。
    /// </summary>
    private static string? GetSelectedPathViaClipboard(IntPtr explorerHwnd)
    {
        IDataObject? backup = null;
        try
        {
            try
            {
                backup = Clipboard.GetDataObject();
            }
            catch
            {
                // 剪贴板可能被其它程序占用
            }

            uint sequence = GetClipboardSequenceNumber();
            keybd_event((byte)VkControl, (byte)0, 0, UIntPtr.Zero);
            keybd_event((byte)0x43, (byte)0, 0, UIntPtr.Zero); // C
            keybd_event((byte)0x43, (byte)0, KeyEventKeyUp, UIntPtr.Zero);
            keybd_event((byte)VkControl, (byte)0, KeyEventKeyUp, UIntPtr.Zero);

            bool changed = false;
            for (int i = 0; i < 20; i++)
            {
                if (GetClipboardSequenceNumber() != sequence)
                {
                    changed = true;
                    break;
                }
                Thread.Sleep(5);
            }

            string? path = null;
            if (changed)
            {
                try
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count > 0)
                    {
                        path = files[0];
                    }
                }
                catch
                {
                    // 剪贴板内容不是文件列表
                }
            }

            if (backup != null)
            {
                try
                {
                    Clipboard.SetDataObject(backup, true);
                }
                catch
                {
                    // 恢复失败时保留复制出来的文件，不影响预览
                }
            }
            return path;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
        ReleaseMouseHook();
        _previewForm?.Dispose();
        _previewForm = null;
        if (_shellApplication != null)
        {
            try
            {
                Marshal.FinalReleaseComObject(_shellApplication);
            }
            catch
            {
                // 忽略
            }
            _shellApplication = null;
        }
    }

    // ================= Win32 =================

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public int rcCaretLeft;
        public int rcCaretTop;
        public int rcCaretRight;
        public int rcCaretBottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo lpgti);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    private sealed class QuickLookKeyboardHook : IDisposable
    {
        private readonly LowLevelKeyboardProc _process;
        private readonly Func<int, bool> _keyDown;
        private readonly Func<int, bool> _keyUp;
        private IntPtr _handle;
        public int ErrorCode { get; private set; }

        public QuickLookKeyboardHook(Func<int, bool> keyDown, Func<int, bool> keyUp)
        {
            _keyDown = keyDown;
            _keyUp = keyUp;
            _process = ProcessHook;
        }

        public bool Install()
        {
            _handle = SetWindowsHookEx(WhKeyboardLl, _process, IntPtr.Zero, 0);
            if (_handle == IntPtr.Zero)
            {
                ErrorCode = Marshal.GetLastWin32Error();
                _handle = SetWindowsHookEx(WhKeyboardLl, _process,
                    GetModuleHandle(null), 0);
                if (_handle == IntPtr.Zero)
                {
                    ErrorCode = Marshal.GetLastWin32Error();
                }
            }
            return _handle != IntPtr.Zero;
        }

        private IntPtr ProcessHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                int vk = Marshal.ReadInt32(lParam);
                try
                {
                    if (message == WmKeyDown || message == WmSysKeyDown)
                    {
                        if (_keyDown(vk))
                        {
                            return (IntPtr)1;
                        }
                    }
                    else if (message == WmKeyUp || message == WmSysKeyUp)
                    {
                        if (_keyUp(vk))
                        {
                            return (IntPtr)1;
                        }
                    }
                }
                catch
                {
                    // 钩子回调不能抛出异常
                }
            }
            return CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    private sealed class QuickLookMouseHook : IDisposable
    {
        private readonly LowLevelMouseProc _process;
        private readonly Func<int, Point, bool> _wheel;
        private IntPtr _handle;
        public int ErrorCode { get; private set; }

        public QuickLookMouseHook(Func<int, Point, bool> wheel)
        {
            _wheel = wheel;
            _process = ProcessHook;
        }

        public bool Install()
        {
            _handle = SetWindowsHookEx(WhMouseLl, _process, IntPtr.Zero, 0);
            if (_handle == IntPtr.Zero)
            {
                ErrorCode = Marshal.GetLastWin32Error();
                _handle = SetWindowsHookEx(WhMouseLl, _process, GetModuleHandle(null), 0);
                if (_handle == IntPtr.Zero)
                {
                    ErrorCode = Marshal.GetLastWin32Error();
                }
            }
            return _handle != IntPtr.Zero;
        }

        private IntPtr ProcessHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WmMouseWheel)
            {
                try
                {
                    var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
                    int delta = (short)(data.MouseData >> 16);
                    if (delta != 0 && _wheel(delta, data.Pt))
                    {
                        return (IntPtr)1;
                    }
                }
                catch
                {
                    // 钩子回调不能抛出异常
                }
            }
            return CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
