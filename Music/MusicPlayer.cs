using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MiniMonitor.Music;

public enum MusicPlaybackState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// 本地音乐播放引擎。完全基于 Windows 原生音频输出（NAudio / WASAPI / WinMM），
/// 不下载、不上传任何内容。
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private readonly MusicSettings _settings;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Random _random = new();

    private IWavePlayer? _device;
    private AudioFileReader? _reader;
    private EqSampleProvider? _eq;
    private StereoWidthSampleProvider? _stereo;
    private VarispeedSampleProvider? _speed;
    private GainSampleProvider? _gain;

    private readonly List<MusicTrack> _queue = new();
    private readonly Stack<int> _history = new();
    private int _index = -1;
    private bool _ending;
    private bool _disposed;
    private bool _stopAfterFade;

    private double _rgFactor = 1.0;
    private float _fadeGain = 1f;
    private float _fadeFrom = 1f;
    private float _fadeTarget = 1f;
    private DateTime _fadeStarted = DateTime.UtcNow;
    private int _fadeDurationMs;

    private TimeSpan? _abA;
    private TimeSpan? _abB;
    private DateTime? _sleepAt;

    public MusicPlayer(MusicSettings settings)
    {
        _settings = settings;
        _timer = new System.Windows.Forms.Timer { Interval = 200 };
        _timer.Tick += (_, _) => OnTick();
    }

    public event EventHandler? StateChanged;
    public event EventHandler? TrackChanged;
    public event EventHandler? QueueChanged;
    public event EventHandler? PositionChanged;
    public event EventHandler<string>? PlaybackError;

    public MusicPlaybackState State { get; private set; } = MusicPlaybackState.Stopped;

    public MusicTrack? CurrentTrack => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;

    public IReadOnlyList<MusicTrack> Queue => _queue;

    public int QueueIndex => _index;

    public bool IsPlaying => State == MusicPlaybackState.Playing;

    public bool IsPaused => State == MusicPlaybackState.Paused;

    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;

    public TimeSpan Duration => _reader?.TotalTime ?? CurrentTrack?.Duration ?? TimeSpan.Zero;

    public TimeSpan? AbLoopStart => _abA;

    public TimeSpan? AbLoopEnd => _abB;

    public DateTime? SleepDeadline => _sleepAt;

    public void SetQueue(IEnumerable<MusicTrack> tracks, int startIndex, bool autoPlay)
    {
        _queue.Clear();
        _queue.AddRange(tracks);
        _history.Clear();
        _index = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        if (_index >= 0)
        {
            LoadTrack(autoPlay);
        }
        else
        {
            StopInternal();
        }
    }

    public void PlayTrack(MusicTrack track, IEnumerable<MusicTrack>? context = null)
    {
        if (context != null)
        {
            _queue.Clear();
            _queue.AddRange(context);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        int index = _queue.FindIndex(t => t.Id == track.Id);
        if (index < 0)
        {
            _queue.Add(track);
            QueueChanged?.Invoke(this, EventArgs.Empty);
            index = _queue.Count - 1;
        }
        _index = index;
        _history.Clear();
        LoadTrack(true);
    }

    public void PlayAt(int index)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }
        if (_index >= 0 && _index != index)
        {
            _history.Push(_index);
        }
        _index = index;
        LoadTrack(true);
    }

    public void TogglePlayPause()
    {
        if (_queue.Count == 0)
        {
            return;
        }
        if (State == MusicPlaybackState.Playing)
        {
            Pause();
        }
        else if (State == MusicPlaybackState.Paused)
        {
            Resume();
        }
        else
        {
            if (_index < 0) _index = 0;
            LoadTrack(true);
        }
    }

    public void Pause()
    {
        if (_device == null || State != MusicPlaybackState.Playing)
        {
            return;
        }
        _device.Pause();
        State = MusicPlaybackState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (_device == null)
        {
            LoadTrack(true);
            return;
        }
        _device.Play();
        State = MusicPlaybackState.Playing;
        if (_settings.FadeInOut)
        {
            StartFade(1f, 350);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (_settings.FadeInOut && State == MusicPlaybackState.Playing)
        {
            StartFade(0f, 250);
            _stopAfterFade = true;
            return;
        }
        StopInternal();
    }

    private void StopInternal()
    {
        _timer.Stop();
        _stopAfterFade = false;
        if (_device != null)
        {
            try { _device.Stop(); } catch { }
            _device.Dispose();
            _device = null;
        }
        _reader?.Dispose();
        _reader = null;
        _eq = null;
        _speed = null;
        _stereo = null;
        _gain = null;
        State = MusicPlaybackState.Stopped;
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        if (_queue.Count == 0)
        {
            return;
        }
        bool hasNext;
        int next;
        if (_settings.Shuffle && _queue.Count > 1)
        {
            do
            {
                next = _random.Next(_queue.Count);
            } while (next == _index);
            hasNext = true;
        }
        else
        {
            next = _index + 1;
            hasNext = next < _queue.Count;
            if (!hasNext && _settings.RepeatMode == "all")
            {
                next = 0;
                hasNext = true;
            }
        }
        if (!hasNext)
        {
            Stop();
            return;
        }
        if (_index >= 0)
        {
            _history.Push(_index);
        }
        _index = next;
        LoadTrack(true);
    }

    public void Previous()
    {
        if (_queue.Count == 0)
        {
            return;
        }
        if (Position > TimeSpan.FromSeconds(5))
        {
            Seek(TimeSpan.Zero);
            return;
        }
        int previous;
        if (_history.Count > 0)
        {
            previous = _history.Pop();
        }
        else
        {
            previous = _index - 1;
            if (previous < 0)
            {
                previous = _settings.RepeatMode == "all" ? _queue.Count - 1 : 0;
            }
        }
        _index = Math.Clamp(previous, 0, _queue.Count - 1);
        LoadTrack(true);
    }

    public void Seek(TimeSpan position)
    {
        if (_reader == null)
        {
            return;
        }
        try
        {
            TimeSpan target = position;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > _reader.TotalTime) target = _reader.TotalTime;
            _reader.CurrentTime = target;
            _ending = false;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // 部分流式解码器不支持精确定位
        }
    }

    public void SeekFraction(double fraction)
    {
        if (_reader == null)
        {
            return;
        }
        Seek(TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 1) * _reader.TotalTime.TotalSeconds));
    }

    public void SetVolume(int volume)
    {
        _settings.Volume = Math.Clamp(volume, 0, 100);
        ApplyGain();
    }

    public void SetMute(bool mute)
    {
        _settings.Mute = mute;
        ApplyGain();
    }

    public void SetShuffle(bool shuffle)
    {
        _settings.Shuffle = shuffle;
    }

    public string CycleRepeatMode()
    {
        _settings.RepeatMode = _settings.RepeatMode switch
        {
            "off" => "all",
            "all" => "one",
            _ => "off"
        };
        return _settings.RepeatMode;
    }

    public void SetEq(float[] gains, bool enabled, float preampDb)
    {
        _settings.EqGains = gains.Take(6).ToArray();
        _settings.EqEnabled = enabled;
        _settings.PreampDb = preampDb;
        _eq?.Update(_settings.EqGains, _settings.PreampDb, enabled);
    }

    public void SetStereo(float balance, float width)
    {
        _settings.Balance = Math.Clamp(balance, -1f, 1f);
        _settings.StereoWidth = Math.Clamp(width, 0f, 2f);
        if (_stereo != null)
        {
            _stereo.Balance = _settings.Balance;
            _stereo.Width = _settings.StereoWidth;
        }
    }

    public void SetSpeed(double speed)
    {
        _settings.PlaybackSpeed = Math.Clamp(speed, 0.5, 2.0);
        if (_speed != null)
        {
            _speed.Speed = (float)_settings.PlaybackSpeed;
        }
    }

    public void SetReplayGainEnabled(bool enabled)
    {
        _settings.ReplayGainEnabled = enabled;
        _rgFactor = enabled && CurrentTrack != null
            ? Math.Clamp(Math.Pow(10, CurrentTrack.GainDb / 20.0), 0.25, 4.0)
            : 1.0;
        ApplyGain();
    }

    public void SetSleepTimer(int minutes)
    {
        _settings.SleepMinutes = Math.Clamp(minutes, 0, 24 * 60);
        _sleepAt = minutes <= 0 ? null : DateTime.Now.AddMinutes(minutes);
    }

    public void SetAbLoop(TimeSpan? a, TimeSpan? b)
    {
        _abA = a;
        _abB = b;
    }

    public void ClearAbLoop()
    {
        _abA = null;
        _abB = null;
    }

    private void LoadTrack(bool autoPlay)
    {
        var track = CurrentTrack;
        if (track == null)
        {
            StopInternal();
            return;
        }
        _ending = false;
        _stopAfterFade = false;
        ClearAbLoop();
        if (!File.Exists(track.Path))
        {
            PlaybackError?.Invoke(this, $"文件不存在：{track.Path}");
            State = MusicPlaybackState.Stopped;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        try
        {
            DisposeChain();
            var reader = new AudioFileReader(track.Path);
            _reader = reader;
            ISampleProvider chain = reader;
            var eq = new EqSampleProvider(chain);
            eq.Update(_settings.EqGains, _settings.PreampDb, _settings.EqEnabled);
            _eq = eq;
            chain = eq;
            var stereo = new StereoWidthSampleProvider(chain)
            {
                Balance = _settings.Balance,
                Width = _settings.StereoWidth
            };
            _stereo = stereo;
            chain = stereo;
            var speed = new VarispeedSampleProvider(chain) { Speed = (float)_settings.PlaybackSpeed };
            _speed = speed;
            chain = speed;
            var gain = new GainSampleProvider(chain);
            _gain = gain;
            chain = gain;
            _rgFactor = _settings.ReplayGainEnabled
                ? Math.Clamp(Math.Pow(10, track.GainDb / 20.0), 0.25, 4.0)
                : 1.0;
            _fadeGain = _settings.FadeInOut ? 0f : 1f;
            _fadeFrom = _fadeGain;
            _fadeTarget = 1f;
            _fadeStarted = DateTime.UtcNow;
            _fadeDurationMs = 700;
            ApplyGain();

            _device = CreateDevice();
            try
            {
                _device.Init(_gain);
            }
            catch when (_settings.ExclusiveOutput)
            {
                // 独占模式不可用时自动回退到共享输出
                try { _device.Dispose(); } catch { }
                _device = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 };
                _device.Init(_gain);
            }
            if (autoPlay)
            {
                _device.Play();
                State = MusicPlaybackState.Playing;
            }
            else
            {
                State = MusicPlaybackState.Paused;
            }
            _timer.Start();
            StateChanged?.Invoke(this, EventArgs.Empty);
            TrackChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DisposeChain();
            State = MusicPlaybackState.Stopped;
            StateChanged?.Invoke(this, EventArgs.Empty);
            PlaybackError?.Invoke(this,
                $"无法播放“{track.DisplayTitle}”：{ex.Message}\r\n" +
                "支持 Windows 原生解码的 MP3 / FLAC / WAV / AIFF / M4A / AAC / WMA 等格式。");
        }
    }

    private IWavePlayer CreateDevice()
    {
        if (_settings.ExclusiveOutput)
        {
            try
            {
                return new WasapiOut(AudioClientShareMode.Exclusive, 0);
            }
            catch
            {
                // 独占输出失败时回退到共享模式
            }
        }
        return new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 };
    }

    private void DisposeChain()
    {
        if (_device != null)
        {
            try { _device.Stop(); } catch { }
            _device.Dispose();
            _device = null;
        }
        _reader?.Dispose();
        _reader = null;
        _eq = null;
        _stereo = null;
        _speed = null;
        _gain = null;
    }

    private void ApplyGain()
    {
        if (_gain == null)
        {
            return;
        }
        float user = _settings.Mute ? 0f : _settings.Volume / 100f;
        _gain.Gain = (float)(user * _rgFactor * _fadeGain);
    }

    private void StartFade(float target, int durationMs)
    {
        _fadeFrom = _fadeGain;
        _fadeTarget = target;
        _fadeStarted = DateTime.UtcNow;
        _fadeDurationMs = Math.Max(30, durationMs);
    }

    private void OnTick()
    {
        if (_reader == null)
        {
            return;
        }
        if (_fadeGain != _fadeTarget)
        {
            double elapsed = (DateTime.UtcNow - _fadeStarted).TotalMilliseconds;
            float progress = (float)Math.Clamp(elapsed / _fadeDurationMs, 0, 1);
            _fadeGain = _fadeFrom + (_fadeTarget - _fadeFrom) * progress;
            ApplyGain();
            if (progress >= 1)
            {
                _fadeGain = _fadeTarget;
                if (_stopAfterFade && _fadeTarget <= 0.001f)
                {
                    StopInternal();
                    return;
                }
            }
        }

        TimeSpan position = _reader.CurrentTime;
        if (_abA.HasValue && _abB.HasValue && _abB.Value > _abA.Value
            && position >= _abB.Value)
        {
            Seek(_abA.Value);
        }

        if (_sleepAt.HasValue && DateTime.Now >= _sleepAt.Value)
        {
            _sleepAt = null;
            _settings.SleepMinutes = 0;
            Stop();
        }

        PositionChanged?.Invoke(this, EventArgs.Empty);

        if (!_ending && _reader.TotalTime > TimeSpan.Zero
            && position >= _reader.TotalTime - TimeSpan.FromMilliseconds(420))
        {
            _ending = true;
            OnTrackFinished();
        }
    }

    private void OnTrackFinished()
    {
        var track = CurrentTrack;
        if (track != null && _reader != null)
        {
            double played = Math.Max(_reader.CurrentTime.TotalSeconds, 0);
            if (played >= Math.Min(30, Math.Max(5, track.DurationSeconds * 0.3)))
            {
                PlayCompleted?.Invoke(this, track);
            }
        }
        if (_settings.RepeatMode == "one")
        {
            _ending = false;
            Seek(TimeSpan.Zero);
            return;
        }
        bool hasNext = _settings.Shuffle && _queue.Count > 1 || _index + 1 < _queue.Count
            || _settings.RepeatMode == "all";
        if (!hasNext)
        {
            StopInternal();
            return;
        }
        Next();
    }

    /// <summary>曲目自然播放完成时触发（用于写入播放次数）。</summary>
    public event EventHandler<MusicTrack>? PlayCompleted;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        DisposeChain();
    }
}

