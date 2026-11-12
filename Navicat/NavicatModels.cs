using System.Text.Json.Serialization;

namespace MiniMonitor.Navicat;

internal enum DbKind
{
    SQLite,
    MySQL,
    PostgreSQL,
    SqlServer,
    Oracle
}

internal static class DbKindExtensions
{
    public static string DisplayName(this DbKind kind) => kind switch
    {
        DbKind.SQLite => "SQLite",
        DbKind.MySQL => "MySQL / MariaDB",
        DbKind.PostgreSQL => "PostgreSQL",
        DbKind.SqlServer => "SQL Server",
        DbKind.Oracle => "Oracle",
        _ => kind.ToString()
    };

    public static string ShortName(this DbKind kind) => kind switch
    {
        DbKind.SQLite => "SQLite",
        DbKind.MySQL => "MySQL",
        DbKind.PostgreSQL => "PostgreSQL",
        DbKind.SqlServer => "SQL Server",
        DbKind.Oracle => "Oracle",
        _ => kind.ToString()
    };

    public static int DefaultPort(this DbKind kind) => kind switch
    {
        DbKind.MySQL => 3306,
        DbKind.PostgreSQL => 5432,
        DbKind.SqlServer => 1433,
        DbKind.Oracle => 1521,
        _ => 0
    };

    public static NvIcon Icon(this DbKind kind) => kind switch
    {
        DbKind.SQLite => NvIcon.Sqlite,
        DbKind.MySQL => NvIcon.MySQL,
        DbKind.PostgreSQL => NvIcon.PostgreSQL,
        DbKind.SqlServer => NvIcon.SqlServer,
        DbKind.Oracle => NvIcon.Oracle,
        _ => NvIcon.Database
    };
}

internal sealed class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "localhost";
    public string Group { get; set; } = "";
    public DbKind Kind { get; set; } = DbKind.MySQL;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool SavePassword { get; set; } = true;
    public string Color { get; set; } = "#2B6CB0";
    public bool UseSsl { get; set; }
    public string SslMode { get; set; } = "Preferred";
    public string Charset { get; set; } = "";
    public int ConnectTimeout { get; set; } = 15;
    public string RawConnectionString { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime LastUsed { get; set; } = DateTime.Now;

    // SSH 隧道
    public bool UseSsh { get; set; }
    public string SshHost { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = "";
    public string SshPassword { get; set; } = "";
    public string SshKeyFile { get; set; } = "";
    public string SshPassphrase { get; set; } = "";
    public string SshRemoteHost { get; set; } = "127.0.0.1";
    public int SshRemotePort { get; set; }
    public bool SshSavePassword { get; set; } = true;

    public ConnectionProfile Clone()
    {
        var copy = (ConnectionProfile)MemberwiseClone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = Name + " - 副本";
        return copy;
    }

    public ConnectionProfile CopyForConnect()
    {
        return (ConnectionProfile)MemberwiseClone();
    }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Group) ? Name : Group + " / " + Name;

    [JsonIgnore]
    public string ServerLabel => Kind == DbKind.SQLite ? Database : Host + ":" + Port;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = Kind == DbKind.SQLite
                ? (string.IsNullOrWhiteSpace(Database) ? "SQLite" : Path.GetFileNameWithoutExtension(Database))
                : (string.IsNullOrWhiteSpace(Host) ? "localhost" : Host);
        }
        if (Port <= 0)
        {
            Port = Kind.DefaultPort();
        }
        if (SshRemotePort <= 0)
        {
            SshRemotePort = Port;
        }
        if (string.IsNullOrWhiteSpace(SshRemoteHost))
        {
            SshRemoteHost = "127.0.0.1";
        }
        if (string.IsNullOrWhiteSpace(Color))
        {
            Color = "#2B6CB0";
        }
    }
}

