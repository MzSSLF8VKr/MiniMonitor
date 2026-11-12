using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 截图插件全局服务：注册全局快捷键、执行截图/编辑/录屏。
/// 默认截图与录屏开启；管理界面默认不打开。
/// </summary>
public sealed class ScreenshotPluginService : IDisposable
{
    public const int HotkeyScreenshot = 1;
    public const int HotkeyRecordPrint = 2;
    public const int HotkeyRecordF9 = 3;
    public const int HotkeyScroll = 4;

    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const int VkC = 0x43;
    private const int VkW = 0x57;
    private const int VkSnapshot = 0x2C;
    private const int VkF9 = 0x78;

    private readonly AppConfig _config;
    private readonly ScreenRecorder _recorder;
    private HotkeyWindow? _hotkeyWindow;
    private RecordingIndicatorForm? _indicator;
    private bool _started;
    private bool _capturing;

    public event Action? StateChanged;
    public event Action<string>? StatusChanged;

    public ScreenshotPluginService(AppConfig config)
    {
        _config = config;
        _recorder = new ScreenRecorder();
    }

    public bool IsCapturing => _capturing;
    public bool IsRecording => _recorder.IsRecording;
    public bool ScreenshotHotkeyRegistered { get; private set; }
    public bool ScrollHotkeyRegistered { get; private set; }
    public bool RecordPrintHotkeyRegistered { get; private set; }
    public bool RecordF9HotkeyRegistered { get; private set; }
    public string? HotkeyError { get; private set; }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
        _started = true;
        ApplyHotkeys();
    }

    public void ApplyHotkeys()
    {
        if (!_started || _hotkeyWindow == null)
        {
            return;
        }
        _hotkeyWindow.Unregister(HotkeyScreenshot);
        _hotkeyWindow.Unregister(HotkeyScroll);
        _hotkeyWindow.Unregister(HotkeyRecordPrint);
        _hotkeyWindow.Unregister(HotkeyRecordF9);
        ScreenshotHotkeyRegistered = false;
        ScrollHotkeyRegistered = false;
        RecordPrintHotkeyRegistered = false;
        RecordF9HotkeyRegistered = false;
        HotkeyError = null;

        if (_config.ScreenshotEnabled)
        {
            ScreenshotHotkeyRegistered = _hotkeyWindow.Register(
                HotkeyScreenshot, ModControl | ModShift | ModNoRepeat, VkC);
            ScrollHotkeyRegistered = _hotkeyWindow.Register(
                HotkeyScroll, ModControl | ModShift | ModNoRepeat, VkW);
            if (!ScreenshotHotkeyRegistered)
            {
                HotkeyError = "Ctrl+Shift+C 注册失败，可能被其它程序占用。";
            }
            if (!ScrollHotkeyRegistered)
            {
                HotkeyError = (HotkeyError ?? "") + " Ctrl+Shift+W 注册失败，可能被其它程序占用。";
            }
        }
        if (_config.ScreenRecordingEnabled)
        {
            RecordPrintHotkeyRegistered = _hotkeyWindow.Register(
                HotkeyRecordPrint, ModShift | ModNoRepeat, VkSnapshot);
            RecordF9HotkeyRegistered = _hotkeyWindow.Register(
                HotkeyRecordF9, ModShift | ModNoRepeat, VkF9);
            if (!RecordPrintHotkeyRegistered && !RecordF9HotkeyRegistered)
            {
                HotkeyError = (HotkeyError ?? "") + " Shift+PrintScreen / Shift+F9 注册失败。";
            }
        }
        StateChanged?.Invoke();
    }

    private void OnHotkeyPressed(int id)
    {
        switch (id)
        {
            case HotkeyScreenshot:
                _ = TakeScreenshotAsync(ScreenshotMode.Rectangle);
                break;
            case HotkeyScroll:
                _ = TakeScreenshotAsync(ScreenshotMode.ScrollingWindow);
                break;
            case HotkeyRecordPrint:
            case HotkeyRecordF9:
                ToggleRecording();
                break;
        }
    }

    public async Task TakeScreenshotAsync(ScreenshotMode mode)
    {
        if (_capturing)
        {
            return;
        }
        _capturing = true;
        StateChanged?.Invoke();
        try
        {
            if (_config.ScreenshotDelaySeconds > 0)
            {
                using var countdown = new CountdownForm(_config.ScreenshotDelaySeconds);
                countdown.ShowDialog();
                if (countdown.Cancelled)
                {
                    StatusChanged?.Invoke("截图已取消。");
                    return;
                }
            }

            Bitmap? image = null;
            switch (mode)
            {
                case ScreenshotMode.FullScreen:
                    image = ScreenCapture.CaptureVirtualScreen();
                    break;
                case ScreenshotMode.ActiveWindow:
                    image = ScreenCapture.CaptureActiveWindow();
                    break;
                case ScreenshotMode.ScrollingWindow:
                    StatusChanged?.Invoke("正在执行滚动截图...");
                    image = await Task.Run(() => ScreenCapture.CaptureScrollingWindow(
                        new Progress<string>(s => StatusChanged?.Invoke(s)), CancellationToken.None));
                    break;
                case ScreenshotMode.Rectangle:
                case ScreenshotMode.RoundedRectangle:
                case ScreenshotMode.Ellipse:
                case ScreenshotMode.Freehand:
                case ScreenshotMode.Polygon:
                    var shape = mode switch
                    {
                        ScreenshotMode.RoundedRectangle => SelectionShape.RoundedRectangle,
                        ScreenshotMode.Ellipse => SelectionShape.Ellipse,
                        ScreenshotMode.Freehand => SelectionShape.Freehand,
                        ScreenshotMode.Polygon => SelectionShape.Polygon,
                        _ => SelectionShape.Rectangle
                    };
                    image = SelectRegion(shape);
                    break;
            }

            if (image == null)
            {
                StatusChanged?.Invoke("截图已取消。");
                return;
            }

            if (_config.ScreenshotAutoCopy)
            {
                try
                {
                    Clipboard.SetImage(image);
                    StatusChanged?.Invoke("截图完成，已复制到剪贴板。");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("截图完成，但复制到剪贴板失败：" + ex.Message);
                }
            }
            else
            {
                StatusChanged?.Invoke("截图完成。");
            }

            if (_config.ScreenshotAutoEditor)
            {
                var editor = new ImageEditorForm(image, _config);
                editor.Show();
            }
            else
            {
                image.Dispose();
            }
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("截图失败：" + ex.Message);
        }
        finally
        {
            _capturing = false;
            StateChanged?.Invoke();
        }
    }

    private static Bitmap? SelectRegion(SelectionShape shape)
    {
        var background = ScreenCapture.CaptureVirtualScreen();
        using var selector = new RegionSelectorForm(background,
            ScreenCapture.VirtualScreenBounds, shape);
        if (selector.ShowDialog() != DialogResult.OK)
        {
            return null;
        }
        return selector.Result;
    }

    public void ToggleRecording()
    {
        if (_recorder.IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    public void StartRecording()
    {
        if (_recorder.IsRecording)
        {
            return;
        }
        var format = ScreenshotSettingParser.ParseFormat(_config.RecordingFormat);
        if (format == RecordingFormat.Mp4 &&
            !FfmpegLocator.IsAvailable(_config.FfmpegPath))
        {
            var choice = MessageBox.Show(
                "未检测到 ffmpeg.exe。\n\n是否改用 GIF 录制？\n" +
                "如需 MP4，请把 ffmpeg.exe 放到程序目录，或在截图插件管理界面指定路径。",
                "屏幕录制",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes)
            {
                return;
            }
            format = RecordingFormat.Gif;
        }

        Rectangle bounds = ScreenCapture.VirtualScreenBounds;
        if (ScreenshotSettingParser.ParseRegion(_config.RecordingMode)
            == RecordingRegionMode.Region)
        {
            var background = ScreenCapture.CaptureVirtualScreen();
            using var selector = new RegionSelectorForm(background,
                ScreenCapture.VirtualScreenBounds, SelectionShape.Rectangle);
            if (selector.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            var screen = ScreenCapture.VirtualScreenBounds;
            bounds = new Rectangle(
                screen.Left + selector.SelectedBounds.X,
                screen.Top + selector.SelectedBounds.Y,
                selector.SelectedBounds.Width,
                selector.SelectedBounds.Height);
        }

        _recorder.FfmpegPath = _config.FfmpegPath;
        string? error;
        try
        {
            error = _recorder.Start(
                bounds,
                format,
                _config.RecordingFps,
                _config.RecordingQuality,
                ScreenshotSettingParser.ParseAudio(_config.RecordingAudio),
                _config.RecordingCursorHighlight,
                _config.RecordingClickEffects);
        }
        catch (Exception ex)
        {
            error = "启动录制失败：" + ex.Message;
        }
        if (error != null)
        {
            MessageBox.Show(error, "无法开始录制",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _indicator = new RecordingIndicatorForm(_recorder.IsRecording);
        _indicator.StopRequested += (_, _) => StopRecording();
        _indicator.Show();
        StatusChanged?.Invoke("屏幕录制已开始。");
        StateChanged?.Invoke();
    }

    public void StopRecording()
    {
        if (!_recorder.IsRecording)
        {
            return;
        }
        StatusChanged?.Invoke("正在保存录制...");
        Cursor.Current = Cursors.WaitCursor;
        string? output = null;
        try
        {
            output = _recorder.Stop();
        }
        finally
        {
            Cursor.Current = Cursors.Default;
            _indicator?.Close();
            _indicator = null;
        }
        StateChanged?.Invoke();
        if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
        {
            StatusChanged?.Invoke("录制已保存：" + output);
            MessageBox.Show("录制已保存：\n" + output, "屏幕录制",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            string error = _recorder.LastError ?? "录制失败。";
            StatusChanged?.Invoke(error);
            MessageBox.Show(error, "屏幕录制失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }
        _hotkeyWindow?.Unregister(HotkeyScreenshot);
        _hotkeyWindow?.Unregister(HotkeyScroll);
        _hotkeyWindow?.Unregister(HotkeyRecordPrint);
        _hotkeyWindow?.Unregister(HotkeyRecordF9);
        _hotkeyWindow?.Dispose();
        _hotkeyWindow = null;
        _started = false;
        ScreenshotHotkeyRegistered = false;
        ScrollHotkeyRegistered = false;
        RecordPrintHotkeyRegistered = false;
        RecordF9HotkeyRegistered = false;
        if (_recorder.IsRecording)
        {
            _recorder.Stop();
        }
        _recorder.Dispose();
        _indicator?.Close();
        _indicator = null;
    }

    public void Dispose()
    {
        Stop();
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event Action<int>? HotkeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "MiniMonitorScreenshotHotkeys",
            Parent = new IntPtr(-3), // HWND_MESSAGE
            Style = 0,
            ExStyle = 0
        });
    }

    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        return RegisterHotKey(Handle, id, modifiers, virtualKey);
    }

    public void Unregister(int id)
    {
        UnregisterHotKey(Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}

internal sealed class CountdownForm : Form
{
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _timer;
    private int _remaining;
    private readonly int _seconds;
    public bool Cancelled { get; private set; }

    public CountdownForm(int seconds)
    {
        _seconds = Math.Max(1, seconds);
        _remaining = _seconds;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(280, 96);
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(15, 23, 42);
        KeyPreview = true;
        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold)
        };
        Controls.Add(_label);
        UpdateLabel();
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0)
            {
                Close();
                return;
            }
            UpdateLabel();
        };
        _timer.Start();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Cancelled = true;
                Close();
            }
        };
    }

    private void UpdateLabel()
    {
        _label.Text = _remaining + " 秒后截图\n按 Esc 取消";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class RecordingIndicatorForm : Form
{
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly DateTime _start = DateTime.Now;
    public event EventHandler? StopRequested;

    public RecordingIndicatorForm(bool recording)
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = new Rectangle(area.Right - 250, area.Top + 20, 230, 64);
        BackColor = Color.FromArgb(15, 23, 42);
        _label = new Label
        {
            Location = new Point(16, 12),
            Size = new Size(150, 42),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            Text = "● 正在录制 00:00"
        };
        var stop = new Button
        {
            Text = "停止",
            Location = new Point(166, 16),
            Size = new Size(52, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 38, 38),
            ForeColor = Color.White
        };
        stop.FlatAppearance.BorderSize = 0;
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_label);
        Controls.Add(stop);
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _start;
            _label.Text = "● 正在录制 " + elapsed.ToString(@"mm\:ss");
        };
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
