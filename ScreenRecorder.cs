using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;

namespace MiniMonitor;

/// <summary>屏幕录制：GIF 原生编码；MP4 通过 FFmpeg 原始帧管道；可选系统/麦克风音频。</summary>
public sealed class ScreenRecorder : IDisposable
{
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;

    private CancellationTokenSource? _cts;
    private Thread? _captureThread;
    private Process? _ffmpegProcess;
    private AudioRecorder? _audio;
    private List<Bitmap>? _gifFrames;
    private Rectangle _bounds;
    private RecordingFormat _format;
    private int _fps;
    private string? _tempVideo;
    private string? _tempAudio;
    private bool _cursorHighlight;
    private bool _clickEffects;
    private volatile bool _recording;
    private bool _leftDown;
    private bool _rightDown;
    private long _gifBytes;
    private readonly List<ClickRipple> _ripples = new();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public bool IsRecording => _recording;
    public string? LastOutput { get; private set; }
    public string? LastError { get; private set; }
    public string? FfmpegPath { get; set; }

    public string? Start(Rectangle bounds, RecordingFormat format, int fps, string quality,
        RecordingAudioSource audioSource, bool cursorHighlight, bool clickEffects)
    {
        if (_recording)
        {
            return "已经在录制中。";
        }
        LastOutput = null;
        LastError = null;
        _format = format;
        _fps = Math.Clamp(fps, 5, 60);
        _cursorHighlight = cursorHighlight;
        _clickEffects = clickEffects;
        _bounds = new Rectangle(bounds.X, bounds.Y,
            Math.Max(2, bounds.Width - bounds.Width % 2),
            Math.Max(2, bounds.Height - bounds.Height % 2));

        string outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MiniMonitor");
        Directory.CreateDirectory(outputDir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string extension = format == RecordingFormat.Gif ? ".gif" : ".mp4";
        string output = Path.Combine(outputDir, "录制_" + stamp + extension);

        _tempVideo = Path.Combine(AppPaths.Temp, "MiniMonitor_" + stamp + ".mp4");
        _gifFrames = null;

        if (format == RecordingFormat.Mp4)
        {
            string? ffmpeg = FfmpegLocator.Find(FfmpegPath);
            if (ffmpeg == null)
            {
                return "未找到 ffmpeg.exe。请把 ffmpeg.exe 放到程序目录，或在截图插件管理界面指定 FFmpeg 路径。";
            }
            string crf = quality switch
            {
                "Low" => "28",
                "Medium" => "23",
                _ => "18"
            };
            string args = "-y -f rawvideo -pix_fmt bgra -s " + _bounds.Width + "x" + _bounds.Height
                + " -r " + _fps + " -i - -an -c:v libx264 -preset veryfast -crf " + crf
                + " -pix_fmt yuv420p \"" + _tempVideo + "\"";
            try
            {
                _ffmpegProcess = Process.Start(new ProcessStartInfo(ffmpeg, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true
                });
                if (_ffmpegProcess == null)
                {
                    return "无法启动 FFmpeg。";
                }
                _ffmpegProcess.ErrorDataReceived += (_, e) => { };
                _ffmpegProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                return "启动 FFmpeg 失败：" + ex.Message;
            }

            if (audioSource != RecordingAudioSource.None)
            {
                _tempAudio = Path.Combine(AppPaths.Temp, "MiniMonitor_" + stamp + ".wav");
                _audio = new AudioRecorder();
                if (!_audio.Start(audioSource, _tempAudio))
                {
                    LastError = "音频录制启动失败，继续录制无声视频。";
                    _audio.Dispose();
                    _audio = null;
                    _tempAudio = null;
                }
            }
        }
        else
        {
            _gifFrames = new List<Bitmap>();
        }

        _tempOutputPath = output;
        _cts = new CancellationTokenSource();
        _recording = true;
        _leftDown = false;
        _rightDown = false;
        _ripples.Clear();
        _gifBytes = 0;
        _captureThread = new Thread(() => CaptureLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "MiniMonitorScreenRecorder"
        };
        _captureThread.Start();
        return null;
    }

    private string? _tempOutputPath;

    public string? Stop()
    {
        if (!_recording)
        {
            return LastOutput;
        }
        _recording = false;
        _cts?.Cancel();
        if (_captureThread != null && _captureThread != Thread.CurrentThread)
        {
            _captureThread.Join(4000);
        }
        _audio?.Stop();
        _audio?.Dispose();
        _audio = null;

        try
        {
            if (_format == RecordingFormat.Gif)
            {
                var frames = _gifFrames;
                _gifFrames = null;
                if (frames != null && frames.Count > 0)
                {
                    GifEncoder.Save(_tempOutputPath!, frames, 1000 / Math.Max(1, _fps));
                    foreach (var frame in frames)
                    {
                        frame.Dispose();
                    }
                    LastOutput = _tempOutputPath;
                }
                else
                {
                    LastError = "没有可保存的 GIF 帧。";
                }
            }
            else
            {
                FinalizeMp4();
            }
        }
        catch (Exception ex)
        {
            LastError = "保存录制失败：" + ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _captureThread = null;
        }
        return LastOutput;
    }

    private void FinalizeMp4()
    {
        try
        {
            if (_ffmpegProcess != null && _ffmpegProcess.StandardInput.BaseStream.CanWrite)
            {
                _ffmpegProcess.StandardInput.BaseStream.Flush();
                _ffmpegProcess.StandardInput.Close();
            }
            if (_ffmpegProcess != null)
            {
                _ffmpegProcess.WaitForExit(5000);
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
            }
        }
        catch
        {
            // 关闭管道失败时继续尝试读取输出文件
        }

        if (_tempVideo == null || !File.Exists(_tempVideo))
        {
            LastError = "FFmpeg 未生成视频文件。";
            return;
        }

        string? ffmpeg = FfmpegLocator.Find(FfmpegPath);
        if (_tempAudio != null && File.Exists(_tempAudio) && ffmpeg != null)
        {
            string args = "-y -i \"" + _tempVideo + "\" -i \"" + _tempAudio
                + "\" -c:v copy -c:a aac -b:a 192k -shortest \"" + _tempOutputPath + "\"";
            try
            {
                using var mux = Process.Start(new ProcessStartInfo(ffmpeg, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                });
                mux?.WaitForExit(10000);
            }
            catch
            {
                // 音频混流失败时退回无声视频
            }
        }

        if (!File.Exists(_tempOutputPath))
        {
            File.Move(_tempVideo, _tempOutputPath!);
        }
        LastOutput = _tempOutputPath;
        try
        {
            File.Delete(_tempVideo);
            if (_tempAudio != null)
            {
                File.Delete(_tempAudio);
            }
        }
        catch
        {
            // 临时文件清理失败可忽略
        }
    }

    private void CaptureLoop(CancellationToken token)
    {
        int frameInterval = Math.Max(1, 1000 / _fps);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (!token.IsCancellationRequested)
            {
                long frameStart = stopwatch.ElapsedMilliseconds;
                using var bitmap = ScreenCapture.CaptureRegion(_bounds);
                using (var g = Graphics.FromImage(bitmap))
                {
                    ScreenCapture.DrawCursor(g, _bounds);
                    if (_cursorHighlight || _clickEffects)
                    {
                        TrackClicks();
                        ScreenCapture.DrawCursorEffects(g, _bounds, _ripples, _cursorHighlight);
                    }
                }

                if (_format == RecordingFormat.Mp4)
                {
                    if (!WriteRawFrame(bitmap))
                    {
                        break;
                    }
                }
                else
                {
                    Bitmap gifFrame = ResizeForGif(bitmap);
                    if (_gifFrames != null)
                    {
                        _gifFrames.Add(gifFrame);
                        _gifBytes += gifFrame.Width * (long)gifFrame.Height * 4;
                        if (_gifFrames.Count > _fps * 20 || _gifBytes > 220L * 1024 * 1024)
                        {
                            // GIF 受内存限制约 20 秒，避免超大分辨率导致内存占用失控
                            break;
                        }
                    }
                }

                int elapsed = (int)(stopwatch.ElapsedMilliseconds - frameStart);
                int sleep = frameInterval - elapsed;
                if (sleep > 0)
                {
                    Thread.Sleep(sleep);
                }
            }
        }
        catch (Exception ex)
        {
            LastError = "录制线程异常：" + ex.Message;
        }
    }

