using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace MiniMonitor.Web.YamlMind;

/// <summary>
/// YamlMind 的 SQLite 存储层（原生 C# 复刻）。
/// 数据结构与 Python 版保持一致（docs / doc_bodies / doc_trees / doc_versions / settings / jobs / meta），
/// 因此可以直接打开已有的「文档\yamlmind\mind.db」。
/// </summary>
public sealed class YamlMindStore : IDisposable
{
    public const int SchemaVersion = 5;
    public const int MaxVersionsPerDoc = 50;
    public const int VersionTimeGapSeconds = 120;
    public const int AutoBackupKeep = 10;

    public static readonly IReadOnlyDictionary<string, string> DefaultSettings = new Dictionary<string, string>
    {
        ["lock_mode"] = "on",
        ["theme"] = "dark",
        ["layout"] = "radial",
        ["auto_save"] = "on",
        ["guide_lines"] = "on",
        ["font_size"] = "14"
    };

    public static readonly (int Hour, int Minute) OptimizeAt = (1, 30);
    public static readonly (int Hour, int Minute) RelockAt = (0, 10);

    private readonly object _sync = new();
    private SqliteConnection? _conn;

    public string DataDir { get; }
    public string BackupDir { get; }
    public string DbPath { get; }

    public YamlMindStore(string? dataDir = null)
    {
        DataDir = string.IsNullOrWhiteSpace(dataDir) ? ResolveDefaultDataDir() : Path.GetFullPath(dataDir);
        BackupDir = Path.Combine(DataDir, "backups");
        DbPath = Path.Combine(DataDir, "mind.db");
    }

    public static string ResolveDefaultDataDir()
    {
        string? env = Environment.GetEnvironmentVariable("YAMLMIND_DATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }
        // 统一存储：文档\MiniMonitor\YamlMind（老版本 yamlmind 数据由 AppPaths 启动时自动迁移）。
        return AppPaths.Sub("YamlMind");
    }

    public void Initialize()
    {
        lock (_sync)
        {
            if (_conn != null)
            {
                return;
            }
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(BackupDir);
            bool fresh = !File.Exists(DbPath) || new FileInfo(DbPath).Length == 0;
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            };
            _conn = new SqliteConnection(builder.ToString());
            _conn.Open();
            if (fresh)
            {
                ExecRaw($"PRAGMA page_size=8192");
            }
            ExecRaw("PRAGMA journal_mode=WAL");
            ExecRaw("PRAGMA synchronous=NORMAL");
            ExecRaw("PRAGMA auto_vacuum=INCREMENTAL");
            ExecRaw("PRAGMA temp_store=MEMORY");
            ExecRaw("PRAGMA cache_size=-131072");
            ExecRaw("PRAGMA mmap_size=536870912");
            ExecRaw("PRAGMA busy_timeout=60000");
            ExecRaw("PRAGMA foreign_keys=ON");
            CreateSchema();
            Migrate();
            using (var tx = BeginTx())
            {
                if (ScalarRaw("SELECT value FROM meta WHERE key='schema'") == null)
                {
                    Exec("INSERT INTO meta(key,value) VALUES('schema',@v)", tx, ("@v", SchemaVersion.ToString()));
                }
                foreach (var pair in DefaultSettings)
                {
                    Exec("INSERT INTO settings(key,value,updated_at) VALUES(@k,@v,@t) ON CONFLICT(key) DO NOTHING",
                        tx, ("@k", pair.Key), ("@v", pair.Value), ("@t", NowMs()));
                }
                tx.Commit();
            }
            Seed();
        }
    }

    private static readonly string[] BaseSchema =
    {
        @"CREATE TABLE IF NOT EXISTS docs (
            id            TEXT PRIMARY KEY,
            title         TEXT NOT NULL,
            bytes         INTEGER NOT NULL DEFAULT 0,
            node_count    INTEGER NOT NULL DEFAULT 0,
            max_depth     INTEGER NOT NULL DEFAULT 0,
            branch_count  INTEGER NOT NULL DEFAULT 0,
            valid         INTEGER NOT NULL DEFAULT 1,
            error         TEXT,
            revision      INTEGER NOT NULL DEFAULT 0,
            preview       TEXT NOT NULL DEFAULT '',
            created_at    INTEGER NOT NULL,
            updated_at    INTEGER NOT NULL,
            opened_at     INTEGER NOT NULL DEFAULT 0)",

        "CREATE INDEX IF NOT EXISTS idx_docs_updated ON docs(updated_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_docs_created ON docs(created_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_docs_title ON docs(title COLLATE NOCASE)",