/// <summary>6 段均衡器 + 前置增益。</summary>
internal sealed class EqSampleProvider : ISampleProvider
{
    public static readonly float[] Frequencies = { 60f, 230f, 910f, 3600f, 14000f };

    private readonly ISampleProvider _source;
    private BiQuadFilter[,]? _filters;
    private bool _enabled;
    private float _preamp = 1f;

    public EqSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void Update(float[] gainsDb, float preampDb, bool enabled)
    {
        _enabled = enabled;
        _preamp = (float)Math.Pow(10, preampDb / 20.0);
        int channels = Math.Max(1, WaveFormat.Channels);
        int sampleRate = WaveFormat.SampleRate;
        var filters = new BiQuadFilter[Frequencies.Length, channels];
        for (int band = 0; band < Frequencies.Length; band++)
        {
            float gain = gainsDb.Length > band ? gainsDb[band] : 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                filters[band, channel] = BiQuadFilter.PeakingEQ(sampleRate, Frequencies[band], 1.0f, gain);
            }
        }
        _filters = filters;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (!_enabled || _filters == null)
        {
            return read;
        }
        int channels = Math.Max(1, WaveFormat.Channels);
        for (int i = 0; i < read; i += channels)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                float sample = buffer[offset + i + channel] * _preamp;
                for (int band = 0; band < Frequencies.Length; band++)
                {
                    sample = _filters[band, channel].Transform(sample);
                }
                buffer[offset + i + channel] = Math.Clamp(sample, -1f, 1f);
            }
        }
        return read;
    }
}

