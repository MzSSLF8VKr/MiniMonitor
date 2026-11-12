using System.Data;
using System.Globalization;
using System.Text;

namespace MiniMonitor.Navicat;

/// <summary>跨库传输 / 同步共用辅助。</summary>
internal static class NvSyncUtil
{
    public static TableInfo TargetTable(DbSession target, TableInfo source)
    {
        var table = new TableInfo { Name = source.Name };
        switch (target.Kind)
        {
            case DbKind.MySQL:
                table.Database = target.CurrentDatabase;
                break;
            case DbKind.PostgreSQL:
                table.Schema = "public";
                break;
            case DbKind.SqlServer:
                table.Schema = "dbo";
                break;
            case DbKind.Oracle:
                table.Schema = string.IsNullOrWhiteSpace(target.Profile.Username)
                    ? target.CurrentDatabase.ToUpperInvariant()
                    : target.Profile.Username.ToUpperInvariant();
                break;
        }
        return table;
    }

    public static string CreateTableOnTarget(DbSession source, DbSession target, TableInfo sourceTable,
        out TableInfo targetTable)
    {
        targetTable = TargetTable(target, sourceTable);
        var columns = source.GetColumns(sourceTable);
        var indexes = source.GetIndexes(sourceTable);
        return target.BuildCreateTable(targetTable, columns, indexes, new List<ForeignKeyInfo>(),
            typeSource: source.Kind, includeIfNotExists: true);
    }

    /// <summary>SET IDENTITY_INSERT 需要 schema.table（不能带数据库前缀）。</summary>
    public static string IdentityInsertTable(DbSession target, TableInfo table)
    {
        if (target.Kind != DbKind.SqlServer)
        {
            return target.Qualify(table);
        }
        return string.IsNullOrWhiteSpace(table.Schema)
            ? target.Quote(table.Name)
            : target.Quote(table.Schema) + "." + target.Quote(table.Name);
    }

    public static string AddColumnSql(DbSession target, TableInfo targetTable, ColumnInfo sourceColumn,
        DbKind sourceKind)
    {
        string type = NvSqlUtil.MapType(sourceKind, target.Kind, sourceColumn);
        var sb = new StringBuilder("ALTER TABLE ").Append(target.Qualify(targetTable)).Append(' ');
        if (target.Kind == DbKind.Oracle)
        {
            sb.Append("ADD (").Append(target.Quote(sourceColumn.Name)).Append(' ').Append(type);
            if (!sourceColumn.Nullable)
            {
                sb.Append(" NOT NULL");
            }
            if (!string.IsNullOrWhiteSpace(sourceColumn.DefaultValue))
            {
                sb.Append(" DEFAULT ").Append(sourceColumn.DefaultValue);
            }
            sb.Append(')');
            return sb.ToString();
        }
        if (target.Kind == DbKind.SqlServer)
        {
            sb.Append("ADD ").Append(target.Quote(sourceColumn.Name)).Append(' ').Append(type);
        }
        else
        {
            sb.Append("ADD COLUMN ").Append(target.Quote(sourceColumn.Name)).Append(' ').Append(type);
        }
        if (!sourceColumn.Nullable)
        {
            sb.Append(" NOT NULL");
        }
        if (!string.IsNullOrWhiteSpace(sourceColumn.DefaultValue))
        {
            sb.Append(" DEFAULT ").Append(sourceColumn.DefaultValue);
        }
        return sb.ToString();
    }

    public static string AlterColumnTypeSql(DbSession target, TableInfo targetTable, string column,
        string newType, bool notNull, string? defaultValue, string? comment)
    {
        string table = target.Qualify(targetTable);
        return target.Kind switch
        {
            DbKind.MySQL => "ALTER TABLE " + table + " MODIFY COLUMN " + target.Quote(column) + " " + newType
                + (notNull ? " NOT NULL" : " NULL")
                + (!string.IsNullOrWhiteSpace(defaultValue) ? " DEFAULT " + defaultValue : "")
                + (!string.IsNullOrWhiteSpace(comment) ? " COMMENT '" + comment.Replace("'", "''") + "'" : ""),
            DbKind.PostgreSQL => "ALTER TABLE " + table + " ALTER COLUMN " + target.Quote(column) + " TYPE " + newType,
            DbKind.SqlServer => "ALTER TABLE " + table + " ALTER COLUMN " + target.Quote(column) + " " + newType
                + (notNull ? " NOT NULL" : " NULL"),
            DbKind.Oracle => "ALTER TABLE " + table + " MODIFY (" + target.Quote(column) + " " + newType
                + (notNull ? " NOT NULL" : "") + ")",
            _ => ""
        };
    }

    public static string NormalizeType(string type)
    {
        return type.ToLowerInvariant().Replace(" ", "").Replace("`", "").Replace("\"", "")
            .Replace("[", "").Replace("]", "");
    }

    public static object? ConvertForTarget(object? value, ColumnInfo target)
    {
        if (value == null || value == DBNull.Value)
        {
            return DBNull.Value;
        }
        if (value is byte[])
        {
            return value;
        }
        string type = target.DataType.ToLowerInvariant();
        string raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        try
        {
            if (type.Contains("int") || type is "serial" or "bigserial" or "smallserial")
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            if (type.Contains("decimal") || type.Contains("numeric") || type == "number" || type.Contains("money"))
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            if (type.Contains("double") || type.Contains("float") || type == "real")
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            if (type.Contains("bool") || type == "bit")
            {
                if (value is bool b)
                {
                    return b;
                }
                return raw.Equals("1") || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            if (type.Contains("date") || type.Contains("time"))
            {
                if (value is DateTime dt)
                {
                    return dt;
                }
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                {
                    return parsed;
                }
            }
            if (type.Contains("char") || type.Contains("text") || type.Contains("clob")
                || type.Contains("json") || type.Contains("uuid"))
            {
                return raw;
            }
        }
        catch
        {
            // 保持原值，让数据库给出明确错误
        }
        return value;
    }

    public static string KeyOf(DataRow row, IReadOnlyList<string> columns)
    {
        var sb = new StringBuilder();
        foreach (string column in columns)
        {
            object? value = row[column];
            if (value is byte[] bytes)
            {
                sb.Append(Convert.ToBase64String(bytes));
            }
            else
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            sb.Append('\u0001');
        }
        return sb.ToString();
    }

    public static bool ValuesEqual(object? a, object? b)
    {
        if (a == null || a == DBNull.Value)
        {
            return b == null || b == DBNull.Value;
        }
        if (b == null || b == DBNull.Value)
        {
            return false;
        }
        if (a is byte[] ba && b is byte[] bb)
        {
            return ba.AsSpan().SequenceEqual(bb);
        }
        if (a is DateTime da && b is DateTime db)
        {
            return Math.Abs((da - db).TotalSeconds) < 0.001;
        }
        try
        {
            return Convert.ToString(a, CultureInfo.InvariantCulture)
                == Convert.ToString(b, CultureInfo.InvariantCulture);
        }
        catch
        {
            return Equals(a, b);
        }
    }
}
