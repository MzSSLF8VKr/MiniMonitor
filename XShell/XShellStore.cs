using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniMonitor.XShell;

public static class XShellPaths
{
    public static string Root
    {
        get
        {
            string dir = AppPaths.Sub("xshell");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SessionsFile => Path.Combine(Root, "sessions.json");
    public static string GroupsFile => Path.Combine(Root, "groups.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string KnownHostsFile => Path.Combine(Root, "known_hosts.json");
    public static string RecentFile => Path.Combine(Root, "recent.json");

    public static string LogsDir
    {
        get
        {
            string dir = Path.Combine(Root, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string WebRoot
    {
        get
        {
            string dir = Path.Combine(Root, "web");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

public static class XShellJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

public static class XShellSecret
{
    private const string Prefix = "dpapi:";

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plain);
            byte[] encrypted = Dpapi.Protect(bytes);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        catch
        {
            // DPAPI 不可用时不做 Base64 明文回退（那等于把密码明文落盘），
            // 改为不保存该密码，下次连接时需要重新输入。
            return "";
        }
    }

    public static string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        try
        {
            if (value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                byte[] bytes = Convert.FromBase64String(value[Prefix.Length..]);
                byte[] plain = Dpapi.Unprotect(bytes);
                return Encoding.UTF8.GetString(plain);
            }
            // 兼容旧版本遗留的 b64:（Base64 明文）条目：仅解密到内存，不再新写这种格式。
            const string FallbackPrefix = "b64:";
            if (value.StartsWith(FallbackPrefix, StringComparison.Ordinal))
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value[FallbackPrefix.Length..]));
            }
            return value;
        }
        catch
        {
            return "";
        }
    }
}

public sealed class XShellStore
{
    public List<XSessionProfile> Sessions { get; private set; } = new();
    public List<string> Groups { get; private set; } = new();
    public XShellSettings Settings { get; private set; } = new();
    public List<string> KnownHosts { get; private set; } = new();

    public static XShellStore Load()
    {
        var store = new XShellStore();
        store.Sessions = ReadJson<List<XSessionProfile>>(XShellPaths.SessionsFile) ?? new();
        store.Groups = ReadJson<List<string>>(XShellPaths.GroupsFile) ?? new();
        store.Settings = (ReadJson<XShellSettings>(XShellPaths.SettingsFile) ?? new()).Normalize();
        store.KnownHosts = ReadJson<List<string>>(XShellPaths.KnownHostsFile) ?? new();
        foreach (var profile in store.Sessions)
        {
            profile.Normalize();
            profile.Password = XShellSecret.Unprotect(profile.PasswordCipher);
            profile.PrivateKeyPassphrase = XShellSecret.Unprotect(profile.PrivateKeyPassphraseCipher);
            profile.JumpPassword = XShellSecret.Unprotect(profile.JumpPasswordCipher);
        }

        // 旧版本默认字号为 11；升级后统一改为 16，同时保留用户已自定义的更大字号。
        bool migratedFont = false;
        if (!store.Settings.FontSizeMigrated16)
        {
            store.Settings.FontSizeMigrated16 = true;
            migratedFont = true;
            if (store.Settings.FontSize <= 11)
            {
                store.Settings.FontSize = 16;
            }
            foreach (var profile in store.Sessions)
            {
                if (profile.FontSize <= 11)
                {
                    profile.FontSize = 16;
                }
            }
        }

        // 默认终端字体统一为 Consolas：旧版本从未自定义字体（仍是 Cascadia Mono）时迁移一次。
        bool migratedFamily = false;
        if (!store.Settings.FontFamilyMigrated)
        {
            store.Settings.FontFamilyMigrated = true;
            migratedFamily = true;
            if (string.Equals(store.Settings.FontFamily, "Cascadia Mono", StringComparison.OrdinalIgnoreCase))
            {
                store.Settings.FontFamily = "Consolas";
            }
        }

        // 旧版本会自动创建“本地 PowerShell”示例会话及其“本地”分组；
        // 现在默认不再提供本地会话，启动时清理这两个自动生成的项目。
        bool removedLegacy = store.Sessions.RemoveAll(IsLegacyLocalPowerShell) > 0;

        bool seeded = store.SeedQuickLaunch();
        foreach (var profile in store.Sessions)
        {
            if (!string.IsNullOrWhiteSpace(profile.Group)
                && !store.Groups.Contains(profile.Group, StringComparer.OrdinalIgnoreCase))
            {
                store.Groups.Add(profile.Group);
            }
        }
        store.Groups = store.Groups
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        int removedGroups = store.Groups.RemoveAll(g =>
            string.Equals(g, "本地", StringComparison.OrdinalIgnoreCase)
            && !store.Sessions.Any(s => string.Equals(s.Group, "本地",
                StringComparison.OrdinalIgnoreCase)));
        if (!store.Groups.Contains("默认", StringComparer.OrdinalIgnoreCase))
        {
            store.Groups.Add("默认");
        }
        if (removedLegacy || removedGroups > 0 || seeded || migratedFont || migratedFamily)
        {
            // 把清理 / 预置结果写回磁盘，避免每次启动重复处理。
            store.Save();
        }
        return store;
    }

    /// <summary>
    /// 首次启动时写入“预置快速启动”分组（位于“默认”上方）。
    /// 使用 Settings.QuickLaunchSeeded 记忆，用户删除后不会再次自动添加。
    /// </summary>
    private bool SeedQuickLaunch()
    {
        // 版本 2：一次性补齐 14 个预置会话（包括 192.168.33.90-master）。
        // 只按“分组 + 名称”判断是否已存在，避免同主机会话存在于其他分组时被误跳过。
        if (Settings.QuickLaunchSeedVersion >= 2)
        {
            return false;
        }
        Settings.QuickLaunchSeedVersion = 2;
        const string group = "预置快速启动";
        if (!Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Insert(0, group);
        }
        string local = DefaultDownloadsFolder();
        var presets = new (string Name, string Host)[]
        {
            ("192.168.33.90-master", "192.168.33.90"),
            ("192.168.33.91-node1", "192.168.33.91"),
            ("192.168.33.92-node2", "192.168.33.92"),
            ("192.168.33.93-node3", "192.168.33.93"),
            ("192.168.33.94-node4", "192.168.33.94"),
            ("192.168.33.95-node5", "192.168.33.95"),
            ("192.168.33.96-Ubuntu", "192.168.33.96"),
            ("192.168.33.97-Debain", "192.168.33.97"),
            ("192.168.33.98-Alamlinux", "192.168.33.98"),
            ("192.168.33.99-Rocky", "192.168.33.99"),
            ("192.168.66.90", "192.168.66.90"),
            ("192.168.66.91", "192.168.66.91"),
            ("192.168.66.92", "192.168.66.92"),
            ("192.168.66.93", "192.168.66.93")
        };
        foreach (var (name, host) in presets)
        {
            if (Sessions.Any(profile =>
                string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(profile.Group, group, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            Sessions.Add(new XSessionProfile
            {
                Name = name,
                Group = group,
                Protocol = XProtocol.Ssh,
                Host = host,
                Port = 22,
                UserName = "root",
                AuthMethod = XAuthMethod.Password,
                Password = "1",
                SftpEnabled = true,
                RemoteStartFolder = "/root",
                LocalStartFolder = local
            });
        }
        return true;
    }

    private static string DefaultDownloadsFolder()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = Path.Combine(profile, "Downloads");
        string download = Path.Combine(profile, "Download");
        return Directory.Exists(downloads) ? downloads
            : Directory.Exists(download) ? download
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static bool IsLegacyLocalPowerShell(XSessionProfile profile)
    {
        return profile.Protocol == XProtocol.Local
            && string.Equals(profile.Name, "本地 PowerShell", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.Group, "本地", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(profile.LocalCommand ?? ""), "powershell.exe",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals((profile.LocalArguments ?? "").Trim(), "-NoLogo",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>所有分组（含没有会话的空分组），“预置快速启动”在最前，“默认”次之。</summary>
    public List<string> AllGroups()
    {
        var groups = new List<string>();
        groups.AddRange(Groups);
        groups.AddRange(Sessions.Select(s => s.Group));
        groups = groups
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => string.Equals(g, "预置快速启动", StringComparison.OrdinalIgnoreCase) ? 0
                : string.Equals(g, "默认", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(g => g, StringComparer.CurrentCulture)
            .ToList();
        return groups;
    }

    public void AddGroup(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
        {
            return;
        }
        if (!Groups.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(name);
        }
        Save();
    }

    public bool RenameGroup(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (oldName.Length == 0 || newName.Length == 0
            || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (var profile in Sessions)
        {
            if (string.Equals(profile.Group, oldName, StringComparison.OrdinalIgnoreCase))
            {
                profile.Group = newName;
            }
        }
        Groups.RemoveAll(g => string.Equals(g, oldName, StringComparison.OrdinalIgnoreCase));
        if (!Groups.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(newName);
        }
        Save();
        return true;
    }

    public void DeleteGroup(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || string.Equals(name, "默认", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        foreach (var profile in Sessions)
        {
            if (string.Equals(profile.Group, name, StringComparison.OrdinalIgnoreCase))
            {
                profile.Group = "默认";
            }
        }
        Groups.RemoveAll(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase));
        if (!Groups.Contains("默认", StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add("默认");
        }
        Save();
    }

    public void Save()
    {
        foreach (var profile in Sessions)
        {
            profile.PasswordCipher = XShellSecret.Protect(profile.Password);
            profile.PrivateKeyPassphraseCipher = XShellSecret.Protect(profile.PrivateKeyPassphrase);
            profile.JumpPasswordCipher = XShellSecret.Protect(profile.JumpPassword);
        }
        var knownGroups = Groups.Concat(Sessions.Select(s => s.Group))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!knownGroups.Contains("默认", StringComparer.OrdinalIgnoreCase))
        {
            knownGroups.Add("默认");
        }
        Groups = knownGroups;
        WriteJson(XShellPaths.SessionsFile, Sessions);
        WriteJson(XShellPaths.GroupsFile, Groups);
        WriteJson(XShellPaths.SettingsFile, Settings);
        WriteJson(XShellPaths.KnownHostsFile, KnownHosts);
    }

    public XSessionProfile? Find(string id) =>
        Sessions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Remove(XSessionProfile profile)
    {
        Sessions.Remove(profile);
        Save();
    }

    public void AddOrUpdate(XSessionProfile profile)
    {
        profile.Normalize();
        if (!string.IsNullOrWhiteSpace(profile.Group)
            && !Groups.Contains(profile.Group, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(profile.Group);
        }
        var existing = Find(profile.Id);
        if (existing == null)
        {
            Sessions.Add(profile);
        }
        else
        {
            int index = Sessions.IndexOf(existing);
            Sessions[index] = profile;
        }
        Save();
    }

    public void Touch(XSessionProfile profile)
    {
        profile.LastUsedAt = DateTime.Now;
        profile.ConnectCount++;
        Settings.RecentHosts.RemoveAll(x =>
            string.Equals(x, profile.DisplayAddress, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(profile.DisplayAddress))
        {
            Settings.RecentHosts.Insert(0, profile.DisplayAddress);
            if (Settings.RecentHosts.Count > 20)
            {
                Settings.RecentHosts.RemoveRange(20, Settings.RecentHosts.Count - 20);
            }
        }
        Save();
    }

    public bool IsKnownHost(string fingerprint) =>
        KnownHosts.Contains(fingerprint, StringComparer.OrdinalIgnoreCase);

    public void RememberHost(string fingerprint)
    {
        if (!IsKnownHost(fingerprint))
        {
            KnownHosts.Add(fingerprint);
            Save();
        }
    }

    public string ExportSessions()
    {
        string file = Path.Combine(XShellPaths.Root,
            "sessions-export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        WriteJson(file, Sessions);
        return file;
    }

    public int ImportSessions(string file)
    {
        var imported = ReadJson<List<XSessionProfile>>(file);
        if (imported == null)
        {
            return 0;
        }
        int count = 0;
        foreach (var profile in imported)
        {
            profile.Normalize();
            profile.Id = Guid.NewGuid().ToString("N");
            profile.Password = XShellSecret.Unprotect(profile.PasswordCipher);
            profile.PrivateKeyPassphrase = XShellSecret.Unprotect(profile.PrivateKeyPassphraseCipher);
            profile.JumpPassword = XShellSecret.Unprotect(profile.JumpPasswordCipher);
            Sessions.Add(profile);
            count++;
        }
        Save();
        return count;
    }

    private static T? ReadJson<T>(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(file, Encoding.UTF8), XShellJson.Options)
                : default;
        }
        catch
        {
            return default;
        }
    }

    private static void WriteJson<T>(string file, T value)
    {
        try
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, XShellJson.Options), Encoding.UTF8);
            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            // 配置写入失败不阻断会话。
        }
    }
}

public static class XShellPalettes
{
    public static readonly XTerminalPalette XShell = new()
    {
        Name = "XShell 经典"
    };

    public static readonly XTerminalPalette SolarizedDark = new()
    {
        Name = "Solarized Dark",
        Background = "#002b36", Foreground = "#839496", Cursor = "#93a1a1",
        Selection = "#073642", Black = "#073642", Red = "#dc322f", Green = "#859900",
        Yellow = "#b58900", Blue = "#268bd2", Magenta = "#d33682", Cyan = "#2aa198",
        White = "#eee8d5", BrightBlack = "#586e75", BrightRed = "#cb4b16",
        BrightGreen = "#586e75", BrightYellow = "#657b83", BrightBlue = "#839496",
        BrightMagenta = "#6c71c4", BrightCyan = "#93a1a1", BrightWhite = "#fdf6e3"
    };

    public static readonly XTerminalPalette Monokai = new()
    {
        Name = "Monokai",
        Background = "#272822", Foreground = "#f8f8f2", Cursor = "#f8f8f0",
        Selection = "#49483e", Black = "#272822", Red = "#f92672", Green = "#a6e22e",
        Yellow = "#f4bf75", Blue = "#66d9ef", Magenta = "#ae81ff", Cyan = "#a1efe4",
        White = "#f8f8f2", BrightBlack = "#75715e", BrightRed = "#f92672",
        BrightGreen = "#a6e22e", BrightYellow = "#f4bf75", BrightBlue = "#66d9ef",
        BrightMagenta = "#ae81ff", BrightCyan = "#a1efe4", BrightWhite = "#f9f8f5"
    };

    public static readonly XTerminalPalette Ubuntu = new()
    {
        Name = "Ubuntu",
        Background = "#300a24", Foreground = "#eeeeec", Cursor = "#ffffff",
        Selection = "#4d3a45", Black = "#2e3436", Red = "#cc0000", Green = "#4e9a06",
        Yellow = "#c4a000", Blue = "#3465a4", Magenta = "#75507b", Cyan = "#06989a",
        White = "#d3d7cf", BrightBlack = "#555753", BrightRed = "#ef2929",
        BrightGreen = "#8ae234", BrightYellow = "#fce94f", BrightBlue = "#729fcf",
        BrightMagenta = "#ad7fa8", BrightCyan = "#34e2e2", BrightWhite = "#eeeeec"
    };

    public static readonly XTerminalPalette Light = new()
    {
        Name = "浅色",
        Background = "#ffffff", Foreground = "#1f2d3d", Cursor = "#0e4b8b",
        Selection = "#cfe3f7", Black = "#1f2d3d", Red = "#c0392b", Green = "#1e8449",
        Yellow = "#9a6700", Blue = "#1f5fa8", Magenta = "#8e2f8b", Cyan = "#0e7c86",
        White = "#e9eef4", BrightBlack = "#6b7c93", BrightRed = "#e74c3c",
        BrightGreen = "#27ae60", BrightYellow = "#c19c00", BrightBlue = "#2b7cd3",
        BrightMagenta = "#b04ab0", BrightCyan = "#12a4af", BrightWhite = "#ffffff"
    };

    public static readonly XTerminalPalette[] All =
    {
        XShell, SolarizedDark, Monokai, Ubuntu, Light
    };

    public static XTerminalPalette Get(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? XShell;
}
