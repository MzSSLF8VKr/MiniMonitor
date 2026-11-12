using System.Drawing;

namespace MiniMonitor.Video;

internal sealed class LosslessFile
{
    public string Path { get; set; } = "";
    public VideoMediaInfo Info { get; set; } = new();
    public List<LosslessSegment> Segments { get; set; } = new();
    public List<double> Keyframes { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool KeyframesLoaded { get; set; }
    public bool WaveformLoaded { get; set; }
    public bool LoadingDetails { get; set; }
    public float[]? Waveform { get; set; }
    public string? ThumbnailFile { get; set; }
    public Image? Thumbnail { get; set; }

    public string Name => System.IO.Path.GetFileName(Path);
    public double Duration => Info.Duration;

    public void EnsureFullSegment()
    {
        if (Segments.Count == 0 && Duration > 0.05)
        {
            Segments.Add(new LosslessSegment
            {
                Start = 0,
                End = Duration,
                Name = "完整文件"
            });
        }
    }

    public LosslessSegment? SegmentAt(double time)
    {
        return Segments.FirstOrDefault(segment => time >= segment.Start && time <= segment.End);
    }

    public double NearestKeyframe(double time)
    {
        if (Keyframes.Count == 0)
        {
            return time;
        }
        int low = 0;
        int high = Keyframes.Count - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Keyframes[middle] < time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        double candidate = Keyframes[low];
        if (low > 0 && Math.Abs(Keyframes[low - 1] - time) < Math.Abs(candidate - time))
        {
            candidate = Keyframes[low - 1];
        }
        return candidate;
    }
}

internal sealed class LosslessSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Start { get; set; }
    public double End { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public double Duration => Math.Max(0, End - Start);

    public LosslessSegment Clone()
    {
        return new LosslessSegment
        {
            Id = Guid.NewGuid().ToString("N"),
            Start = Start,
            End = End,
            Name = Name,
            Enabled = Enabled
        };
    }
}
