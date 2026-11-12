using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniMonitor.Rss;

public sealed class RemoteFeed
{
    public string RemoteRef { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Link { get; set; }
    public string? GroupName { get; set; }
}

public sealed class RemoteArticle
{
    public string RemoteRef { get; set; } = "";
    public string FeedRemoteRef { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Link { get; set; }
    public string? Content { get; set; }
    public string? Thumb { get; set; }
    public long Published { get; set; }
    public bool IsRead { get; set; }
    public bool Starred { get; set; }
}

/// <summary>
/// Fever / Google Reader API 客户端。Fluent Reader 官方支持的服务端（FreshRSS、Tiny Tiny RSS、
/// Miniflux、The Old Reader、BazQux、Inoreader 等）均可通过这两种协议接入。
/// </summary>
public abstract class RssServiceClient : IDisposable
{
    protected readonly RssSettings Settings;
    protected readonly HttpClient Client;

    protected RssServiceClient(RssSettings settings, HttpClient client)
    {
        Settings = settings;
        Client = client;
    }

    public abstract string DisplayName { get; }

    public abstract Task TestAsync(CancellationToken cancellationToken);

    public abstract Task<List<RemoteFeed>> GetFeedsAsync(CancellationToken cancellationToken);

    public abstract Task<List<RemoteArticle>> GetArticlesAsync(int limit, CancellationToken cancellationToken);

    public abstract Task PushStateAsync(RssArticle article, CancellationToken cancellationToken);

    public abstract Task MarkFeedReadAsync(RssFeed feed, CancellationToken cancellationToken);

    public virtual void Dispose()
    {
    }

    public static RssServiceClient Create(RssSettings settings, HttpClient http)
    {
        return settings.ServiceType switch
        {
            "fever" => new FeverClient(settings, http),
            "greader" => new GReaderClient(settings, http),
            _ => throw new InvalidOperationException("当前为本地 RSS 模式，无需服务端同步。")
        };
    }
}

/// <summary>Fever API 客户端。</summary>
internal sealed class FeverClient : RssServiceClient
{
    private readonly string _endpoint;
    private readonly string _apiKey;

    public FeverClient(RssSettings settings, HttpClient http) : base(settings, http)
    {
        string raw = settings.ServiceEndpoint.Trim();
        if (raw.Contains('?'))
        {
            raw = raw[..raw.IndexOf('?')];
        }
        _endpoint = raw.TrimEnd('/') + "/";
        string password = RssSecrets.Unprotect(settings.ServicePassword);
        _apiKey = Md5Hex(settings.ServiceUsername.Trim() + ":" + password);
    }

    public override string DisplayName => "Fever API";

    private static string Md5Hex(string value)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<JsonDocument> ApiAsync(string extra, string? postParams,
        CancellationToken cancellationToken)
    {
        string url = _endpoint + "?api" + extra;
        string body = "api_key=" + Uri.EscapeDataString(_apiKey) + (postParams ?? "");
        using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await Client.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(text);
    }

    public override async Task TestAsync(CancellationToken cancellationToken)
    {
        using var document = await ApiAsync("", null, cancellationToken);
        var root = document.RootElement;
        bool ok = root.TryGetProperty("auth", out var auth)
            && (auth.ValueKind == JsonValueKind.True
                || (auth.ValueKind == JsonValueKind.Number && auth.GetInt32() != 0));
        if (!ok)
        {
            throw new InvalidOperationException("Fever 认证失败，请检查端点、用户名与密码。");
        }
    }

    public override async Task<List<RemoteFeed>> GetFeedsAsync(CancellationToken cancellationToken)
    {
        using var document = await ApiAsync("&feeds=1&groups=1&feeds_groups=1", null, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("feeds", out var feeds) || feeds.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Fever 未返回订阅源列表。");
        }
        var groupNames = new Dictionary<long, string>();
        if (root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                long id = GetLong(group, "id");
                string name = GetString(group, "title") ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    groupNames[id] = name.Trim();
                }
            }
        }
        var feedGroups = new Dictionary<long, string>();
        if (root.TryGetProperty("feeds_groups", out var mappings) && mappings.ValueKind == JsonValueKind.Array)
        {
            foreach (var mapping in mappings.EnumerateArray())
            {
                long gid = GetLong(mapping, "group_id");
                string ids = GetString(mapping, "feed_ids") ?? "";
                if (!groupNames.TryGetValue(gid, out string? groupName))
                {
                    continue;
                }
                foreach (string part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (long.TryParse(part.Trim(), out long fid))
                    {
                        feedGroups[fid] = groupName;
                    }
                }
            }
        }

