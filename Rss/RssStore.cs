using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MiniMonitor.Rss;

/// <summary>
/// FluentRSS 本地数据库（SQLite）。订阅源、分组、文章、全文缓存与服务端映射都保存在
/// %APPDATA%\MiniMonitor\fluentrss.db，关闭阅读器后由后台同步服务继续写入。
/// </summary>
public sealed class RssStore
{
    private readonly object _gate = new();
    private readonly string _dbPath;

    public static string DefaultPath { get; } = Path.Combine(
        AppConfig.ConfigDir, "fluentrss.db");

    public RssStore(string? path = null)
    {
        _dbPath = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        Initialize();
    }

    public string DatabasePath => _dbPath;

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS rss_groups (
                    gid INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    position INTEGER NOT NULL DEFAULT 0,
                    expanded INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS rss_feeds (
                    sid INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    url TEXT NOT NULL UNIQUE,
                    link TEXT,
                    description TEXT,
                    icon BLOB,
                    icon_url TEXT,
                    gid INTEGER,
                    position INTEGER NOT NULL DEFAULT 0,
                    etag TEXT,
                    last_modified TEXT,
                    last_fetch INTEGER NOT NULL DEFAULT 0,
                    last_error TEXT,
                    hidden INTEGER NOT NULL DEFAULT 0,
                    open_mode INTEGER NOT NULL DEFAULT 0,
                    notify INTEGER NOT NULL DEFAULT 1,
                    fetch_limit INTEGER NOT NULL DEFAULT 0,
                    remote_ref TEXT
                );
                CREATE TABLE IF NOT EXISTS rss_articles (
                    aid INTEGER PRIMARY KEY AUTOINCREMENT,
                    sid INTEGER NOT NULL,
                    guid TEXT NOT NULL,
                    title TEXT NOT NULL DEFAULT '',
                    author TEXT,
                    link TEXT,
                    content TEXT,
                    snippet TEXT,
                    thumb TEXT,
                    published INTEGER NOT NULL DEFAULT 0,
                    fetched INTEGER NOT NULL DEFAULT 0,
                    is_read INTEGER NOT NULL DEFAULT 0,
                    starred INTEGER NOT NULL DEFAULT 0,
                    hidden INTEGER NOT NULL DEFAULT 0,
                    full_content TEXT,
                    remote_ref TEXT,
                    read_dirty INTEGER NOT NULL DEFAULT 0,
                    star_dirty INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(sid, guid)
                );
                CREATE INDEX IF NOT EXISTS idx_rss_articles_date ON rss_articles(published DESC, aid DESC);
                CREATE INDEX IF NOT EXISTS idx_rss_articles_feed ON rss_articles(sid, published DESC);
                CREATE INDEX IF NOT EXISTS idx_rss_articles_unread ON rss_articles(sid, is_read);
                CREATE TABLE IF NOT EXISTS rss_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT
                );
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
    }

    // ================= 分组 =================

    public List<RssGroup> GetGroups()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT gid,name,position,expanded FROM rss_groups ORDER BY position,gid";
            using var reader = command.ExecuteReader();
            var list = new List<RssGroup>();
            while (reader.Read())
            {
                list.Add(new RssGroup
                {
                    Gid = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Position = reader.GetInt32(2),
                    Expanded = reader.GetInt32(3) != 0
                });
            }
            return list;
        }
    }

    public int AddGroup(string name)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO rss_groups(name,position,expanded)
                VALUES($name,(SELECT COALESCE(MAX(position),0)+1 FROM rss_groups),1);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$name", name.Trim());
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public void RenameGroup(int gid, string name)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_groups SET name=$name WHERE gid=$gid";
            command.Parameters.AddWithValue("$name", name.Trim());
            command.Parameters.AddWithValue("$gid", gid);
            command.ExecuteNonQuery();
        }
    }

    public void DeleteGroup(int gid)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE rss_feeds SET gid=NULL WHERE gid=$gid";
                command.Parameters.AddWithValue("$gid", gid);
                command.ExecuteNonQuery();
            }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM rss_groups WHERE gid=$gid";
                command.Parameters.AddWithValue("$gid", gid);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public void SetGroupExpanded(int gid, bool expanded)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_groups SET expanded=$expanded WHERE gid=$gid";
            command.Parameters.AddWithValue("$expanded", expanded ? 1 : 0);
            command.Parameters.AddWithValue("$gid", gid);
            command.ExecuteNonQuery();
        }
    }

    public void ReorderGroups(IReadOnlyList<int> orderedGids)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            for (int i = 0; i < orderedGids.Count; i++)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE rss_groups SET position=$position WHERE gid=$gid";
                command.Parameters.AddWithValue("$position", i);
                command.Parameters.AddWithValue("$gid", orderedGids[i]);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    // ================= 订阅源 =================

    public List<RssFeed> GetFeeds()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT f.sid,f.title,f.url,f.link,f.description,f.icon,f.icon_url,f.gid,f.position,
                       f.etag,f.last_modified,f.last_fetch,f.last_error,f.hidden,f.open_mode,f.notify,
                       f.fetch_limit,f.remote_ref,
                       (SELECT COUNT(*) FROM rss_articles a WHERE a.sid=f.sid AND a.is_read=0 AND a.hidden=0) AS unread,
                       (SELECT COUNT(*) FROM rss_articles a WHERE a.sid=f.sid) AS total
                FROM rss_feeds f
                ORDER BY f.position,f.sid
                """;
            using var reader = command.ExecuteReader();
            var list = new List<RssFeed>();
            while (reader.Read())
            {
                list.Add(ReadFeed(reader, 0));
            }
            return list;
        }
    }

    public RssFeed? GetFeed(int sid)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT f.sid,f.title,f.url,f.link,f.description,f.icon,f.icon_url,f.gid,f.position,
                       f.etag,f.last_modified,f.last_fetch,f.last_error,f.hidden,f.open_mode,f.notify,
                       f.fetch_limit,f.remote_ref,
                       (SELECT COUNT(*) FROM rss_articles a WHERE a.sid=f.sid AND a.is_read=0 AND a.hidden=0),
                       (SELECT COUNT(*) FROM rss_articles a WHERE a.sid=f.sid)
                FROM rss_feeds f WHERE f.sid=$sid
                """;
            command.Parameters.AddWithValue("$sid", sid);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadFeed(reader, 0) : null;
        }
    }

    private static RssFeed ReadFeed(SqliteDataReader reader, int offset)
    {
        return new RssFeed
        {
            Sid = reader.GetInt32(offset + 0),
            Title = reader.GetString(offset + 1),
            Url = reader.GetString(offset + 2),
            Link = reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
            Description = reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4),
            Icon = reader.IsDBNull(offset + 5) ? null : (byte[])reader[offset + 5],
            IconUrl = reader.IsDBNull(offset + 6) ? null : reader.GetString(offset + 6),
            Gid = reader.IsDBNull(offset + 7) ? null : reader.GetInt32(offset + 7),
            Position = reader.GetInt32(offset + 8),
            ETag = reader.IsDBNull(offset + 9) ? null : reader.GetString(offset + 9),
            LastModified = reader.IsDBNull(offset + 10) ? null : reader.GetString(offset + 10),
            LastFetch = reader.GetInt64(offset + 11),
            LastError = reader.IsDBNull(offset + 12) ? null : reader.GetString(offset + 12),
            Hidden = reader.GetInt32(offset + 13) != 0,
            OpenMode = reader.GetInt32(offset + 14) == 1 ? RssOpenMode.Webpage : RssOpenMode.Content,
            Notify = reader.GetInt32(offset + 15) != 0,
            FetchLimit = reader.GetInt32(offset + 16),
            RemoteRef = reader.IsDBNull(offset + 17) ? null : reader.GetString(offset + 17),
            Unread = reader.GetInt32(offset + 18),
            Total = reader.GetInt32(offset + 19)
        };
    }

    /// <summary>添加订阅源；URL 已存在时返回已有 sid，并置 created=false。</summary>
    public int AddFeed(RssFeed feed, out bool created)
    {
        lock (_gate)
        {
            using var connection = Open();
            using (var existing = connection.CreateCommand())
            {
                existing.CommandText = "SELECT sid FROM rss_feeds WHERE url=$url";
                existing.Parameters.AddWithValue("$url", feed.Url);
                var result = existing.ExecuteScalar();
                if (result != null)
                {
                    created = false;
                    return Convert.ToInt32(result);
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO rss_feeds(title,url,link,description,icon,icon_url,gid,position,
                                      open_mode,notify,fetch_limit,remote_ref)
                VALUES($title,$url,$link,$description,$icon,$iconUrl,$gid,
                       (SELECT COALESCE(MAX(position),0)+1 FROM rss_feeds),
                       $openMode,$notify,$fetchLimit,$remoteRef);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(feed.Title) ? feed.Host : feed.Title);
            command.Parameters.AddWithValue("$url", feed.Url);
            command.Parameters.AddWithValue("$link", (object?)feed.Link ?? DBNull.Value);
            command.Parameters.AddWithValue("$description", (object?)feed.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("$icon", (object?)feed.Icon ?? DBNull.Value);
            command.Parameters.AddWithValue("$iconUrl", (object?)feed.IconUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$gid", (object?)feed.Gid ?? DBNull.Value);
            command.Parameters.AddWithValue("$openMode", (int)feed.OpenMode);
            command.Parameters.AddWithValue("$notify", feed.Notify ? 1 : 0);
            command.Parameters.AddWithValue("$fetchLimit", feed.FetchLimit);
            command.Parameters.AddWithValue("$remoteRef", (object?)feed.RemoteRef ?? DBNull.Value);
            created = true;
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public void UpdateFeedMeta(int sid, string? title, string? link, string? description, string? iconUrl)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE rss_feeds SET
                    title=CASE WHEN $title IS NULL OR $title='' THEN title ELSE $title END,
                    link=COALESCE($link,link),
                    description=COALESCE($description,description),
                    icon_url=COALESCE($iconUrl,icon_url)
                WHERE sid=$sid
                """;
            command.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
            command.Parameters.AddWithValue("$link", (object?)link ?? DBNull.Value);
            command.Parameters.AddWithValue("$description", (object?)description ?? DBNull.Value);
            command.Parameters.AddWithValue("$iconUrl", (object?)iconUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    public void UpdateFeedSettings(int sid, string title, int? gid, RssOpenMode openMode, bool notify, int fetchLimit)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE rss_feeds SET title=$title,gid=$gid,open_mode=$openMode,notify=$notify,fetch_limit=$fetchLimit
                WHERE sid=$sid
                """;
            command.Parameters.AddWithValue("$title", title.Trim());
            command.Parameters.AddWithValue("$gid", (object?)gid ?? DBNull.Value);
            command.Parameters.AddWithValue("$openMode", (int)openMode);
            command.Parameters.AddWithValue("$notify", notify ? 1 : 0);
            command.Parameters.AddWithValue("$fetchLimit", fetchLimit);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    public void UpdateFeedUrl(int sid, string url)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE rss_feeds SET url=$url,etag=NULL,last_modified=NULL,last_fetch=0
                WHERE sid=$sid
                """;
            command.Parameters.AddWithValue("$url", url);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    public void MoveFeed(int sid, int? gid)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_feeds SET gid=$gid WHERE sid=$sid";
            command.Parameters.AddWithValue("$gid", (object?)gid ?? DBNull.Value);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    public void SetFeedHidden(int sid, bool hidden)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_feeds SET hidden=$hidden WHERE sid=$sid";
            command.Parameters.AddWithValue("$hidden", hidden ? 1 : 0);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    public void DeleteFeed(int sid)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            foreach (var sql in new[]
            {
                "DELETE FROM rss_articles WHERE sid=$sid",
                "DELETE FROM rss_feeds WHERE sid=$sid"
            })
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$sid", sid);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public void UpdateFeedFetchState(int sid, string? etag, string? lastModified,
        long lastFetch, string? error, bool keepEtags = true)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE rss_feeds SET
                    etag=CASE WHEN $keep=1 THEN COALESCE($etag,etag) ELSE $etag END,
                    last_modified=CASE WHEN $keep=1 THEN COALESCE($lastModified,last_modified) ELSE $lastModified END,
                    last_fetch=$lastFetch,
                    last_error=$error
                WHERE sid=$sid
                """;
            command.Parameters.AddWithValue("$keep", keepEtags ? 1 : 0);
            command.Parameters.AddWithValue("$etag", (object?)etag ?? DBNull.Value);
            command.Parameters.AddWithValue("$lastModified", (object?)lastModified ?? DBNull.Value);
            command.Parameters.AddWithValue("$lastFetch", lastFetch);
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    public void SetFeedIcon(int sid, byte[]? icon)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_feeds SET icon=$icon WHERE sid=$sid";
            command.Parameters.AddWithValue("$icon", (object?)icon ?? DBNull.Value);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    // ================= 文章 =================

    public bool ArticleExists(int sid, string guid)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM rss_articles WHERE sid=$sid AND guid=$guid LIMIT 1";
            command.Parameters.AddWithValue("$sid", sid);
            command.Parameters.AddWithValue("$guid", guid);
            return command.ExecuteScalar() != null;
        }
    }

    public long InsertArticle(RssArticle article)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO rss_articles
                    (sid,guid,title,author,link,content,snippet,thumb,published,fetched,
                     is_read,starred,hidden,full_content,remote_ref,read_dirty,star_dirty)
                VALUES($sid,$guid,$title,$author,$link,$content,$snippet,$thumb,$published,$fetched,
                       $read,$starred,$hidden,$full,$remote,$readDirty,$starDirty)
                ON CONFLICT(sid,guid) DO NOTHING
                RETURNING aid;
                """;
            command.Parameters.AddWithValue("$sid", article.Sid);
            command.Parameters.AddWithValue("$guid", article.Guid);
            command.Parameters.AddWithValue("$title", article.Title ?? "");
            command.Parameters.AddWithValue("$author", (object?)article.Author ?? DBNull.Value);
            command.Parameters.AddWithValue("$link", (object?)article.Link ?? DBNull.Value);
            command.Parameters.AddWithValue("$content", (object?)article.Content ?? DBNull.Value);
            command.Parameters.AddWithValue("$snippet", (object?)article.Snippet ?? DBNull.Value);
            command.Parameters.AddWithValue("$thumb", (object?)article.Thumb ?? DBNull.Value);
            command.Parameters.AddWithValue("$published", article.Published);
            command.Parameters.AddWithValue("$fetched", article.Fetched);
            command.Parameters.AddWithValue("$read", article.IsRead ? 1 : 0);
            command.Parameters.AddWithValue("$starred", article.Starred ? 1 : 0);
            command.Parameters.AddWithValue("$hidden", article.Hidden ? 1 : 0);
            command.Parameters.AddWithValue("$full", (object?)article.FullContent ?? DBNull.Value);
            command.Parameters.AddWithValue("$remote", (object?)article.RemoteRef ?? DBNull.Value);
            command.Parameters.AddWithValue("$readDirty", article.ReadDirty ? 1 : 0);
            command.Parameters.AddWithValue("$starDirty", article.StarDirty ? 1 : 0);
            var result = command.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt64(result);
        }
    }

    public List<RssArticle> QueryArticles(RssQuery query)
    {
        lock (_gate)
        {
            using var connection = Open();
            var (where, parameters, isRegex) = BuildWhere(query);
            if (isRegex)
            {
                // 正则无法交给 SQLite：先取出该范围最近的文章，再在内存中匹配。
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT a.aid,a.sid,a.guid,a.title,a.author,a.link,a.content,a.snippet,a.thumb,
                           a.published,a.fetched,a.is_read,a.starred,a.hidden,a.full_content,a.remote_ref,
                           a.read_dirty,a.star_dirty,
                           f.title AS source_title,f.icon AS source_icon
                    FROM rss_articles a JOIN rss_feeds f ON f.sid=a.sid
                    {where}
                    ORDER BY a.published DESC,a.aid DESC
                    LIMIT 5000
                    """;
                foreach (var p in parameters)
                {
                    command.Parameters.AddWithValue(p.Key, p.Value);
                }
                using var reader = command.ExecuteReader();
                var all = new List<RssArticle>();
                while (reader.Read())
                {
                    all.Add(ReadArticle(reader));
                }
                var regex = BuildRegex(query);
                var filtered = regex == null
                    ? all
                    : all.Where(a => Matches(a, regex, query.SearchContent)).ToList();
                return filtered.Skip(query.Offset).Take(query.Limit).ToList();
            }

            using var pageCommand = connection.CreateCommand();
            pageCommand.CommandText = $"""
                SELECT a.aid,a.sid,a.guid,a.title,a.author,a.link,a.content,a.snippet,a.thumb,
                       a.published,a.fetched,a.is_read,a.starred,a.hidden,a.full_content,a.remote_ref,
                       a.read_dirty,a.star_dirty,
                       f.title AS source_title,f.icon AS source_icon
                FROM rss_articles a JOIN rss_feeds f ON f.sid=a.sid
                {where}
                ORDER BY a.published DESC,a.aid DESC
                LIMIT $limit OFFSET $offset
                """;
            foreach (var p in parameters)
            {
                pageCommand.Parameters.AddWithValue(p.Key, p.Value);
            }
            pageCommand.Parameters.AddWithValue("$limit", query.Limit);
            pageCommand.Parameters.AddWithValue("$offset", query.Offset);
            using var pageReader = pageCommand.ExecuteReader();
            var list = new List<RssArticle>();
            while (pageReader.Read())
            {
                list.Add(ReadArticle(pageReader));
            }
            return list;
        }
    }

    private static (string Where, List<KeyValuePair<string, object>> Parameters, bool IsRegex) BuildWhere(RssQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<KeyValuePair<string, object>>();
        switch (query.Scope.Kind)
        {
            case RssScopeKind.Feed:
                clauses.Add("a.sid=$scopeSid");
                parameters.Add(new("$scopeSid", query.Scope.Id));
                break;
            case RssScopeKind.Group:
                clauses.Add("a.sid IN (SELECT sid FROM rss_feeds WHERE gid=$scopeGid)");
                parameters.Add(new("$scopeGid", query.Scope.Id));
                break;
            case RssScopeKind.Unread:
                clauses.Add("a.is_read=0");
                break;
            case RssScopeKind.Starred:
                clauses.Add("a.starred=1");
                break;
            case RssScopeKind.Today:
                long today = new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds();
                clauses.Add("a.published>=$today");
                parameters.Add(new("$today", today));
                break;
        }
        if (query.UnreadOnly)
        {
            clauses.Add("a.is_read=0");
        }
        if (query.StarredOnly)
        {
            clauses.Add("a.starred=1");
        }
        if (!query.IncludeHidden)
        {
            clauses.Add("a.hidden=0");
            if (query.Scope.Kind is RssScopeKind.All or RssScopeKind.Unread
                or RssScopeKind.Starred or RssScopeKind.Today)
            {
                // 被隐藏的订阅源不出现在“全部 / 未读 / 星标 / 今天”等智能视图中
                clauses.Add("a.sid NOT IN (SELECT sid FROM rss_feeds WHERE hidden=1)");
            }
        }
        bool isRegex = query.UseRegex && !string.IsNullOrWhiteSpace(query.SearchText);
        if (!isRegex && !string.IsNullOrWhiteSpace(query.SearchText))
        {
            string like = "%" + query.SearchText
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_") + "%";
            if (query.SearchContent)
            {
                clauses.Add("(a.title LIKE $q ESCAPE '\\' OR a.content LIKE $q ESCAPE '\\' OR a.snippet LIKE $q ESCAPE '\\')");
            }
            else
            {
                clauses.Add("a.title LIKE $q ESCAPE '\\'");
            }
            parameters.Add(new("$q", like));
        }
        string where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        return (where, parameters, isRegex);
    }

    private static Regex? BuildRegex(RssQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.SearchText))
        {
            return null;
        }
        var options = RegexOptions.None;
        if (!query.CaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }
        try
        {
            return new Regex(query.SearchText, options, TimeSpan.FromSeconds(2));
        }
        catch
        {
            return null;
        }
    }

    private static bool Matches(RssArticle article, Regex regex, bool includeContent)
    {
        try
        {
            if (regex.IsMatch(article.Title ?? ""))
            {
                return true;
            }
            return includeContent && regex.IsMatch(RssParser.HtmlToText(article.Content ?? ""));
        }
        catch
        {
            return false;
        }
    }

    private static RssArticle ReadArticle(SqliteDataReader reader)
    {
        return new RssArticle
        {
            Aid = reader.GetInt64(0),
            Sid = reader.GetInt32(1),
            Guid = reader.GetString(2),
            Title = reader.GetString(3),
            Author = reader.IsDBNull(4) ? null : reader.GetString(4),
            Link = reader.IsDBNull(5) ? null : reader.GetString(5),
            Content = reader.IsDBNull(6) ? null : reader.GetString(6),
            Snippet = reader.IsDBNull(7) ? null : reader.GetString(7),
            Thumb = reader.IsDBNull(8) ? null : reader.GetString(8),
            Published = reader.GetInt64(9),
            Fetched = reader.GetInt64(10),
            IsRead = reader.GetInt32(11) != 0,
            Starred = reader.GetInt32(12) != 0,
            Hidden = reader.GetInt32(13) != 0,
            FullContent = reader.IsDBNull(14) ? null : reader.GetString(14),
            RemoteRef = reader.IsDBNull(15) ? null : reader.GetString(15),
            ReadDirty = reader.GetInt32(16) != 0,
            StarDirty = reader.GetInt32(17) != 0,
            SourceTitle = reader.IsDBNull(18) ? "" : reader.GetString(18),
            SourceIcon = reader.IsDBNull(19) ? null : (byte[])reader[19]
        };
    }

    public RssArticle? GetArticle(long aid)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.aid,a.sid,a.guid,a.title,a.author,a.link,a.content,a.snippet,a.thumb,
                       a.published,a.fetched,a.is_read,a.starred,a.hidden,a.full_content,a.remote_ref,
                       a.read_dirty,a.star_dirty,
                       f.title AS source_title,f.icon AS source_icon
                FROM rss_articles a JOIN rss_feeds f ON f.sid=a.sid
                WHERE a.aid=$aid
                """;
            command.Parameters.AddWithValue("$aid", aid);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadArticle(reader) : null;
        }
    }

    public void SaveFullContent(long aid, string html)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_articles SET full_content=$html WHERE aid=$aid";
            command.Parameters.AddWithValue("$html", html);
            command.Parameters.AddWithValue("$aid", aid);
            command.ExecuteNonQuery();
        }
    }

    public void MarkRead(long aid, bool read, bool dirty = true)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_articles SET is_read=$read,read_dirty=$dirty WHERE aid=$aid";
            command.Parameters.AddWithValue("$read", read ? 1 : 0);
            command.Parameters.AddWithValue("$dirty", dirty ? 1 : 0);
            command.Parameters.AddWithValue("$aid", aid);
            command.ExecuteNonQuery();
        }
    }

    public void MarkStar(long aid, bool starred, bool dirty = true)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_articles SET starred=$starred,star_dirty=$dirty WHERE aid=$aid";
            command.Parameters.AddWithValue("$starred", starred ? 1 : 0);
            command.Parameters.AddWithValue("$dirty", dirty ? 1 : 0);
            command.Parameters.AddWithValue("$aid", aid);
            command.ExecuteNonQuery();
        }
    }

    public void SetHidden(long aid, bool hidden)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_articles SET hidden=$hidden WHERE aid=$aid";
            command.Parameters.AddWithValue("$hidden", hidden ? 1 : 0);
            command.Parameters.AddWithValue("$aid", aid);
            command.ExecuteNonQuery();
        }
    }

    public void MarkRangeRead(IReadOnlyList<long> aids, bool read)
    {
        if (aids.Count == 0)
        {
            return;
        }
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            for (int offset = 0; offset < aids.Count; offset += 500)
            {
                var chunk = aids.Skip(offset).Take(500).ToArray();
                var names = chunk.Select((_, i) => "$p" + i).ToArray();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    $"UPDATE rss_articles SET is_read=$read,read_dirty=1 WHERE aid IN ({string.Join(",", names)})";
                command.Parameters.AddWithValue("$read", read ? 1 : 0);
                for (int i = 0; i < chunk.Length; i++)
                {
                    command.Parameters.AddWithValue(names[i], chunk[i]);
                }
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public void MarkAllRead(RssScope scope)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            string where = scope.Kind switch
            {
                RssScopeKind.Feed => "sid=$id",
                RssScopeKind.Group => "sid IN (SELECT sid FROM rss_feeds WHERE gid=$id)",
                RssScopeKind.Starred => "starred=1",
                RssScopeKind.Today => "published>=$id",
                _ => "1=1"
            };
            command.CommandText = $"UPDATE rss_articles SET is_read=1,read_dirty=1 WHERE {where}";
            if (scope.Kind is RssScopeKind.Feed or RssScopeKind.Group)
            {
                command.Parameters.AddWithValue("$id", scope.Id);
            }
            else if (scope.Kind == RssScopeKind.Today)
            {
                command.Parameters.AddWithValue("$id",
                    new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds());
            }
            command.ExecuteNonQuery();
        }
    }

    public void EnforceRetention(int sid, int maxArticles)
    {
        if (maxArticles <= 0)
        {
            return;
        }
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM rss_articles
                WHERE sid=$sid AND starred=0 AND aid NOT IN (
                    SELECT aid FROM rss_articles WHERE sid=$sid ORDER BY published DESC,aid DESC LIMIT $max
                )
                """;
            command.Parameters.AddWithValue("$sid", sid);
            command.Parameters.AddWithValue("$max", maxArticles);
            command.ExecuteNonQuery();
        }
    }

    public int DeleteArticlesOlderThan(int days)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM rss_articles WHERE published<$cutoff AND starred=0";
            command.Parameters.AddWithValue("$cutoff",
                DateTimeOffset.UtcNow.AddDays(-Math.Max(0, days)).ToUnixTimeSeconds());
            return command.ExecuteNonQuery();
        }
    }

    public int DeleteAllArticles()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM rss_articles";
            int count = command.ExecuteNonQuery();
            using var reset = connection.CreateCommand();
            reset.CommandText = "UPDATE rss_feeds SET etag=NULL,last_modified=NULL,last_fetch=0";
            reset.ExecuteNonQuery();
            return count;
        }
    }

    // ================= 备份 / 还原 =================

    /// <summary>用 SQLite 在线备份接口复制数据库，避免直接拷贝 WAL 文件导致数据不一致。</summary>
    public void BackupDatabaseTo(string path)
    {
        lock (_gate)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            using var source = Open();
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                // 关闭连接池，确保备份文件句柄在 Dispose 后立即释放，
                // 否则随后的 ZIP 打包会提示“文件正被另一个进程使用”。
                Pooling = false
            };
            using var destination = new SqliteConnection(builder.ToString());
            destination.Open();
            source.BackupDatabase(destination);
        }
    }

    /// <summary>从备份数据库还原全部订阅与文章。</summary>
    public void RestoreDatabaseFrom(string path)
    {
        lock (_gate)
        {
            SqliteConnection.ClearAllPools();
            var readBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            using var source = new SqliteConnection(readBuilder.ToString());
            source.Open();
            using var destination = Open();
            source.BackupDatabase(destination);
        }
    }

    public void ClearFullContentCache()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_articles SET full_content=NULL; VACUUM;";
            command.ExecuteNonQuery();
        }
    }

    public long GetDatabaseSize()
    {
        try
        {
            return new FileInfo(_dbPath).Length;
        }
        catch
        {
            return 0;
        }
    }

    // ================= 统计 =================

    public (int All, int Today, int Starred) GetSmartCounts()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM rss_articles WHERE is_read=0 AND hidden=0
                        AND sid NOT IN (SELECT sid FROM rss_feeds WHERE hidden=1)) AS unread,
                    (SELECT COUNT(*) FROM rss_articles WHERE published>=$today AND hidden=0
                        AND sid NOT IN (SELECT sid FROM rss_feeds WHERE hidden=1)) AS today,
                    (SELECT COUNT(*) FROM rss_articles WHERE starred=1 AND hidden=0
                        AND sid NOT IN (SELECT sid FROM rss_feeds WHERE hidden=1)) AS starred
                """;
            command.Parameters.AddWithValue("$today",
                new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds());
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return (0, 0, 0);
            }
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }
    }

    public int GetUnreadCount(RssScope scope)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            string where = scope.Kind switch
            {
                RssScopeKind.Feed => "sid=$id",
                RssScopeKind.Group => "sid IN (SELECT sid FROM rss_feeds WHERE gid=$id)",
                RssScopeKind.Starred => "starred=1",
                RssScopeKind.Today => "published>=$id",
                _ => "1=1"
            };
            command.CommandText = $"SELECT COUNT(*) FROM rss_articles WHERE is_read=0 AND hidden=0 AND {where}";
            if (scope.Kind is RssScopeKind.All or RssScopeKind.Unread
                or RssScopeKind.Starred or RssScopeKind.Today)
            {
                command.CommandText += " AND sid NOT IN (SELECT sid FROM rss_feeds WHERE hidden=1)";
            }
            if (scope.Kind is RssScopeKind.Feed or RssScopeKind.Group)
            {
                command.Parameters.AddWithValue("$id", scope.Id);
            }
            else if (scope.Kind == RssScopeKind.Today)
            {
                command.Parameters.AddWithValue("$id",
                    new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds());
            }
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    // ================= 服务端同步辅助 =================

    public List<RssArticle> GetDirtyArticles(int limit = 200)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.aid,a.sid,a.guid,a.title,a.author,a.link,a.content,a.snippet,a.thumb,
                       a.published,a.fetched,a.is_read,a.starred,a.hidden,a.full_content,a.remote_ref,
                       a.read_dirty,a.star_dirty,
                       f.title AS source_title,f.icon AS source_icon
                FROM rss_articles a JOIN rss_feeds f ON f.sid=a.sid
                WHERE (a.read_dirty=1 OR a.star_dirty=1) AND a.remote_ref IS NOT NULL
                ORDER BY a.aid DESC LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var list = new List<RssArticle>();
            while (reader.Read())
            {
                list.Add(ReadArticle(reader));
            }
            return list;
        }
    }

    public void ClearDirty(long aid)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_articles SET read_dirty=0,star_dirty=0 WHERE aid=$aid";
            command.Parameters.AddWithValue("$aid", aid);
            command.ExecuteNonQuery();
        }
    }

    public RssFeed? FindFeedByRemoteRef(string remoteRef)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT f.sid,f.title,f.url,f.link,f.description,f.icon,f.icon_url,f.gid,f.position,
                       f.etag,f.last_modified,f.last_fetch,f.last_error,f.hidden,f.open_mode,f.notify,
                       f.fetch_limit,f.remote_ref,0,0
                FROM rss_feeds f WHERE f.remote_ref=$ref LIMIT 1
                """;
            command.Parameters.AddWithValue("$ref", remoteRef);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadFeed(reader, 0) : null;
        }
    }

    public void SetFeedRemoteRef(int sid, string? remoteRef)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_feeds SET remote_ref=$ref WHERE sid=$sid";
            command.Parameters.AddWithValue("$ref", (object?)remoteRef ?? DBNull.Value);
            command.Parameters.AddWithValue("$sid", sid);
            command.ExecuteNonQuery();
        }
    }

    public void UpdateArticleStates(string? remoteRef, bool? read, bool? starred)
    {
        if (string.IsNullOrEmpty(remoteRef))
        {
            return;
        }
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var sets = new List<string>();
            if (read.HasValue)
            {
                sets.Add("is_read=$read");
            }
            if (starred.HasValue)
            {
                sets.Add("starred=$starred");
            }
            if (sets.Count == 0)
            {
                return;
            }
            command.CommandText = $"UPDATE rss_articles SET {string.Join(",", sets)} WHERE remote_ref=$ref";
            if (read.HasValue)
            {
                command.Parameters.AddWithValue("$read", read.Value ? 1 : 0);
            }
            if (starred.HasValue)
            {
                command.Parameters.AddWithValue("$starred", starred.Value ? 1 : 0);
            }
            command.Parameters.AddWithValue("$ref", remoteRef);
            command.ExecuteNonQuery();
        }
    }
}
