using System.Drawing;
using System.Drawing.Imaging;

namespace MiniMonitor.Rss;

/// <summary>
/// RSS 后台同步服务。应用启动后即开始计时，默认每 60 分钟抓取一次全部订阅源；
/// 关闭阅读器窗口不会停止同步。打开阅读器时会额外强制同步一次。
/// </summary>
public sealed class RssSyncService : IDisposable
{
    private readonly RssStore _store;
    private readonly AppConfig _config;
    private readonly HttpClient _http;
    private readonly System.Threading.Timer _timer;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private DateTimeOffset? _lastSync;
    private bool _syncing;
    private bool _disposed;
    private CancellationTokenSource? _cts;

    public RssSyncService(RssStore store, AppConfig config)
    {
        _store = store;
        _config = config;
        _http = Http.CreateClient();
        if (config.Rss.LastSyncUnix > 0)
        {
            try
            {
                _lastSync = DateTimeOffset.FromUnixTimeSeconds(config.Rss.LastSyncUnix).ToLocalTime();
            }
            catch
            {
                _lastSync = null;
            }
        }
        // 每 30 秒检查一次是否到期；首次检查在启动 20 秒后。
        _timer = new System.Threading.Timer(_ => CheckDue(), null,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
    }

    public bool IsSyncing
    {
        get
        {
            lock (_stateGate)
            {
                return _syncing;
            }
        }
    }

    public DateTimeOffset? LastSync
    {
        get
        {
            lock (_stateGate)
            {
                return _lastSync;
            }
        }
    }

    public string? LastError { get; private set; }

    /// <summary>同步完成（无论成功或部分失败）。</summary>
    public event Action<RssSyncResult>? SyncCompleted;

    /// <summary>数据库内容发生变化，界面应刷新列表。</summary>
    public event Action? DataChanged;

    /// <summary>同步开始/结束，用于更新状态栏。</summary>
    public event Action? StateChanged;

    public void Start()
    {
        // 构造函数已启动计时器；这里保留显式接口以便阅读器窗口调用。
        CheckDue();
    }

    private void CheckDue()
    {
        if (_disposed)
        {
            return;
        }
        int interval = _config.Rss.SyncIntervalMinutes;
        if (interval <= 0)
        {
            return;
        }
        var last = LastSync;
        if (last.HasValue && DateTimeOffset.Now - last.Value < TimeSpan.FromMinutes(interval))
        {
            return;
        }
        if (!last.HasValue && !_config.Rss.SyncOnStartup
            && DateTimeOffset.Now - _startedAt < TimeSpan.FromMinutes(interval))
        {
            // 关闭“启动后自动同步”时，首次同步等到一个完整间隔之后再执行
            return;
        }
        _ = SyncAsync(manual: false);
    }

    /// <summary>立即同步。manual=true 表示由“刷新/打开阅读器”触发。</summary>
    public Task<RssSyncResult?> SyncAsync(bool manual) => SyncCoreAsync(manual, null);

    /// <summary>只刷新一个订阅源（订阅源右键菜单 / 双击）。</summary>
    public Task<RssSyncResult?> SyncFeedAsync(int sid) => SyncCoreAsync(true, sid);

    private async Task<RssSyncResult?> SyncCoreAsync(bool manual, int? onlyFeedSid)
    {
        if (_disposed)
        {
            return null;
        }
        if (!await _syncGate.WaitAsync(0))
        {
            return null;
        }
        lock (_stateGate)
        {
            _syncing = true;
        }
        Raise(StateChanged);
        var result = new RssSyncResult { TriggeredManually = manual };
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try
        {
            if (_config.Rss.ServiceType is "fever" or "greader")
            {
                await SyncServiceBackendAsync(result, _cts.Token);
            }
            else if (onlyFeedSid.HasValue)
            {
                var feed = _store.GetFeed(onlyFeedSid.Value);
                if (feed != null)
                {
                    await SyncSingleFeedAsync(feed, result, _config.Rss.Rules.ToList(), _cts.Token);
                }
            }
            else
            {
                await SyncLocalAsync(result, _cts.Token);
            }
            LastError = null;
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            result.Errors.Add("同步超时，已中止。");
            LastError = "同步超时";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            result.Errors.Add(ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            var now = DateTimeOffset.Now;
            lock (_stateGate)
            {
                _syncing = false;
                _lastSync = now;
            }
            _config.Rss.LastSyncUnix = now.ToUnixTimeSeconds();
            _config.Rss.LastSyncErrors = string.Join("\r\n", result.Errors.Take(8));
            _config.Save();
            _syncGate.Release();
            Raise(StateChanged);
            Raise(DataChanged);
            try
            {
                SyncCompleted?.Invoke(result);
            }
            catch
            {
                // 订阅方的界面异常不应影响同步服务
            }
        }
        return result;
    }

    private static void Raise(Action? handler)
    {
        try
        {
            handler?.Invoke();
        }
        catch
        {
            // 忽略订阅方异常
        }
    }

    /// <summary>调整同步间隔后调用，让新设置立即生效。</summary>
    public void Reschedule()
    {
        if (_config.Rss.SyncIntervalMinutes <= 0)
        {
            return;
        }
        var last = LastSync;
        if (last == null || DateTimeOffset.Now - last.Value >= TimeSpan.FromMinutes(_config.Rss.SyncIntervalMinutes))
        {
            _ = SyncAsync(false);
        }
    }

    private async Task SyncServiceBackendAsync(RssSyncResult result, CancellationToken cancellationToken)
    {
        var (newArticles, updatedFeeds, errors) = await RssServiceSync.SyncAsync(
            _store, _config.Rss, _http, cancellationToken);
        result.NewArticles = newArticles;
        result.UpdatedFeeds = updatedFeeds;
        result.Errors.AddRange(errors);
        if (newArticles > 0)
        {
            var newest = _store.QueryArticles(new RssQuery { Limit = 5, IncludeHidden = true });
            result.NewTitles.AddRange(newest.Take(5).Select(a => a.DisplayTitle));
        }
    }

    private async Task SyncLocalAsync(RssSyncResult result, CancellationToken cancellationToken)
    {
        var feeds = _store.GetFeeds();
        if (feeds.Count == 0)
        {
            return;
        }
        var rules = _config.Rss.Rules.ToList();
        using var limiter = new SemaphoreSlim(4, 4);
        var tasks = feeds.Select(async feed =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                await SyncSingleFeedAsync(feed, result, rules, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (result)
                {
                    result.FailedFeeds++;
                }
                lock (result.Errors)
                {
                    result.Errors.Add($"{feed.Title}：{ex.Message}");
                }
                _store.UpdateFeedFetchState(feed.Sid, null, null,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ex.Message, keepEtags: true);
            }
            finally
            {
                limiter.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private async Task SyncSingleFeedAsync(RssFeed feed, RssSyncResult result,
        List<RssRule> rules, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
        request.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept",
            "application/rss+xml,application/atom+xml,application/feed+json,application/xml,text/xml,*/*;q=0.8");
        if (!string.IsNullOrWhiteSpace(feed.ETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", feed.ETag);
        }
        if (!string.IsNullOrWhiteSpace(feed.LastModified))
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", feed.LastModified);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            _store.UpdateFeedFetchState(feed.Sid, null, null,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(), null, keepEtags: true);
            return;
        }
        response.EnsureSuccessStatusCode();
        string text = await Http.ReadStringLimitedAsync(response, 8 * 1024 * 1024, cancellationToken);
        var parsed = RssParser.Parse(text, feed.Url);
        string? etag = response.Headers.ETag?.Tag;
        string? lastModified = response.Content.Headers.LastModified?.ToString("R");

        bool placeholderTitle = string.IsNullOrWhiteSpace(feed.Title)
            || string.Equals(feed.Title, feed.Host, StringComparison.OrdinalIgnoreCase)
            || string.Equals(feed.Title, feed.Url, StringComparison.OrdinalIgnoreCase);
        if (placeholderTitle && !string.IsNullOrWhiteSpace(parsed.Title))
        {
            _store.UpdateFeedMeta(feed.Sid, parsed.Title, parsed.Link, parsed.Description, parsed.IconUrl);
            feed.Title = parsed.Title;
        }
        else
        {
            _store.UpdateFeedMeta(feed.Sid, null, parsed.Link, parsed.Description, parsed.IconUrl);
        }
        if (!string.IsNullOrWhiteSpace(parsed.Link))
        {
            feed.Link = parsed.Link;
        }

        if (feed.Icon == null)
        {
            var iconUrl = RssParser.GuessFaviconUrl(new RssFeed
            {
                Url = feed.Url,
                Link = parsed.Link ?? feed.Link,
                IconUrl = parsed.IconUrl
            });
            if (!string.IsNullOrWhiteSpace(iconUrl))
            {
                await TryDownloadIconAsync(feed.Sid, iconUrl, cancellationToken);
            }
        }

        int inserted = 0;
        foreach (var parsedArticle in parsed.Articles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = RssRules.Apply(rules, feed, parsedArticle);
            var article = new RssArticle
            {
                Sid = feed.Sid,
                Guid = parsedArticle.Guid,
                Title = parsedArticle.Title,
                Author = parsedArticle.Author,
                Link = parsedArticle.Link,
                Content = parsedArticle.Content,
                Snippet = parsedArticle.Snippet,
                Thumb = parsedArticle.Thumb,
                Published = parsedArticle.Published,
                Fetched = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsRead = outcome.MarkRead,
                Starred = outcome.Star,
                Hidden = outcome.Hide
            };
            long aid = _store.InsertArticle(article);
            if (aid <= 0)
            {
                continue;
            }
            inserted++;
            if (outcome.Notify && feed.Notify && !article.IsRead && !article.Hidden)
            {
                lock (result.NewTitles)
                {
                    if (result.NewTitles.Count < 6)
                    {
                        result.NewTitles.Add(article.DisplayTitle);
                    }
                }
            }
        }

        if (inserted > 0)
        {
            lock (result)
            {
                result.NewArticles += inserted;
                result.UpdatedFeeds++;
            }
            _store.EnforceRetention(feed.Sid, _config.Rss.MaxArticlesPerFeed);
        }
        _store.UpdateFeedFetchState(feed.Sid, etag, lastModified,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), null, keepEtags: true);
    }

    private async Task TryDownloadIconAsync(int sid, string iconUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, iconUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length is 0 or > 512 * 1024)
            {
                return;
            }
            using var input = new MemoryStream(bytes);
            using var image = Image.FromStream(input);
            using var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, 0, 0, 32, 32);
            }
            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            _store.SetFeedIcon(sid, output.ToArray());
        }
        catch
        {
            // 图标失败不影响订阅源同步
        }
    }

    /// <summary>服务端模式下把“全部标为已读”推送到服务器。</summary>
    public async Task PushMarkAllReadAsync(RssScope scope)
    {
        if (_config.Rss.ServiceType is not ("fever" or "greader"))
        {
            return;
        }
        try
        {
            using var client = RssServiceClient.Create(_config.Rss, _http);
            var feeds = _store.GetFeeds();
            var targets = scope.Kind switch
            {
                RssScopeKind.Feed => feeds.Where(f => f.Sid == scope.Id),
                RssScopeKind.Group => feeds.Where(f => f.Gid == scope.Id),
                _ => feeds
            };
            foreach (var feed in targets)
            {
                try
                {
                    await client.MarkFeedReadAsync(feed, CancellationToken.None);
                }
                catch
                {
                    // 单个订阅源推送失败不阻塞其它订阅源
                }
            }
        }
        catch
        {
            // 服务端未配置时忽略
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Dispose();
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // 忽略
        }
        _cts?.Dispose();
        _http.Dispose();
    }
}