        var result = new List<RemoteFeed>();
        foreach (var feed in feeds.EnumerateArray())
        {
            long id = GetLong(feed, "id");
            string? url = GetString(feed, "url");
            if (id == 0 || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }
            result.Add(new RemoteFeed
            {
                RemoteRef = id.ToString(CultureInfo.InvariantCulture),
                Title = GetString(feed, "title") ?? url,
                Url = url,
                Link = null,
                GroupName = feedGroups.TryGetValue(id, out string? name) ? name : null
            });
        }
        return result;
    }

    public override async Task<List<RemoteArticle>> GetArticlesAsync(int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 50, 5000);
        var result = new List<RemoteArticle>();
        long maxId = long.MaxValue;
        bool fallbackInt32 = false;
        int guard = 0;
        while (result.Count < limit && guard++ < 200)
        {
            JsonDocument document;
            try
            {
                document = await ApiAsync($"&items&max_id={maxId}", null, cancellationToken);
            }
            catch when (!fallbackInt32)
            {
                // 部分服务端只接受 32 位 max_id
                fallbackInt32 = true;
                maxId = int.MaxValue;
                continue;
            }
            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Fever 未返回文章列表。");
                }
                int count = 0;
                long minId = maxId;
                foreach (var item in items.EnumerateArray())
                {
                    count++;
                    long id = GetLong(item, "id");
                    if (id <= 0)
                    {
                        continue;
                    }
                    minId = Math.Min(minId, id);
                    if (result.Any(a => a.RemoteRef == id.ToString(CultureInfo.InvariantCulture)))
                    {
                        continue;
                    }
                    string? html = GetString(item, "html");
                    long published = GetLong(item, "created_on_time");
                    if (published <= 0)
                    {
                        published = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    }
                    result.Add(new RemoteArticle
                    {
                        RemoteRef = id.ToString(CultureInfo.InvariantCulture),
                        FeedRemoteRef = GetLong(item, "feed_id").ToString(CultureInfo.InvariantCulture),
                        Title = GetString(item, "title") ?? "",
                        Author = GetString(item, "author"),
                        Link = GetString(item, "url"),
                        Content = RssParser.MakeAbsoluteHtml(RssParser.SanitizeHtml(html), null),
                        Published = published,
                        IsRead = GetBool(item, "is_read"),
                        Starred = GetBool(item, "is_saved")
                    });
                    if (result.Count >= limit)
                    {
                        break;
                    }
                }
                if (count < 50 || minId >= maxId)
                {
                    break;
                }
                maxId = minId - 1;
            }
        }
        return result;
    }

    public override async Task PushStateAsync(RssArticle article, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(article.RemoteRef))
        {
            return;
        }
        if (article.ReadDirty)
        {
            using (var document = await ApiAsync("",
                       $"&mark=item&as={(article.IsRead ? "read" : "unread")}&id={article.RemoteRef}",
                       cancellationToken))
            {
                _ = document;
            }
        }
        if (article.StarDirty)
        {
            using (var document = await ApiAsync("",
                       $"&mark=item&as={(article.Starred ? "saved" : "unsaved")}&id={article.RemoteRef}",
                       cancellationToken))
            {
                _ = document;
            }
        }
    }

    public override async Task MarkFeedReadAsync(RssFeed feed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(feed.RemoteRef))
        {
            return;
        }
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1;
        using var _ = await ApiAsync("",
            $"&mark=feed&as=read&id={feed.RemoteRef}&before={timestamp}",
            cancellationToken);
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static long GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out long number2)
                ? number2
                : 0;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return false;
        }
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetInt32(out int number) && number != 0,
            JsonValueKind.String => value.GetString() is "1" or "true",
            _ => false
        };
    }
}

/// <summary>Google Reader API 客户端（The Old Reader、BazQux、FreshRSS、Inoreader 等）。</summary>
internal sealed class GReaderClient : RssServiceClient
{
    private const string AllTag = "user/-/state/com.google/reading-list";
    private const string ReadTag = "user/-/state/com.google/read";
    private const string StarTag = "user/-/state/com.google/starred";

    private readonly string _endpoint;
    private string? _auth;
    private string? _token;

    public GReaderClient(RssSettings settings, HttpClient http) : base(settings, http)
    {
        string raw = settings.ServiceEndpoint.Trim();
        if (raw.Contains('?'))
        {
            raw = raw[..raw.IndexOf('?')];
        }
        _endpoint = raw.TrimEnd('/');
    }

