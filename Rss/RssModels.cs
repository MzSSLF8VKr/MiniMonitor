namespace MiniMonitor.Rss;

/// <summary>文章列表视图类型，与 Fluent Reader 的四种视图对应。</summary>
public enum RssViewMode
{
    List,
    Cards,
    Magazine,
    Compact
}

/// <summary>文章筛选：全部 / 仅未读 / 仅星标。</summary>
public enum RssFilterMode
{
    All,
    Unread,
    Starred
}

/// <summary>订阅源文章的默认打开方式。</summary>
public enum RssOpenMode
{
    Content,
    Webpage
}

/// <summary>左侧导航选中的范围。</summary>
public enum RssScopeKind
{
    All,
    Unread,
    Starred,
    Today,
    Feed,
    Group
}

/// <summary>一次查询的订阅范围。</summary>
public sealed class RssScope
{
    public RssScopeKind Kind { get; init; } = RssScopeKind.All;
    public int Id { get; init; }
    public string Title { get; init; } = "全部文章";

    public static RssScope All() => new() { Kind = RssScopeKind.All, Title = "全部文章" };
    public static RssScope Unread() => new() { Kind = RssScopeKind.Unread, Title = "未读文章" };
    public static RssScope Starred() => new() { Kind = RssScopeKind.Starred, Title = "星标文章" };
    public static RssScope Today() => new() { Kind = RssScopeKind.Today, Title = "今天" };
    public static RssScope Feed(int sid, string title) => new() { Kind = RssScopeKind.Feed, Id = sid, Title = title };
    public static RssScope Group(int gid, string title) => new() { Kind = RssScopeKind.Group, Id = gid, Title = title };
}

/// <summary>订阅分组（文件夹）。</summary>
public sealed class RssGroup
{
    public int Gid { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public bool Expanded { get; set; } = true;
}

/// <summary>订阅源。</summary>
public sealed class RssFeed
{
    public int Sid { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Link { get; set; }
    public string? Description { get; set; }
    public byte[]? Icon { get; set; }
    public string? IconUrl { get; set; }
    public int? Gid { get; set; }
    public int Position { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public long LastFetch { get; set; }
    public string? LastError { get; set; }
    public bool Hidden { get; set; }
    public RssOpenMode OpenMode { get; set; } = RssOpenMode.Content;
    public bool Notify { get; set; } = true;
    public int FetchLimit { get; set; }
    public string? RemoteRef { get; set; }
    public int Unread { get; set; }
    public int Total { get; set; }

    public string Host
    {
        get
        {
            try
            {
                return new Uri(Url).Host;
            }
            catch
            {
                return Url;
            }
        }
    }
}

/// <summary>一篇文章。</summary>
public sealed class RssArticle
{
    public long Aid { get; set; }
    public int Sid { get; set; }
    public string Guid { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Link { get; set; }
    public string? Content { get; set; }
    public string? Snippet { get; set; }
    public string? Thumb { get; set; }
    public long Published { get; set; }
    public long Fetched { get; set; }
    public bool IsRead { get; set; }
    public bool Starred { get; set; }
    public bool Hidden { get; set; }
    public string? FullContent { get; set; }
    public string? RemoteRef { get; set; }
    public string SourceTitle { get; set; } = "";
    public byte[]? SourceIcon { get; set; }
    public bool ReadDirty { get; set; }
    public bool StarDirty { get; set; }

    public DateTimeOffset PublishedTime =>
        DateTimeOffset.FromUnixTimeSeconds(Published <= 0 ? 0 : Published).ToLocalTime();

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "（无标题）" : Title;

    public RssArticle CloneForView()
    {
        return (RssArticle)MemberwiseClone();
    }
}

/// <summary>文章查询参数。</summary>
public sealed class RssQuery
{
    public RssScope Scope { get; set; } = RssScope.All();
    public bool UnreadOnly { get; set; }
    public bool StarredOnly { get; set; }
    public bool IncludeHidden { get; set; }
    public string? SearchText { get; set; }
    public bool UseRegex { get; set; }
    public bool CaseSensitive { get; set; }
    public bool SearchContent { get; set; } = true;
    public int Offset { get; set; }
    public int Limit { get; set; } = 100;
}

/// <summary>解析得到的一篇文章（尚未入库）。</summary>
public sealed class ParsedArticle
{
    public string Guid { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Link { get; set; }
    public string? Content { get; set; }
    public string? Snippet { get; set; }
    public string? Thumb { get; set; }
    public long Published { get; set; }
}

/// <summary>解析得到的订阅源。</summary>
public sealed class ParsedFeed
{
    public string Title { get; set; } = "";
    public string? Link { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public List<ParsedArticle> Articles { get; } = new();
}

/// <summary>站点自动发现得到的候选订阅源。</summary>
public sealed class DiscoveredFeed
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Type { get; set; }
}

/// <summary>
/// 自动处理规则：抓取时按正则匹配标题/正文，执行隐藏、标为已读、星标或通知。
/// 与 Fluent Reader 的“规则”功能对应。
/// </summary>
public sealed class RssRule
{
    /// <summary>作用范围："all"、"group:1"、"feed:2"。</summary>
    public string Target { get; set; } = "all";

    /// <summary>正则表达式。</summary>
    public string Pattern { get; set; } = "";

    /// <summary>匹配范围："title"、"content"、"both"。</summary>
    public string Field { get; set; } = "title";

    public bool CaseSensitive { get; set; }

    /// <summary>true = 命中时执行；false = 未命中时执行。</summary>
    public bool Match { get; set; } = true;

    public bool MarkRead { get; set; }
    public bool Star { get; set; }
    public bool Hide { get; set; }
    public bool Notify { get; set; }

    public string TargetText
    {
        get
        {
            if (Target == "all")
            {
                return "全部订阅源";
            }
            if (Target.StartsWith("group:", StringComparison.Ordinal))
            {
                return "分组 #" + Target["group:".Length..];
            }
            if (Target.StartsWith("feed:", StringComparison.Ordinal))
            {
                return "订阅源 #" + Target["feed:".Length..];
            }
            return Target;
        }
    }

    public string ActionText
    {
        get
        {
            var actions = new List<string>();
            if (MarkRead) actions.Add("标为已读");
            if (Star) actions.Add("加星标");
            if (Hide) actions.Add("隐藏");
            if (Notify) actions.Add("通知");
            return actions.Count == 0 ? "无动作" : string.Join("、", actions);
        }
    }

    public string FieldText => Field switch
    {
        "content" => "正文",
        "both" => "标题 + 正文",
        _ => "标题"
    };

    public RssRule Clone()
    {
        return (RssRule)MemberwiseClone();
    }
}

/// <summary>一次同步的结果。</summary>
public sealed class RssSyncResult
{
    public int NewArticles { get; set; }
    public int UpdatedFeeds { get; set; }
    public int FailedFeeds { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> NewTitles { get; } = new();
    public DateTimeOffset FinishedAt { get; set; } = DateTimeOffset.Now;
    public bool Cancelled { get; set; }
    public bool TriggeredManually { get; set; }
}
