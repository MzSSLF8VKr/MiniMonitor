using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 全局鼠标手势：按住右键向下、再向右，松开后关闭当前前台窗口。
/// 浏览器发送 Ctrl+W 关闭当前标签；其他窗口发送 WM_CLOSE。
/// 仅实现这一种手势；右键未形成手势时自动补发正常右键单击。
/// </summary>
public sealed class GlobalMouseGestureService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmClose = 0x0010;
    private const uint LlmhfInjected = 0x00000001;
    private const int MinDownPixels = 40;
    private const int MinRightPixels = 40;
    private const uint GaRoot = 2;
    private const int VkRButton = 0x02;
    private const int VkEscape = 0x1B;
    private static readonly IntPtr ReplayMarker = new IntPtr(0x4D4D4745); // "MMGE"

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkF4 = 0x73;
    private const ushort VkW = 0x57;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MousePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public MousePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out MousePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(MousePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    private readonly object _sync = new();
    private readonly GestureLogStore _log;
    private HookProc? _hookProc;
    private IntPtr _hookProcPtr;
    private IntPtr _hookHandle;
    private System.Windows.Forms.Timer? _pollTimer;
    private bool _running;
    private string? _lastError;
    private DateTime? _lastTrigger;
    private string _lastAction = "";
    private volatile bool _rightDown;
    private volatile bool _hookRightDownSeen;
    private volatile bool _injectingReplay;
    private MousePoint _startPoint;
    private MousePoint _pivotPoint;
    private IntPtr _targetWindow;
    private int _maxDownY;
    private int _maxRightX;
    private bool _downReached;
    private bool _gestureDetected;
    private bool _pollWasDown;
    private bool _pollTracking;
    private bool _pollDownReached;
    private bool _pollGestureDetected;
    private bool _pollActionQueued;
    private MousePoint _pollStartPoint;
    private MousePoint _pollPivotPoint;
    private int _pollMaxDownY;
    private int _pollMaxRightX;
    private IntPtr _pollTargetWindow;
    private int _callbackCount;
    private bool _injectedLogged;

    public GlobalMouseGestureService(GestureLogStore? log = null)
    {
        _log = log ?? new GestureLogStore();
        _log.Info("全局鼠标手势服务初始化");
    }

    public GestureLogStore Log => _log;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _running;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    public DateTime? LastTriggerTime
    {
        get
        {
            lock (_sync)
            {
                return _lastTrigger;
            }
        }
    }

    public string LastAction
    {
        get
        {
            lock (_sync)
            {
                return _lastAction;
            }
        }
    }

    public bool Start()
    {
        _log.Info("请求启动全局鼠标手势服务");
        lock (_sync)
        {
            if (_running)
            {
                _log.Debug("服务已在运行，忽略重复启动");
                return true;
            }
            _lastError = null;
            _callbackCount = 0;
            _injectedLogged = false;
            _hookProc = HookCallback;
            _hookProcPtr = Marshal.GetFunctionPointerForDelegate(_hookProc);
            IntPtr module = Process.GetCurrentProcess().MainModule?.BaseAddress
                ?? GetModuleHandleW(null);
            _hookHandle = SetWindowsHookExW(WhMouseLl, _hookProcPtr, module, 0);
            if (_hookHandle == IntPtr.Zero)
            {
                int code = Marshal.GetLastWin32Error();
                _lastError = "安装全局鼠标钩子失败。Windows 错误 " + code + "："
                    + new Win32Exception(code).Message;
                _log.Error("SetWindowsHookEx 失败，Windows 错误 " + code + "："
                    + new Win32Exception(code).Message);
                _hookProc = null;
                _hookProcPtr = IntPtr.Zero;
                return false;
            }
            _running = true;
            _log.Info("WH_MOUSE_LL 安装成功，Hook=0x" + _hookHandle.ToInt64().ToString("X")
                + "，安装线程=" + GetCurrentThreadId()
                + "，必须在有消息循环的 UI 线程上运行");
            StartPollingFallback();
            return true;
        }
    }

    public void Stop()
    {
        IntPtr hook;
        bool wasRightDown;
        _log.Info("请求停止全局鼠标手势服务");
        lock (_sync)
        {
            hook = _hookHandle;
            _hookHandle = IntPtr.Zero;
            wasRightDown = _rightDown;
            _running = false;
            _rightDown = false;
            _hookRightDownSeen = false;
            _gestureDetected = false;
            _downReached = false;
        }
        StopPollingFallback();

        if (hook != IntPtr.Zero)
        {
            bool ok = UnhookWindowsHookEx(hook);
            _log.Info("卸载 WH_MOUSE_LL：result=" + ok
                + "，Windows 错误 " + Marshal.GetLastWin32Error());
        }

        if (wasRightDown)
        {
            _log.Warn("停止时鼠标右键仍按下，将补发一次右键单击");
            // 右键按下时钩子屏蔽了 down；停止时补发一次右键，避免丢失用户原本的右键操作。
            Task.Run(async () =>
            {
                await Task.Delay(40);
                ReplayRightClick();
            });
        }
        _log.Info("全局鼠标手势服务已停止");
    }

    private void StartPollingFallback()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _pollTimer.Tick += (_, _) => PollMouseFallback();
        _pollTimer.Start();
        _log.Info("已启动鼠标右键轮询后备（15ms）：钩子回调未被系统调用时，靠 GetAsyncKeyState 兜底");
    }

    private void StopPollingFallback()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _pollTimer = null;
        }
        ResetPollingState();
        _pollWasDown = false;
    }

    private void PollMouseFallback()
    {
        if (!_running)
        {
            return;
        }
        if (_injectingReplay)
        {
            _pollWasDown = false;
            CancelPollingFallback();
            return;
        }

        bool down = (GetAsyncKeyState(VkRButton) & 0x8000) != 0;
        if (down && !_pollWasDown)
        {
            _pollWasDown = true;
            if (_hookRightDownSeen)
            {
                CancelPollingFallback();
                return;
            }
            if (!GetCursorPos(out MousePoint point))
            {
                return;
            }
            _pollTargetWindow = WindowFromPoint(point);
            IntPtr root = GetAncestor(_pollTargetWindow, GaRoot);
            if (root != IntPtr.Zero)
            {
                _pollTargetWindow = root;
            }
            if (IsVmwareWindow(_pollTargetWindow))
            {
                _log.Info("右键按下位于 VMware 窗口，轮询后备跳过手势");
                _pollTracking = false;
                return;
            }

            _pollTracking = true;
            _pollStartPoint = point;
            _pollPivotPoint = point;
            _pollMaxDownY = point.Y;
            _pollMaxRightX = point.X;
            _pollDownReached = false;
            _pollGestureDetected = false;
            _pollActionQueued = false;
            _log.Warn("钩子未收到右键按下，轮询后备启动 @(" + point.X + "," + point.Y
                + ")，目标 " + DescribeWindowFast(_pollTargetWindow)
                + "；后备模式无法屏蔽右键菜单，识别成功后会立即关闭目标");
            return;
        }

        if (!down && _pollWasDown)
        {
            _pollWasDown = false;
            if (!_pollTracking)
            {
                ResetPollingState();
                return;
            }
            MousePoint end = _pollPivotPoint;
            GetCursorPos(out end);
            int downMax = _pollMaxDownY - _pollStartPoint.Y;
            int rightMax = _pollMaxRightX - _pollStartPoint.X;
            bool triggered = _pollGestureDetected;
            _log.Info("轮询后备右键松开 @(" + end.X + "," + end.Y + ")，手势="
                + (triggered ? "识别成功" : "未识别")
                + "，起点=(" + _pollStartPoint.X + "," + _pollStartPoint.Y + ")"
                + "，拐点=(" + _pollPivotPoint.X + "," + _pollPivotPoint.Y + ")"
                + "，最大下移=" + downMax + "px，最大右移=" + rightMax + "px"
                + "，阈值=" + MinDownPixels + "/" + MinRightPixels + "px");
            if (triggered)
            {
                if (!_pollActionQueued)
                {
                    IntPtr target = _pollTargetWindow;
                    if (target == IntPtr.Zero || !IsWindow(target))
                    {
                        target = GetForegroundWindow();
                    }
                    _log.Info("轮询后备识别手势，准备关闭目标 " + DescribeWindowFast(target));
                    Task.Run(async () =>
                    {
                        await Task.Delay(80);
                        SendEscape();
                        await Task.Delay(40);
                        CloseTarget(target);
                    });
                }
            }
            ResetPollingState();
            return;
        }

        if (down && _pollTracking)
        {
            if (GetCursorPos(out MousePoint point))
            {
                UpdatePollGesture(point);
            }
        }
    }

    private void UpdatePollGesture(MousePoint point)
    {
        int dx = point.X - _pollStartPoint.X;
        int dy = point.Y - _pollStartPoint.Y;
        if (point.Y > _pollMaxDownY)
        {
            _pollMaxDownY = point.Y;
        }
        if (point.X > _pollMaxRightX)
        {
            _pollMaxRightX = point.X;
        }

        if (!_pollDownReached)
        {
            if (dy >= MinDownPixels && Math.Abs(dx) <= Math.Max(32, dy * 3 / 4))
            {
                _pollDownReached = true;
                _pollPivotPoint = point;
                _log.Info("轮询后备向下段达到阈值：起点=(" + _pollStartPoint.X + ","
                    + _pollStartPoint.Y + ")，当前=(" + point.X + "," + point.Y + ")");
            }
            return;
        }
        if (_pollGestureDetected)
        {
            return;
        }
        if (point.Y > _pollPivotPoint.Y && Math.Abs(point.X - _pollPivotPoint.X) < 12)
        {
            _pollPivotPoint = point;
        }
        int right = point.X - _pollPivotPoint.X;
        int vertical = Math.Abs(point.Y - _pollPivotPoint.Y);
        if (right >= MinRightPixels && vertical <= Math.Max(40, right))
        {
            _pollGestureDetected = true;
            _log.Info("轮询后备手势识别成功：拐点=(" + _pollPivotPoint.X + ","
                + _pollPivotPoint.Y + ")，当前=(" + point.X + "," + point.Y
                + ")，向右=" + right + "px");
            TriggerPollingFallbackAction();
        }
    }

    private void TriggerPollingFallbackAction()
    {
        if (_pollActionQueued)
        {
            return;
        }
        _pollActionQueued = true;
        IntPtr target = _pollTargetWindow;
        if (target == IntPtr.Zero || !IsWindow(target))
        {
            target = GetForegroundWindow();
        }
        _log.Info("轮询后备已识别手势，立即关闭目标（钩子失效时无法等待右键松开）："
            + DescribeWindowFast(target));
        Task.Run(async () =>
        {
            await Task.Delay(30);
            CloseTarget(target);
        });
    }

    private void CancelPollingFallback()
    {
        _pollTracking = false;
        _pollDownReached = false;
        _pollGestureDetected = false;
        _pollActionQueued = false;
    }

    private void ResetPollingState()
    {
        _pollTracking = false;
        _pollDownReached = false;
        _pollGestureDetected = false;
        _pollActionQueued = false;
        _pollTargetWindow = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        try
        {
            int message = wParam.ToInt32();
            MsllHookStruct data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            _callbackCount++;
            if (_callbackCount == 1)
            {
                _log.Info("钩子回调首次触发：msg=0x" + message.ToString("X")
                    + "，pt=(" + data.Point.X + "," + data.Point.Y + ")"
                    + "，flags=0x" + data.Flags.ToString("X")
                    + "，extra=0x" + data.ExtraInfo.ToInt64().ToString("X"));
            }

            bool injected = (data.Flags & LlmhfInjected) != 0;
            if (injected && (_injectingReplay || data.ExtraInfo == ReplayMarker))
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }
            if (injected && !_injectedLogged)
            {
                _injectedLogged = true;
                _log.Info("钩子收到非本程序注入的鼠标事件，将按正常输入处理");
            }

            if (message == WmRButtonDown)
            {
                _hookRightDownSeen = true;
                CancelPollingFallback();
                _targetWindow = IntPtr.Zero;
                IntPtr under = WindowFromPoint(data.Point);
                if (under != IntPtr.Zero)
                {
                    _targetWindow = GetAncestor(under, GaRoot);
                    if (_targetWindow == IntPtr.Zero)
                    {
                        _targetWindow = under;
                    }
                }

                if (IsVmwareWindow(_targetWindow))
                {
                    // 在 VMware 窗口内右键时完全不拦截，让右键菜单和虚拟机操作保持原样。
                    _rightDown = false;
                    _targetWindow = IntPtr.Zero;
                    _downReached = false;
                    _gestureDetected = false;
                    _log.Info("右键按下位于 VMware 窗口，手势自动临时失效，放行右键");
                    return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                _rightDown = true;
                _startPoint = data.Point;
                _pivotPoint = data.Point;
                _maxDownY = data.Point.Y;
                _maxRightX = data.Point.X;
                _downReached = false;
                _gestureDetected = false;
                // 屏蔽物理右键按下，等松开时再决定补发右键还是执行手势。
                return new IntPtr(1);
            }

            if (message == WmRButtonUp && _hookRightDownSeen && !_rightDown)
            {
                // 对应 VMware 窗口内的右键按下：未拦截 down，这里只需复位状态。
                _hookRightDownSeen = false;
                CancelPollingFallback();
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (message == WmMouseMove && _rightDown)
            {
                UpdateGesture(data.Point);
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (message == WmRButtonUp && _rightDown)
            {
                _rightDown = false;
                _hookRightDownSeen = false;
                bool triggered = _gestureDetected;
                int downMax = _maxDownY - _startPoint.Y;
                int rightMax = _maxRightX - _startPoint.X;
                _gestureDetected = false;
                _downReached = false;
                _log.Info("右键松开 @(" + data.Point.X + "," + data.Point.Y + ")，手势="
                    + (triggered ? "识别成功" : "未识别")
                    + "，起点=(" + _startPoint.X + "," + _startPoint.Y + ")"
                    + "，拐点=(" + _pivotPoint.X + "," + _pivotPoint.Y + ")"
                    + "，最大下移=" + downMax + "px，最大右移=" + rightMax + "px"
                    + "，阈值=" + MinDownPixels + "/" + MinRightPixels + "px");

                if (triggered)
                {
                    IntPtr target = _targetWindow;
                    if (target == IntPtr.Zero || !IsWindow(target))
                    {
                        target = GetForegroundWindow();
                    }
                    _targetWindow = IntPtr.Zero;
                    Task.Run(async () =>
                    {
                        await Task.Delay(60);
                        CloseTarget(target);
                    });
                }
                else
                {
                    _targetWindow = IntPtr.Zero;
                    _log.Info("未形成手势，补发正常右键单击");
                    Task.Run(async () =>
                    {
                        await Task.Delay(40);
                        ReplayRightClick();
                    });
                }
                return new IntPtr(1);
            }
        }
        catch (Exception ex)
        {
            // 低级鼠标钩子异常不能影响系统鼠标事件，记录后继续传递。
            _log.Error("鼠标钩子回调异常：" + ex.Message);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void UpdateGesture(MousePoint point)
    {
        int dx = point.X - _startPoint.X;
        int dy = point.Y - _startPoint.Y;
        if (point.Y > _maxDownY)
        {
            _maxDownY = point.Y;
        }
        if (point.X > _maxRightX)
        {
            _maxRightX = point.X;
        }

        if (!_downReached)
        {
            if (dy >= MinDownPixels && Math.Abs(dx) <= Math.Max(32, dy * 3 / 4))
            {
                _downReached = true;
                _pivotPoint = point;
                _log.Info("向下段达到阈值：起点=(" + _startPoint.X + "," + _startPoint.Y
                    + ")，当前=(" + point.X + "," + point.Y + ")，dx=" + dx + "，dy=" + dy);
            }
            return;
        }

        if (_gestureDetected)
        {
            return;
        }

        // 继续向下时更新拐点，保证从真正的底部开始计算向右段。
        if (point.Y > _pivotPoint.Y && Math.Abs(point.X - _pivotPoint.X) < 12)
        {
            _pivotPoint = point;
        }

        int right = point.X - _pivotPoint.X;
        int vertical = Math.Abs(point.Y - _pivotPoint.Y);
        if (right >= MinRightPixels && vertical <= Math.Max(40, right))
        {
            _gestureDetected = true;
            _log.Info("手势识别成功：拐点=(" + _pivotPoint.X + "," + _pivotPoint.Y
                + ")，当前=(" + point.X + "," + point.Y + ")，向右=" + right
                + "px，垂直偏移=" + vertical + "px");
        }
    }

    private void ReplayRightClick()
    {
        _injectingReplay = true;
        try
        {
            uint down = SendMouse(MouseEventRightDown);
            Thread.Sleep(12);
            uint up = SendMouse(MouseEventRightUp);
            if (down == 0 || up == 0)
            {
                _log.Error("补发右键失败：down=" + down + "，up=" + up
                    + "，Windows 错误 " + Marshal.GetLastWin32Error());
            }
            else
            {
                _log.Info("补发右键成功：down=" + down + "，up=" + up);
            }
        }
        finally
        {
            _injectingReplay = false;
        }
    }

    private void SendEscape()
    {
        Input[] inputs = { KeyInput(VkEscape, false), KeyInput(VkEscape, true) };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        _log.Info("轮询后备发送 Esc 关闭右键菜单：SendInput=" + sent
            + "，Windows 错误 " + Marshal.GetLastWin32Error());
    }

    private static uint SendMouse(uint flags)
    {
        var input = new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Flags = flags,
                    ExtraInfo = ReplayMarker
                }
            }
        };
        return SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    private static void SendKey(ushort virtualKey, bool keyUp)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventKeyUp : 0
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    private uint SendCtrlW()
    {
        Input[] inputs =
        {
            KeyInput(VkControl, false),
            KeyInput(VkW, false),
            KeyInput(VkW, true),
            KeyInput(VkControl, true)
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input KeyInput(ushort virtualKey, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventKeyUp : 0
                }
            }
        };
    }

    private bool ActivateWindow(IntPtr target)
    {
        if (GetForegroundWindow() == target)
        {
            _log.Info("目标窗口已是前台，无需激活");
            return true;
        }
        bool setOk = SetForegroundWindow(target);
        if (!setOk)
        {
            SwitchToThisWindow(target, true);
        }
        Thread.Sleep(40);
        bool active = GetForegroundWindow() == target;
        _log.Info("激活目标窗口：SetForegroundWindow=" + setOk
            + "，当前前台=0x" + GetForegroundWindow().ToInt64().ToString("X")
            + "，激活成功=" + active);
        return active;
    }

    private void CloseTarget(IntPtr target)
    {
        if (target == IntPtr.Zero || !IsWindow(target))
        {
            _log.Warn("关闭目标无效或窗口已不存在，目标=0x"
                + target.ToInt64().ToString("X"));
            return;
        }
        if (IsVmwareWindow(target))
        {
            // 最后一道保护：即使手势识别路径出现异常，也绝不关闭 VMware 窗口。
            _log.Info("目标为 VMware 窗口，手势自动临时失效，不执行关闭");
            return;
        }
        try
        {
            _log.Info("开始关闭目标：" + DescribeWindow(target));
            bool browser = IsBrowserWindow(target);
            _log.Info("浏览器判定：" + (browser ? "是" : "否"));
            if (browser)
            {
                bool active = ActivateWindow(target);
                if (active)
                {
                    uint sent = SendCtrlW();
                    _log.Info("发送 Ctrl+W：SendInput=" + sent
                        + "，Windows 错误 " + Marshal.GetLastWin32Error());
                    SetLastAction("已关闭浏览器标签页");
                }
                else
                {
                    // 无法激活目标浏览器时，退回关闭窗口，避免把 Ctrl+W 发给错误的窗口。
                    bool posted = PostMessageW(target, WmClose, IntPtr.Zero, IntPtr.Zero);
                    _log.Warn("无法激活浏览器，改发 WM_CLOSE：result=" + posted
                        + "，Windows 错误 " + Marshal.GetLastWin32Error());
                    SetLastAction("已关闭浏览器窗口");
                }
            }
            else
            {
                bool posted = PostMessageW(target, WmClose, IntPtr.Zero, IntPtr.Zero);
                _log.Info("发送 WM_CLOSE：result=" + posted
                    + "，Windows 错误 " + Marshal.GetLastWin32Error());
                SetLastAction("已关闭窗口");
            }
        }
        catch (Exception ex)
        {
            _log.Error("关闭目标窗口异常：" + ex.Message);
        }
    }

    private void SetLastAction(string action)
    {
        lock (_sync)
        {
            _lastTrigger = DateTime.Now;
            _lastAction = action;
        }
    }

    private static string DescribeWindowFast(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return "hwnd=0x0（未找到）";
        }
        try
        {
            var className = new StringBuilder(256);
            GetClassNameW(hWnd, className, className.Capacity);
            GetWindowThreadProcessId(hWnd, out uint processId);
            return "hwnd=0x" + hWnd.ToInt64().ToString("X")
                + "，class=" + className
                + "，pid=" + processId;
        }
        catch
        {
            return "hwnd=0x" + hWnd.ToInt64().ToString("X");
        }
    }

    private static string DescribeWindowBrief(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return "hwnd=0x0（未找到）";
        }
        try
        {
            var className = new StringBuilder(256);
            GetClassNameW(hWnd, className, className.Capacity);
            GetWindowThreadProcessId(hWnd, out uint processId);
            var title = new StringBuilder(256);
            GetWindowTextW(hWnd, title, title.Capacity);
            string text = title.ToString();
            if (text.Length > 80)
            {
                text = text[..80] + "...";
            }
            return "hwnd=0x" + hWnd.ToInt64().ToString("X")
                + "，class=" + className
                + "，pid=" + processId
                + "，title=\"" + text + "\"";
        }
        catch
        {
            return "hwnd=0x" + hWnd.ToInt64().ToString("X");
        }
    }

    private static string DescribeWindow(IntPtr hWnd)
    {
        string brief = DescribeWindowBrief(hWnd);
        if (hWnd == IntPtr.Zero)
        {
            return brief;
        }
        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            using Process process = Process.GetProcessById((int)processId);
            return brief + "，process=" + process.ProcessName;
        }
        catch
        {
            return brief + "，process=?";
        }
    }

    /// <summary>目标窗口是否属于 VMware 系列软件（Workstation / Player / Remote Console / Horizon）。</summary>
    private static bool IsVmwareWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0)
            {
                return false;
            }
            using Process process = Process.GetProcessById((int)processId);
            string name = process.ProcessName.ToLowerInvariant();
            return name.StartsWith("vmware", StringComparison.Ordinal)
                || name is "vmplayer" or "vmrc";
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBrowserWindow(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            using Process process = Process.GetProcessById((int)processId);
            string name = process.ProcessName.ToLowerInvariant();
            return name is "chrome" or "msedge" or "firefox" or "brave" or "opera"
                or "vivaldi" or "chromium" or "iexplore" or "360chrome" or "360se"
                or "qqbrowser" or "sogouexplorer" or "maxthon" or "theworld"
                or "duckduckgo" or "arc" or "yandex" or "liebao" or "2345explorer"
                or "2345chrome" or "ucbrowser";
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _log.Info("全局鼠标手势服务释放");
        _log.Flush();
    }
}
