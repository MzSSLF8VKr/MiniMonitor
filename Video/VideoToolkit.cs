using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniMonitor.Video;

internal sealed class VideoMediaInfo
{
    public string Path { get; set; } = "";
    public string FormatName { get; set; } = "";
    public double Duration { get; set; }
    public long Size { get; set; }
    public long BitRate { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public string VideoCodec { get; set; } = "";
    public string PixelFormat { get; set; } = "";
    public string AudioCodec { get; set; } = "";
    public int AudioChannels { get; set; }
    public int AudioSampleRate { get; set; }
    public int Rotation { get; set; }
    public bool IsImage { get; set; }
    public bool HasVideo => Width > 0 && Height > 0;
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec);
    public bool HasKeyframes { get; set; }

    public string AspectLabel => Width > 0 && Height > 0
        ? Width + "×" + Height + (Fps > 0 ? " · " + Fps.ToString("0.###", CultureInfo.InvariantCulture) + " fps" : "")
        : "—";

    public string CodecLabel
    {
        get
        {
            string video = string.IsNullOrWhiteSpace(VideoCodec) ? "" : "视频 " + VideoCodec;
            string audio = string.IsNullOrWhiteSpace(AudioCodec) ? "" : "音频 " + AudioCodec;
            if (video.Length == 0)
            {
                return audio.Length == 0 ? "—" : audio;
            }
            return audio.Length == 0 ? video : video + " · " + audio;
        }
    }
}

internal sealed class HardwareEncoders
{
    public bool H264Nvenc { get; set; }
    public bool HevcNvenc { get; set; }
    public bool Av1Nvenc { get; set; }
    public bool H264Qsv { get; set; }
    public bool HevcQsv { get; set; }
    public bool H264Amf { get; set; }
    public bool HevcAmf { get; set; }
    public bool SvtAv1 { get; set; }
    public bool AomAv1 { get; set; }
    public bool GpuAvailable => H264Nvenc || HevcNvenc || H264Qsv || HevcQsv || H264Amf || HevcAmf;

    public string Label
    {
        get
        {
            if (H264Nvenc || HevcNvenc || Av1Nvenc) return "NVIDIA NVENC";
            if (H264Qsv || HevcQsv) return "Intel Quick Sync";
            if (H264Amf || HevcAmf) return "AMD AMF";
            return "CPU";
        }
    }

    public string H264(bool gpu)
    {
        if (gpu && H264Nvenc) return "h264_nvenc";
        if (gpu && H264Qsv) return "h264_qsv";
        if (gpu && H264Amf) return "h264_amf";
        return "libx264";
    }

    public string Hevc(bool gpu)
    {
        if (gpu && HevcNvenc) return "hevc_nvenc";
        if (gpu && HevcQsv) return "hevc_qsv";
        if (gpu && HevcAmf) return "hevc_amf";
        return "libx265";
    }

    public string Av1(bool gpu)
    {
        if (gpu && Av1Nvenc) return "av1_nvenc";
        if (SvtAv1) return "libsvtav1";
        return "libaom-av1";
    }
}

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

internal sealed record FfmpegResult(int ExitCode, string StdErr);