    private void TrackClicks()
    {
        bool left = (GetAsyncKeyState(VkLButton) & 0x8000) != 0;
        bool right = (GetAsyncKeyState(VkRButton) & 0x8000) != 0;
        if (left && !_leftDown)
        {
            _ripples.Add(new ClickRipple(Cursor.Position, MouseButtons.Left));
        }
        if (right && !_rightDown)
        {
            _ripples.Add(new ClickRipple(Cursor.Position, MouseButtons.Right));
        }
        _leftDown = left;
        _rightDown = right;
    }

    private bool WriteRawFrame(Bitmap bitmap)
    {
        try
        {
            if (_ffmpegProcess == null)
            {
                return false;
            }
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bitmap.Width * 4;
                var row = new byte[rowBytes];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, rowBytes);
                    _ffmpegProcess.StandardInput.BaseStream.Write(row, 0, rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Bitmap ResizeForGif(Bitmap source)
    {
        const int maxWidth = 960;
        const int maxHeight = 640;
        double scale = Math.Min(1, Math.Min(maxWidth / (double)source.Width,
            maxHeight / (double)source.Height));
        if (scale >= 1)
        {
            return source.Clone() as Bitmap ?? new Bitmap(source);
        }
        int width = Math.Max(1, (int)(source.Width * scale));
        int height = Math.Max(1, (int)(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return result;
    }

    public void Dispose()
    {
        if (_recording)
        {
            Stop();
        }
        _cts?.Dispose();
    }
}

internal sealed class AudioRecorder : IDisposable
{
    private IWaveIn? _capture;
    private WaveFileWriter? _writer;

    public bool Start(RecordingAudioSource source, string path)
    {
        try
        {
            if (source == RecordingAudioSource.System)
            {
                _capture = new WasapiLoopbackCapture();
            }
            else
            {
                _capture = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(44100, 16, 1),
                    BufferMilliseconds = 50
                };
            }
            _writer = new WaveFileWriter(path, _capture.WaveFormat);
            _capture.DataAvailable += (_, e) =>
            {
                try
                {
                    _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                }
                catch
                {
                    // 音频写入失败不影响视频
                }
            };
            _capture.StartRecording();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            // 忽略设备停止异常
        }
        _capture?.Dispose();
        _capture = null;
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose()
    {
        Stop();
    }
}

internal static class FfmpegLocator
{
    public static string? Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        string appDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(appDir, "ffmpeg.exe"),
            Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path != null)
        {
            foreach (var dir in path.Split(';'))
            {
                string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    public static bool IsAvailable(string? configured)
    {
        return Find(configured) != null;
    }
}
