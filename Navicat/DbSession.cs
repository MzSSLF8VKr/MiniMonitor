using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Renci.SshNet;

namespace MiniMonitor.Navicat;

/// <summary>
/// 数据库会话：统一封装 MySQL / PostgreSQL / SQL Server / Oracle / SQLite 的连接、
/// 查询、元数据读取与 DDL 生成。所有方法内部串行化，可从后台线程安全调用。
/// </summary>
internal sealed class DbSession : IDisposable
{
    private readonly object _gate = new();
    private readonly ConnectionProfile _profile;
    private readonly string? _initialDatabase;
    private DbConnection? _connection;
    private DbTransaction? _transaction;
    private SshClient? _ssh;
    private ForwardedPortLocal? _forward;
    private string _host = "";
    private int _port;
    private bool _disposed;

    public DbSession(ConnectionProfile profile, string? database = null)
    {
        _profile = profile;
        _initialDatabase = database;
        CurrentDatabase = database ?? profile.Database;
    }

    public ConnectionProfile Profile => _profile;
    public DbKind Kind => _profile.Kind;
    public string CurrentDatabase { get; private set; } = "";
    public string ServerVersion { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public bool IsOpen => _connection is { State: ConnectionState.Open };
    public bool HasTransaction => _transaction != null;

    public string Quote(string identifier)
    {
        return Kind switch
        {
            DbKind.MySQL => "`" + identifier.Replace("`", "``") + "`",
            DbKind.SqlServer => "[" + identifier.Replace("]", "]]") + "]",
            DbKind.Oracle => "\"" + identifier.Replace("\"", "\"\"") + "\"",
            _ => "\"" + identifier.Replace("\"", "\"\"") + "\""
        };
    }

    public string Qualify(string? database, string? schema, string name)
    {
        var parts = new List<string>();
        if (Kind is DbKind.MySQL or DbKind.SqlServer && !string.IsNullOrWhiteSpace(database))
        {
            parts.Add(Quote(database!));
        }
        if (Kind is DbKind.Oracle && !string.IsNullOrWhiteSpace(schema))
        {
            parts.Add(Quote(schema!));
        }
        else if (Kind is DbKind.PostgreSQL or DbKind.SqlServer && !string.IsNullOrWhiteSpace(schema)
            && !string.Equals(schema, "public", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(Quote(schema!));
        }
        else if (Kind is DbKind.PostgreSQL && !string.IsNullOrWhiteSpace(schema))
        {
            parts.Add(Quote(schema!));
        }
        parts.Add(Quote(name));
        return string.Join(".", parts);
    }

    public string Qualify(TableInfo table) => Qualify(table.Database, table.Schema, table.Name);

    public string Param(string name) => (Kind == DbKind.Oracle ? ":" : "@") + name;

    // ================= 连接 =================

    public void Open()
    {
        lock (_gate)
        {
            OpenCore(_initialDatabase);
        }
    }

    public void Reopen(string? database)
    {
        lock (_gate)
        {
            OpenCore(database);
        }
    }

    private void OpenCore(string? database)
    {
        CloseCore();
        try
        {
            if (_profile.UseSsh)
            {
                StartSshTunnel();
            }
            else
            {
                _host = string.IsNullOrWhiteSpace(_profile.Host) ? "127.0.0.1" : _profile.Host.Trim();
                _port = _profile.Port;
            }

            string db = ResolveDatabase(database);
            NvLog.Info($"打开连接：{_profile.Name} · {Kind.ShortName()} · "
                + $"{_host}:{_port} · 数据库={db} · 用户={_profile.Username} · SSL={_profile.UseSsl} · SSH={_profile.UseSsh}");
            string connectionString = BuildConnectionString(db);
            _connection = CreateConnection(connectionString);
            _connection.Open();
            CurrentDatabase = db;
            try
            {
                ServerVersion = _connection.ServerVersion ?? "";
            }
            catch
            {
                ServerVersion = "";
            }
            LastError = "";
            NvLog.Info($"连接成功：{_profile.Name} · 版本={ServerVersion} · 数据库={CurrentDatabase}");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            NvLog.Error(ex, $"连接失败：{_profile.Name} · {Kind.ShortName()} · "
                + $"{_profile.Host}:{_profile.Port} · 数据库={_profile.Database}");
            CloseCore();
            throw;
        }
    }

    private string ResolveDatabase(string? database)
    {
        string db = database ?? _initialDatabase ?? _profile.Database;
        if (_profile.Kind == DbKind.SQLite)
        {
            string path = string.IsNullOrWhiteSpace(db) ? "database.db" : Environment.ExpandEnvironmentVariables(db.Trim());
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            }
            return path;
        }
        if (string.IsNullOrWhiteSpace(db))
        {
            db = _profile.Kind switch
            {
                DbKind.PostgreSQL => "postgres",
                DbKind.SqlServer => "master",
                DbKind.Oracle => "ORCL",
                _ => ""
            };
        }
        return db;
    }

    private DbConnection CreateConnection(string connectionString)
    {
        return _profile.Kind switch
        {
            DbKind.SQLite => new SqliteConnection(connectionString),
            DbKind.MySQL => new MySqlConnection(connectionString),
            DbKind.PostgreSQL => new NpgsqlConnection(connectionString),
            DbKind.SqlServer => new SqlConnection(connectionString),
            DbKind.Oracle => new OracleConnection(connectionString),
            _ => throw new NotSupportedException("不支持的数据库类型")
        };
    }

    private string BuildConnectionString(string database)
    {
        string host = string.IsNullOrWhiteSpace(_host) ? "127.0.0.1" : _host;
        int port = _port > 0 ? _port : _profile.Kind.DefaultPort();
        int timeout = _profile.ConnectTimeout <= 0 ? 15 : _profile.ConnectTimeout;

        switch (_profile.Kind)
        {
            case DbKind.SQLite:
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = database,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                    ForeignKeys = true,
                    Pooling = false
                };
                return builder.ConnectionString;
            }
            case DbKind.MySQL:
            {
                var builder = new MySqlConnectionStringBuilder
                {
                    Server = host,
                    Port = (uint)port,
                    UserID = _profile.Username,
                    Password = _profile.Password,
                    Database = database,
                    ConnectionTimeout = (uint)timeout,
                    AllowUserVariables = true,
                    AllowLoadLocalInfile = true,
                    ConvertZeroDateTime = true,
                    TreatTinyAsBoolean = true,
                    Pooling = false,
                    SslMode = ParseMySqlSslMode()
                };
                if (!string.IsNullOrWhiteSpace(_profile.Charset))
                {
                    builder.CharacterSet = _profile.Charset;
                }
                if (!string.IsNullOrWhiteSpace(_profile.RawConnectionString))
                {
                    builder.ConnectionString += ";" + _profile.RawConnectionString.Trim().Trim(';');
                }
                return builder.ConnectionString;
            }
            case DbKind.PostgreSQL:
            {
                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = port,
                    Username = _profile.Username,
                    Password = _profile.Password,
                    Database = database,
                    Timeout = timeout,
                    CommandTimeout = 300,
                    Pooling = false,
                    SslMode = ParseNpgsqlSslMode()
                };
                if (!string.IsNullOrWhiteSpace(_profile.RawConnectionString))
                {
                    builder.ConnectionString += ";" + _profile.RawConnectionString.Trim().Trim(';');
                }
                return builder.ConnectionString;
            }
            case DbKind.SqlServer:
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = host + "," + port,
                    InitialCatalog = database,
                    UserID = _profile.Username,
                    Password = _profile.Password,
                    ConnectTimeout = timeout,
                    TrustServerCertificate = true,
                    Encrypt = _profile.UseSsl,
                    Pooling = false,
                    MultipleActiveResultSets = true
                };
                if (!string.IsNullOrWhiteSpace(_profile.RawConnectionString))
                {
                    builder.ConnectionString += ";" + _profile.RawConnectionString.Trim().Trim(';');
                }
                return builder.ConnectionString;
            }
            case DbKind.Oracle:
            {
                var builder = new OracleConnectionStringBuilder
                {
                    UserID = _profile.Username,
                    Password = _profile.Password,
                    DataSource = host + ":" + port + "/" + database
                };
                return builder.ConnectionString;
            }
            default:
                throw new NotSupportedException("不支持的数据库类型");
        }
    }

    private MySqlSslMode ParseMySqlSslMode()
    {
        if (!_profile.UseSsl)
        {
            return MySqlSslMode.None;
        }
        return _profile.SslMode.ToLowerInvariant() switch
        {
            "disable" or "none" => MySqlSslMode.None,
            "require" or "required" => MySqlSslMode.Required,
            "verifyca" or "verify-ca" => MySqlSslMode.VerifyCA,
            "verifyfull" or "verify-full" => MySqlSslMode.VerifyFull,
            _ => MySqlSslMode.Preferred
        };
    }

    private SslMode ParseNpgsqlSslMode()
    {
        if (!_profile.UseSsl)
        {
            return SslMode.Disable;
        }
        return _profile.SslMode.ToLowerInvariant() switch
        {
            "disable" or "none" => SslMode.Disable,
            "allow" => SslMode.Allow,
            "require" or "required" => SslMode.Require,
            "verifyca" or "verify-ca" => SslMode.VerifyCA,
            "verifyfull" or "verify-full" => SslMode.VerifyFull,
            _ => SslMode.Prefer
        };
    }

    private void StartSshTunnel()
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(_profile.SshKeyFile) && File.Exists(_profile.SshKeyFile))
        {
            try
            {
                PrivateKeyFile key = string.IsNullOrEmpty(_profile.SshPassphrase)
                    ? new PrivateKeyFile(_profile.SshKeyFile)
                    : new PrivateKeyFile(_profile.SshKeyFile, _profile.SshPassphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(_profile.SshUser, key));
            }
            catch (Exception ex)
            {
                LastError = "SSH 私钥读取失败：" + ex.Message;
            }
        }
        if (!string.IsNullOrEmpty(_profile.SshPassword))
        {
            methods.Add(new PasswordAuthenticationMethod(_profile.SshUser, _profile.SshPassword));
        }
        if (methods.Count == 0)
        {
            methods.Add(new KeyboardInteractiveAuthenticationMethod(_profile.SshUser));
        }

        var info = new ConnectionInfo(
            _profile.SshHost,
            _profile.SshPort <= 0 ? 22 : _profile.SshPort,
            _profile.SshUser,
            methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(_profile.ConnectTimeout <= 0 ? 15 : _profile.ConnectTimeout)
        };
        if (methods[^1] is KeyboardInteractiveAuthenticationMethod keyboard)
        {
            keyboard.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = _profile.SshPassword;
                }
            };
        }

        _ssh = new SshClient(info);
        _ssh.Connect();
        _forward = new ForwardedPortLocal("127.0.0.1", 0, _profile.SshRemoteHost,
            (uint)(_profile.SshRemotePort > 0 ? _profile.SshRemotePort : _profile.Port));
        _ssh.AddForwardedPort(_forward);
        _forward.Start();
        _host = "127.0.0.1";
        _port = (int)_forward.BoundPort;
    }

    private void CloseCore()
    {
        try
        {
            _transaction?.Dispose();
        }
        catch
        {
            // ignore
        }
        _transaction = null;
        try
        {
            _connection?.Dispose();
        }
        catch
        {
            // ignore
        }
        _connection = null;
        try
        {
            _forward?.Dispose();
        }
        catch
        {
            // ignore
        }
        _forward = null;
        try
        {
            _ssh?.Dispose();
        }
        catch
        {
            // ignore
        }
        _ssh = null;
    }

    public void Close()
    {
        lock (_gate)
        {
            CloseCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            CloseCore();
        }
    }

    // ================= 查询执行 =================

    public BatchResult ExecuteBatch(string sql, int timeout = 300, int maxRows = 10000,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureOpen();
            var result = new BatchResult();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (Kind == DbKind.Oracle)
                {
                    foreach (string statement in NvSqlUtil.SplitScript(sql))
                    {
                        ExecuteOne(statement, timeout, maxRows, result, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                else
                {
                    using var command = CreateCommand(sql, timeout);
                    using var registration = cancellationToken.Register(() =>
                    {
                        try
                        {
                            command.Cancel();
                        }
                        catch
                        {
                            // ignore
                        }
                    });
                    using var reader = command.ExecuteReader();
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.ResultSets.Add(ReadResultSet(reader, maxRows, result.Messages));
                    }
                    while (reader.NextResult());
                }
            }
            catch (OperationCanceledException)
            {
                NvLog.Warn("SQL 已取消：" + NvLog.Truncate(sql, 1200));
                throw;
            }
            catch (Exception ex)
            {
                NvLog.Error(ex, "SQL 执行失败：" + NvLog.Truncate(sql, 1600));
                throw;
            }
            finally
            {
                watch.Stop();
                result.Elapsed = watch.Elapsed;
            }
            NvLog.Debug($"SQL 完成：{result.ResultSets.Count} 个结果集 · "
                + $"{result.Elapsed.TotalMilliseconds:0} ms · " + NvLog.Truncate(sql, 800));
            return result;
        }
    }

    private void ExecuteOne(string statement, int timeout, int maxRows, BatchResult result,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(statement, timeout);
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                command.Cancel();
            }
            catch
            {
                // ignore
            }
        });
        using var reader = command.ExecuteReader();
        do
        {
            result.ResultSets.Add(ReadResultSet(reader, maxRows, result.Messages));
        }
        while (reader.NextResult());
    }

    private QueryResultSet ReadResultSet(DbDataReader reader, int maxRows, List<string> messages)
    {
        var set = new QueryResultSet();
        if (reader.FieldCount > 0)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                set.Columns.Add(string.IsNullOrEmpty(reader.GetName(i)) ? "列" + (i + 1) : reader.GetName(i));
            }
            while (reader.Read())
            {
                if (set.Rows.Count >= maxRows)
                {
                    set.Message = $"结果超过 {maxRows} 行，仅显示前 {maxRows} 行。";
                    messages.Add(set.Message);
                    while (reader.Read())
                    {
                        // 继续读取以保持连接状态正常，但不保存
                    }
                    break;
                }
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    try
                    {
                        object value = reader.GetValue(i);
                        row[i] = value is DBNull ? null : value;
                    }
                    catch
                    {
                        row[i] = null;
                    }
                }
                set.Rows.Add(row);
            }
        }
        else
        {
            set.AffectedRows = reader.RecordsAffected;
            set.Message = $"命令执行成功，影响 {Math.Max(0, reader.RecordsAffected)} 行。";
            messages.Add(set.Message);
        }
        return set;
    }

    private void EnsureOpen()
    {
        if (_connection is not { State: ConnectionState.Open })
        {
            throw new InvalidOperationException("数据库连接未打开。");
        }
    }

    private DbCommand CreateCommand(string sql, int timeout)
    {
        var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = timeout <= 0 ? 300 : timeout;
        if (_transaction != null)
        {
            command.Transaction = _transaction;
        }
        command.CommandType = CommandType.Text;
        return command;
    }

    public int Execute(string sql, int timeout = 300)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand(sql, timeout);
            return command.ExecuteNonQuery();
        }
    }

    public int ExecuteWithParams(string sql, IDictionary<string, object?> parameters, int timeout = 300)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand(sql, timeout);
            AddParameters(command, parameters);
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>执行包含多条语句的脚本（Oracle 等不支持一次发送多条语句的驱动会逐条执行）。</summary>
    public int ExecuteScript(string sql, int timeout = 600)
    {
        int executed = 0;
        foreach (string statement in NvSqlUtil.SplitScript(sql))
        {
            if (!NvSqlUtil.HasExecutableSql(statement))
            {
                continue;
            }
            Execute(statement, timeout);
            executed++;
        }
        return executed;
    }

    public object? Scalar(string sql, IDictionary<string, object?>? parameters = null, int timeout = 60)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand(sql, timeout);
            AddParameters(command, parameters);
            object? value = command.ExecuteScalar();
            return value is DBNull ? null : value;
        }
    }

    public DataTable Query(string sql, IDictionary<string, object?>? parameters = null, int timeout = 120)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand(sql, timeout);
            AddParameters(command, parameters);
            using var reader = command.ExecuteReader();
            var table = new DataTable();
            table.Load(reader);
            return table;
        }
    }

    /// <summary>逐行流式读取，备份/导出大表时避免一次性加载全部数据。</summary>
    public void StreamRows(string sql, Action<DbDataReader> handler, int timeout = 300)
    {
        lock (_gate)
        {
            EnsureOpen();
            using var command = CreateCommand(sql, timeout);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                handler(reader);
            }
        }
    }

    private void AddParameters(DbCommand command, IDictionary<string, object?>? parameters)
    {
        if (parameters == null)
        {
            return;
        }
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    public void BeginTransaction()
    {
        lock (_gate)
        {
            EnsureOpen();
            _transaction ??= _connection!.BeginTransaction();
        }
    }

    public void Commit()
    {
        lock (_gate)
        {
            _transaction?.Commit();
            _transaction?.Dispose();
            _transaction = null;
        }
    }

    public void Rollback()
    {
        lock (_gate)
        {
            _transaction?.Rollback();
            _transaction?.Dispose();
            _transaction = null;
        }
    }

    public static (bool Ok, string Message) Test(ConnectionProfile profile)
    {
        try
        {
            using var session = new DbSession(profile);
            session.Open();
            string version = session.ServerVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                version = session.Kind == DbKind.SQLite ? "SQLite" : "已连接";
            }
            return (true, "连接成功：" + version);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "测试连接失败：" + profile.Name + " · " + profile.Kind.ShortName()
                + " · " + profile.ServerLabel);
            return (false, ex.Message);
        }
    }

    private static string Str(DataRow row, string column, string fallback = "")
    {
        return row[column] is object value && value != DBNull.Value
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback
            : fallback;
    }

    private static long? Long(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
        {
            return null;
        }
        return long.TryParse(Convert.ToString(row[column], CultureInfo.InvariantCulture), out long value)
            ? value
            : null;
    }

    private static bool Bool(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
        {
            return false;
        }
        string text = Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? "";
        return text.Equals("1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("t", StringComparison.OrdinalIgnoreCase)
            || text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("YES", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Y", StringComparison.OrdinalIgnoreCase);
    }

    // ================= 元数据 =================

    public List<string> GetDatabases()
    {
        var list = new List<string>();
        DataTable table;
        switch (Kind)
        {
            case DbKind.SQLite:
                table = Query("PRAGMA database_list");
                foreach (DataRow row in table.Rows)
                {
                    list.Add(Str(row, "name", "main"));
                }
                return list;
            case DbKind.MySQL:
                table = Query("SELECT SCHEMA_NAME FROM information_schema.SCHEMATA ORDER BY SCHEMA_NAME");
                break;
            case DbKind.PostgreSQL:
                table = Query("SELECT datname FROM pg_database WHERE NOT datistemplate ORDER BY datname");
                break;
            case DbKind.SqlServer:
                table = Query("SELECT name FROM sys.databases ORDER BY name");
                break;
            case DbKind.Oracle:
                table = Query("SELECT username FROM all_users ORDER BY username");
                break;
            default:
                return list;
        }
        foreach (DataRow row in table.Rows)
        {
            list.Add(Convert.ToString(row[0], CultureInfo.InvariantCulture) ?? "");
        }
        return list.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    public List<TableInfo> GetTables(string database)
    {
        var list = new List<TableInfo>();
        DataTable table;
        switch (Kind)
        {
            case DbKind.SQLite:
                table = Query("SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
                foreach (DataRow row in table.Rows)
                {
                    list.Add(new TableInfo
                    {
                        Database = database,
                        Name = Str(row, "name"),
                        Comment = Str(row, "sql")
                    });
                }
                return list;
            case DbKind.MySQL:
                table = Query(@"SELECT TABLE_SCHEMA AS db_name, TABLE_NAME AS table_name, TABLE_ROWS AS row_count, TABLE_COMMENT AS comment
                                FROM information_schema.TABLES
                                WHERE TABLE_TYPE='BASE TABLE' AND TABLE_SCHEMA=@db
                                ORDER BY TABLE_NAME",
                    new Dictionary<string, object?> { ["@" + "db"] = database });
                break;
            case DbKind.PostgreSQL:
                table = Query(@"SELECT t.table_schema AS schema_name, t.table_name AS table_name,
                                       c.reltuples::bigint AS row_count,
                                       obj_description(c.oid, 'pg_class') AS comment
                                FROM information_schema.tables t
                                JOIN pg_class c ON c.relname=t.table_name
                                JOIN pg_namespace n ON n.oid=c.relnamespace AND n.nspname=t.table_schema
                                WHERE t.table_type='BASE TABLE'
                                  AND t.table_schema NOT IN ('pg_catalog','information_schema')
                                ORDER BY t.table_schema, t.table_name");
                break;
            case DbKind.SqlServer:
                table = Query(@"SELECT s.name AS schema_name, t.name AS table_name,
                                       SUM(p.rows) AS row_count, CAST(ep.value AS NVARCHAR(500)) AS comment
                                FROM sys.tables t
                                JOIN sys.schemas s ON s.schema_id=t.schema_id
                                LEFT JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
                                LEFT JOIN sys.extended_properties ep ON ep.major_id=t.object_id AND ep.minor_id=0 AND ep.name='MS_Description'
                                GROUP BY s.name, t.name, CAST(ep.value AS NVARCHAR(500))
                                ORDER BY s.name, t.name");
                break;
            case DbKind.Oracle:
                table = Query(@"SELECT OWNER AS schema_name, TABLE_NAME AS table_name, NUM_ROWS AS row_count
                                FROM all_tables WHERE OWNER=:owner ORDER BY TABLE_NAME",
                    new Dictionary<string, object?> { [":owner"] = database.ToUpperInvariant() });
                break;
            default:
                return list;
        }

        foreach (DataRow row in table.Rows)
        {
            list.Add(new TableInfo
            {
                Database = database,
                Schema = table.Columns.Contains("schema_name") ? Str(row, "schema_name") : "",
                Name = Str(row, "table_name"),
                Rows = table.Columns.Contains("row_count") ? Long(row, "row_count") : null,
                Comment = table.Columns.Contains("comment") ? Str(row, "comment") : null
            });
        }
        return list;
    }

    public List<TableInfo> GetViews(string database)
    {
        var list = new List<TableInfo>();
        DataTable table;
        switch (Kind)
        {
            case DbKind.SQLite:
                table = Query("SELECT name, sql FROM sqlite_master WHERE type='view' ORDER BY name");
                foreach (DataRow row in table.Rows)
                {
                    list.Add(new TableInfo
                    {
                        Database = database,
                        Name = Str(row, "name"),
                        IsView = true,
                        Comment = Str(row, "sql")
                    });
                }
                return list;
            case DbKind.MySQL:
                table = Query(@"SELECT TABLE_SCHEMA AS schema_name, TABLE_NAME AS table_name
                                FROM information_schema.VIEWS WHERE TABLE_SCHEMA=@db ORDER BY TABLE_NAME",
                    new Dictionary<string, object?> { ["@db"] = database });
                break;
            case DbKind.PostgreSQL:
                table = Query(@"SELECT table_schema AS schema_name, table_name
                                FROM information_schema.views
                                WHERE table_schema NOT IN ('pg_catalog','information_schema')
                                ORDER BY table_schema, table_name");
                break;
            case DbKind.SqlServer:
                table = Query(@"SELECT s.name AS schema_name, v.name AS table_name
                                FROM sys.views v JOIN sys.schemas s ON s.schema_id=v.schema_id
                                ORDER BY s.name, v.name");
                break;
            case DbKind.Oracle:
                table = Query(@"SELECT OWNER AS schema_name, VIEW_NAME AS table_name FROM all_views
                                WHERE OWNER=:owner ORDER BY VIEW_NAME",
                    new Dictionary<string, object?> { [":owner"] = database.ToUpperInvariant() });
                break;
            default:
                return list;
        }
        foreach (DataRow row in table.Rows)
        {
            list.Add(new TableInfo
            {
                Database = database,
                Schema = table.Columns.Contains("schema_name") ? Str(row, "schema_name") : "",
                Name = Str(row, "table_name"),
                IsView = true
            });
        }
        return list;
    }

    public List<ColumnInfo> GetColumns(TableInfo table)
    {
        var columns = new List<ColumnInfo>();
        DataTable data;
        switch (Kind)
        {
            case DbKind.SQLite:
            {
                string escaped = table.Name.Replace("'", "''");
                data = Query("PRAGMA table_info('" + escaped + "')");
                foreach (DataRow row in data.Rows)
                {
                    string type = Str(row, "type");
                    columns.Add(new ColumnInfo
                    {
                        Name = Str(row, "name"),
                        DataType = type,
                        FullType = type,
                        Nullable = !Bool(row, "notnull"),
                        DefaultValue = Str(row, "dflt_value"),
                        PrimaryKey = Long(row, "pk") is > 0,
                        AutoIncrement = type.Contains("INT", StringComparison.OrdinalIgnoreCase)
                            && Long(row, "pk") is > 0,
                        IsBinary = type.Contains("BLOB", StringComparison.OrdinalIgnoreCase),
                        Ordinal = (int)(Long(row, "cid") ?? 0) + 1
                    });
                }
                break;
            }
            case DbKind.MySQL:
            {
                data = Query(@"SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT,
                                      EXTRA, COLUMN_KEY, COLUMN_COMMENT, ORDINAL_POSITION,
                                      CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
                               FROM information_schema.COLUMNS
                               WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@table
                               ORDER BY ORDINAL_POSITION",
                    new Dictionary<string, object?>
                    {
                        ["@db"] = table.Database,
                        ["@table"] = table.Name
                    });
                foreach (DataRow row in data.Rows)
                {
                    string extra = Str(row, "EXTRA");
                    columns.Add(new ColumnInfo
                    {
                        Name = Str(row, "COLUMN_NAME"),
                        DataType = Str(row, "DATA_TYPE"),
                        FullType = Str(row, "COLUMN_TYPE"),
                        Nullable = Str(row, "IS_NULLABLE").Equals("YES", StringComparison.OrdinalIgnoreCase),
                        DefaultValue = row["COLUMN_DEFAULT"] == DBNull.Value ? null : Str(row, "COLUMN_DEFAULT"),
                        AutoIncrement = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                        PrimaryKey = Str(row, "COLUMN_KEY").Equals("PRI", StringComparison.OrdinalIgnoreCase),
                        Comment = Str(row, "COLUMN_COMMENT"),
                        Ordinal = (int)(Long(row, "ORDINAL_POSITION") ?? 0),
                        Length = Long(row, "CHARACTER_MAXIMUM_LENGTH"),
                        Precision = (int?)Long(row, "NUMERIC_PRECISION"),
                        Scale = (int?)Long(row, "NUMERIC_SCALE")
                    });
                }
                break;
            }
            case DbKind.PostgreSQL:
            {
                data = Query(@"SELECT a.attname AS column_name,
                                      t.typname AS data_type,
                                      format_type(a.atttypid, a.atttypmod) AS full_type,
                                      NOT a.attnotnull AS is_nullable,
                                      pg_get_expr(ad.adbin, ad.adrelid) AS column_default,
                                      a.attnum AS ordinal_position,
                                      col_description(a.attrelid, a.attnum) AS comment,
                                      (a.attidentity <> '') AS is_identity,
                                      CASE WHEN a.atttypmod > 0 THEN (a.atttypmod - 4)::bigint ELSE NULL END AS char_length
                               FROM pg_attribute a
                               JOIN pg_class c ON c.oid=a.attrelid
                               JOIN pg_namespace n ON n.oid=c.relnamespace
                               JOIN pg_type t ON t.oid=a.atttypid
                               LEFT JOIN pg_attrdef ad ON ad.adrelid=a.attrelid AND ad.adnum=a.attnum
                               WHERE n.nspname=@schema AND c.relname=@table AND a.attnum>0 AND NOT a.attisdropped
                               ORDER BY a.attnum",
                    new Dictionary<string, object?>
                    {
                        ["@schema"] = string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema,
                        ["@table"] = table.Name
                    });
                var pks = GetPrimaryKeyColumns(table);
                foreach (DataRow row in data.Rows)
                {
                    string name = Str(row, "column_name");
                    string fullType = Str(row, "full_type");
                    string def = row["column_default"] == DBNull.Value ? "" : Str(row, "column_default");
                    columns.Add(new ColumnInfo
                    {
                        Name = name,
                        DataType = Str(row, "data_type"),
                        FullType = fullType,
                        Nullable = Bool(row, "is_nullable"),
                        DefaultValue = string.IsNullOrEmpty(def) ? null : def,
                        PrimaryKey = pks.Contains(name),
                        AutoIncrement = Bool(row, "is_identity") || def.Contains("nextval(", StringComparison.OrdinalIgnoreCase),
                        Comment = row["comment"] == DBNull.Value ? null : Str(row, "comment"),
                        Ordinal = (int)(Long(row, "ordinal_position") ?? 0),
                        Length = Long(row, "char_length"),
                        IsBinary = fullType.Contains("bytea", StringComparison.OrdinalIgnoreCase)
                    });
                }
                break;
            }
            case DbKind.SqlServer:
            {
                data = Query(@"SELECT c.name AS column_name, t.name AS data_type,
                                      c.max_length, c.precision, c.scale, c.is_nullable,
                                      c.is_identity, d.definition AS column_default,
                                      c.column_id AS ordinal_position, c.is_computed
                               FROM sys.columns c
                               JOIN sys.types t ON t.user_type_id=c.user_type_id
                               LEFT JOIN sys.default_constraints d ON d.parent_object_id=c.object_id AND d.parent_column_id=c.column_id
                               WHERE c.object_id=OBJECT_ID(@qualified)
                               ORDER BY c.column_id",
                    new Dictionary<string, object?> { ["@qualified"] = Qualify(table) });
                var pks = GetPrimaryKeyColumns(table);
                foreach (DataRow row in data.Rows)
                {
                    string typeName = Str(row, "data_type");
                    long maxLength = Long(row, "max_length") ?? 0;
                    int precision = (int)(Long(row, "precision") ?? 0);
                    int scale = (int)(Long(row, "scale") ?? 0);
                    string full = typeName;
                    if (typeName is "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary")
                    {
                        long len = typeName.StartsWith("n", StringComparison.OrdinalIgnoreCase) && maxLength > 0
                            ? maxLength / 2
                            : maxLength;
                        full += len < 0 ? "(max)" : "(" + len + ")";
                    }
                    else if (typeName is "decimal" or "numeric")
                    {
                        full += "(" + precision + "," + scale + ")";
                    }
                    columns.Add(new ColumnInfo
                    {
                        Name = Str(row, "column_name"),
                        DataType = typeName,
                        FullType = full,
                        Nullable = Bool(row, "is_nullable"),
                        DefaultValue = row["column_default"] == DBNull.Value ? null : Str(row, "column_default"),
                        PrimaryKey = pks.Contains(Str(row, "column_name")),
                        AutoIncrement = Bool(row, "is_identity"),
                        Ordinal = (int)(Long(row, "ordinal_position") ?? 0),
                        Length = maxLength < 0 ? null : maxLength,
                        Precision = precision,
                        Scale = scale,
                        IsBinary = typeName.Contains("binary", StringComparison.OrdinalIgnoreCase)
                            || typeName == "image"
                    });
                }
                break;
            }
            case DbKind.Oracle:
            {
                data = Query(@"SELECT c.COLUMN_NAME, c.DATA_TYPE, c.DATA_LENGTH, c.DATA_PRECISION, c.DATA_SCALE,
                                      c.NULLABLE, c.DATA_DEFAULT, c.COLUMN_ID,
                                      (SELECT cc.COMMENTS FROM all_col_comments cc
                                       WHERE cc.OWNER=c.OWNER AND cc.TABLE_NAME=c.TABLE_NAME AND cc.COLUMN_NAME=c.COLUMN_NAME) AS COMMENT
                               FROM all_tab_columns c
                               WHERE c.OWNER=:owner AND c.TABLE_NAME=:table
                               ORDER BY c.COLUMN_ID",
                    new Dictionary<string, object?>
                    {
                        [":owner"] = (string.IsNullOrWhiteSpace(table.Schema) ? table.Database : table.Schema).ToUpperInvariant(),
                        [":table"] = table.Name.ToUpperInvariant()
                    });
                var pks = GetPrimaryKeyColumns(table);
                foreach (DataRow row in data.Rows)
                {
                    string type = Str(row, "DATA_TYPE");
                    long length = Long(row, "DATA_LENGTH") ?? 0;
                    int precision = (int)(Long(row, "DATA_PRECISION") ?? 0);
                    int scale = (int)(Long(row, "DATA_SCALE") ?? 0);
                    string full = type;
                    if (type is "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" && length > 0)
                    {
                        full += "(" + length + ")";
                    }
                    else if (type is "NUMBER" && precision > 0)
                    {
                        full += "(" + precision + (scale > 0 ? "," + scale : "") + ")";
                    }
                    columns.Add(new ColumnInfo
                    {
                        Name = Str(row, "COLUMN_NAME"),
                        DataType = type,
                        FullType = full,
                        Nullable = Str(row, "NULLABLE").Equals("Y", StringComparison.OrdinalIgnoreCase),
                        DefaultValue = row["DATA_DEFAULT"] == DBNull.Value ? null : Str(row, "DATA_DEFAULT").Trim(),
                        PrimaryKey = pks.Contains(Str(row, "COLUMN_NAME")),
                        Comment = row["COMMENT"] == DBNull.Value ? null : Str(row, "COMMENT"),
                        Ordinal = (int)(Long(row, "COLUMN_ID") ?? 0),
                        Length = length,
                        Precision = precision,
                        Scale = scale,
                        IsBinary = type is "BLOB" or "RAW" or "LONG RAW"
                    });
                }
                break;
            }
        }
        return columns;
    }

    public HashSet<string> GetPrimaryKeyColumns(TableInfo table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            DataTable data;
            switch (Kind)
            {
                case DbKind.SQLite:
                    data = Query("PRAGMA table_info('" + table.Name.Replace("'", "''") + "')");
                    foreach (DataRow row in data.Rows)
                    {
                        if (Long(row, "pk") is > 0)
                        {
                            set.Add(Str(row, "name"));
                        }
                    }
                    break;
                case DbKind.MySQL:
                    data = Query(@"SELECT COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE
                                   WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@table AND CONSTRAINT_NAME='PRIMARY'
                                   ORDER BY ORDINAL_POSITION",
                        new Dictionary<string, object?>
                        {
                            ["@db"] = table.Database,
                            ["@table"] = table.Name
                        });
                    foreach (DataRow row in data.Rows)
                    {
                        set.Add(Convert.ToString(row[0], CultureInfo.InvariantCulture) ?? "");
                    }
                    break;
                case DbKind.PostgreSQL:
                    data = Query(@"SELECT kcu.column_name
                                   FROM information_schema.table_constraints tc
                                   JOIN information_schema.key_column_usage kcu
                                     ON tc.constraint_name=kcu.constraint_name
                                    AND tc.table_schema=kcu.table_schema
                                    AND tc.table_name=kcu.table_name
                                   WHERE tc.constraint_type='PRIMARY KEY'
                                     AND tc.table_schema=@schema AND tc.table_name=@table
                                   ORDER BY kcu.ordinal_position",
                        new Dictionary<string, object?>
                        {
                            ["@schema"] = string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema,
                            ["@table"] = table.Name
                        });
                    foreach (DataRow row in data.Rows)
                    {
                        set.Add(Convert.ToString(row[0], CultureInfo.InvariantCulture) ?? "");
                    }
                    break;
                case DbKind.SqlServer:
                    data = Query(@"SELECT c.name
                                   FROM sys.indexes i
                                   JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                                   JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                                   WHERE i.is_primary_key=1 AND i.object_id=OBJECT_ID(@qualified)
                                   ORDER BY ic.key_ordinal",
                        new Dictionary<string, object?> { ["@qualified"] = Qualify(table) });
                    foreach (DataRow row in data.Rows)
                    {
                        set.Add(Convert.ToString(row[0], CultureInfo.InvariantCulture) ?? "");
                    }
                    break;
                case DbKind.Oracle:
                    data = Query(@"SELECT cc.COLUMN_NAME
                                   FROM all_constraints ac
                                   JOIN all_cons_columns cc ON cc.OWNER=ac.OWNER AND cc.CONSTRAINT_NAME=ac.CONSTRAINT_NAME
                                   WHERE ac.CONSTRAINT_TYPE='P' AND ac.OWNER=:owner AND ac.TABLE_NAME=:table
                                   ORDER BY cc.POSITION",
                        new Dictionary<string, object?>
                        {
                            [":owner"] = (string.IsNullOrWhiteSpace(table.Schema) ? table.Database : table.Schema).ToUpperInvariant(),
                            [":table"] = table.Name.ToUpperInvariant()
                        });
                    foreach (DataRow row in data.Rows)
                    {
                        set.Add(Convert.ToString(row[0], CultureInfo.InvariantCulture) ?? "");
                    }
                    break;
            }
        }
        catch
        {
            // 元数据读取失败时当作无主键
        }
        return set;
    }

    public string GetPrimaryKeyName(TableInfo table)
    {
        try
        {
            switch (Kind)
            {
                case DbKind.MySQL:
                    return "PRIMARY";
                case DbKind.PostgreSQL:
                {
                    object? value = Scalar(@"SELECT conname FROM pg_constraint
                                             WHERE conrelid=(quote_ident(@schema)||'.'||quote_ident(@table))::regclass
                                               AND contype='p' LIMIT 1",
                        new Dictionary<string, object?>
                        {
                            ["@schema"] = string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema,
                            ["@table"] = table.Name
                        });
                    return value?.ToString() ?? "";
                }
                case DbKind.SqlServer:
                {
                    object? value = Scalar(@"SELECT kc.name FROM sys.key_constraints kc
                                             WHERE kc.parent_object_id=OBJECT_ID(@qualified) AND kc.type='PK'",
                        new Dictionary<string, object?> { ["@qualified"] = Qualify(table) });
                    return value?.ToString() ?? "";
                }
                case DbKind.Oracle:
                {
                    object? value = Scalar(@"SELECT CONSTRAINT_NAME FROM all_constraints
                                             WHERE CONSTRAINT_TYPE='P' AND OWNER=:owner AND TABLE_NAME=:table",
                        new Dictionary<string, object?>
                        {
                            [":owner"] = (string.IsNullOrWhiteSpace(table.Schema) ? table.Database : table.Schema).ToUpperInvariant(),
                            [":table"] = table.Name.ToUpperInvariant()
                        });
                    return value?.ToString() ?? "";
                }
                default:
                    return "";
            }
        }
        catch
        {
            return "";
        }
    }

    public List<IndexInfo> GetIndexes(TableInfo table)
    {
        var list = new List<IndexInfo>();
        try
        {
            switch (Kind)
            {
                case DbKind.SQLite:
                {
                    var indexes = Query("PRAGMA index_list('" + table.Name.Replace("'", "''") + "')");
                    foreach (DataRow row in indexes.Rows)
                    {
                        string name = Str(row, "name");
                        var info = new IndexInfo
                        {
                            Name = name,
                            Unique = Bool(row, "unique"),
                            Primary = Str(row, "origin").Equals("pk", StringComparison.OrdinalIgnoreCase)
                        };
                        var cols = Query("PRAGMA index_info('" + name.Replace("'", "''") + "')");
                        foreach (DataRow col in cols.Rows)
                        {
                            info.Columns.Add(Str(col, "name"));
                        }
                        list.Add(info);
                    }
                    break;
                }
                case DbKind.MySQL:
                {
                    var data = Query("SHOW INDEX FROM " + Qualify(table));
                    var map = new Dictionary<string, IndexInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in data.Rows)
                    {
                        string name = Str(row, "Key_name");
                        if (!map.TryGetValue(name, out var info))
                        {
                            info = new IndexInfo
                            {
                                Name = name,
                                Unique = !Bool(row, "Non_unique"),
                                Primary = name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
                            };
                            map[name] = info;
                            list.Add(info);
                        }
                        info.Columns.Add(Str(row, "Column_name"));
                    }
                    break;
                }
                case DbKind.PostgreSQL:
                {
                    var data = Query(@"SELECT indexname, indexdef FROM pg_indexes
                                       WHERE schemaname=@schema AND tablename=@table ORDER BY indexname",
                        new Dictionary<string, object?>
                        {
                            ["@schema"] = string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema,
                            ["@table"] = table.Name
                        });
                    foreach (DataRow row in data.Rows)
                    {
                        string definition = Str(row, "indexdef");
                        var info = new IndexInfo
                        {
                            Name = Str(row, "indexname"),
                            Unique = definition.Contains("UNIQUE INDEX", StringComparison.OrdinalIgnoreCase),
                            Primary = definition.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase),
                            Definition = definition
                        };
                        int open = definition.IndexOf('(');
                        int close = definition.LastIndexOf(')');
                        if (open > 0 && close > open)
                        {
                            foreach (string part in definition[(open + 1)..close].Split(','))
                            {
                                info.Columns.Add(part.Trim().Trim('"'));
                            }
                        }
                        list.Add(info);
                    }
                    break;
                }
                case DbKind.SqlServer:
                {
                    var data = Query(@"SELECT i.name AS index_name, i.is_unique, i.is_primary_key,
                                              c.name AS column_name, ic.key_ordinal
                                       FROM sys.indexes i
                                       JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                                       JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                                       WHERE i.object_id=OBJECT_ID(@qualified) AND i.name IS NOT NULL
                                       ORDER BY i.name, ic.key_ordinal",
                        new Dictionary<string, object?> { ["@qualified"] = Qualify(table) });
                    var map = new Dictionary<string, IndexInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in data.Rows)
                    {
                        string name = Str(row, "index_name");
                        if (!map.TryGetValue(name, out var info))
                        {
                            info = new IndexInfo
                            {
                                Name = name,
                                Unique = Bool(row, "is_unique"),
                                Primary = Bool(row, "is_primary_key")
                            };
                            map[name] = info;
                            list.Add(info);
                        }
                        info.Columns.Add(Str(row, "column_name"));
                    }
                    break;
                }
                case DbKind.Oracle:
                {
                    var data = Query(@"SELECT i.INDEX_NAME, i.UNIQUENESS, c.COLUMN_NAME, c.COLUMN_POSITION
                                       FROM all_indexes i
                                       JOIN all_ind_columns c ON c.INDEX_OWNER=i.OWNER AND c.INDEX_NAME=i.INDEX_NAME
                                       WHERE i.TABLE_OWNER=:owner AND i.TABLE_NAME=:table
                                       ORDER BY i.INDEX_NAME, c.COLUMN_POSITION",
                        new Dictionary<string, object?>
                        {
                            [":owner"] = (string.IsNullOrWhiteSpace(table.Schema) ? table.Database : table.Schema).ToUpperInvariant(),
                            [":table"] = table.Name.ToUpperInvariant()
                        });
                    var map = new Dictionary<string, IndexInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in data.Rows)
                    {
                        string name = Str(row, "INDEX_NAME");
                        if (!map.TryGetValue(name, out var info))
                        {
                            info = new IndexInfo
                            {
                                Name = name,
                                Unique = Str(row, "UNIQUENESS").Equals("UNIQUE", StringComparison.OrdinalIgnoreCase)
                            };
                            map[name] = info;
                            list.Add(info);
                        }
                        info.Columns.Add(Str(row, "COLUMN_NAME"));
                    }
                    break;
                }
            }
        }
        catch
        {
            // 部分数据库/低权限用户无法读取索引
        }
        return list;
    }

    public List<ForeignKeyInfo> GetForeignKeys(TableInfo table)
    {
        var list = new List<ForeignKeyInfo>();
        try
        {
            DataTable data;
            switch (Kind)
            {
                case DbKind.SQLite:
                {
                    data = Query("PRAGMA foreign_key_list('" + table.Name.Replace("'", "''") + "')");
                    var map = new Dictionary<string, ForeignKeyInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow row in data.Rows)
                    {
                        string id = Str(row, "id");
                        if (!map.TryGetValue(id, out var info))
                        {
                            info = new ForeignKeyInfo
                            {
                                Name = "FK_" + table.Name + "_" + id,
                                RefTable = Str(row, "table"),
                                OnDelete = Str(row, "on_delete"),
                                OnUpdate = Str(row, "on_update")
                            };
                            map[id] = info;
                            list.Add(info);
                        }
                        info.Columns.Add(Str(row, "from"));
                        info.RefColumns.Add(Str(row, "to"));
                    }
                    break;
                }
                case DbKind.MySQL:
                case DbKind.PostgreSQL:
                    data = Query(@"SELECT tc.constraint_name, kcu.column_name, kcu.table_schema, kcu.table_name,
                                          ccu.table_schema AS ref_schema, ccu.table_name AS ref_table,
                                          ccu.column_name AS ref_column, rc.delete_rule, rc.update_rule,
                                          kcu.ordinal_position
                                   FROM information_schema.table_constraints tc
                                   JOIN information_schema.key_column_usage kcu
                                     ON tc.constraint_name=kcu.constraint_name
                                    AND tc.table_schema=kcu.table_schema
                                    AND tc.table_name=kcu.table_name
                                   JOIN information_schema.constraint_column_usage ccu
                                     ON ccu.constraint_name=tc.constraint_name
                                    AND ccu.table_schema=tc.table_schema
                                   LEFT JOIN information_schema.referential_constraints rc
                                     ON rc.constraint_name=tc.constraint_name
                                    AND rc.constraint_schema=tc.table_schema
                                   WHERE tc.constraint_type='FOREIGN KEY'
                                     AND kcu.table_schema=@schema AND kcu.table_name=@table
                                   ORDER BY tc.constraint_name, kcu.ordinal_position",
                        new Dictionary<string, object?>
                        {
                            ["@schema"] = Kind == DbKind.MySQL
                                ? table.Database
                                : (string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema),
                            ["@table"] = table.Name
                        });
                    break;
                case DbKind.SqlServer:
                    data = Query(@"SELECT fk.name AS constraint_name, pc.name AS column_name,
                                          rs.name AS ref_schema, rt.name AS ref_table, rc.name AS ref_column,
                                          fk.delete_referential_action_desc AS delete_rule,
                                          fk.update_referential_action_desc AS update_rule,
                                          fkc.constraint_column_id AS ordinal_position
                                   FROM sys.foreign_keys fk
                                   JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
                                   JOIN sys.tables pt ON pt.object_id=fk.parent_object_id
                                   JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
                                   JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
                                   JOIN sys.tables rt ON rt.object_id=fk.referenced_object_id
                                   JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
                                   JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
                                   WHERE pt.name=@table AND ps.name=@schema
                                   ORDER BY fk.name, fkc.constraint_column_id",
                        new Dictionary<string, object?>
                        {
                            ["@schema"] = string.IsNullOrWhiteSpace(table.Schema) ? "dbo" : table.Schema,
                            ["@table"] = table.Name
                        });
                    break;
                case DbKind.Oracle:
                    data = Query(@"SELECT ac.CONSTRAINT_NAME, cc.COLUMN_NAME, ac.R_CONSTRAINT_NAME,
                                          rc.TABLE_NAME AS ref_table, rcc.COLUMN_NAME AS ref_column, cc.POSITION
                                   FROM all_constraints ac
                                   JOIN all_cons_columns cc ON cc.OWNER=ac.OWNER AND cc.CONSTRAINT_NAME=ac.CONSTRAINT_NAME
                                   JOIN all_constraints rc ON rc.OWNER=ac.R_OWNER AND rc.CONSTRAINT_NAME=ac.R_CONSTRAINT_NAME
                                   JOIN all_cons_columns rcc ON rcc.OWNER=rc.OWNER AND rcc.CONSTRAINT_NAME=rc.CONSTRAINT_NAME
                                                            AND rcc.POSITION=cc.POSITION
                                   WHERE ac.CONSTRAINT_TYPE='R' AND ac.OWNER=:owner AND ac.TABLE_NAME=:table
                                   ORDER BY ac.CONSTRAINT_NAME, cc.POSITION",
                        new Dictionary<string, object?>
                        {
                            [":owner"] = (string.IsNullOrWhiteSpace(table.Schema) ? table.Database : table.Schema).ToUpperInvariant(),
                            [":table"] = table.Name.ToUpperInvariant()
                        });
                    break;
                default:
                    return list;
            }

            var map2 = new Dictionary<string, ForeignKeyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in data.Rows)
            {
                string name = Str(row, "constraint_name");
                if (!map2.TryGetValue(name, out var info))
                {
                    info = new ForeignKeyInfo
                    {
                        Name = name,
                        RefTable = (data.Columns.Contains("ref_schema") && !string.IsNullOrWhiteSpace(Str(row, "ref_schema"))
                            ? Str(row, "ref_schema") + "." : "") + Str(row, "ref_table"),
                        OnDelete = data.Columns.Contains("delete_rule") ? Str(row, "delete_rule") : null,
                        OnUpdate = data.Columns.Contains("update_rule") ? Str(row, "update_rule") : null
                    };
                    map2[name] = info;
                    list.Add(info);
                }
                info.Columns.Add(Str(row, "column_name"));
                if (data.Columns.Contains("ref_column"))
                {
                    info.RefColumns.Add(Str(row, "ref_column"));
                }
            }
        }
        catch
        {
            // 低权限时忽略
        }
        return list;
    }

    public List<RoutineInfo> GetRoutines(string database)
    {
        var list = new List<RoutineInfo>();
        try
        {
            DataTable data;
            switch (Kind)
            {
                case DbKind.MySQL:
                    data = Query(@"SELECT ROUTINE_SCHEMA AS schema_name, ROUTINE_NAME AS routine_name, ROUTINE_TYPE
                                   FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=@db ORDER BY ROUTINE_NAME",
                        new Dictionary<string, object?> { ["@db"] = database });
                    break;
                case DbKind.PostgreSQL:
                    data = Query(@"SELECT n.nspname AS schema_name, p.proname AS routine_name,
                                          CASE WHEN p.prokind='p' THEN 'PROCEDURE' ELSE 'FUNCTION' END AS routine_type
                                   FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                                   WHERE n.nspname NOT IN ('pg_catalog','information_schema')
                                   ORDER BY n.nspname, p.proname");
                    break;
                case DbKind.SqlServer:
                    data = Query(@"SELECT s.name AS schema_name, o.name AS routine_name,
                                          CASE WHEN o.type='P' THEN 'PROCEDURE' ELSE 'FUNCTION' END AS routine_type
                                   FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
                                   WHERE o.type IN ('P','FN','IF','TF') ORDER BY s.name, o.name");
                    break;
                case DbKind.Oracle:
                    data = Query(@"SELECT OWNER AS schema_name, OBJECT_NAME AS routine_name, OBJECT_TYPE AS routine_type
                                   FROM all_objects WHERE OBJECT_TYPE IN ('FUNCTION','PROCEDURE') AND OWNER=:owner
                                   ORDER BY OBJECT_NAME",
                        new Dictionary<string, object?> { [":owner"] = database.ToUpperInvariant() });
                    break;
                default:
                    return list;
            }
            foreach (DataRow row in data.Rows)
            {
                list.Add(new RoutineInfo
                {
                    Database = database,
                    Schema = data.Columns.Contains("schema_name") ? Str(row, "schema_name") : "",
                    Name = Str(row, "routine_name"),
                    Type = Str(row, "routine_type")
                });
            }
        }
        catch
        {
            // ignore
        }
        return list;
    }

    public string GetTableDdl(TableInfo table)
    {
        try
        {
            switch (Kind)
            {
                case DbKind.SQLite:
                    return Convert.ToString(Scalar("SELECT sql FROM sqlite_master WHERE name=@name AND type IN ('table','view')",
                        new Dictionary<string, object?> { ["@name"] = table.Name }), CultureInfo.InvariantCulture) ?? "";
                case DbKind.MySQL:
                {
                    var data = Query("SHOW CREATE TABLE " + Qualify(table));
                    return data.Rows.Count > 0 ? Convert.ToString(data.Rows[0][1], CultureInfo.InvariantCulture) ?? "" : "";
                }
                default:
                {
                    var columns = GetColumns(table);
                    var indexes = GetIndexes(table);
                    var fks = GetForeignKeys(table);
                    return BuildCreateTable(table, columns, indexes, fks);
                }
            }
        }
        catch (Exception ex)
        {
            return "-- 读取 DDL 失败：" + ex.Message;
        }
    }

    public string GetViewDdl(TableInfo view)
    {
        try
        {
            switch (Kind)
            {
                case DbKind.SQLite:
                    return Convert.ToString(Scalar("SELECT sql FROM sqlite_master WHERE name=@name AND type='view'",
                        new Dictionary<string, object?> { ["@name"] = view.Name }), CultureInfo.InvariantCulture) ?? "";
                case DbKind.MySQL:
                {
                    var data = Query("SHOW CREATE VIEW " + Qualify(view));
                    return data.Rows.Count > 0 ? Convert.ToString(data.Rows[0][1], CultureInfo.InvariantCulture) ?? "" : "";
                }
                case DbKind.PostgreSQL:
                {
                    object? def = Scalar(@"SELECT pg_get_viewdef((quote_ident(n.nspname)||'.'||quote_ident(c.relname))::regclass, true)
                                           FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                                           WHERE c.relname=@name AND n.nspname=@schema",
                        new Dictionary<string, object?>
                        {
                            ["@name"] = view.Name,
                            ["@schema"] = string.IsNullOrWhiteSpace(view.Schema) ? "public" : view.Schema
                        });
                    return "CREATE OR REPLACE VIEW " + Qualify(view) + " AS\n" + (def?.ToString() ?? "");
                }
                case DbKind.SqlServer:
                {
                    object? def = Scalar("SELECT OBJECT_DEFINITION(OBJECT_ID(@qualified))",
                        new Dictionary<string, object?> { ["@qualified"] = Qualify(view) });
                    return def?.ToString() ?? "";
                }
                case DbKind.Oracle:
                {
                    object? text = Scalar(@"SELECT TEXT FROM all_views WHERE OWNER=:owner AND VIEW_NAME=:name",
                        new Dictionary<string, object?>
                        {
                            [":owner"] = (string.IsNullOrWhiteSpace(view.Schema) ? view.Database : view.Schema).ToUpperInvariant(),
                            [":name"] = view.Name.ToUpperInvariant()
                        });
                    string body = text?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        body = Convert.ToString(Scalar(@"SELECT DBMS_METADATA.GET_DDL('VIEW', :name, :owner) FROM dual",
                            new Dictionary<string, object?>
                            {
                                [":name"] = view.Name.ToUpperInvariant(),
                                [":owner"] = (string.IsNullOrWhiteSpace(view.Schema) ? view.Database : view.Schema).ToUpperInvariant()
                            }), CultureInfo.InvariantCulture) ?? "";
                        return body;
                    }
                    return "CREATE OR REPLACE VIEW " + Qualify(view) + " AS\n" + body;
                }
                default:
                    return "";
            }
        }
        catch (Exception ex)
        {
            return "-- 读取视图定义失败：" + ex.Message;
        }
    }

    public string GetRoutineDdl(RoutineInfo routine)
    {
        try
        {
            switch (Kind)
            {
                case DbKind.MySQL:
                {
                    var data = Query("SHOW CREATE " + routine.Type + " "
                        + Qualify(routine.Database, routine.Schema, routine.Name));
                    return data.Rows.Count > 0 && data.Columns.Count > 2
                        ? Convert.ToString(data.Rows[0][2], CultureInfo.InvariantCulture) ?? ""
                        : "";
                }
                case DbKind.PostgreSQL:
                {
                    object? def = Scalar(@"SELECT pg_get_functiondef(p.oid)
                                           FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                                           WHERE p.proname=@name AND n.nspname=@schema LIMIT 1",
                        new Dictionary<string, object?>
                        {
                            ["@name"] = routine.Name,
                            ["@schema"] = string.IsNullOrWhiteSpace(routine.Schema) ? "public" : routine.Schema
                        });
                    return def?.ToString() ?? "";
                }
                case DbKind.SqlServer:
                {
                    object? def = Scalar("SELECT OBJECT_DEFINITION(OBJECT_ID(@qualified))",
                        new Dictionary<string, object?> { ["@qualified"] = Qualify(routine.Database, routine.Schema, routine.Name) });
                    return def?.ToString() ?? "";
                }
                case DbKind.Oracle:
                {
                    object? def = Scalar("SELECT DBMS_METADATA.GET_DDL(:type, :name, :owner) FROM dual",
                        new Dictionary<string, object?>
                        {
                            [":type"] = routine.Type,
                            [":name"] = routine.Name.ToUpperInvariant(),
                            [":owner"] = (string.IsNullOrWhiteSpace(routine.Schema) ? routine.Database : routine.Schema).ToUpperInvariant()
                        });
                    return def?.ToString() ?? "";
                }
                default:
                    return "";
            }
        }
        catch (Exception ex)
        {
            return "-- 读取定义失败：" + ex.Message;
        }
    }

    /// <summary>根据列/索引/外键生成 CREATE TABLE 语句（用于非 MySQL / SQLite 及跨库建表）。</summary>
    public string BuildCreateTable(TableInfo table, List<ColumnInfo> columns, List<IndexInfo> indexes,
        List<ForeignKeyInfo> fks, DbKind? typeSource = null, bool includeIfNotExists = false)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ");
        if (includeIfNotExists && Kind != DbKind.Oracle && Kind != DbKind.SqlServer)
        {
            sb.Append("IF NOT EXISTS ");
        }
        sb.Append(Qualify(table)).AppendLine(" (");
        var definitions = new List<string>();
        foreach (var column in columns.OrderBy(c => c.Ordinal))
        {
            string type = typeSource.HasValue ? NvSqlUtil.MapType(typeSource.Value, Kind, column) : column.FullType;
            if (string.IsNullOrWhiteSpace(type))
            {
                type = "TEXT";
            }
            var line = new StringBuilder("  ").Append(Quote(column.Name)).Append(' ').Append(type);
            if (column.AutoIncrement && Kind != DbKind.MySQL)
            {
                line.Append(Kind switch
                {
                    DbKind.PostgreSQL => column.FullType.Contains("serial", StringComparison.OrdinalIgnoreCase)
                        ? "" : " GENERATED BY DEFAULT AS IDENTITY",
                    DbKind.SqlServer => " IDENTITY(1,1)",
                    DbKind.Oracle => " GENERATED BY DEFAULT AS IDENTITY",
                    _ => ""
                });
            }
            if (!column.Nullable)
            {
                line.Append(" NOT NULL");
            }
            if (!string.IsNullOrWhiteSpace(column.DefaultValue) && !column.AutoIncrement)
            {
                string def = column.DefaultValue!.Trim();
                if (Kind == DbKind.PostgreSQL && def.Contains("nextval(", StringComparison.OrdinalIgnoreCase))
                {
                    // serial 默认值不再重复输出
                }
                else
                {
                    line.Append(" DEFAULT ").Append(def);
                }
            }
            if (column.AutoIncrement && Kind == DbKind.MySQL)
            {
                line.Append(" AUTO_INCREMENT");
            }
            if (Kind == DbKind.MySQL && !string.IsNullOrWhiteSpace(column.Comment))
            {
                line.Append(" COMMENT '").Append(column.Comment!.Replace("'", "''")).Append('\'');
            }
            definitions.Add(line.ToString());
        }

        var pkColumns = columns.Where(c => c.PrimaryKey).Select(c => Quote(c.Name)).ToList();
        if (pkColumns.Count > 0)
        {
            definitions.Add("  PRIMARY KEY (" + string.Join(", ", pkColumns) + ")");
        }
        foreach (var fk in fks)
        {
            if (fk.Columns.Count == 0 || fk.RefColumns.Count == 0)
            {
                continue;
            }
            definitions.Add("  CONSTRAINT " + Quote(fk.Name) + " FOREIGN KEY (" +
                string.Join(", ", fk.Columns.Select(Quote)) + ") REFERENCES " + fk.RefTable + " (" +
                string.Join(", ", fk.RefColumns.Select(Quote)) + ")"
                + (!string.IsNullOrWhiteSpace(fk.OnDelete) ? " ON DELETE " + fk.OnDelete : "")
                + (!string.IsNullOrWhiteSpace(fk.OnUpdate) ? " ON UPDATE " + fk.OnUpdate : ""));
        }
        sb.AppendLine(string.Join("," + Environment.NewLine, definitions));
        sb.AppendLine(");");

        foreach (var index in indexes.Where(i => !i.Primary && i.Columns.Count > 0))
        {
            sb.Append("CREATE ").Append(index.Unique ? "UNIQUE " : "").Append("INDEX ")
                .Append(Quote(index.Name)).Append(" ON ").Append(Qualify(table)).Append(" (")
                .Append(string.Join(", ", index.Columns.Select(Quote))).AppendLine(");");
        }

        if (Kind == DbKind.PostgreSQL)
        {
            foreach (var column in columns.Where(c => !string.IsNullOrWhiteSpace(c.Comment)))
            {
                sb.Append("COMMENT ON COLUMN ").Append(Qualify(table)).Append('.').Append(Quote(column.Name))
                    .Append(" IS '").Append(column.Comment!.Replace("'", "''")).AppendLine("';");
            }
            if (!string.IsNullOrWhiteSpace(table.Comment))
            {
                sb.Append("COMMENT ON TABLE ").Append(Qualify(table)).Append(" IS '")
                    .Append(table.Comment.Replace("'", "''")).AppendLine("';");
            }
        }
        return sb.ToString();
    }

    // ================= 用户 / 服务器 =================

    public List<UserInfo> GetUsers()
    {
        var list = new List<UserInfo>();
        try
        {
            DataTable data = Kind switch
            {
                DbKind.MySQL => Query("SELECT User, Host FROM mysql.user ORDER BY User, Host"),
                DbKind.PostgreSQL => Query("SELECT usename, CASE WHEN usesuper THEN '超级用户' ELSE '' END AS info FROM pg_user ORDER BY usename"),
                DbKind.SqlServer => Query(@"SELECT name, CASE WHEN type='S' THEN 'SQL 登录' ELSE type_desc END AS info
                                            FROM sys.server_principals WHERE type IN ('S','U','G') ORDER BY name"),
                DbKind.Oracle => Query("SELECT username, account_status FROM all_users ORDER BY username"),
                _ => new DataTable()
            };
            foreach (DataRow row in data.Rows)
            {
                list.Add(new UserInfo
                {
                    Name = Convert.ToString(row[0], CultureInfo.InvariantCulture) ?? "",
                    Host = data.Columns.Count > 2 ? "" : (Kind == DbKind.MySQL ? Convert.ToString(row[1], CultureInfo.InvariantCulture) ?? "" : ""),
                    Info = Kind == DbKind.MySQL
                        ? ""
                        : (data.Columns.Count > 1 ? Convert.ToString(row[1], CultureInfo.InvariantCulture) ?? "" : "")
                });
            }
            if (Kind == DbKind.MySQL)
            {
                // 第二列是 Host
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Host = Convert.ToString(data.Rows[i][1], CultureInfo.InvariantCulture) ?? "";
                }
            }
        }
        catch
        {
            // ignore
        }
        return list;
    }

    public DataTable GetPrivileges(UserInfo user)
    {
        try
        {
            return Kind switch
            {
                DbKind.MySQL => Query("SHOW GRANTS FOR " + Quote(user.Name) + "@" + Quote(user.Host)),
                DbKind.PostgreSQL => Query(@"SELECT grantee, table_schema, table_name, privilege_type
                                             FROM information_schema.role_table_grants WHERE grantee=@user",
                    new Dictionary<string, object?> { ["@user"] = user.Name }),
                DbKind.SqlServer => Query(@"SELECT pr.name AS role_name
                                           FROM sys.server_role_members m
                                           JOIN sys.server_principals r ON r.principal_id=m.role_principal_id
                                           JOIN sys.server_principals pr ON pr.principal_id=m.member_principal_id
                                           WHERE pr.name=@user",
                    new Dictionary<string, object?> { ["@user"] = user.Name }),
                DbKind.Oracle => Query(@"SELECT privilege, admin_option FROM dba_sys_privs WHERE grantee=:user",
                    new Dictionary<string, object?> { [":user"] = user.Name.ToUpperInvariant() }),
                _ => new DataTable()
            };
        }
        catch
        {
            return new DataTable();
        }
    }

    public DataTable GetProcesses()
    {
        try
        {
            return Kind switch
            {
                DbKind.MySQL => Query("SELECT id, user, host, db, command, time, state, LEFT(info, 500) AS info FROM information_schema.processlist ORDER BY id"),
                DbKind.PostgreSQL => Query(@"SELECT pid, usename, client_addr, datname, state,
                                                    EXTRACT(EPOCH FROM (now()-query_start))::int AS seconds,
                                                    LEFT(query, 500) AS query
                                             FROM pg_stat_activity WHERE pid <> pg_backend_pid() ORDER BY pid"),
                DbKind.SqlServer => Query(@"SELECT session_id, login_name, host_name, DB_NAME(database_id) AS database_name,
                                                   status, cpu_time, memory_usage, program_name
                                            FROM sys.dm_exec_sessions WHERE is_user_process=1 ORDER BY session_id"),
                DbKind.Oracle => Query(@"SELECT sid, serial#, username, program, status, machine, osuser
                                         FROM v$session WHERE type='USER' ORDER BY sid"),
                _ => new DataTable()
            };
        }
        catch
        {
            return new DataTable();
        }
    }

    public void KillProcess(string id)
    {
        switch (Kind)
        {
            case DbKind.MySQL:
                Execute("KILL " + id);
                break;
            case DbKind.PostgreSQL:
                Execute("SELECT pg_terminate_backend(" + id + ")");
                break;
            case DbKind.SqlServer:
                Execute("KILL " + id);
                break;
            case DbKind.Oracle:
            {
                string[] parts = id.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length >= 2)
                {
                    Execute("ALTER SYSTEM KILL SESSION '" + parts[0] + "," + parts[1] + "'");
                }
                break;
            }
            default:
                throw new NotSupportedException("当前数据库不支持终止会话。");
        }
    }

    public DataTable GetServerVariables()
    {
        try
        {
            return Kind switch
            {
                DbKind.MySQL => Query("SHOW VARIABLES"),
                DbKind.PostgreSQL => Query("SELECT name, setting AS value, unit, short_desc AS description FROM pg_settings ORDER BY name"),
                DbKind.SqlServer => Query(@"SELECT name, CAST(value_in_use AS NVARCHAR(200)) AS value, description
                                            FROM sys.configurations ORDER BY name"),
                DbKind.Oracle => Query("SELECT name, value, description FROM v$parameter ORDER BY name"),
                _ => new DataTable()
            };
        }
        catch
        {
            return new DataTable();
        }
    }

    public List<string> GetTableNamesForBackup()
    {
        try
        {
            return GetTables(CurrentDatabase).Select(t => Qualify(t)).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public string GetDatabaseSizeText()
    {
        try
        {
            if (Kind == DbKind.SQLite)
            {
                long pages = Convert.ToInt64(Scalar("PRAGMA page_count") ?? 0);
                long pageSize = Convert.ToInt64(Scalar("PRAGMA page_size") ?? 0);
                return FormatSize(pages * pageSize);
            }
            object? value = Kind switch
            {
                DbKind.MySQL => Scalar(@"SELECT ROUND(SUM(DATA_LENGTH+INDEX_LENGTH)/1024/1024, 2)
                                         FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"),
                DbKind.PostgreSQL => Scalar("SELECT pg_size_pretty(pg_database_size(current_database()))"),
                DbKind.SqlServer => Scalar(@"SELECT CAST(SUM(size)*8.0/1024 AS DECIMAL(18,2))
                                             FROM sys.database_files"),
                DbKind.Oracle => Scalar(@"SELECT ROUND(SUM(bytes)/1024/1024, 2) FROM user_segments"),
                _ => null
            };
            return value?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return size.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
