using System.Drawing;

namespace MiniMonitor;

public enum ScreenshotMode
{
    FullScreen,
    ActiveWindow,
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Freehand,
    Polygon,
    ScrollingWindow
}

public enum SelectionShape
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Freehand,
    Polygon
}

public enum RecordingFormat
{
    Mp4,
    Gif
}

public enum RecordingAudioSource
{
    None,
    System,
    Microphone
}

public enum RecordingRegionMode
{
    FullScreen,
    Region
}

public sealed record CaptureOutcome(Bitmap Image, Rectangle Bounds, ScreenshotMode Mode);

public static class ScreenshotSettingParser
{
    public static SelectionShape ParseShape(string? value)
    {
        return value switch
        {
            "RoundedRectangle" => SelectionShape.RoundedRectangle,
            "Ellipse" => SelectionShape.Ellipse,
            "Freehand" => SelectionShape.Freehand,
            "Polygon" => SelectionShape.Polygon,
            _ => SelectionShape.Rectangle
        };
    }

    public static RecordingFormat ParseFormat(string? value)
    {
        return value == "GIF" ? RecordingFormat.Gif : RecordingFormat.Mp4;
    }

    public static RecordingAudioSource ParseAudio(string? value)
    {
        return value switch
        {
            "System" => RecordingAudioSource.System,
            "Microphone" => RecordingAudioSource.Microphone,
            _ => RecordingAudioSource.None
        };
    }

    public static RecordingRegionMode ParseRegion(string? value)
    {
        return value == "Region" ? RecordingRegionMode.Region : RecordingRegionMode.FullScreen;
    }

    public static string FormatToString(RecordingFormat format)
    {
        return format == RecordingFormat.Gif ? "GIF" : "MP4";
    }

    public static string AudioToString(RecordingAudioSource source)
    {
        return source switch
        {
            RecordingAudioSource.System => "System",
            RecordingAudioSource.Microphone => "Microphone",
            _ => "None"
        };
    }

    public static string RegionToString(RecordingRegionMode mode)
    {
        return mode == RecordingRegionMode.Region ? "Region" : "FullScreen";
    }
}
