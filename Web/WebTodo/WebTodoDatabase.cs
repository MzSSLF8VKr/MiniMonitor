using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace MiniMonitor.Web.WebTodo;

/// <summary>WebTodo SQLite 存储（projects / tasks / meta）。</summary>
public sealed class WebTodoDatabase : IDisposable
{
    private readonly object _sync = new();
    private SqliteConnection? _conn;

    public string DbPath { get; }

    public WebTodoDatabase(string dbPath)
    {
        DbPath = dbPath;
    }

    public void Initialize()
    {
        lock (_sync)
        {
            if (_conn != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            };
            _conn = new SqliteConnection(builder.ToString());
            _conn.Open();
            ExecRaw("PRAGMA journal_mode=WAL");
            ExecRaw("PRAGMA foreign_keys=ON");
            ExecRaw("PRAGMA busy_timeout=5000");
            CreateSchema();
        }
    }

    private static readonly string[] Schema =
    {
        @"CREATE TABLE IF NOT EXISTS projects (
            id              TEXT PRIMARY KEY,
            name            TEXT NOT NULL,
            description     TEXT NOT NULL DEFAULT '',
            color           TEXT NOT NULL DEFAULT '#6366f1',
            color_mode      TEXT NOT NULL DEFAULT 'solid',
            icon            TEXT NOT NULL DEFAULT '',
            expire_at       TEXT,
            pinned          INTEGER NOT NULL DEFAULT 0,
            archived        INTEGER NOT NULL DEFAULT 0,
            sort_order      INTEGER NOT NULL DEFAULT 0,
            created_at      TEXT NOT NULL,
            updated_at      TEXT NOT NULL
        )",
        @"CREATE TABLE IF NOT EXISTS tasks (
            id           TEXT PRIMARY KEY,
            project_id   TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
            title        TEXT NOT NULL,
            notes        TEXT NOT NULL DEFAULT '',
            priority     INTEGER NOT NULL DEFAULT 0,
            status       TEXT NOT NULL DEFAULT 'open',
            due_at       TEXT,
            completed_at TEXT,
            sort_order   INTEGER NOT NULL DEFAULT 0,
            created_at   TEXT NOT NULL,
            updated_at   TEXT NOT NULL
        )",
        "CREATE INDEX IF NOT EXISTS idx_tasks_project ON tasks(project_id, status, sort_order)",
        "CREATE INDEX IF NOT EXISTS idx_tasks_due     ON tasks(due_at)",
        @"CREATE TABLE IF NOT EXISTS meta (
            key   TEXT PRIMARY KEY,
            value TEXT
        )"
    };

    private void CreateSchema()
    {
        foreach (string sql in Schema)
        {
            ExecRaw(sql);
        }
        // 旧版本遗留的操作日志 / 推送日志表与提醒字段：彻底清理（老用户库中可能仍存在）。
        ExecRaw("DROP TABLE IF EXISTS push_logs");
        ExecRaw("DROP TABLE IF EXISTS audit_logs");
        ExecRaw("DROP INDEX IF EXISTS idx_push_created");
        ExecRaw("DROP INDEX IF EXISTS idx_push_status");
        ExecRaw("DROP INDEX IF EXISTS idx_audit_ts");
        ExecRaw("DROP INDEX IF EXISTS idx_audit_action");
        ExecRaw("DROP INDEX IF EXISTS idx_audit_ip");
        ExecRaw("DROP INDEX IF EXISTS idx_tasks_remind");
        try { ExecRaw("ALTER TABLE tasks DROP COLUMN remind_at"); } catch { }
        try { ExecRaw("ALTER TABLE tasks DROP COLUMN reminded"); } catch { }
        try { ExecRaw("ALTER TABLE projects DROP COLUMN expire_notified"); } catch { }
    }

    private SqliteConnection Conn => _conn ?? throw new InvalidOperationException("数据库尚未初始化");
    private SqliteTransaction? _transaction;

    public void BeginTransaction()
    {
        lock (_sync)
        {
            _transaction ??= Conn.BeginTransaction();
        }
    }

    public void Commit()
    {
        lock (_sync)
        {
            try { _transaction?.Commit(); } finally { _transaction?.Dispose(); _transaction = null; }
        }
    }

    public void Rollback()
    {
        lock (_sync)
        {
            try { _transaction?.Rollback(); } finally { _transaction?.Dispose(); _transaction = null; }
        }
    }

    private SqliteCommand Cmd(string sql)
    {
        var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        if (_transaction != null)
        {
            cmd.Transaction = _transaction;
        }
        return cmd;
    }

    private void ExecRaw(string sql)
    {
        using var cmd = Cmd(sql);
        cmd.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand cmd, (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    public int Execute(string sql, params (string Name, object? Value)[] args)
    {
        lock (_sync)
        {
            using var cmd = Cmd(sql);
            Bind(cmd, args);
            return cmd.ExecuteNonQuery();
        }
    }

    public long Insert(string sql, params (string Name, object? Value)[] args)
    {
        lock (_sync)
        {
            using var cmd = Cmd(sql);
            Bind(cmd, args);
            cmd.ExecuteNonQuery();
            using var idCmd = Cmd("SELECT last_insert_rowid()");
            return Convert.ToInt64(idCmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    public long Scalar(string sql, params (string Name, object? Value)[] args)
    {
        lock (_sync)
        {
            using var cmd = Cmd(sql);
            Bind(cmd, args);
            object? value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
    }

    public object? ScalarObject(string sql, params (string Name, object? Value)[] args)
    {
        lock (_sync)
        {
            using var cmd = Cmd(sql);
            Bind(cmd, args);
            object? value = cmd.ExecuteScalar();
            return value == DBNull.Value ? null : value;
        }
    }

    public List<Dictionary<string, object?>> Query(string sql, params (string Name, object? Value)[] args)
    {
        lock (_sync)
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
    }

    public Dictionary<string, object?>? QueryOne(string sql, params (string Name, object? Value)[] args)
    {
        List<Dictionary<string, object?>> rows = Query(sql, args);
        return rows.Count > 0 ? rows[0] : null;
    }

    public string GetMeta(string key, string fallback = "")
    {
        Dictionary<string, object?>? row = QueryOne("SELECT value FROM meta WHERE key=@k", ("@k", key));
        return row == null ? fallback : Convert.ToString(row["value"], CultureInfo.InvariantCulture) ?? fallback;
    }

    public void SetMeta(string key, string value)
    {
        Execute("INSERT INTO meta(key,value) VALUES(@k,@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            ("@k", key), ("@v", value));
    }

    public long NextSortOrder(string table, string where = "", params (string Name, object? Value)[] args)
    {
        string sql = $"SELECT MAX(sort_order) FROM {table}";
        if (!string.IsNullOrWhiteSpace(where))
        {
            sql += " WHERE " + where;
        }
        return Scalar(sql, args) + 1000;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            try { _transaction?.Dispose(); } catch { }
            _transaction = null;
            try { _conn?.Close(); } catch { }
            _conn?.Dispose();
            _conn = null;
        }
    }
}

public static class WebTodoSerializer
{
    public static Dictionary<string, object?> ProjectFromRow(Dictionary<string, object?> row)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = row.GetValueOrDefault("id"),
            ["name"] = row.GetValueOrDefault("name"),
            ["description"] = row.GetValueOrDefault("description") ?? "",
            ["color"] = row.GetValueOrDefault("color") ?? "#6366f1",
            ["colorMode"] = row.GetValueOrDefault("color_mode") ?? "solid",
            ["icon"] = row.GetValueOrDefault("icon") ?? "",
            ["expireAt"] = row.GetValueOrDefault("expire_at"),
            ["expireNotified"] = Convert.ToInt64(row.GetValueOrDefault("expire_notified") ?? 0L) != 0,
            ["pinned"] = Convert.ToInt64(row.GetValueOrDefault("pinned") ?? 0L) != 0,
            ["archived"] = Convert.ToInt64(row.GetValueOrDefault("archived") ?? 0L) != 0,
            ["sortOrder"] = row.GetValueOrDefault("sort_order") ?? 0L,
            ["createdAt"] = row.GetValueOrDefault("created_at"),
            ["updatedAt"] = row.GetValueOrDefault("updated_at")
        };
    }

    public static Dictionary<string, object?> TaskFromRow(Dictionary<string, object?> row)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = row.GetValueOrDefault("id"),
            ["projectId"] = row.GetValueOrDefault("project_id"),
            ["title"] = row.GetValueOrDefault("title"),
            ["notes"] = row.GetValueOrDefault("notes") ?? "",
            ["priority"] = row.GetValueOrDefault("priority") ?? 0L,
            ["status"] = row.GetValueOrDefault("status") ?? "open",
            ["dueAt"] = row.GetValueOrDefault("due_at"),
            ["completedAt"] = row.GetValueOrDefault("completed_at"),
            ["sortOrder"] = row.GetValueOrDefault("sort_order") ?? 0L,
            ["createdAt"] = row.GetValueOrDefault("created_at"),
            ["updatedAt"] = row.GetValueOrDefault("updated_at")
        };
    }
}
