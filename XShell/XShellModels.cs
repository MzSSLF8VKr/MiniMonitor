using System.IO.Ports;
using System.Text.Json.Serialization;

namespace MiniMonitor.XShell;

public enum XProtocol
{
    Ssh,
    Telnet,
    Serial,
    Local
}

public enum XAuthMethod
{
    Password,
    PrivateKey,
    KeyboardInteractive,
    None
}

public enum XTunnelType
{
    Local,
    Remote,
    Dynamic
}

/// <summary>XShell 会话配置（Tabby 的 profile + XShell 的会话属性）。</summary>
public sealed class XSessionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新建会话";
    public string Group { get; set; } = "默认";
    public XProtocol Protocol { get; set; } = XProtocol.Ssh;

    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string UserName { get; set; } = "";
    public XAuthMethod AuthMethod { get; set; } = XAuthMethod.Password;

    /// <summary>DPAPI 加密后的密码，不直接落盘明文。</summary>
    public string PasswordCipher { get; set; } = "";
    [JsonIgnore]
    public string Password { get; set; } = "";

    public string PrivateKeyPath { get; set; } = "";
    public string PrivateKeyPassphraseCipher { get; set; } = "";
    [JsonIgnore]
    public string PrivateKeyPassphrase { get; set; } = "";

    public string TerminalType { get; set; } = "xterm-256color";
    public string EncodingName { get; set; } = "UTF-8";
    public int Columns { get; set; } = 120;
    public int Rows { get; set; } = 32;
    public string ColorScheme { get; set; } = "XShell 经典";
    public int FontSize { get; set; } = 16;
    public int Scrollback { get; set; } = 10000;
    public bool LogSession { get; set; }
    public string LogFolder { get; set; } = "";

    public bool SftpEnabled { get; set; } = true;
    public string RemoteStartFolder { get; set; } = "";
    /// <summary>打开 SFTP 时默认使用的本地目录；为空时使用上次目录。</summary>
    public string LocalStartFolder { get; set; } = "";

    // Telnet / 通用
    public string TelnetEncoding { get; set; } = "UTF-8";

    // 串口
    public string SerialPortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Handshake Handshake { get; set; } = Handshake.None;
    public bool DtrEnable { get; set; }
    public bool RtsEnable { get; set; }

    // 本地 Shell
    public string LocalCommand { get; set; } = "powershell.exe";
    public string LocalArguments { get; set; } = "-NoLogo";

    // SSH 跳板机
    public bool UseJumpHost { get; set; }
    public string JumpHost { get; set; } = "";
    public int JumpPort { get; set; } = 22;
    public string JumpUserName { get; set; } = "";
    public string JumpPasswordCipher { get; set; } = "";
    [JsonIgnore]
    public string JumpPassword { get; set; } = "";
    public string JumpPrivateKeyPath { get; set; } = "";

    // SSH 隧道
    public List<XTunnelRule> Tunnels { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; }
    public int ConnectCount { get; set; }

    [JsonIgnore]
    public string DisplayAddress => Protocol switch
    {
        XProtocol.Serial => SerialPortName + " · " + BaudRate,
        XProtocol.Local => LocalCommand,
        _ => (string.IsNullOrWhiteSpace(UserName) ? "" : UserName + "@") + Host
            + (Port > 0 ? ":" + Port : "")
    };

    [JsonIgnore]
    public string ProtocolText => Protocol switch
    {
        XProtocol.Ssh => "SSH",
        XProtocol.Telnet => "Telnet",
        XProtocol.Serial => "Serial",
        XProtocol.Local => "Local Shell",
        _ => Protocol.ToString()
    };

    public XSessionProfile Clone()
    {
        var copy = (XSessionProfile)MemberwiseClone();
        copy.Tunnels = Tunnels.Select(t => t.Clone()).ToList();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name += " - 副本";
        return copy;
    }

    public XSessionProfile Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "新建会话" : Name.Trim();
        Group = string.IsNullOrWhiteSpace(Group) ? "默认" : Group.Trim();
        Host = (Host ?? "").Trim();
        UserName = (UserName ?? "").Trim();
        if (Port < 1 || Port > 65535)
        {
            Port = Protocol == XProtocol.Telnet ? 23 : 22;
        }
        Columns = Math.Clamp(Columns, 20, 500);
        Rows = Math.Clamp(Rows, 5, 300);
        FontSize = Math.Clamp(FontSize, 8, 32);
        Scrollback = Math.Clamp(Scrollback, 500, 200000);
        Tunnels ??= new();
        return this;
    }
}