        @"CREATE TABLE IF NOT EXISTS doc_bodies (
            doc_id  TEXT PRIMARY KEY,
            content TEXT NOT NULL)",

        @"CREATE TABLE IF NOT EXISTS doc_trees (
            doc_id    TEXT PRIMARY KEY,
            tree_json TEXT NOT NULL,
            built_at  INTEGER NOT NULL)",

        @"CREATE TABLE IF NOT EXISTS doc_versions (
            vid        INTEGER PRIMARY KEY AUTOINCREMENT,
            doc_id     TEXT NOT NULL,
            revision   INTEGER NOT NULL,
            title      TEXT NOT NULL,
            payload    BLOB NOT NULL,
            raw_bytes  INTEGER NOT NULL,
            created_at INTEGER NOT NULL)",
        "CREATE INDEX IF NOT EXISTS idx_versions_doc ON doc_versions(doc_id, vid DESC)",

        @"CREATE TABLE IF NOT EXISTS settings (
            key        TEXT PRIMARY KEY,
            value      TEXT NOT NULL,
            updated_at INTEGER NOT NULL
        ) WITHOUT ROWID",

        @"CREATE TABLE IF NOT EXISTS jobs (
            job_id     INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL,
            started_at INTEGER NOT NULL,
            finished_at INTEGER,
            status     TEXT NOT NULL DEFAULT 'running',
            detail     TEXT
        )",
        "CREATE INDEX IF NOT EXISTS idx_jobs_name ON jobs(name, started_at DESC)",

        @"CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID"
    };

    private void CreateSchema()
    {
        foreach (string sql in BaseSchema)
        {
            ExecRaw(sql);
        }
        try
        {
            ExecRaw("CREATE VIRTUAL TABLE IF NOT EXISTS docs_fts USING fts5(title, digest, doc_id UNINDEXED, tokenize='trigram')");
            ExecRaw("CREATE VIRTUAL TABLE IF NOT EXISTS docs_big USING fts5(tokens, doc_id UNINDEXED)");
        }
        catch
        {
            // 个别 SQLite 构建不带 trigram 分词器：搜索走正文 LIKE 扫描，不影响其它功能
        }
    }

    private void Migrate()
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = Cmd("PRAGMA table_info(docs)"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }
        if (!columns.Contains("preview"))
        {
            ExecRaw("ALTER TABLE docs ADD COLUMN preview TEXT NOT NULL DEFAULT ''");
        }
    }

    private const string DemoTemplate = """
# 这是一份 YAML 思维导图文档
# 编辑器会自动保存(也可 Ctrl+S)，点右上角「展示思维导图」即可在新标签页查看
产品规划:
  市场分析:
    - 竞品拆解: 5 家
    - 用户画像:
        核心人群: 中小团队
        付费意愿: 中高
    - 渠道策略:
        - title: 线上
          children: [内容社区, 搜索广告, 开发者社区]
        - title: 线下
          children: [行业展会, 渠道代理]
  产品能力:
    YAML 编辑:
      语法高亮: 已支持
      对齐虚线: 已支持
      自动保存: 已支持
    思维导图:
      放射布局: 左右均衡分布
      树形布局: 向右展开
      自动编号: 按层级与书写顺序
  里程碑:
    - Q1: 原型验证
    - Q2: 正式版本
    - Q3: 企业版
团队与预算:
  团队: [产品 2 人, 研发 4 人, 设计 1 人]
  预算: 120 万
  风险:
    人力: 招聘周期偏长
    技术: 大文档渲染性能
""";

    private void Seed()
    {
        if (CountDocsInternal("") != 0)
        {
            return;
        }
        // 内置案例只保留一个，首次启动时写入
        CreateDoc("欢迎 · YAML 思维导图", DemoTemplate);
    }

    // ============================== 通用 SQL ==============================

    private SqliteConnection Conn => _conn ?? throw new InvalidOperationException("数据库尚未初始化");

    private SqliteCommand Cmd(string sql, SqliteTransaction? tx = null)
    {
        var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        if (tx != null)
        {
            cmd.Transaction = tx;
        }
        return cmd;
    }

    private SqliteTransaction BeginTx() => Conn.BeginTransaction();

    private void ExecRaw(string sql)
    {
        using var cmd = Cmd(sql);
        cmd.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand cmd, params (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private int Exec(string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(sql, tx);
        Bind(cmd, args);
        return cmd.ExecuteNonQuery();
    }

    private long Insert(string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(sql, tx);
        Bind(cmd, args);
        cmd.ExecuteNonQuery();
        using var idCmd = Cmd("SELECT last_insert_rowid()", tx);
        return Convert.ToInt64(idCmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private object? ScalarRaw(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(sql);
        Bind(cmd, args);
        object? value = cmd.ExecuteScalar();
        return value == DBNull.Value ? null : value;
    }

    private long ScalarLong(string sql, params (string Name, object? Value)[] args)
    {
        object? value = ScalarRaw(sql, args);
        return value == null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private string ScalarString(string sql, params (string Name, object? Value)[] args)
    {
        object? value = ScalarRaw(sql, args);
        return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private List<Dictionary<string, object?>> Query(string sql, params (string Name, object? Value)[] args)
    {
        var rows = new List<Dictionary<string, object?>>();
        using var cmd = Cmd(sql);
        Bind(cmd, args);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    private Dictionary<string, object?>? QueryOne(string sql, params (string Name, object? Value)[] args)
    {
        List<Dictionary<string, object?>> rows = Query(sql, args);
        return rows.Count > 0 ? rows[0] : null;
    }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string? Str(Dictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) && value != null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;
    }

    private static long Long(Dictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) && value != null
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : 0L;
    }

    // ============================== 设置 ==============================

    public Dictionary<string, string> GetSettings()
    {
        lock (_sync)
        {
            var settings = new Dictionary<string, string>(DefaultSettings, StringComparer.Ordinal);
            foreach (var row in Query("SELECT key,value FROM settings"))
            {
                settings[Str(row, "key") ?? ""] = Str(row, "value") ?? "";
            }
            return settings;
        }
    }

    public void PutSetting(string key, string value)
    {
        lock (_sync)
        {
            using var tx = BeginTx();
            Exec("INSERT INTO settings(key,value,updated_at) VALUES(@k,@v,@t) " +
                 "ON CONFLICT(key) DO UPDATE SET value=excluded.value, updated_at=excluded.updated_at",
                tx, ("@k", key), ("@v", value), ("@t", NowMs()));
            tx.Commit();
        }
    }

    public bool LockModeOn()
    {
        Dictionary<string, string> settings = GetSettings();
        return settings.TryGetValue("lock_mode", out string? mode) && mode == "on";
    }

    // ============================== 文档 ==============================

    private const string MetaCols =
        "id,title,bytes,node_count,max_depth,branch_count,valid,error,revision,preview,created_at,updated_at,opened_at";

    private static readonly Dictionary<string, string> Sorts = new(StringComparer.Ordinal)
    {
        ["updated"] = "updated_at DESC, id",
        ["created"] = "created_at DESC, id",
        ["title"] = "title COLLATE NOCASE ASC, id",
        ["size"] = "bytes DESC, id",
        ["nodes"] = "node_count DESC, id"
    };

    private static string NewId() => Guid.NewGuid().ToString("N")[..16];

    public List<Dictionary<string, object?>> ListDocs(string sort = "updated", string search = "", int limit = 40,
        int offset = 0)
    {
        lock (_sync)
        {
            string order = Sorts.TryGetValue(sort, out string? o) ? o : Sorts["updated"];
            limit = Math.Max(1, Math.Min(limit, 500));
            offset = Math.Max(0, offset);
            search = (search ?? "").Trim();
            if (search.Length == 0)
            {
                return Query($"SELECT {MetaCols} FROM docs ORDER BY {order} LIMIT @l OFFSET @o",
                    ("@l", limit), ("@o", offset));
            }
            string pattern = "%" + EscapeLike(search) + "%";
            return Query(
                $"SELECT d.{MetaCols.Replace(",", ",d.")} FROM docs d " +
                "LEFT JOIN doc_bodies b ON b.doc_id=d.id " +
                "WHERE d.title LIKE @p ESCAPE '\\' OR b.content LIKE @p ESCAPE '\\' " +
                $"ORDER BY d.{order} LIMIT @l OFFSET @o",
                ("@p", pattern), ("@l", limit), ("@o", offset));
        }
    }

    private static string EscapeLike(string text)
    {
        return text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    public int CountDocs(string search = "")
    {
        lock (_sync)
        {
            return CountDocsInternal(search);
        }
    }

    private int CountDocsInternal(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return (int)ScalarLong("SELECT COUNT(*) FROM docs");
        }
        string pattern = "%" + EscapeLike(search.Trim()) + "%";
        return (int)ScalarLong(
            "SELECT COUNT(*) FROM docs d LEFT JOIN doc_bodies b ON b.doc_id=d.id " +
            "WHERE d.title LIKE @p ESCAPE '\\' OR b.content LIKE @p ESCAPE '\\'", ("@p", pattern));
    }

    public Dictionary<string, object?>? GetMeta(string docId)
    {
        lock (_sync)
        {
            return QueryOne($"SELECT {MetaCols} FROM docs WHERE id=@id", ("@id", docId));
        }
    }

    public string GetBody(string docId)
    {
        lock (_sync)
        {
            return ScalarString("SELECT content FROM doc_bodies WHERE doc_id=@id", ("@id", docId));
        }
    }

    public Dictionary<string, object?>? GetDoc(string docId, bool withBody = true)
    {
        lock (_sync)
        {
            var meta = QueryOne($"SELECT {MetaCols} FROM docs WHERE id=@id", ("@id", docId));
            if (meta == null)
            {
                return null;
            }
            if (withBody)
            {
                meta["content"] = ScalarString("SELECT content FROM doc_bodies WHERE doc_id=@id", ("@id", docId));
            }
            return meta;
        }
    }

    public void TouchOpened(string docId)
    {
        lock (_sync)
        {
            Exec("UPDATE docs SET opened_at=@t WHERE id=@id", null, ("@t", NowMs()), ("@id", docId));
        }
    }

    public Dictionary<string, object?> CreateDoc(string title, string content = "", string? template = null)
    {
        lock (_sync)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0)
            {
                title = "未命名导图";
            }
            if (content.Length == 0 && template != null)
            {
                content = template;
            }
            var parsed = YamlMindTree.Parse(content, title);
            string docId = NewId();
            long ts = NowMs();
            string? errorJson = parsed.Error != null
                ? parsed.Error.ToJsonString(HttpResponse.JsonOptions)
                : null;
            string treeJson = YamlMindTree.Slim(parsed).ToJsonString(HttpResponse.JsonOptions);
            string preview = YamlMindTree.Preview(parsed);
            using var tx = BeginTx();
            Exec("INSERT INTO docs(id,title,bytes,node_count,max_depth,branch_count,valid,error,revision,preview," +
                 "created_at,updated_at,opened_at) VALUES(@id,@title,@bytes,@nodes,@depth,@branch,@valid,@err,0,@preview,@c,@u,@o)",
                tx,
                ("@id", docId), ("@title", title), ("@bytes", Encoding.UTF8.GetByteCount(content)),
                ("@nodes", parsed.Nodes), ("@depth", parsed.MaxDepth), ("@branch", parsed.Branches),
                ("@valid", parsed.Ok ? 1 : 0), ("@err", errorJson), ("@preview", preview),
                ("@c", ts), ("@u", ts), ("@o", ts));
            Exec("INSERT INTO doc_bodies(doc_id,content) VALUES(@id,@content)", tx, ("@id", docId), ("@content", content));
            Exec("INSERT INTO doc_trees(doc_id,tree_json,built_at) VALUES(@id,@tree,@t)", tx,
                ("@id", docId), ("@tree", treeJson), ("@t", ts));
            tx.Commit();
            var created = QueryOne($"SELECT {MetaCols} FROM docs WHERE id=@id", ("@id", docId))!;
            created["content"] = content;
            return created;
        }
    }

    public Dictionary<string, object?> SaveDoc(string docId, string? content = null, string? title = null,
        int? expectRevision = null, bool snapshot = true)
    {
        lock (_sync)
        {
            var meta = QueryOne($"SELECT {MetaCols} FROM docs WHERE id=@id", ("@id", docId))
                       ?? throw new KeyNotFoundException(docId);
            long ts = NowMs();
            string oldTitle = Str(meta, "title") ?? "";
            string newTitle = title != null ? (title.Trim().Length > 0 ? title.Trim() : oldTitle) : oldTitle;
            string existingBody = ScalarString("SELECT content FROM doc_bodies WHERE doc_id=@id", ("@id", docId));
            string body = content ?? existingBody;
            bool conflict = expectRevision.HasValue && expectRevision.Value != Long(meta, "revision");
            string? previousBody = snapshot ? existingBody : null;

            var parsed = YamlMindTree.Parse(body, newTitle);
            string? errorJson = parsed.Error != null
                ? parsed.Error.ToJsonString(HttpResponse.JsonOptions)
                : null;
            string treeJson = YamlMindTree.Slim(parsed).ToJsonString(HttpResponse.JsonOptions);
            string preview = YamlMindTree.Preview(parsed);
            int revision;
            using (var tx = BeginTx())
            {
                Exec("UPDATE docs SET title=@title, bytes=@bytes, node_count=@nodes, max_depth=@depth, " +
                     "branch_count=@branch, valid=@valid, error=@err, preview=@preview, revision=revision+1, " +
                     "updated_at=@u WHERE id=@id", tx,
                    ("@title", newTitle), ("@bytes", Encoding.UTF8.GetByteCount(body)), ("@nodes", parsed.Nodes),
                    ("@depth", parsed.MaxDepth), ("@branch", parsed.Branches), ("@valid", parsed.Ok ? 1 : 0),
                    ("@err", errorJson), ("@preview", preview), ("@u", ts), ("@id", docId));
                Exec("INSERT INTO doc_bodies(doc_id,content) VALUES(@id,@content) " +
                     "ON CONFLICT(doc_id) DO UPDATE SET content=excluded.content", tx,
                    ("@id", docId), ("@content", body));
                Exec("INSERT INTO doc_trees(doc_id,tree_json,built_at) VALUES(@id,@tree,@t) " +
                     "ON CONFLICT(doc_id) DO UPDATE SET tree_json=excluded.tree_json, built_at=excluded.built_at",
                    tx, ("@id", docId), ("@tree", treeJson), ("@t", ts));
                revision = (int)ScalarLong("SELECT revision FROM docs WHERE id=@id", ("@id", docId));
                if (snapshot && previousBody != null && previousBody != body && ShouldSnapshot(docId))
                {
                    byte[] compressed = ZlibCompress(Encoding.UTF8.GetBytes(previousBody));
                    Exec("INSERT INTO doc_versions(doc_id,revision,title,payload,raw_bytes,created_at) " +
                         "VALUES(@id,@rev,@title,@payload,@raw,@t)", tx,
                        ("@id", docId), ("@rev", revision), ("@title", newTitle), ("@payload", compressed),
                        ("@raw", Encoding.UTF8.GetByteCount(previousBody)), ("@t", ts));
                }
                PruneVersions(docId, tx);
                tx.Commit();
            }
            var output = QueryOne($"SELECT {MetaCols} FROM docs WHERE id=@id", ("@id", docId)) ?? new();
            output["content"] = body;
            output["revision_conflict"] = conflict;
            output["parsed_ok"] = parsed.Ok;
            return output;
        }
    }

    private bool ShouldSnapshot(string docId)
    {
        object? last = ScalarRaw("SELECT created_at FROM doc_versions WHERE doc_id=@id ORDER BY vid DESC LIMIT 1",
            ("@id", docId));
        if (last == null)
        {
            return true;
        }
        return NowMs() - Convert.ToInt64(last, CultureInfo.InvariantCulture) >= VersionTimeGapSeconds * 1000L;
    }

    private void PruneVersions(string docId, SqliteTransaction tx)
    {
        var vids = new List<long>();
        using (var cmd = Cmd("SELECT vid FROM doc_versions WHERE doc_id=@id ORDER BY vid DESC", tx))
        {
            Bind(cmd, ("@id", docId));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                vids.Add(reader.GetInt64(0));
            }
        }
        for (int i = MaxVersionsPerDoc; i < vids.Count; i++)
        {
            Exec("DELETE FROM doc_versions WHERE vid=@v", tx, ("@v", vids[i]));
        }
    }

    public bool DeleteDoc(string docId)
    {
        if (LockModeOn())
        {
            throw new PermissionDeniedException("locked");
        }
        lock (_sync)
        {
            using var tx = BeginTx();
            int changed = Exec("DELETE FROM docs WHERE id=@id", tx, ("@id", docId));
            Exec("DELETE FROM doc_bodies WHERE doc_id=@id", tx, ("@id", docId));
            Exec("DELETE FROM doc_trees WHERE doc_id=@id", tx, ("@id", docId));
            Exec("DELETE FROM doc_versions WHERE doc_id=@id", tx, ("@id", docId));
            try
            {
                Exec("DELETE FROM docs_fts WHERE doc_id=@id", tx, ("@id", docId));
                Exec("DELETE FROM docs_big WHERE doc_id=@id", tx, ("@id", docId));
            }
            catch
            {
                // FTS 表可能不存在
            }
            tx.Commit();
            return changed > 0;
        }
    }

    public Dictionary<string, object?> DuplicateDoc(string docId, string? newTitle = null)
    {
        var src = GetDoc(docId);
        if (src == null)
        {
            throw new KeyNotFoundException(docId);
        }
        string title = newTitle ?? $"{Str(src, "title")} 副本";
        return CreateDoc(title, GetBody(docId));
    }

    public JsonObject? GetTree(string docId, bool force = false)
    {
        lock (_sync)
        {
            if (!force)
            {
                string cached = ScalarString("SELECT tree_json FROM doc_trees WHERE doc_id=@id", ("@id", docId));
                if (cached.Length > 0)
                {
                    try
                    {
                        if (JsonNode.Parse(cached) is JsonObject cachedObj)
                        {
                            return cachedObj;
                        }
                    }
                    catch
                    {
                        // 缓存损坏，重建
                    }
                }
            }
            var body = QueryOne("SELECT content FROM doc_bodies WHERE doc_id=@id", ("@id", docId));
            if (body == null)
            {
                return null;
            }
            string title = ScalarString("SELECT title FROM docs WHERE id=@id", ("@id", docId));
            string content = Str(body, "content") ?? "";
            var parsed = YamlMindTree.Parse(content, title);
            string treeJson = parsed.ToFullJson().ToJsonString(HttpResponse.JsonOptions);
            using var tx = BeginTx();
            Exec("INSERT INTO doc_trees(doc_id,tree_json,built_at) VALUES(@id,@tree,@t) " +
                 "ON CONFLICT(doc_id) DO UPDATE SET tree_json=excluded.tree_json, built_at=excluded.built_at",
                tx, ("@id", docId), ("@tree", treeJson), ("@t", NowMs()));
            tx.Commit();
            return JsonNode.Parse(treeJson) as JsonObject;
        }
    }

    /// <summary>把 JsonObject 转成可再次序列化的对象树（保持字段顺序与类型）。</summary>
    public static Dictionary<string, object?> JsonObjectToDictionary(JsonObject obj)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in obj)
        {
            dict[pair.Key] = JsonToObject(pair.Value);
        }
        return dict;
    }

    public static List<object?> JsonArrayToList(JsonArray arr)
    {
        var list = new List<object?>();
        foreach (JsonNode? node in arr)
        {
            list.Add(JsonToObject(node));
        }
        return list;
    }

    private static object? JsonToObject(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject obj => JsonObjectToDictionary(obj),
            JsonArray arr => JsonArrayToList(arr),
            JsonValue value => value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Number => value.TryGetValue<long>(out long l) ? l : value.GetValue<double>(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            },
            _ => null
        };
    }

    // ============================== 历史版本 ==============================

    public List<Dictionary<string, object?>> Versions(string docId, int limit = 30)
    {
        lock (_sync)
        {
            return Query("SELECT vid,revision,title,raw_bytes,created_at FROM doc_versions " +
                         "WHERE doc_id=@id ORDER BY vid DESC LIMIT @l", ("@id", docId), ("@l", limit));
        }
    }

    public Dictionary<string, object?>? VersionContent(long vid)
    {
        lock (_sync)
        {
            var row = QueryOne("SELECT doc_id,title,payload FROM doc_versions WHERE vid=@v", ("@v", vid));
            if (row == null)
            {
                return null;
            }
            byte[] payload = row.TryGetValue("payload", out object? p) && p is byte[] bytes ? bytes : Array.Empty<byte>();
            return new Dictionary<string, object?>
            {
                ["doc_id"] = Str(row, "doc_id"),
                ["title"] = Str(row, "title"),
                ["content"] = Encoding.UTF8.GetString(ZlibDecompress(payload))
            };
        }
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] ZlibDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    // ============================== 备份 ==============================

    public JsonObject ExportBackup(bool includeVersions = false)
    {
        lock (_sync)
        {
            var docs = new JsonArray();
            foreach (var row in Query($"SELECT {MetaCols} FROM docs ORDER BY updated_at DESC"))
            {
                var doc = RowToJson(row);
                doc["content"] = GetBody(Str(row, "id") ?? "");
                if (includeVersions)
                {
                    var versions = new JsonArray();
                    foreach (var v in Query(
                                 "SELECT vid,revision,title,payload,created_at FROM doc_versions " +
                                 "WHERE doc_id=@id ORDER BY vid DESC", ("@id", Str(row, "id") ?? "")))
                    {
                        byte[] payload = v.TryGetValue("payload", out object? p) && p is byte[] bytes
                            ? bytes : Array.Empty<byte>();
                        versions.Add(new JsonObject
                        {
                            ["vid"] = Long(v, "vid"),
                            ["revision"] = Long(v, "revision"),
                            ["title"] = Str(v, "title"),
                            ["created_at"] = Long(v, "created_at"),
                            ["content_b64"] = Convert.ToBase64String(ZlibDecompress(payload))
                        });
                    }
                    doc["versions"] = versions;
                }
                docs.Add(doc);
            }
            var settings = new JsonObject();
            foreach (var pair in GetSettings())
            {
                settings[pair.Key] = pair.Value;
            }
            return new JsonObject
            {
                ["app"] = "yamlmind",
                ["backup_version"] = 1,
                ["schema"] = SchemaVersion,
                ["exported_at"] = NowMs(),
                ["settings"] = settings,
                ["counts"] = new JsonObject { ["docs"] = docs.Count },
                ["docs"] = docs
            };
        }
    }

    public JsonObject ImportBackup(JsonObject payload, string mode = "merge")
    {
        if (LockModeOn())
        {
            throw new PermissionDeniedException("locked");
        }
        string? app = payload["app"] is JsonValue appValue && appValue.TryGetValue<string>(out string? appText)
            ? appText : null;
        if (app != "yamlmind" || payload["docs"] is not JsonArray docs)
        {
            throw new ValueErrorException("not a yamlmind backup file");
        }
        if (mode != "merge")
        {
            throw new ValueErrorException($"unsupported import mode: {mode}");
        }
        lock (_sync)
        {
            int created = 0, updated = 0, skipped = 0;
            long ts = NowMs();
            using var tx = BeginTx();
            foreach (JsonNode? item in docs)
            {
                if (item is not JsonObject d)
                {
                    continue;
                }
                string docId = d["id"]?.GetValue<string>() ?? NewId();
                string title = (d["title"]?.GetValue<string>() ?? "未命名导图").Trim();
                string content = d["content"]?.GetValue<string>() ?? "";
                var parsed = YamlMindTree.Parse(content, title);
                string treeJson = YamlMindTree.Slim(parsed).ToJsonString(HttpResponse.JsonOptions);
                var existing = QueryOne("SELECT id,revision FROM docs WHERE id=@id", ("@id", docId));
                int incomingRevision = d["revision"] is JsonValue rv && rv.TryGetValue<int>(out int r) ? r : 0;
                if (existing != null && incomingRevision <= Long(existing, "revision"))
                {
                    skipped++;
                    continue;
                }
                string? errorJson = parsed.Error?.ToJsonString(HttpResponse.JsonOptions);
                Exec("INSERT INTO docs(id,title,bytes,node_count,max_depth,branch_count,valid,error,revision," +
                     "preview,created_at,updated_at,opened_at) VALUES(@id,@title,@bytes,@nodes,@depth,@branch," +
                     "@valid,@err,@rev,@preview,@c,@u,@o) ON CONFLICT(id) DO UPDATE SET title=excluded.title," +
                     " bytes=excluded.bytes, node_count=excluded.node_count, max_depth=excluded.max_depth," +
                     " branch_count=excluded.branch_count, valid=excluded.valid, error=excluded.error," +
                     " revision=excluded.revision, preview=excluded.preview, updated_at=excluded.updated_at", tx,
                    ("@id", docId), ("@title", title), ("@bytes", Encoding.UTF8.GetByteCount(content)),
                    ("@nodes", parsed.Nodes), ("@depth", parsed.MaxDepth), ("@branch", parsed.Branches),
                    ("@valid", parsed.Ok ? 1 : 0), ("@err", errorJson), ("@rev", incomingRevision + 1),
                    ("@preview", YamlMindTree.Preview(parsed)),
                    ("@c", d["created_at"] is JsonValue cv && cv.TryGetValue<long>(out long c) ? c : ts),
                    ("@u", ts),
                    ("@o", d["opened_at"] is JsonValue ov && ov.TryGetValue<long>(out long o) ? o : 0));
                Exec("INSERT INTO doc_bodies(doc_id,content) VALUES(@id,@content) " +
                     "ON CONFLICT(doc_id) DO UPDATE SET content=excluded.content", tx,
                    ("@id", docId), ("@content", content));
                Exec("INSERT INTO doc_trees(doc_id,tree_json,built_at) VALUES(@id,@tree,@t) " +
                     "ON CONFLICT(doc_id) DO UPDATE SET tree_json=excluded.tree_json, built_at=excluded.built_at",
                    tx, ("@id", docId), ("@tree", treeJson), ("@t", ts));
                created += existing == null ? 1 : 0;
                updated += existing == null ? 0 : 1;
                if (d["versions"] is JsonArray versions)
                {
                    foreach (JsonNode? vnode in versions)
                    {
                        if (vnode is not JsonObject v)
                        {
                            continue;
                        }
                        try
                        {
                            string b64 = v["content_b64"]?.GetValue<string>() ?? "";
                            byte[] raw = Convert.FromBase64String(b64);
                            byte[] blob = ZlibCompress(raw);
                            long vCreated = v["created_at"] is JsonValue vc && vc.TryGetValue<long>(out long vt) ? vt : ts;
                            long vRev = v["revision"] is JsonValue vr && vr.TryGetValue<long>(out long vrr) ? vrr : 0;
                            Exec("INSERT INTO doc_versions(doc_id,revision,title,payload,raw_bytes,created_at) " +
                                 "VALUES(@id,@rev,@title,@payload,@raw,@t)", tx,
                                ("@id", docId), ("@rev", vRev), ("@title", v["title"]?.GetValue<string>() ?? title),
                                ("@payload", blob), ("@raw", raw.Length), ("@t", vCreated));
                        }
                        catch
                        {
                            // 单条历史版本损坏时跳过
                        }
                    }
                }
            }
            tx.Commit();
            return new JsonObject
            {
                ["restored"] = docs.Count,
                ["created"] = created,
                ["updated"] = updated,
                ["skipped"] = skipped
            };
        }
    }

    private static JsonObject RowToJson(Dictionary<string, object?> row)
    {
        var obj = new JsonObject();
        foreach (var pair in row)
        {
            obj[pair.Key] = pair.Value switch
            {
                null => null,
                byte[] bytes => Convert.ToBase64String(bytes),
                long l => l,
                int i => i,
                double d => d,
                bool b => b,
                _ => Convert.ToString(pair.Value, CultureInfo.InvariantCulture)
            };
        }
        return obj;
    }

    public List<JsonObject> ListBackups()
    {
        var output = new List<JsonObject>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(BackupDir, "*.json"))
            {
                var info = new FileInfo(file);
                output.Add(new JsonObject
                {
                    ["name"] = info.Name,
                    ["bytes"] = info.Length,
                    ["mtime"] = (long)((DateTimeOffset)info.LastWriteTimeUtc).ToUnixTimeMilliseconds()
                });
            }
        }
        catch
        {
            return output;
        }
        return output.OrderByDescending(o => o["mtime"]?.GetValue<long>() ?? 0).ToList();
    }

    public JsonObject AutoBackup()
    {
        string name = $"auto-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        string path = Path.Combine(BackupDir, name);
        var payload = ExportBackup(includeVersions: false);
        string temp = path + ".tmp";
        File.WriteAllText(temp, payload.ToJsonString(HttpResponse.JsonOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
        int pruned = 0;
        foreach (JsonObject backup in ListBackups().Skip(AutoBackupKeep))
        {
            try
            {
                File.Delete(Path.Combine(BackupDir, backup["name"]!.GetValue<string>()));
                pruned++;
            }
            catch
            {
                // ignore
            }
        }
        return new JsonObject
        {
            ["name"] = name,
            ["bytes"] = new FileInfo(path).Length,
            ["docs"] = payload["counts"]?["docs"]?.GetValue<int>() ?? 0,
            ["pruned"] = pruned
        };
    }

    // ============================== 维护 / 统计 ==============================

    public long JobStart(string name)
    {
        lock (_sync)
        {
            return Insert("INSERT INTO jobs(name,started_at,status) VALUES(@n,@t,'running')",
                null, ("@n", name), ("@t", NowMs()));
        }
    }

    public void JobFinish(long jobId, string status, string detail)
    {
        lock (_sync)
        {
            using var tx = BeginTx();
            Exec("UPDATE jobs SET finished_at=@t, status=@s, detail=@d WHERE job_id=@id", tx,
                ("@t", NowMs()), ("@s", status), ("@d", detail.Length > 4000 ? detail[..4000] : detail),
                ("@id", jobId));
            Exec("DELETE FROM jobs WHERE job_id NOT IN (SELECT job_id FROM jobs ORDER BY job_id DESC LIMIT 200)", tx);
            tx.Commit();
        }
    }

    public Dictionary<string, object?>? LastJob(string name)
    {
        lock (_sync)
        {
            return QueryOne("SELECT job_id,name,started_at,finished_at,status,detail FROM jobs " +
                            "WHERE name=@n ORDER BY job_id DESC LIMIT 1", ("@n", name));
        }
    }

    public JsonObject DbStats()
    {
        lock (_sync)
        {
            long pageCount = ScalarLong("PRAGMA page_count");
            long pageSize = ScalarLong("PRAGMA page_size");
            long freelist = ScalarLong("PRAGMA freelist_count");
            long fileSize = File.Exists(DbPath) ? new FileInfo(DbPath).Length : 0;
            string walPath = DbPath + "-wal";
            long walSize = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;
            long docCount = ScalarLong("SELECT COUNT(*) FROM docs");
            long contentBytes = ScalarLong("SELECT COALESCE(SUM(bytes),0) FROM docs");
            long versionCount = ScalarLong("SELECT COUNT(*) FROM doc_versions");
            long versionBytes = ScalarLong("SELECT COALESCE(SUM(raw_bytes),0) FROM doc_versions");
            long ftsDocs = 0, bigDocs = 0;
            try { ftsDocs = ScalarLong("SELECT COUNT(*) FROM docs_fts"); } catch { }
            try { bigDocs = ScalarLong("SELECT COUNT(*) FROM docs_big"); } catch { }
            var backups = new JsonArray();
            foreach (JsonObject backup in ListBackups().Take(AutoBackupKeep))
            {
                backups.Add(backup.DeepClone());
            }
            var lastOpt = LastJob("optimize");
            return new JsonObject
            {
                ["db_path"] = DbPath,
                ["file_bytes"] = fileSize,
                ["wal_bytes"] = walSize,
                ["page_size"] = pageSize,
                ["page_count"] = pageCount,
                ["freelist_pages"] = freelist,
                ["journal_mode"] = ScalarString("PRAGMA journal_mode"),
                ["doc_count"] = docCount,
                ["content_bytes"] = contentBytes,
                ["version_count"] = versionCount,
                ["version_bytes"] = versionBytes,
                ["fts_docs"] = ftsDocs,
                ["big_docs"] = bigDocs,
                ["last_optimize"] = lastOpt != null ? RowToJson(lastOpt) : null,
                ["backups"] = backups,
                ["sqlite"] = ScalarString("SELECT sqlite_version()")
            };
        }
    }

    /// <summary>启动自检：只补齐缺失的正文/缓存树，避免每次启动都做全量优化。</summary>
    public void StartupSelfCheck()
    {
        lock (_sync)
        {
            foreach (var row in Query("SELECT d.id FROM docs d LEFT JOIN doc_bodies b ON b.doc_id=d.id WHERE b.content IS NULL"))
            {
                Exec("INSERT OR IGNORE INTO doc_bodies(doc_id,content) VALUES(@id,'')", null,
                    ("@id", Str(row, "id") ?? ""));
            }
            foreach (var row in Query("SELECT d.id FROM docs d LEFT JOIN doc_trees t ON t.doc_id=d.id WHERE t.tree_json IS NULL"))
            {
                string id = Str(row, "id") ?? "";
                string title = ScalarString("SELECT title FROM docs WHERE id=@id", ("@id", id));
                string body = ScalarString("SELECT content FROM doc_bodies WHERE doc_id=@id", ("@id", id));
                var parsed = YamlMindTree.Parse(body, title);
                Exec("INSERT OR REPLACE INTO doc_trees(doc_id,tree_json,built_at) VALUES(@id,@tree,@t)", null,
                    ("@id", id), ("@tree", YamlMindTree.Slim(parsed).ToJsonString(HttpResponse.JsonOptions)),
                    ("@t", NowMs()));
            }
        }
    }

    public JsonObject OptimizeDb(bool rebuildFts = false)
    {
        long job = JobStart("optimize");
        var detail = new JsonObject();
        try
        {
            lock (_sync)
            {
                long before = DbStats().FileBytesSafe();
                ExecRaw("PRAGMA wal_checkpoint(TRUNCATE)");
                detail["wal_checkpoint"] = "truncated";
                ExecRaw("PRAGMA incremental_vacuum");
                detail["incremental_vacuum"] = true;
                int pruned = 0;
                foreach (var row in Query("SELECT id FROM docs"))
                {
                    string id = Str(row, "id") ?? "";
                    using var tx = BeginTx();
                    var vids = new List<long>();
                    using (var cmd = Cmd("SELECT vid FROM doc_versions WHERE doc_id=@id ORDER BY vid DESC", tx))
                    {
                        Bind(cmd, ("@id", id));
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read()) vids.Add(reader.GetInt64(0));
                    }
                    for (int i = MaxVersionsPerDoc; i < vids.Count; i++)
                    {
                        Exec("DELETE FROM doc_versions WHERE vid=@v", tx, ("@v", vids[i]));
                        pruned++;
                    }
                    tx.Commit();
                }
                detail["versions_pruned"] = pruned;
                int repaired = 0;
                foreach (var row in Query("SELECT d.id FROM docs d LEFT JOIN doc_bodies b ON b.doc_id=d.id WHERE b.content IS NULL"))
                {
                    Exec("INSERT OR IGNORE INTO doc_bodies(doc_id,content) VALUES(@id,'')", null,
                        ("@id", Str(row, "id") ?? ""));
                    repaired++;
                }
                detail["bodies_repaired"] = repaired;
                int built = 0;
                foreach (var row in Query("SELECT d.id FROM docs d LEFT JOIN doc_trees t ON t.doc_id=d.id WHERE t.tree_json IS NULL"))
                {
                    string id = Str(row, "id") ?? "";
                    string title = ScalarString("SELECT title FROM docs WHERE id=@id", ("@id", id));
                    string body = ScalarString("SELECT content FROM doc_bodies WHERE doc_id=@id", ("@id", id));
                    var parsed = YamlMindTree.Parse(body, title);
                    Exec("INSERT OR REPLACE INTO doc_trees(doc_id,tree_json,built_at) VALUES(@id,@tree,@t)",
                        null, ("@id", id), ("@tree", YamlMindTree.Slim(parsed).ToJsonString(HttpResponse.JsonOptions)),
                        ("@t", NowMs()));
                    built++;
                }
                detail["trees_built"] = built;
                if (rebuildFts)
                {
                    detail["fts"] = "rebuilt";
                }
                ExecRaw("ANALYZE");
                try { ExecRaw("PRAGMA optimize"); } catch { }
                try
                {
                    detail["auto_backup"] = AutoBackup()["name"]?.GetValue<string>();
                }
                catch (Exception ex)
                {
                    detail["auto_backup_error"] = ex.GetType().Name + ": " + ex.Message;
                }
                long after = DbStats().FileBytesSafe();
                detail["bytes_before"] = before;
                detail["bytes_after"] = after;
                detail["bytes_reclaimed"] = Math.Max(0, before - after);
                JobFinish(job, "ok", detail.ToJsonString(HttpResponse.JsonOptions));
                return new JsonObject { ["status"] = "ok", ["detail"] = detail };
            }
        }
        catch (Exception ex)
        {
            JobFinish(job, "error", ex.ToString());
            return new JsonObject { ["status"] = "error", ["detail"] = ex.ToString() };
        }
    }

    public JsonObject VacuumFull()
    {
        long job = JobStart("vacuum");
        try
        {
            lock (_sync)
            {
                ExecRaw("PRAGMA wal_checkpoint(TRUNCATE)");
                long before = DbStats().FileBytesSafe();
                ExecRaw("VACUUM");
                ExecRaw("ANALYZE");
                long after = DbStats().FileBytesSafe();
                var detail = new JsonObject
                {
                    ["bytes_before"] = before,
                    ["bytes_after"] = after,
                    ["bytes_reclaimed"] = Math.Max(0, before - after)
                };
                JobFinish(job, "ok", detail.ToJsonString(HttpResponse.JsonOptions));
                return new JsonObject { ["status"] = "ok", ["detail"] = detail };
            }
        }
        catch (Exception ex)
        {
            JobFinish(job, "error", ex.ToString());
            return new JsonObject { ["status"] = "error", ["detail"] = ex.ToString() };
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            try { _conn?.Close(); } catch { }
            _conn?.Dispose();
            _conn = null;
        }
    }

    public sealed class PermissionDeniedException : Exception
    {
        public PermissionDeniedException(string message) : base(message) { }
    }

    public sealed class ValueErrorException : Exception
    {
        public ValueErrorException(string message) : base(message) { }
    }
}

internal static class JsonObjectExtensions
{
    public static long FileBytesSafe(this JsonObject stats)
    {
        try
        {
            return stats["file_bytes"]?.GetValue<long>() ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