/// <summary>立体声宽度与左右平衡。</summary>
internal sealed class StereoWidthSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public float Balance { get; set; }
    public float Width { get; set; } = 1f;

    public StereoWidthSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (WaveFormat.Channels != 2 || Math.Abs(Balance) < 0.001f && Math.Abs(Width - 1f) < 0.001f)
        {
            return read;
        }
        float leftGain = Balance <= 0 ? 1f : 1f - Balance;
        float rightGain = Balance >= 0 ? 1f : 1f + Balance;
        for (int i = 0; i + 1 < read; i += 2)
        {
            float left = buffer[offset + i];
            float right = buffer[offset + i + 1];
            float mid = (left + right) * 0.5f;
            float side = (left - right) * 0.5f * Width;
            buffer[offset + i] = Math.Clamp((mid + side) * leftGain, -1f, 1f);
            buffer[offset + i + 1] = Math.Clamp((mid - side) * rightGain, -1f, 1f);
        }
        return read;
    }
}

/// <summary>均衡增益（用户音量 × ReplayGain × 淡入淡出）。</summary>
internal sealed class GainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    public float Gain { get; set; } = 1f;

    public GainSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (Math.Abs(Gain - 1f) < 0.0001f)
        {
            return read;
        }
        for (int i = 0; i < read; i++)
        {
            buffer[offset + i] = Math.Clamp(buffer[offset + i] * Gain, -1f, 1f);
        }
        return read;
    }
}