internal sealed class ColumnInfo
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string FullType { get; set; } = "";
    public bool Nullable { get; set; } = true;
    public string? DefaultValue { get; set; }
    public bool PrimaryKey { get; set; }
    public bool AutoIncrement { get; set; }
    public bool IsBinary { get; set; }
    public string? Comment { get; set; }
    public int Ordinal { get; set; }
    public long? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }

    public string DisplayType => string.IsNullOrWhiteSpace(FullType) ? DataType : FullType;
}

internal sealed class IndexInfo
{
    public string Name { get; set; } = "";
    public bool Unique { get; set; }
    public bool Primary { get; set; }
    public List<string> Columns { get; set; } = new();
    public string? Definition { get; set; }

    public string ColumnText => string.Join(", ", Columns);
}

internal sealed class ForeignKeyInfo
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public string RefTable { get; set; } = "";
    public List<string> RefColumns { get; set; } = new();
    public string? OnDelete { get; set; }
    public string? OnUpdate { get; set; }

    public string ColumnText => string.Join(", ", Columns);
    public string RefColumnText => string.Join(", ", RefColumns);
}

internal sealed class TableInfo
{
    public string Database { get; set; } = "";
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public long? Rows { get; set; }
    public string? Comment { get; set; }
    public bool IsView { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Schema) ? Name : Schema + "." + Name;
}

internal sealed class RoutineInfo
{
    public string Database { get; set; } = "";
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string DisplayName => string.IsNullOrEmpty(Schema) ? Name : Schema + "." + Name;
}

internal sealed class UserInfo
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string? Info { get; set; }
    public string DisplayName => string.IsNullOrEmpty(Host) ? Name : Name + "@" + Host;
}

internal sealed class QueryResultSet
{
    public List<string> Columns { get; } = new();
    public List<object?[]> Rows { get; } = new();
    public int AffectedRows { get; set; }
    public string? Message { get; set; }
    public bool HasGrid => Columns.Count > 0;
}

internal sealed class BatchResult
{
    public List<QueryResultSet> ResultSets { get; } = new();
    public TimeSpan Elapsed { get; set; }
    public List<string> Messages { get; } = new();
    public bool Cancelled { get; set; }
}

internal sealed class NavicatSettings
{
    public int PageSize { get; set; } = 500;
    public int MaxResultRows { get; set; } = 10000;
    public bool ConfirmDangerous { get; set; } = true;
    public bool AutoCommit { get; set; } = true;
    public float EditorFontSize { get; set; } = 10.5f;
    public string ExportFolder { get; set; } = "";
    public string BackupFolder { get; set; } = "";
    public bool ShowWelcomePage { get; set; } = true;
    public bool WordWrap { get; set; }
    public bool HighlightCurrentLine { get; set; } = true;
}

internal enum NvTaskType
{
    Query,
    Backup,
    Export
}

internal sealed class NvScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新任务";
    public NvTaskType Type { get; set; } = NvTaskType.Query;
    public string ProfileId { get; set; } = "";
    public string Database { get; set; } = "";
    public string Sql { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string ExportFormat { get; set; } = "CSV";
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 60;
    public string DailyTime { get; set; } = "02:00";
    public string ScheduleType { get; set; } = "Interval";
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public string LastResult { get; set; } = "";
}

internal sealed class NvNodeTag
{
    public string Kind { get; set; } = "";
    public ConnectionProfile? Profile { get; set; }
    public DbSession? Session { get; set; }
    public string? Database { get; set; }
    public string? Schema { get; set; }
    public string? Name { get; set; }
    public string? Extra { get; set; }
    public string State { get; set; } = "";
    public bool IsView { get; set; }
}

/// <summary>传输/同步/导出时选择的表。</summary>
internal sealed class TableChoice
{
    public string Database { get; set; } = "";
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName => string.IsNullOrEmpty(Schema) ? Name : Schema + "." + Name;
    public bool Selected { get; set; } = true;
}

internal sealed class DbSearchHit
{
    public string Database { get; set; } = "";
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Display => (string.IsNullOrEmpty(Schema) ? "" : Schema + ".") + Name;
}
