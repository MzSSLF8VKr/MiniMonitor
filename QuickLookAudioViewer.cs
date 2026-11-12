using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MiniMonitor;

/// <summary>QuickLook 音频预览：播放 / 暂停、进度拖动、音量与基本信息。</summary>
internal sealed class QuickLookAudioViewer : UserControl
{
    private readonly Panel _card;
    private readonly Label _title;
    private readonly Label _meta;
    private readonly PillButton _playButton;
    private readonly TrackBar _seek;
    private readonly Label _timeLabel;
    private readonly TrackBar _volume;
    private readonly Label _hint;
    private readonly System.Windows.Forms.Timer _timer;
    private WaveStream? _reader;
    private IWavePlayer? _output;
    private bool _updatingSeek;
    private bool _disposed;
    private bool _stopping;
    private string _path = "";

    public QuickLookAudioViewer()
    {
        BackColor = Color.FromArgb(15, 23, 42);
        DoubleBuffered = true;

        _card = new Panel
        {
            BackColor = Color.FromArgb(30, 41, 59),
            Size = new Size(680, 320),
            Anchor = AnchorStyles.None
        };
        Controls.Add(_card);

        _title = new Label
        {
            Location = new Point(30, 24),
            Size = new Size(620, 34),
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            Text = "音频预览"
        };
        _meta = new Label
        {
            Location = new Point(30, 60),
            Size = new Size(620, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };
        _playButton = new PillButton
        {
            Location = new Point(285, 104),
            Size = new Size(110, 42),
            Text = "暂停",
            ButtonStyle = PillButtonStyle.Primary
        };
        _playButton.Click += (_, _) => TogglePlay();
        _seek = new TrackBar
        {
            Location = new Point(30, 166),
            Size = new Size(620, 34),
            Minimum = 0,
            Maximum = 1000,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 50
        };
        _seek.Scroll += (_, _) => SeekToPosition();
        _seek.MouseUp += (_, _) => SeekToPosition();
        _timeLabel = new Label
        {
            Location = new Point(30, 204),
            Size = new Size(620, 24),
            Font = new Font("Consolas", 9f),
            ForeColor = Color.FromArgb(226, 232, 240),
            BackColor = Color.Transparent,
            Text = "00:00 / 00:00",
            TextAlign = ContentAlignment.MiddleCenter
        };
        var volumeTitle = new Label
        {
            Location = new Point(30, 248),
            Size = new Size(46, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.Transparent,
            Text = "音量"
        };
        _volume = new TrackBar
        {
            Location = new Point(76, 242),
            Size = new Size(180, 32),
            Minimum = 0,
            Maximum = 100,
            Value = 80,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 10
        };
        _volume.ValueChanged += (_, _) =>
        {
            if (_output != null)
            {
                _output.Volume = _volume.Value / 100f;
            }
        };
        _hint = new Label
        {
            Location = new Point(270, 246),
            Size = new Size(380, 26),
            Font = new Font("Microsoft YaHei UI", 7.8f),
            ForeColor = Color.FromArgb(100, 116, 139),
            BackColor = Color.Transparent,
            Text = "空格 / Esc 关闭预览",
            TextAlign = ContentAlignment.MiddleRight
        };
        _card.Controls.AddRange(new Control[]
        {
            _title, _meta, _playButton, _seek, _timeLabel, volumeTitle, _volume, _hint
        });
        Resize += (_, _) => LayoutCard();

        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => UpdateProgress();
    }

    public void PausePlayback()
    {
        if (_output != null && _output.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
        }
    }

    public void LoadFile(string path)
    {
        _path = path;
        DisposePlayback();
        _title.Text = Path.GetFileName(path);
        _meta.Text = "正在加载音频…";
        _timeLabel.Text = "00:00 / 00:00";
        _seek.Value = 0;
        try
        {
            MediaFoundationApi.Startup();
        }
        catch
        {
            // 旧系统可能不支持 Media Foundation
        }
        try
        {
            _reader = OpenReader(path);
            _output = CreateOutput();
            _output.Init(_reader);
            _output.Volume = _volume.Value / 100f;
            _output.PlaybackStopped += OnPlaybackStopped;
            var format = _reader.WaveFormat;
            _meta.Text = FormatDuration(_reader.TotalTime)
                + "  ·  " + format.SampleRate + " Hz"
                + "  ·  " + format.Channels + " 声道"
                + "  ·  " + FormatSize(new FileInfo(path).Length);
            _playButton.Text = "暂停";
            _playButton.Enabled = true;
            _output.Play();
            _timer.Start();
        }
        catch (Exception ex)
        {
            _meta.Text = "无法播放该音频：" + ex.Message;
            _playButton.Text = "播放";
            _playButton.Enabled = false;
        }
    }

    private static WaveStream OpenReader(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".wav")
        {
            return new WaveFileReader(path);
        }
        if (extension == ".mp3")
        {
            try
            {
                return new MediaFoundationReader(path);
            }
            catch
            {
                return new Mp3FileReader(path);
            }
        }
        return new MediaFoundationReader(path);
    }

    private static IWavePlayer CreateOutput()
    {
        try
        {
            return new WasapiOut(AudioClientShareMode.Shared, 80);
        }
        catch
        {
            return new WaveOutEvent();
        }
    }

    private void TogglePlay()
    {
        if (_output == null || _reader == null)
        {
            return;
        }
        if (_output.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
            _playButton.Text = "播放";
        }
        else
        {
            if (_reader.Position >= _reader.Length)
            {
                _reader.Position = 0;
            }
            _output.Play();
            _playButton.Text = "暂停";
        }
    }

    private void SeekToPosition()
    {
        if (_updatingSeek || _reader == null || _reader.TotalTime.TotalMilliseconds <= 0)
        {
            return;
        }
        double seconds = _reader.TotalTime.TotalSeconds * (_seek.Value / 1000.0);
        try
        {
            _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0,
                _reader.TotalTime.TotalSeconds));
        }
        catch
        {
            // 某些格式不支持随机定位
        }
        UpdateTimeLabel();
    }

    private void UpdateProgress()
    {
        if (_reader == null || _disposed)
        {
            return;
        }
        if (_reader.TotalTime.TotalMilliseconds > 0)
        {
            _updatingSeek = true;
            int value = (int)Math.Round(
                _reader.CurrentTime.TotalMilliseconds / _reader.TotalTime.TotalMilliseconds * 1000);
            _seek.Value = Math.Clamp(value, 0, 1000);
            _updatingSeek = false;
        }
        UpdateTimeLabel();
    }

    private void UpdateTimeLabel()
    {
        if (_reader == null)
        {
            return;
        }
        _timeLabel.Text = FormatDuration(_reader.CurrentTime)
            + " / " + FormatDuration(_reader.TotalTime);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_disposed || _stopping)
        {
            return;
        }
        if (e.Exception != null)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _meta.Text = "播放失败：" + e.Exception.Message;
                    _playButton.Text = "播放";
                }));
            }
            catch
            {
                // 窗口已关闭
            }
            return;
        }
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_reader == null)
                {
                    return;
                }
                if (_reader.Position >= _reader.Length)
                {
                    _reader.Position = 0;
                    _seek.Value = 0;
                    _playButton.Text = "播放";
                }
            }));
        }
        catch
        {
            // 窗口已关闭
        }
    }

    private static string FormatDuration(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }
        if (time.TotalHours >= 1)
        {
            return ((int)time.TotalHours).ToString("00") + ":"
                + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00");
        }
        return time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0") + " KB";
        }
        return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
    }

    private void LayoutCard()
    {
        int width = Math.Max(480, Math.Min(760, Width - 80));
        int height = Math.Max(300, Math.Min(360, Height - 60));
        _card.Size = new Size(width, height);
        _card.Location = new Point((Width - width) / 2, (Height - height) / 2);
        _title.Width = width - 60;
        _meta.Width = width - 60;
        _playButton.Location = new Point((width - _playButton.Width) / 2, 104);
        _seek.Width = width - 60;
        _timeLabel.Width = width - 60;
        _hint.Width = Math.Max(120, width - 300);
        _hint.Location = new Point(width - _hint.Width - 30, 246);
    }

    private void DisposePlayback()
    {
        _stopping = true;
        _timer?.Stop();
        try
        {
            _output?.Stop();
        }
        catch
        {
            // 忽略
        }
        try
        {
            _output?.Dispose();
        }
        catch
        {
            // 忽略
        }
        _output = null;
        try
        {
            _reader?.Dispose();
        }
        catch
        {
            // 忽略
        }
        _reader = null;
        _stopping = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            DisposePlayback();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
