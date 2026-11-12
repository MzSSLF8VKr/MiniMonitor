using System.IO;

namespace MiniMonitor;

/// <summary>
/// 全局鼠标手势日志已停用：钩子热路径不再记录、不再落盘，避免任何 I/O 干扰鼠标移动。
/// 保留此类仅为兼容既有界面代码与调用点。
/// </summary>
public sealed class GestureLogStore
{
    public const int MaxEntries = 0;

    private readonly string _path;

    public GestureLogStore()
    {
        _path = Path.Combine(AppConfig.ConfigDir, "gesture.log");
        DeleteLogFile();
    }

    public string LogPath => _path;

    public int Count => 0;

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message)
    {
    }

    public void Debug(string message)
    {
    }

    public void Add(string level, string message)
    {
    }

    public string SnapshotText() => "";

    public void Flush()
    {
    }

    /// <summary>清理历史版本留下的 gesture.log，避免继续占用空间。</summary>
    private void DeleteLogFile()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // 文件被占用时忽略
        }
    }
}