public sealed class XTunnelRule
{
    public string Name { get; set; } = "本地转发";
    public XTunnelType Type { get; set; } = XTunnelType.Local;
    public string BindHost { get; set; } = "127.0.0.1";
    public int BindPort { get; set; }
    public string TargetHost { get; set; } = "127.0.0.1";
    public int TargetPort { get; set; }
    public bool Enabled { get; set; } = true;

    public XTunnelRule Clone() => (XTunnelRule)MemberwiseClone();
}

public sealed class XShellSettings
{
    public string Theme { get; set; } = "Light";
    /// <summary>界面主题：Dark（默认）/ Light / System（跟随 Windows）。</summary>
    public string UiTheme { get; set; } = "Dark";
    public string TerminalTheme { get; set; } = "XShell 经典";
    public int FontSize { get; set; } = 16;
    /// <summary>终端字体，默认 Consolas。</summary>
    public string FontFamily { get; set; } = "Consolas";
    /// <summary>是否已完成旧版本默认字号 11 → 16 的一次性迁移。</summary>
    public bool FontSizeMigrated16 { get; set; }
    /// <summary>是否已完成旧版本默认字体 Cascadia Mono → Consolas 的一次性迁移。</summary>
    public bool FontFamilyMigrated { get; set; }
    public int Scrollback { get; set; } = 10000;
    public bool ConfirmCloseSession { get; set; } = true;
    public bool ShowWelcomeOnStart { get; set; } = true;
    /// <summary>快速会话栏（原快速连接栏位置）默认显示。</summary>
    public bool ShowQuickLaunch { get; set; } = true;
    /// <summary>会话管理器默认不显示，点击（功能区按钮 / 左边缘标签）后才显示。</summary>
    public bool ShowSessionManager { get; set; }
    /// <summary>预置快速启动分组的写入版本；用于补齐旧版本缺失的预置项。</summary>
    public int QuickLaunchSeedVersion { get; set; }
    public bool ShowStatusBar { get; set; }
    public bool AutoReconnect { get; set; }
    public int AutoReconnectSeconds { get; set; } = 5;
    public bool SftpDoubleClickDownloads { get; set; } = true;
    public string LastLocalFolder { get; set; } = "";
    public List<string> RecentHosts { get; set; } = new();
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; } = true;

    public XShellSettings Normalize()
    {
        UiTheme = UiTheme switch
        {
            "Light" or "浅色" => "Light",
            "System" or "跟随系统" => "System",
            _ => "Dark"
        };
        FontSize = Math.Clamp(FontSize, 8, 32);
        Scrollback = Math.Clamp(Scrollback, 500, 200000);
        AutoReconnectSeconds = Math.Clamp(AutoReconnectSeconds, 1, 120);
        RecentHosts ??= new();
        return this;
    }
}

/// <summary>终端配色方案；Name 与 C# 设置界面、JS 主题保持一致。</summary>
public sealed class XTerminalPalette
{
    public string Name { get; init; } = "";
    public string Background { get; init; } = "#0c0c0c";
    public string Foreground { get; init; } = "#cccccc";
    public string Cursor { get; init; } = "#ffffff";
    public string Selection { get; init; } = "#264f78";
    public string Black { get; init; } = "#0c0c0c";
    public string Red { get; init; } = "#c50f1f";
    public string Green { get; init; } = "#13a10e";
    public string Yellow { get; init; } = "#c19c00";
    public string Blue { get; init; } = "#0037da";
    public string Magenta { get; init; } = "#881798";
    public string Cyan { get; init; } = "#3a96dd";
    public string White { get; init; } = "#cccccc";
    public string BrightBlack { get; init; } = "#767676";
    public string BrightRed { get; init; } = "#e74856";
    public string BrightGreen { get; init; } = "#16c60c";
    public string BrightYellow { get; init; } = "#f9f1a5";
    public string BrightBlue { get; init; } = "#3b78ff";
    public string BrightMagenta { get; init; } = "#b4009e";
    public string BrightCyan { get; init; } = "#61d6d6";
    public string BrightWhite { get; init; } = "#f2f2f2";

    public object ToWebTheme() => new
    {
        background = Background,
        foreground = Foreground,
        cursor = Cursor,
        cursorAccent = Background,
        selectionBackground = Selection,
        selectionForeground = "#ffffff",
        black = Black,
        red = Red,
        green = Green,
        yellow = Yellow,
        blue = Blue,
        magenta = Magenta,
        cyan = Cyan,
        white = White,
        brightBlack = BrightBlack,
        brightRed = BrightRed,
        brightGreen = BrightGreen,
        brightYellow = BrightYellow,
        brightBlue = BrightBlue,
        brightMagenta = BrightMagenta,
        brightCyan = BrightCyan,
        brightWhite = BrightWhite
    };
}
