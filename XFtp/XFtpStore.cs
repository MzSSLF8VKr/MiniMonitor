using System.Text.Json;

namespace MiniMonitor.XFtp;

internal static class XFtpPaths
{
    public static string Root
    {
        get
        {
            string dir = AppPaths.Sub("xftp");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SitesFile => Path.Combine(Root, "sites.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
}

/// <summary>XFtp 站点与设置持久化；密码使用 Windows DPAPI 加密。</summary>
internal sealed class XFtpStore
{
    private sealed class SiteFile
    {
        public List<XFtpSite> Sites { get; set; } = new();
    }

    public List<XFtpSite> Sites { get; private set; } = new();
    public XFtpSettings Settings { get; private set; } = new();

    public static XFtpStore Load()
    {
        var store = new XFtpStore();
        try
        {
            if (File.Exists(XFtpPaths.SitesFile))
            {
                var data = JsonSerializer.Deserialize<SiteFile>(
                    File.ReadAllText(XFtpPaths.SitesFile), XShell.XShellJson.Options);
                if (data?.Sites != null)
                {
                    store.Sites = data.Sites;
                    foreach (var site in store.Sites)
                    {
                        site.Decrypt();
                    }
                }
            }
        }
        catch
        {
            store.Sites = new List<XFtpSite>();
        }
        try
        {
            if (File.Exists(XFtpPaths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<XFtpSettings>(
                    File.ReadAllText(XFtpPaths.SettingsFile), XShell.XShellJson.Options);
                if (settings != null)
                {
                    store.Settings = settings;
                }
            }
        }
        catch
        {
            store.Settings = new XFtpSettings();
        }
        store.Settings.WindowWidth = Math.Max(960, store.Settings.WindowWidth);
        store.Settings.WindowHeight = Math.Max(620, store.Settings.WindowHeight);
        return store;
    }

    public void Save()
    {
        try
        {
            var copy = new SiteFile
            {
                Sites = Sites.Select(site =>
                {
                    var clone = site.Clone();
                    clone.Encrypt();
                    return clone;
                }).ToList()
            };
            File.WriteAllText(XFtpPaths.SitesFile,
                JsonSerializer.Serialize(copy, XShell.XShellJson.Options));
        }
        catch
        {
            // 站点保存失败不影响传输。
        }
        try
        {
            File.WriteAllText(XFtpPaths.SettingsFile,
                JsonSerializer.Serialize(Settings, XShell.XShellJson.Options));
        }
        catch
        {
            // 设置保存失败不影响使用。
        }
    }

    public XFtpSite? FindSite(string host, int port, string user)
    {
        return Sites.FirstOrDefault(site =>
            string.Equals(site.Host, host, StringComparison.OrdinalIgnoreCase)
            && site.Port == port
            && string.Equals(site.User, user, StringComparison.OrdinalIgnoreCase));
    }

    public XFtpSite AddOrUpdate(XFtpSite site)
    {
        var existing = FindSite(site.Host, site.Port, site.User);
        if (existing != null)
        {
            existing.Name = string.IsNullOrWhiteSpace(site.Name) ? existing.Name : site.Name;
            existing.Protocol = site.Protocol;
            existing.Password = site.Password;
            existing.RemotePath = site.RemotePath;
            existing.LocalPath = site.LocalPath;
            existing.LastUsedAt = DateTime.Now;
            Save();
            return existing;
        }
        site.LastUsedAt = DateTime.Now;
        Sites.Add(site);
        Save();
        return site;
    }
}