    public override string DisplayName => "Google Reader API";

    private async Task<HttpRequestMessage> BuildRequestAsync(string path, HttpMethod method,
        FormUrlEncodedContent? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, _endpoint + path);
        request.Headers.TryAddWithoutValidation("Authorization", _auth ?? "");
        request.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
        if (Settings.ServiceType == "greader")
        {
            if (!string.IsNullOrWhiteSpace(Settings.ServiceAppId))
            {
                request.Headers.TryAddWithoutValidation("AppId", Settings.ServiceAppId);
                request.Headers.TryAddWithoutValidation("AppKey", Settings.ServiceAppKey);
            }
            else if (_endpoint.Contains("inoreader", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("AppId", "999999298");
                request.Headers.TryAddWithoutValidation("AppKey", "KPbKYXTfgrKbwmroOeYC7mcW21ZRwF5Y");
            }
        }
        if (body != null)
        {
            request.Content = body;
        }
        await Task.CompletedTask;
        return request;
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        string password = RssSecrets.Unprotect(Settings.ServicePassword);
        if (string.IsNullOrWhiteSpace(Settings.ServiceUsername) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("请填写 Google Reader 服务的用户名与密码。");
        }
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = Settings.ServiceUsername,
            ["Passwd"] = password,
            ["service"] = "reader"
        });
        using var response = await Client.PostAsync(_endpoint + "/accounts/ClientLogin", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"登录失败（HTTP {(int)response.StatusCode}），请检查用户名与密码。");
        }
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = Regex.Match(text, @"Auth=(\S+)");
        if (!match.Success)
        {
            throw new InvalidOperationException("登录响应中没有 Auth 令牌，服务端可能不支持 Google Reader API。");
        }
        _auth = "GoogleLogin auth=" + match.Groups[1].Value;
        _token = null;
        return _auth;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, HttpMethod method,
        FormUrlEncodedContent? body, CancellationToken cancellationToken, bool retry = true)
    {
        if (_auth == null)
        {
            await LoginAsync(cancellationToken);
        }
        using var request = await BuildRequestAsync(path, method, body, cancellationToken);
        var response = await Client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && retry)
        {
            response.Dispose();
            await LoginAsync(cancellationToken);
            return await SendAsync(path, method, body, cancellationToken, retry: false);
        }
        return response;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_token))
        {
            return _token;
        }
        using var response = await SendAsync("/reader/api/0/token", HttpMethod.Get, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        _token = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        return _token;
    }

    public override async Task TestAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync("/reader/api/0/user-info", HttpMethod.Get, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"连接失败（HTTP {(int)response.StatusCode}）。");
        }
    }

    public override async Task<List<RemoteFeed>> GetFeedsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync("/reader/api/0/subscription/list?output=json",
            HttpMethod.Get, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var result = new List<RemoteFeed>();
        if (!document.RootElement.TryGetProperty("subscriptions", out var subscriptions))
        {
            return result;
        }
        foreach (var item in subscriptions.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            string? url = item.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }
            url ??= item.TryGetProperty("htmlUrl", out var htmlElement) ? htmlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }
            string? groupName = null;
            if (Settings.ServiceImportGroups && item.TryGetProperty("categories", out var categories)
                && categories.ValueKind == JsonValueKind.Array)
            {
                foreach (var category in categories.EnumerateArray())
                {
                    groupName = category.TryGetProperty("label", out var label) ? label.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(groupName))
                    {
                        break;
                    }
                }
            }
            result.Add(new RemoteFeed
            {
                RemoteRef = id,
                Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? url : url,
                Url = url,
                Link = item.TryGetProperty("htmlUrl", out var link) ? link.GetString() : null,
                GroupName = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim()
            });
        }
        return result;
    }

    public override async Task<List<RemoteArticle>> GetArticlesAsync(int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 50, 5000);
        var result = new List<RemoteArticle>();
        string? continuation = null;
        int guard = 0;
        while (result.Count < limit && guard++ < 20)
        {
            string path = $"/reader/api/0/stream/contents?output=json&n={Math.Min(1000, limit - result.Count)}";
            if (Settings.ServiceLastFetched > 0)
            {
                path += "&ot=" + Settings.ServiceLastFetched;
            }
            if (!string.IsNullOrEmpty(continuation))
            {
                path += "&c=" + Uri.EscapeDataString(continuation);
            }
            using var response = await SendAsync(path, HttpMethod.Get, null, cancellationToken);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                break;
            }
            int count = 0;
            foreach (var item in items.EnumerateArray())
            {
                count++;
                string? id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }
                string? feedRef = null;
                if (item.TryGetProperty("origin", out var origin)
                    && origin.TryGetProperty("streamId", out var streamId))
                {
                    feedRef = streamId.GetString();
                }
                string? link = null;
                if (item.TryGetProperty("alternate", out var alternate)
                    && alternate.ValueKind == JsonValueKind.Array && alternate.GetArrayLength() > 0)
                {
                    link = alternate[0].TryGetProperty("href", out var href) ? href.GetString() : null;
                }
                string? content = null;
                if (item.TryGetProperty("content", out var contentElement)
                    && contentElement.TryGetProperty("content", out var contentValue))
                {
                    content = contentValue.GetString();
                }
                if (string.IsNullOrWhiteSpace(content)
                    && item.TryGetProperty("summary", out var summary)
                    && summary.TryGetProperty("content", out var summaryValue))
                {
                    content = summaryValue.GetString();
                }
                bool isRead = false;
                bool starred = false;
                if (item.TryGetProperty("categories", out var categories)
                    && categories.ValueKind == JsonValueKind.Array)
                {
                    foreach (var category in categories.EnumerateArray())
                    {
                        string? tag = category.GetString();
                        if (tag == null)
                        {
                            continue;
                        }
                        if (tag.EndsWith("/read", StringComparison.Ordinal))
                        {
                            isRead = true;
                        }
                        else if (tag.EndsWith("/starred", StringComparison.Ordinal))
                        {
                            starred = true;
                        }
                    }
                }
                long published = 0;
                if (item.TryGetProperty("published", out var publishedElement)
                    && publishedElement.TryGetInt64(out long publishedValue))
                {
                    published = publishedValue;
                }
                if (published <= 0)
                {
                    published = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                result.Add(new RemoteArticle
                {
                    RemoteRef = id,
                    FeedRemoteRef = feedRef ?? "",
                    Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    Author = item.TryGetProperty("author", out var author) ? author.GetString() : null,
                    Link = link,
                    Content = RssParser.MakeAbsoluteHtml(RssParser.SanitizeHtml(content), null),
                    Published = published,
                    IsRead = isRead,
                    Starred = starred
                });
                if (result.Count >= limit)
                {
                    break;
                }
            }
            continuation = document.RootElement.TryGetProperty("continuation", out var next)
                ? next.GetString()
                : null;
            if (count == 0 || string.IsNullOrEmpty(continuation))
            {
                break;
            }
        }
        return result;
    }

    public override async Task PushStateAsync(RssArticle article, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(article.RemoteRef))
        {
            return;
        }
        string token = await GetTokenAsync(cancellationToken);
        if (article.ReadDirty)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["i"] = article.RemoteRef,
                [article.IsRead ? "a" : "r"] = ReadTag,
                ["ac"] = "edit-tags",
                ["T"] = token
            });
            using var response = await SendAsync("/reader/api/0/edit-tag", HttpMethod.Post, form,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        if (article.StarDirty)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["i"] = article.RemoteRef,
                [article.Starred ? "a" : "r"] = StarTag,
                ["ac"] = "edit-tags",
                ["T"] = token
            });
            using var response = await SendAsync("/reader/api/0/edit-tag", HttpMethod.Post, form,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public override async Task MarkFeedReadAsync(RssFeed feed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(feed.RemoteRef))
        {
            return;
        }
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["s"] = feed.RemoteRef
        });
        using var response = await SendAsync("/reader/api/0/mark-all-as-read", HttpMethod.Post, form,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>服务端同步编排：拉取订阅源与文章，推送本地已读/星标状态。</summary>
public static class RssServiceSync
{
    public static async Task<(int NewArticles, int UpdatedFeeds, List<string> Errors)> SyncAsync(
        RssStore store, RssSettings settings, HttpClient http, CancellationToken cancellationToken)
    {
        int newArticles = 0;
        int updatedFeeds = 0;
        var errors = new List<string>();
        using var client = RssServiceClient.Create(settings, http);
        await client.TestAsync(cancellationToken);

        var remoteFeeds = await client.GetFeedsAsync(cancellationToken);
        var localGroups = store.GetGroups();
        var existingFeeds = store.GetFeeds();
        var feedMap = new Dictionary<string, RssFeed>(StringComparer.Ordinal);
        var pendingFeeds = new List<(RemoteFeed Remote, string Url, string? GroupName)>();

        foreach (var remote in remoteFeeds)
        {
            var local = existingFeeds.FirstOrDefault(f =>
                    string.Equals(f.RemoteRef, remote.RemoteRef, StringComparison.Ordinal))
                ?? existingFeeds.FirstOrDefault(f =>
                    string.Equals(f.Url, remote.Url, StringComparison.OrdinalIgnoreCase));
            if (local != null)
            {
                if (local.RemoteRef != remote.RemoteRef)
                {
                    store.SetFeedRemoteRef(local.Sid, remote.RemoteRef);
                    local.RemoteRef = remote.RemoteRef;
                }
                feedMap[remote.RemoteRef] = local;
                continue;
            }
            pendingFeeds.Add((remote, remote.Url, remote.GroupName));
        }

        foreach (var pending in pendingFeeds)
        {
            int? gid = null;
            if (!string.IsNullOrWhiteSpace(pending.GroupName))
            {
                var group = localGroups.FirstOrDefault(g =>
                    string.Equals(g.Name, pending.GroupName, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    int newGid = store.AddGroup(pending.GroupName!);
                    group = new RssGroup { Gid = newGid, Name = pending.GroupName! };
                    localGroups.Add(group);
                }
                gid = group.Gid;
            }
            var feed = new RssFeed
            {
                Title = pending.Remote.Title,
                Url = pending.Url,
                Link = pending.Remote.Link,
                Gid = gid,
                RemoteRef = pending.Remote.RemoteRef
            };
            int sid = store.AddFeed(feed, out bool created);
            feed.Sid = sid;
            feedMap[pending.Remote.RemoteRef] = feed;
            if (created)
            {
                updatedFeeds++;
            }
        }

        var articles = await client.GetArticlesAsync(settings.ServiceFetchLimit, cancellationToken);
        var rules = settings.Rules.ToList();
        foreach (var remote in articles)
        {
            if (!feedMap.TryGetValue(remote.FeedRemoteRef, out var feed))
            {
                feed = store.FindFeedByRemoteRef(remote.FeedRemoteRef);
                if (feed == null)
                {
                    continue;
                }
                feedMap[remote.FeedRemoteRef] = feed;
            }
            var parsed = new ParsedArticle
            {
                Guid = "svc:" + remote.RemoteRef,
                Title = remote.Title,
                Author = remote.Author,
                Link = remote.Link,
                Content = remote.Content,
                Published = remote.Published
            };
            var outcome = RssRules.Apply(rules, feed, parsed, includeNotify: false);
            var article = new RssArticle
            {
                Sid = feed.Sid,
                Guid = parsed.Guid,
                Title = parsed.Title,
                Author = parsed.Author,
                Link = parsed.Link,
                Content = parsed.Content,
                Snippet = RssParser.HtmlToText(parsed.Content) is { Length: > 0 } text
                    ? (text.Length > 280 ? text[..280] + "…" : text)
                    : null,
                Thumb = null,
                Published = parsed.Published,
                Fetched = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsRead = remote.IsRead || outcome.MarkRead,
                Starred = remote.Starred || outcome.Star,
                Hidden = outcome.Hide,
                RemoteRef = remote.RemoteRef
            };
            if (article.Snippet != null)
            {
                article.Snippet = article.Snippet.Replace("\r", " ").Replace("\n", " ").Trim();
            }
            if (article.Content != null)
            {
                var image = Regex.Match(article.Content,
                    @"<img\b[^>]*\bsrc\s*=\s*['""](?<url>[^'""]+)['""]",
                    RegexOptions.IgnoreCase);
                if (image.Success)
                {
                    article.Thumb = image.Groups["url"].Value;
                }
            }
            long aid = store.InsertArticle(article);
            if (aid > 0)
            {
                newArticles++;
            }
            else
            {
                store.UpdateArticleStates(remote.RemoteRef, remote.IsRead, remote.Starred);
            }
        }

        // 推送本地已读 / 星标变更
        foreach (var dirty in store.GetDirtyArticles())
        {
            try
            {
                await client.PushStateAsync(dirty, cancellationToken);
                store.ClearDirty(dirty.Aid);
            }
            catch (Exception ex)
            {
                errors.Add("推送状态失败：" + ex.Message);
                break;
            }
        }

        settings.ServiceLastFetched = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return (newArticles, updatedFeeds, errors);
    }
}