/// <summary>变速播放（类似唱片转速，速度变化会同时改变音高）。</summary>
internal sealed class VarispeedSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _buffer = Array.Empty<float>();
    private int _frames;
    private double _position;
    private float _speed = 1f;

    public VarispeedSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, 0.5f, 2f);
    }

    public int Read(float[] output, int offset, int count)
    {
        if (Math.Abs(_speed - 1f) < 0.001f)
        {
            return _source.Read(output, offset, count);
        }
        int channels = Math.Max(1, WaveFormat.Channels);
        int framesWanted = count / channels;
        int framesDone = 0;
        while (framesDone < framesWanted)
        {
            if (_position + 1.0 >= _frames)
            {
                if (!Refill(framesWanted - framesDone))
                {
                    // 结尾补静音，避免设备欠载
                    Array.Clear(output, offset + framesDone * channels,
                        (framesWanted - framesDone) * channels);
                    framesDone = framesWanted;
                    break;
                }
            }
            int index = Math.Clamp((int)_position, 0, Math.Max(0, _frames - 2));
            float fraction = (float)(_position - index);
            for (int channel = 0; channel < channels; channel++)
            {
                float a = _buffer[index * channels + channel];
                float b = _buffer[(index + 1) * channels + channel];
                output[offset + (framesDone * channels) + channel] = a + (b - a) * fraction;
            }
            _position += _speed;
            framesDone++;
        }
        return framesDone * channels;
    }

    private bool Refill(int framesNeeded)
    {
        int channels = Math.Max(1, WaveFormat.Channels);
        int keep = _frames > 0 ? 1 : 0;
        double newPosition = keep > 0 ? _position - (_frames - 1) : 0;
        int inputFrames = (int)Math.Ceiling(framesNeeded * _speed) + 4;
        int capacity = (keep + inputFrames) * channels;
        if (_buffer.Length < capacity)
        {
            Array.Resize(ref _buffer, capacity);
        }
        if (keep > 0)
        {
            Array.Copy(_buffer, (_frames - 1) * channels, _buffer, 0, channels);
        }
        int read = _source.Read(_buffer, keep * channels, inputFrames * channels);
        if (read <= 0)
        {
            _frames = 0;
            _position = 0;
            return false;
        }
        _frames = keep + read / channels;
        _position = newPosition;
        if (_frames < 2)
        {
            return false;
        }
        _position = Math.Clamp(_position, 0, _frames - 2);
        return true;
    }
}
