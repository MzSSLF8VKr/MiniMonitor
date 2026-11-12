using System.Text;
using System.Text.Json.Nodes;

namespace MiniMonitor.Web.WebTodo;

/// <summary>WebTodo 的配置与数据目录（与 Python 版一致：文档\web_todo，settings 存 config.json）。</summary>
public sealed class TodoConfig
{
    public const int ConfigVersion = 2;
    public const int DefaultPort = 50004;

    public string DataDir { get; }
    public string ConfigPath { get; }
    public string DbPath { get; }
    public string ExportsDir { get; }

    private readonly object _sync = new();
    private JsonObject _settings = new();

    public static readonly JsonObject DefaultSettings = JsonNode.Parse("""
{
  "theme": {"mode": "dark"},
  "defaults": {"priority": 0},
  "clock": {"useNtp": true},
  "server": {"host": "127.0.0.1", "port": 50004}
}
""")!.AsObject();

    public static readonly string[] NtpServers =
    {
        "edu.ntp.org.cn", "time.windows.com", "time.cloudflare.com"
    };

    public const int NtpSyncIntervalSeconds = 30 * 60;
    public const int NtpTimeoutSeconds = 5;

    public TodoConfig(string? dataDir = null)
    {
        DataDir = string.IsNullOrWhiteSpace(dataDir) ? ResolveDefaultDataDir() : Path.GetFullPath(dataDir);
        ConfigPath = Path.Combine(DataDir, "config.json");
        DbPath = Path.Combine(DataDir, "todo.db");
        ExportsDir = Path.Combine(DataDir, "exports");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ExportsDir);
        Load();
    }

    public static string ResolveDefaultDataDir()
    {
        string? env = Environment.GetEnvironmentVariable("TODO_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }
        // 统一存储：文档\MiniMonitor\WebTodo（老版本 web_todo 数据由 AppPaths 启动时自动迁移）。
        return AppPaths.Sub("WebTodo");
    }

    public JsonObject Settings
    {
        get
        {
            lock (_sync)
            {
                return _settings.DeepClone().AsObject();
            }
        }
    }

    private void Load()
    {
        JsonObject? raw = null;
        if (File.Exists(ConfigPath))
        {
            try
            {
                raw = JsonNode.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8)) as JsonObject;
            }
            catch
            {
                try { File.Move(ConfigPath, ConfigPath + ".bad", overwrite: true); } catch { }
            }
        }
        JsonNode? stored = raw?["settings"];
        _settings = Merge(DefaultSettings.DeepClone().AsObject(), stored);
        Save();
    }

    public void Save()
    {
        lock (_sync)
        {
            var payload = new JsonObject
            {
                ["version"] = ConfigVersion,
                ["settings"] = _settings.DeepClone()
            };
            string temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, payload.ToJsonString(TodoClock.JsonOptions) + "\n", new UTF8Encoding(false));
            File.Move(temp, ConfigPath, overwrite: true);
        }
    }

    public JsonObject UpdateSettings(JsonObject changes)
    {
        lock (_sync)
        {
            _settings = Merge(_settings, changes);
            Save();
            return _settings.DeepClone().AsObject();
        }
    }

    public static JsonObject Merge(JsonObject baseObj, JsonNode? incoming)
    {
        var output = baseObj.DeepClone().AsObject();
        if (incoming is not JsonObject obj)
        {
            return output;
        }
        foreach (var pair in obj)
        {
            if (pair.Value == null)
            {
                continue;
            }
            if (pair.Value is JsonObject childObj && output[pair.Key] is JsonObject existing)
            {
                output[pair.Key] = Merge(existing, childObj);
            }
            else
            {
                output[pair.Key] = pair.Value.DeepClone();
            }
        }
        return output;
    }

    public JsonObject Snapshot()
    {
        return new JsonObject
        {
            ["version"] = ConfigVersion,
            ["settings"] = Settings
        };
    }
}
