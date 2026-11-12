using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniMonitor.Navicat;

internal static class NvPaths
{
    public static string Root
    {
        get
        {
            string dir = AppPaths.Sub("navicat");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConnectionsFile => Path.Combine(Root, "connections.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string TasksFile => Path.Combine(Root, "tasks.json");
    public static string BackupsDir
    {
        get
        {
            string dir = Path.Combine(Root, "backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
    public static string LogsDir
    {
        get
        {
            string dir = Path.Combine(Root, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
    public static string ExportsDir
    {
        get
        {
            string dir = AppPaths.Sub("NavicatExports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

internal static class NvJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

internal static class NvSecret
{
    private const string Prefix = "dpapi:";
    private const string FallbackPrefix = "b64:";

    public static string Protect(string plain)
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
            return FallbackPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        }
    }

    public static string Unprotect(string value)
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

internal sealed class ConnectionStore
{
    public List<ConnectionProfile> Connections { get; set; } = new();
    public List<string> Groups { get; set; } = new();

    public static ConnectionStore Load()
    {
        try
        {
            if (File.Exists(NvPaths.ConnectionsFile))
            {
                string json = File.ReadAllText(NvPaths.ConnectionsFile, Encoding.UTF8);
                var store = JsonSerializer.Deserialize<ConnectionStore>(json, NvJson.Options);
                if (store != null)
                {
                    store.Connections ??= new List<ConnectionProfile>();
                    store.Groups ??= new List<string>();
                    foreach (var profile in store.Connections)
                    {
                        profile.Password = NvSecret.Unprotect(profile.Password);
                        profile.SshPassword = NvSecret.Unprotect(profile.SshPassword);
                        profile.SshPassphrase = NvSecret.Unprotect(profile.SshPassphrase);
                        profile.Normalize();
                    }
                    return store;
                }
            }
        }
        catch
        {
            // 配置损坏时使用空列表，原文件保留
        }
        return new ConnectionStore();
    }

    public void Save()
    {
        var knownGroups = Groups.Concat(Connections.Select(c => c.Group))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        Groups = knownGroups;
        var copy = new ConnectionStore
        {
            Groups = knownGroups,
            Connections = Connections.Select(CloneForSave).ToList()
        };
        string json = JsonSerializer.Serialize(copy, NvJson.Options);
        File.WriteAllText(NvPaths.ConnectionsFile, json, new UTF8Encoding(false));
    }

    private static ConnectionProfile CloneForSave(ConnectionProfile profile)
    {
        var copy = profile.CopyForConnect();
        copy.Password = profile.SavePassword ? NvSecret.Protect(profile.Password) : "";
        copy.SshPassword = profile.SshSavePassword ? NvSecret.Protect(profile.SshPassword) : "";
        copy.SshPassphrase = profile.SshSavePassword ? NvSecret.Protect(profile.SshPassphrase) : "";
        return copy;
    }

    public void Export(string path, bool includePasswords)
    {
        var copy = new ConnectionStore
        {
            Groups = Groups.ToList(),
            Connections = Connections.Select(p =>
            {
                var item = p.CopyForConnect();
                if (!includePasswords)
                {
                    item.Password = "";
                    item.SshPassword = "";
                    item.SshPassphrase = "";
                }
                return item;
            }).ToList()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(copy, NvJson.Options), new UTF8Encoding(false));
    }

    public int Import(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        var store = JsonSerializer.Deserialize<ConnectionStore>(json, NvJson.Options);
        if (store == null)
        {
            return 0;
        }
        store.Connections ??= new List<ConnectionProfile>();
        store.Groups ??= new List<string>();
        int count = 0;
        foreach (var profile in store.Connections)
        {
            profile.Id = Guid.NewGuid().ToString("N");
            profile.Password = NvSecret.Unprotect(profile.Password);
            profile.SshPassword = NvSecret.Unprotect(profile.SshPassword);
            profile.SshPassphrase = NvSecret.Unprotect(profile.SshPassphrase);
            profile.Normalize();
            Connections.Add(profile);
            if (!string.IsNullOrWhiteSpace(profile.Group) && !Groups.Contains(profile.Group))
            {
                Groups.Add(profile.Group);
            }
            count++;
        }
        foreach (string group in store.Groups)
        {
            if (!string.IsNullOrWhiteSpace(group) && !Groups.Contains(group))
            {
                Groups.Add(group);
            }
        }
        Save();
        return count;
    }

    public ConnectionProfile? Find(string id) => Connections.FirstOrDefault(c => c.Id == id);
}

internal static class NvSettingsStore
{
    private static NavicatSettings? _cached;

    public static NavicatSettings Current
    {
        get
        {
            if (_cached != null)
            {
                return _cached;
            }
            try
            {
                if (File.Exists(NvPaths.SettingsFile))
                {
                    _cached = JsonSerializer.Deserialize<NavicatSettings>(
                        File.ReadAllText(NvPaths.SettingsFile, Encoding.UTF8), NvJson.Options);
                }
            }
            catch
            {
                // ignore
            }
            _cached ??= new NavicatSettings();
            if (string.IsNullOrWhiteSpace(_cached.ExportFolder))
            {
                _cached.ExportFolder = NvPaths.ExportsDir;
            }
            if (string.IsNullOrWhiteSpace(_cached.BackupFolder))
            {
                _cached.BackupFolder = NvPaths.BackupsDir;
            }
            return _cached;
        }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(NvPaths.SettingsFile,
                JsonSerializer.Serialize(Current, NvJson.Options), new UTF8Encoding(false));
        }
        catch
        {
            // ignore
        }
    }
}

internal static class NvTaskStore
{
    public static List<NvScheduledTask> Load()
    {
        try
        {
            if (File.Exists(NvPaths.TasksFile))
            {
                var list = JsonSerializer.Deserialize<List<NvScheduledTask>>(
                    File.ReadAllText(NvPaths.TasksFile, Encoding.UTF8), NvJson.Options);
                return list?.Where(t => t != null).ToList() ?? new List<NvScheduledTask>();
            }
        }
        catch
        {
            // ignore
        }
        return new List<NvScheduledTask>();
    }

    public static void Save(IEnumerable<NvScheduledTask> tasks)
    {
        try
        {
            File.WriteAllText(NvPaths.TasksFile,
                JsonSerializer.Serialize(tasks.ToList(), NvJson.Options), new UTF8Encoding(false));
        }
        catch
        {
            // ignore
        }
    }
}