/// <summary>
/// 视频工具公共层：定位 ffmpeg / ffprobe、探测媒体信息、读取关键帧、
/// 生成缩略图与波形、检测场景和黑帧、检测 GPU 编码器、执行带进度的 ffmpeg。
/// </summary>
internal static class VideoToolkit
{
    private static readonly Regex SceneTimeRegex = new(@"pts_time:([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled);
    private static readonly Regex BlackStartRegex = new(@"black_start:([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled);
    private static readonly Regex BlackEndRegex = new(@"black_end:([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled);

    public static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    public static bool IsImageFile(string path)
    {
        return Array.IndexOf(ImageExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
    }

    public static string? Ffmpeg(string? configured)
    {
        return FfmpegLocator.Find(configured);
    }

    public static string? Ffprobe(string? configured)
    {
        var names = new List<string>();
        string? ffmpeg = Ffmpeg(configured);
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            string? directory = Path.GetDirectoryName(ffmpeg);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                names.Add(Path.Combine(directory, "ffprobe.exe"));
            }
        }
        names.Add(Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"));
        names.Add(Path.Combine(Environment.CurrentDirectory, "ffprobe.exe"));
        names.Add(@"C:\ffmpeg\bin\ffprobe.exe");
        names.Add(@"C:\Program Files\ffmpeg\bin\ffprobe.exe");
        foreach (string candidate in names)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (string directory in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory.Trim(), "ffprobe.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    public static async Task<VideoMediaInfo> ProbeAsync(string path, string? configuredFfmpeg,
        CancellationToken ct)
    {
        var info = new VideoMediaInfo
        {
            Path = path,
            IsImage = IsImageFile(path)
        };
        try
        {
            var file = new FileInfo(path);
            info.Size = file.Exists ? file.Length : 0;
        }
        catch
        {
            // ignore
        }
        string? ffprobe = Ffprobe(configuredFfmpeg);
        if (ffprobe != null)
        {
            try
            {
                await ProbeWithFfprobeAsync(path, ffprobe, info, ct);
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                VideoLog.Warn("ffprobe 探测失败，尝试 ffmpeg：" + path + " · " + ex.Message);
            }
        }
        string? ffmpeg = Ffmpeg(configuredFfmpeg);
        if (ffmpeg != null)
        {
            try
            {
                await ProbeWithFfmpegAsync(path, ffmpeg, info, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                VideoLog.Warn("ffmpeg 探测失败：" + path + " · " + ex.Message);
            }
        }
        if (info.IsImage)
        {
            info.Duration = 0;
        }
        return info;
    }

    private static async Task ProbeWithFfprobeAsync(string path, string ffprobe,
        VideoMediaInfo info, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-i", path
        };
        var result = await RunCaptureAsync(ffprobe, args, ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
        {
            throw new InvalidOperationException(Tail(result.StdErr, 400));
        }
        using var document = JsonDocument.Parse(result.StdOut);
        var root = document.RootElement;
        if (root.TryGetProperty("format", out var format))
        {
            info.FormatName = GetString(format, "format_long_name");
            if (info.FormatName.Length == 0)
            {
                info.FormatName = GetString(format, "format_name");
            }
            info.Duration = GetDouble(format, "duration");
            info.BitRate = GetLong(format, "bit_rate");
        }
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        bool foundVideo = false;
        bool foundAudio = false;
        foreach (var stream in streams.EnumerateArray())
        {
            string type = GetString(stream, "codec_type");
            if (type == "video" && !foundVideo)
            {
                foundVideo = true;
                info.VideoCodec = GetString(stream, "codec_name");
                info.PixelFormat = GetString(stream, "pix_fmt");
                int width = (int)GetLong(stream, "width");
                int height = (int)GetLong(stream, "height");
                info.Fps = ParseRational(GetString(stream, "r_frame_rate"));
                if (info.Fps <= 0)
                {
                    info.Fps = ParseRational(GetString(stream, "avg_frame_rate"));
                }
                info.Rotation = ReadRotation(stream);
                if (info.Rotation is 90 or 270)
                {
                    info.Width = height;
                    info.Height = width;
                }
                else
                {
                    info.Width = width;
                    info.Height = height;
                }
                if (info.Duration <= 0)
                {
                    info.Duration = GetDouble(stream, "duration");
                }
            }
            else if (type == "audio" && !foundAudio)
            {
                foundAudio = true;
                info.AudioCodec = GetString(stream, "codec_name");
                info.AudioChannels = (int)GetLong(stream, "channels");
                info.AudioSampleRate = (int)GetLong(stream, "sample_rate");
            }
        }
        if (info.IsImage)
        {
            info.Duration = 0;
        }
    }

    private static async Task ProbeWithFfmpegAsync(string path, string ffmpeg,
        VideoMediaInfo info, CancellationToken ct)
    {
        var args = new List<string> { "-hide_banner", "-i", path };
        var result = await RunCaptureAsync(ffmpeg, args, ct);
        string text = result.StdErr;
        var durationMatch = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (durationMatch.Success)
        {
            info.Duration = int.Parse(durationMatch.Groups[1].Value) * 3600
                + int.Parse(durationMatch.Groups[2].Value) * 60
                + double.Parse(durationMatch.Groups[3].Value, CultureInfo.InvariantCulture);
        }
        var videoMatch = Regex.Match(text,
            @"Stream #\d+:\d+.*?: Video: ([^\s,]+).*?(\d{2,5})x(\d{2,5}).*?(\d+(?:\.\d+)?) fps");
        if (videoMatch.Success)
        {
            info.VideoCodec = videoMatch.Groups[1].Value;
            info.Width = int.Parse(videoMatch.Groups[2].Value);
            info.Height = int.Parse(videoMatch.Groups[3].Value);
            info.Fps = double.Parse(videoMatch.Groups[4].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var sizeMatch = Regex.Match(text, @"Video: ([^\s,]+).*?(\d{2,5})x(\d{2,5})");
            if (sizeMatch.Success)
            {
                info.VideoCodec = sizeMatch.Groups[1].Value;
                info.Width = int.Parse(sizeMatch.Groups[2].Value);
                info.Height = int.Parse(sizeMatch.Groups[3].Value);
            }
        }
        var audioMatch = Regex.Match(text,
            @"Stream #\d+:\d+.*?: Audio: ([^\s,]+).*?(\d+) Hz.*?(stereo|mono|(\d+) channels)");
        if (audioMatch.Success)
        {
            info.AudioCodec = audioMatch.Groups[1].Value;
            info.AudioSampleRate = int.Parse(audioMatch.Groups[2].Value);
            info.AudioChannels = audioMatch.Groups[3].Value == "stereo" ? 2
                : audioMatch.Groups[3].Value == "mono" ? 1
                : int.TryParse(audioMatch.Groups[4].Value, out int channels) ? channels : 2;
        }
        if (info.FormatName.Length == 0)
        {
            info.FormatName = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        }
        if (info.IsImage)
        {
            info.Duration = 0;
        }
    }

    private static int ReadRotation(JsonElement stream)
    {
        if (stream.TryGetProperty("tags", out var tags)
            && tags.TryGetProperty("rotate", out var rotate)
            && int.TryParse(rotate.ToString(), out int value))
        {
            return NormalizeRotation(value);
        }
        if (stream.TryGetProperty("side_data_list", out var sides)
            && sides.ValueKind == JsonValueKind.Array)
        {
            foreach (var side in sides.EnumerateArray())
            {
                if (side.TryGetProperty("rotation", out var rotation)
                    && int.TryParse(rotation.ToString(), out int sideValue))
                {
                    return NormalizeRotation(sideValue);
                }
            }
        }
        return 0;
    }

    private static int NormalizeRotation(int value)
    {
        value %= 360;
        if (value < 0)
        {
            value += 360;
        }
        return value;
    }

    private static string GetString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }
        return "";
    }

    private static double GetDouble(JsonElement element, string name)
    {
        string text = GetString(element, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value : 0;
    }

    private static long GetLong(JsonElement element, string name)
    {
        string text = GetString(element, name);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value : 0;
    }

    private static double ParseRational(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }
        int slash = text.IndexOf('/');
        if (slash > 0
            && double.TryParse(text[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator)
            && double.TryParse(text[(slash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator)
            && denominator > 0)
        {
            return numerator / denominator;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value : 0;
    }

    public static async Task<List<double>> GetKeyframesAsync(string path, string? configuredFfmpeg,
        CancellationToken ct)
    {
        var keyframes = new List<double>();
        string? ffprobe = Ffprobe(configuredFfmpeg);
        if (ffprobe == null || IsImageFile(path))
        {
            return keyframes;
        }
        var args = new List<string>
        {
            "-v", "error", "-select_streams", "v:0", "-show_packets",
            "-show_entries", "packet=pts_time,flags", "-of", "csv=p=0", path
        };
        var result = await RunCaptureAsync(ffprobe, args, ct);
        if (result.ExitCode != 0)
        {
            return keyframes;
        }
        foreach (string raw in result.StdOut.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            string[] parts = line.Split(',');
            if (parts.Length < 2)
            {
                continue;
            }
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
            {
                continue;
            }
            if (!parts[1].Contains('K'))
            {
                continue;
            }
            if (time >= 0)
            {
                keyframes.Add(time);
            }
        }
        keyframes.Sort();
        var unique = new List<double>();
        foreach (double time in keyframes)
        {
            if (unique.Count == 0 || Math.Abs(unique[^1] - time) > 0.0005)
            {
                unique.Add(time);
            }
        }
        return unique;
    }

    public static async Task<string?> ExtractThumbnailAsync(string path, double time, int width,
        string? configuredFfmpeg, CancellationToken ct)
    {
        string? ffmpeg = Ffmpeg(configuredFfmpeg);
        if (ffmpeg == null)
        {
            return null;
        }
        try
        {
            string folder = Path.Combine(AppPaths.Temp, "VideoCache");
            Directory.CreateDirectory(folder);
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(path + "|" + File.GetLastWriteTimeUtc(path).Ticks
                    + "|" + time.ToString("F3", CultureInfo.InvariantCulture) + "|" + width)))[..16];
            string output = Path.Combine(folder, key + ".jpg");
            if (File.Exists(output))
            {
                return output;
            }
            var args = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-ss", time.ToString("F3", CultureInfo.InvariantCulture),
                "-i", path, "-frames:v", "1",
                "-vf", $"scale={width}:{Math.Max(2, width * 9 / 16)}:force_original_aspect_ratio=decrease,"
                    + $"pad={width}:{Math.Max(2, width * 9 / 16)}:(ow-iw)/2:(oh-ih)/2:black",
                "-q:v", "4", output
            };
            var result = await RunCaptureAsync(ffmpeg, args, ct);
            return result.ExitCode == 0 && File.Exists(output) ? output : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            VideoLog.Warn("缩略图生成失败：" + path + " · " + ex.Message);
            return null;
        }
    }

    public static async Task<float[]> GetWaveformAsync(string path, double duration, int buckets,
        string? configuredFfmpeg, CancellationToken ct)
    {
        string? ffmpeg = Ffmpeg(configuredFfmpeg);
        if (ffmpeg == null || duration <= 0 || buckets <= 0)
        {
            return Array.Empty<float>();
        }
        buckets = Math.Clamp(buckets, 100, 8000);
        var peaks = new float[buckets];
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[]
        {
            "-hide_banner", "-loglevel", "error", "-i", path, "-vn",
            "-ac", "1", "-ar", "1000", "-f", "s16le", "-"
        })
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            using var registration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // ignore
                }
            });
            _ = process.StandardError.ReadToEndAsync(ct);
            long totalSamples = Math.Max(1, (long)(duration * 1000));
            long samplesPerBucket = Math.Max(1, totalSamples / buckets);
            var buffer = new byte[64 * 1024];
            long sampleIndex = 0;
            while (true)
            {
                int read = await process.StandardOutput.BaseStream.ReadAsync(buffer, ct);
                if (read <= 0)
                {
                    break;
                }
                for (int i = 0; i + 1 < read; i += 2)
                {
                    short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                    int bucket = (int)Math.Min(buckets - 1, sampleIndex / samplesPerBucket);
                    float value = Math.Abs((float)sample) / 32768f;
                    if (value > peaks[bucket])
                    {
                        peaks[bucket] = value;
                    }
                    sampleIndex++;
                }
            }
            await process.WaitForExitAsync(CancellationToken.None);
            return peaks;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            VideoLog.Warn("波形生成失败：" + path + " · " + ex.Message);
            return Array.Empty<float>();
        }
    }

