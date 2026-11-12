using System.Text.Json.Serialization;

namespace MiniMonitor.XFtp;

internal enum XFtpProtocol
{
    Ftp,
    Ftps,
    Sftp
}

internal static class XFtpProtocolText
{
    public static string ToText(this XFtpProtocol protocol) => protocol switch
    {
        XFtpProtocol.Ftps => "FTPS",
        XFtpProtocol.Sftp => "SFTP",
        _ => "FTP"
    };

    public static int DefaultPort(this XFtpProtocol protocol) => protocol switch
    {
        XFtpProtocol.Sftp => 22,
        XFtpProtocol.Ftps => 21,
        _ => 21
    };

    public static bool IsSecure(this XFtpProtocol protocol) =>
        protocol != XFtpProtocol.Ftp;
}

/// <summary>站点配置：保存主机、认证信息与默认目录。</summary>
internal sealed class XFtpSite
{
    public string Name { get; set; } = "";
    public XFtpProtocol Protocol { get; set; } = XFtpProtocol.Ftp;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 21;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string PasswordCipher { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    [JsonIgnore]
    public string PrivateKeyPassphrase { get; set; } = "";
    public string RemotePath { get; set; } = "";
    // 跳板机（从 XShell 会话自动带入，不落盘）。
    [JsonIgnore]
    public string JumpHost { get; set; } = "";
    [JsonIgnore]
    public int JumpPort { get; set; } = 22;
    [JsonIgnore]
    public string JumpUser { get; set; } = "";
    [JsonIgnore]
    public string JumpPassword { get; set; } = "";
    [JsonIgnore]
    public string JumpPrivateKeyPath { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public DateTime LastUsedAt { get; set; }

    public string DisplayAddress =>
        Host + (Port > 0 && Port != Protocol.DefaultPort() ? ":" + Port : "")
        + "  ·  " + Protocol.ToText();

    public XFtpSite Clone() => new()
    {
        Name = Name,
        Protocol = Protocol,
        Host = Host,
        Port = Port,
        User = User,
        Password = Password,
        PasswordCipher = PasswordCipher,
        PrivateKeyPath = PrivateKeyPath,
        PrivateKeyPassphrase = PrivateKeyPassphrase,
        RemotePath = RemotePath,
        JumpHost = JumpHost,
        JumpPort = JumpPort,
        JumpUser = JumpUser,
        JumpPassword = JumpPassword,
        JumpPrivateKeyPath = JumpPrivateKeyPath,
        LocalPath = LocalPath,
        LastUsedAt = LastUsedAt
    };

    public void Encrypt()
    {
        PasswordCipher = XShell.XShellSecret.Protect(Password);
        Password = "";
    }

    public void Decrypt()
    {
        Password = XShell.XShellSecret.Unprotect(PasswordCipher);
    }
}

/// <summary>XFtp 设置：窗口状态与显示开关。</summary>
internal sealed class XFtpSettings
{
    public bool EnableQuickConnect { get; set; }
    public bool ShowQuickConnect { get; set; }
    public bool ShowToolbar { get; set; } = true;
    public bool ShowStatusBar { get; set; }
    public bool ShowInTaskbar { get; set; }
    public bool ShowTransferQueue { get; set; }
    public string LastLocalFolder { get; set; } = "";
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 780;
    public bool Maximized { get; set; }
}

/// <summary>远程文件 / 目录条目。</summary>
internal sealed class XFtpEntry
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Permissions { get; init; } = "";
    public string Owner { get; init; } = "";

    public string SizeText => IsDirectory ? "<DIR>" : FormatSize(Size);

    public string TypeText
    {
        get
        {
            if (IsDirectory)
            {
                return "文件夹";
            }
            string ext = Path.GetExtension(Name);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return "文件";
            }
            string type = ext.TrimStart('.').ToUpperInvariant();
            return type + " 文件";
        }
    }

    public static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, size);
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return index == 0 ? value.ToString("0") + " " + units[index]
            : value.ToString("0.##") + " " + units[index];
    }
}

internal enum XFtpTaskState
{
    Waiting,
    Running,
    Success,
    Failed,
    Canceled
}

/// <summary>一个上传 / 下载任务。</summary>
internal sealed class XFtpTransferTask
{
    public string Name { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public bool IsUpload { get; set; }
    public bool IsDirectoryCreate { get; set; }
    public long Total { get; set; }
    public long Done { get; set; }
    public XFtpTaskState State { get; set; } = XFtpTaskState.Waiting;
    public string Error { get; set; } = "";
    public CancellationTokenSource Cancellation { get; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime LastUpdate { get; set; }
    public long LastDone { get; set; }
    public double Speed { get; set; }

    public bool IsFinished =>
        State is XFtpTaskState.Success or XFtpTaskState.Failed or XFtpTaskState.Canceled;
}