    public static async Task<HardwareEncoders> DetectEncodersAsync(string? configuredFfmpeg,
        CancellationToken ct)
    {
        var encoders = new HardwareEncoders();
        string? ffmpeg = Ffmpeg(configuredFfmpeg);
        if (ffmpeg == null)
        {
            return encoders;
        }
        try
        {
            var args = new List<string> { "-hide_banner", "-encoders" };
            var result = await RunCaptureAsync(ffmpeg, args, ct);
            string text = result.StdOut + "\n" + result.StdErr;
            encoders.H264Nvenc = text.Contains("h264_nvenc", StringComparison.Ordinal);
            encoders.HevcNvenc = text.Contains("hevc_nvenc", StringComparison.Ordinal);
            encoders.Av1Nvenc = text.Contains("av1_nvenc", StringComparison.Ordinal);
            encoders.H264Qsv = text.Contains("h264_qsv", StringComparison.Ordinal);
            encoders.HevcQsv = text.Contains("hevc_qsv", StringComparison.Ordinal);
            encoders.H264Amf = text.Contains("h264_amf", StringComparison.Ordinal);
            encoders.HevcAmf = text.Contains("hevc_amf", StringComparison.Ordinal);
            encoders.SvtAv1 = text.Contains("libsvtav1", StringComparison.Ordinal);
            encoders.AomAv1 = text.Contains("libaom-av1", StringComparison.Ordinal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            VideoLog.Warn("编码器检测失败：" + ex.Message);
        }
        return encoders;
    }

    public static async Task<List<double>> DetectScenesAsync(string path, double threshold,
        string? configuredFfmpeg, CancellationToken ct)
    {
        var scenes = new List<double>();
        string? ffmpeg = Ffmpeg(configuredFfmpeg);
        if (ffmpeg == null)
        {
            return scenes;
        }
        threshold = Math.Clamp(threshold, 0.05, 0.95);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "info", "-i", path,
            "-filter:v", $"select='gt(scene,{threshold.ToString("F2", CultureInfo.InvariantCulture)})',showinfo",
            "-an", "-f", "null", "-"
        };
        var result = await RunCaptureAsync(ffmpeg, args, ct);
        foreach (Match match in SceneTimeRegex.Matches(result.StdErr))
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double time))
            {
                scenes.Add(time);
            }
        }
        return scenes.Distinct().OrderBy(value => value).ToList();
    }

    public static async Task<List<(double Start, double End)>> DetectBlackAsync(string path,
        string? configuredFfmpeg, CancellationToken ct)
    {
        var ranges = new List<(double Start, double End)>();
        string? ffmpeg = Ffmpeg(configuredFfmpeg);
        if (ffmpeg == null)
        {
            return ranges;
        }
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "info", "-i", path,
            "-vf", "blackdetect=d=0.10:pix_th=0.10", "-an", "-f", "null", "-"
        };
        var result = await RunCaptureAsync(ffmpeg, args, ct);
        var starts = BlackStartRegex.Matches(result.StdErr)
            .Select(match => double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double time) ? time : 0).ToList();
        var ends = BlackEndRegex.Matches(result.StdErr)
            .Select(match => double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double time) ? time : 0).ToList();
        for (int i = 0; i < Math.Min(starts.Count, ends.Count); i++)
        {
            if (ends[i] > starts[i])
            {
                ranges.Add((starts[i], ends[i]));
            }
        }
        return ranges;
    }

    public static async Task<ProcessResult> RunCaptureAsync(string file, IEnumerable<string> args,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            using var registration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // ignore
                }
            });
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdout, stderr);
            await process.WaitForExitAsync(ct);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public static async Task<FfmpegResult> RunFfmpegAsync(string? configuredFfmpeg,
        IEnumerable<string> arguments, double durationSeconds,
        Action<double, string>? progress, CancellationToken ct)
    {
        string? ffmpeg = Ffmpeg(configuredFfmpeg);
        if (ffmpeg == null)
        {
            throw new InvalidOperationException("未找到 ffmpeg.exe。");
        }
        var args = new List<string>();
        if (!arguments.Contains("-progress"))
        {
            args.Add("-progress");
            args.Add("pipe:1");
            args.Add("-nostats");
        }
        args.AddRange(arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }
        var errorLines = new Queue<string>();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            using var registration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // ignore
                }
            });
            var stderrTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(ct) is { } line)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    lock (errorLines)
                    {
                        errorLines.Enqueue(line);
                        while (errorLines.Count > 120)
                        {
                            errorLines.Dequeue();
                        }
                    }
                }
            }, ct);
            double lastReported = -1;
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                    || line.StartsWith("out_time_ms=", StringComparison.Ordinal))
                {
                    int equal = line.IndexOf('=');
                    if (equal > 0
                        && double.TryParse(line[(equal + 1)..], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double microseconds))
                    {
                        double seconds = microseconds / 1_000_000.0;
                        double fraction = durationSeconds > 0 ? Math.Clamp(seconds / durationSeconds, 0, 1) : 0;
                        if (fraction - lastReported >= 0.005 || fraction >= 1)
                        {
                            lastReported = fraction;
                            progress?.Invoke(fraction, "处理中 " + (fraction * 100)
                                .ToString("0", CultureInfo.InvariantCulture) + "%");
                        }
                    }
                }
                else if (line.StartsWith("speed=", StringComparison.Ordinal))
                {
                    // 速度信息由 progress 回调统一呈现，避免高频刷新
                }
            }
            await stderrTask;
            await process.WaitForExitAsync(ct);
            string error = "";
            lock (errorLines)
            {
                error = string.Join(Environment.NewLine, errorLines);
            }
            if (process.ExitCode != 0)
            {
                VideoLog.Error("ffmpeg 退出码 " + process.ExitCode + Environment.NewLine
                    + string.Join(" ", args) + Environment.NewLine + error);
            }
            return new FfmpegResult(process.ExitCode, error);
        }
        catch (OperationCanceledException)
        {
            throw;
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
}
