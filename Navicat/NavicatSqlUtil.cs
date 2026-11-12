using System.Globalization;
using System.Text;

namespace MiniMonitor.Navicat;

/// <summary>SQL 脚本拆分 / 字面量生成 / 格式化等通用工具。</summary>
internal static class NvSqlUtil
{
    private static readonly HashSet<string> MajorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "UNION",
        "UNION ALL", "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM", "LEFT JOIN",
        "RIGHT JOIN", "INNER JOIN", "FULL JOIN", "CROSS JOIN", "JOIN", "ON", "CREATE TABLE",
        "CREATE VIEW", "ALTER TABLE", "DROP TABLE", "WITH"
    };

    /// <summary>拆分多条 SQL；正确处理单引号、双引号、反引号、注释与 DELIMITER 指令。</summary>
    public static List<string> SplitScript(string script)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(script))
        {
            return result;
        }

        string delimiter = ";";
        var buffer = new StringBuilder();
        int i = 0;
        int n = script.Length;

        while (i < n)
        {
            // 行首 DELIMITER 指令（MySQL 客户端语法）
            int lineStart = i;
            while (lineStart > 0 && script[lineStart - 1] != '\n')
            {
                lineStart--;
            }
            if (IsLineStart(script, i) && i + 9 <= n
                && script.AsSpan(i, 9).Equals("DELIMITER", StringComparison.OrdinalIgnoreCase))
            {
                int lineEnd = script.IndexOf('\n', i);
                if (lineEnd < 0)
                {
                    lineEnd = n;
                }
                string value = script[(i + 9)..lineEnd].Trim();
                if (value.Length > 0)
                {
                    delimiter = value;
                }
                i = lineEnd < n ? lineEnd + 1 : n;
                continue;
            }

            char c = script[i];
            if (c == '\'' || c == '"' || c == '`')
            {
                int end = SkipQuoted(script, i, c);
                buffer.Append(script, i, end - i);
                i = end;
                continue;
            }
            if (delimiter.Length > 0 && i + delimiter.Length <= n
                && string.Compare(script, i, delimiter, 0, delimiter.Length, StringComparison.Ordinal) == 0)
            {
                string statement = buffer.ToString().Trim();
                if (HasSqlContent(statement))
                {
                    result.Add(statement);
                }
                buffer.Clear();
                i += delimiter.Length;
                continue;
            }
            if (c == '$' && TryDollarQuoted(script, i, out int dollarEnd))
            {
                buffer.Append(script, i, dollarEnd - i);
                i = dollarEnd;
                continue;
            }
            if (c == '-' && i + 1 < n && script[i + 1] == '-')
            {
                int end = script.IndexOf('\n', i);
                if (end < 0)
                {
                    end = n;
                }
                buffer.Append(script, i, end - i);
                i = end;
                continue;
            }
            if (c == '#')
            {
                int end = script.IndexOf('\n', i);
                if (end < 0)
                {
                    end = n;
                }
                buffer.Append(script, i, end - i);
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < n && script[i + 1] == '*')
            {
                int end = script.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                buffer.Append(script, i, end - i);
                i = end;
                continue;
            }
            buffer.Append(c);
            i++;
        }

        string last = buffer.ToString().Trim();
        if (HasSqlContent(last))
        {
            result.Add(last);
        }
        return result;
    }

    private static bool IsLineStart(string text, int index)
    {
        int i = index - 1;
        while (i >= 0 && text[i] != '\n')
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
            i--;
        }
        return true;
    }

    /// <summary>识别 PostgreSQL 的 $tag$ ... $tag$ 美元引号字符串。</summary>
    private static bool TryDollarQuoted(string text, int start, out int end)
    {
        end = start;
        int i = start + 1;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }
        if (i >= text.Length || text[i] != '$')
        {
            return false;
        }
        string tag = text[start..(i + 1)];
        if (tag.Length > 64)
        {
            return false;
        }
        int close = text.IndexOf(tag, i + 1, StringComparison.Ordinal);
        if (close < 0)
        {
            end = text.Length;
            return true;
        }
        end = close + tag.Length;
        return true;
    }

    private static int SkipQuoted(string text, int start, char quote)
    {
        int i = start + 1;
        while (i < text.Length)
        {
            if (text[i] == quote)
            {
                if (i + 1 < text.Length && text[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            if (text[i] == '\\' && quote != '`' && i + 1 < text.Length)
            {
                i += 2;
                continue;
            }
            i++;
        }
        return text.Length;
    }

    private static bool HasSqlContent(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal)
                || trimmed.StartsWith("#", StringComparison.Ordinal)
                || (trimmed.StartsWith("/*", StringComparison.Ordinal) && trimmed.EndsWith("*/", StringComparison.Ordinal)))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>语句是否包含可执行 SQL（忽略纯注释）。</summary>
    public static bool HasExecutableSql(string text) => HasSqlContent(text);

    /// <summary>生成 SQL 字面量（用于导出 INSERT / 备份脚本）。</summary>
    public static string Literal(object? value, DbKind kind)
    {
        if (value == null || value is DBNull)
        {
            return "NULL";
        }
        switch (value)
        {
            case string s:
                return "'" + s.Replace("\\", kind == DbKind.MySQL ? "\\\\" : "\\")
                    .Replace("'", "''") + "'";
            case char ch:
                return Literal(ch.ToString(), kind);
            case bool b:
                return kind == DbKind.Oracle ? (b ? "1" : "0") : (b ? "TRUE" : "FALSE");
            case byte[] bytes:
                return kind switch
                {
                    DbKind.MySQL => "0x" + Convert.ToHexString(bytes),
                    DbKind.PostgreSQL => "'\\x" + Convert.ToHexString(bytes).ToLowerInvariant() + "'",
                    DbKind.SqlServer => "0x" + Convert.ToHexString(bytes),
                    DbKind.Oracle => "HEXTORAW('" + Convert.ToHexString(bytes) + "')",
                    _ => "X'" + Convert.ToHexString(bytes) + "'"
                };
            case DateTime dt:
                string text = dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                return kind == DbKind.Oracle ? "TO_DATE('" + text + "','YYYY-MM-DD HH24:MI:SS.FF3')" : "'" + text + "'";
            case DateTimeOffset dto:
                return "'" + dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) + "'";
            case TimeSpan ts:
                return "'" + ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) + "'";
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case decimal m:
                return m.ToString(CultureInfo.InvariantCulture);
            case Guid g:
                return "'" + g + "'";
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return "'" + value.ToString()!.Replace("'", "''") + "'";
        }
    }

    /// <summary>把 SQL 关键字大写并按主要子句换行，做一个温和的美化。</summary>
    public static string Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return sql;
        }

        var tokens = Tokenize(sql);
        var sb = new StringBuilder();
        int indent;
        bool newLine = false;
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Whitespace)
            {
                if (!newLine && sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\n')
                {
                    sb.Append(' ');
                }
                continue;
            }

            string text = token.Text;
            if (token.Kind == TokenKind.Word)
            {
                string upper = text.ToUpperInvariant();
                if (MajorKeywords.Contains(upper))
                {
                    if (sb.Length > 0)
                    {
                        TrimTrailingSpace(sb);
                        sb.AppendLine();
                    }
                    sb.Append(upper);
                    newLine = false;
                    continue;
                }
                if (upper is "AND" or "OR")
                {
                    TrimTrailingSpace(sb);
                    sb.AppendLine();
                    sb.Append("  ").Append(upper);
                    newLine = false;
                    continue;
                }
                if (upper == "AS" || upper == "ON" || upper == "BY" || upper == "INTO")
                {
                    sb.Append(' ').Append(upper);
                    newLine = false;
                    continue;
                }
                sb.Append(text);
                newLine = false;
                continue;
            }
            if (token.Kind == TokenKind.Comma)
            {
                sb.Append(", ");
                newLine = false;
                continue;
            }
            if (token.Kind == TokenKind.NewLine)
            {
                sb.AppendLine();
                newLine = true;
                continue;
            }
            sb.Append(text);
            newLine = false;
        }

        // 简化缩进：SELECT 字段列表缩进 2 空格
        string result = sb.ToString();
        var lines = result.Split('\n').Select(x => x.TrimEnd()).ToList();
        indent = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            string upper = trimmed.ToUpperInvariant();
            if (upper.StartsWith("FROM ", StringComparison.Ordinal)
                || upper == "FROM"
                || upper.StartsWith("WHERE", StringComparison.Ordinal)
                || upper.StartsWith("GROUP BY", StringComparison.Ordinal)
                || upper.StartsWith("ORDER BY", StringComparison.Ordinal)
                || upper.StartsWith("HAVING", StringComparison.Ordinal)
                || upper.StartsWith("LIMIT", StringComparison.Ordinal)
                || upper.StartsWith("UNION", StringComparison.Ordinal))
            {
                indent = 0;
            }
            lines[i] = new string(' ', indent) + trimmed;
            if (trimmed.Length == 0)
            {
                lines[i] = "";
            }
        }
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static void TrimTrailingSpace(StringBuilder sb)
    {
        while (sb.Length > 0 && (sb[^1] == ' ' || sb[^1] == '\t'))
        {
            sb.Length--;
        }
    }

    private enum TokenKind { Word, Text, Comma, Whitespace, NewLine }

    private readonly record struct Token(string Text, TokenKind Kind);

    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (c == '\r')
            {
                i++;
                continue;
            }
            if (c == '\n')
            {
                tokens.Add(new Token("\n", TokenKind.NewLine));
                i++;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                int start = i;
                while (i < sql.Length && char.IsWhiteSpace(sql[i]) && sql[i] != '\n')
                {
                    i++;
                }
                tokens.Add(new Token(sql[start..i], TokenKind.Whitespace));
                continue;
            }
            if (c == ',')
            {
                tokens.Add(new Token(",", TokenKind.Comma));
                i++;
                continue;
            }
            if (c == '\'' || c == '"' || c == '`')
            {
                int end = SkipQuoted(sql, i, c);
                tokens.Add(new Token(sql[i..end], TokenKind.Text));
                i = end;
                continue;
            }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                int end = sql.IndexOf('\n', i);
                if (end < 0)
                {
                    end = sql.Length;
                }
                tokens.Add(new Token(sql[i..end], TokenKind.Text));
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + 2;
                tokens.Add(new Token(sql[i..end], TokenKind.Text));
                i = end;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '$' || c == '.')
            {
                int start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '@'
                    || sql[i] == '$' || sql[i] == '.'))
                {
                    i++;
                }
                if (i == start)
                {
                    i++;
                }
                tokens.Add(new Token(sql[start..i], TokenKind.Word));
                continue;
            }
            tokens.Add(new Token(c.ToString(), TokenKind.Text));
            i++;
        }
        return tokens;
    }

    /// <summary>根据源/目标数据库类型粗略映射数据类型（跨库传输建表用）。</summary>
    public static string MapType(DbKind source, DbKind target, ColumnInfo column)
    {
        if (source == target)
        {
            return column.FullType;
        }

        string type = column.DataType.ToLowerInvariant();
        bool isText = type.Contains("char") || type.Contains("text") || type.Contains("clob")
            || type.Contains("enum") || type.Contains("json");
        bool isInt = type.Contains("int");
        bool isDecimal = type.Contains("decimal") || type.Contains("numeric") || type.Contains("number");
        bool isFloat = type.Contains("float") || type.Contains("double") || type.Contains("real");
        bool isDate = type.Contains("date") || type.Contains("time");
        bool isBool = type.Contains("bool") || type.Contains("bit");
        bool isBlob = type.Contains("blob") || type.Contains("binary") || type.Contains("bytea")
            || type.Contains("image") || type.Contains("raw");

        long length = column.Length ?? 0;
        if (column.Precision is > 0)
        {
            length = column.Precision.Value;
        }

        if (isBlob)
        {
            return target switch
            {
                DbKind.MySQL => "LONGBLOB",
                DbKind.PostgreSQL => "BYTEA",
                DbKind.SqlServer => "VARBINARY(MAX)",
                DbKind.Oracle => "BLOB",
                _ => "BLOB"
            };
        }
        if (isBool)
        {
            return target switch
            {
                DbKind.MySQL => "TINYINT(1)",
                DbKind.PostgreSQL => "BOOLEAN",
                DbKind.SqlServer => "BIT",
                DbKind.Oracle => "NUMBER(1)",
                _ => "INTEGER"
            };
        }
        if (isText)
        {
            return target switch
            {
                DbKind.MySQL => length > 0 && length <= 4000 ? $"VARCHAR({Math.Min(length, 4000)})" : "LONGTEXT",
                DbKind.PostgreSQL => "TEXT",
                DbKind.SqlServer => length > 0 && length <= 4000 ? $"NVARCHAR({Math.Min(length, 4000)})" : "NVARCHAR(MAX)",
                DbKind.Oracle => length > 0 && length <= 4000 ? $"VARCHAR2({Math.Min(length, 4000)})" : "CLOB",
                _ => "TEXT"
            };
        }
        if (isInt)
        {
            return target switch
            {
                DbKind.MySQL => "BIGINT",
                DbKind.PostgreSQL => "BIGINT",
                DbKind.SqlServer => "BIGINT",
                DbKind.Oracle => "NUMBER(19)",
                _ => "INTEGER"
            };
        }
        if (isDecimal)
        {
            int p = column.Precision ?? 18;
            int s = column.Scale ?? 4;
            return target switch
            {
                DbKind.MySQL => $"DECIMAL({p},{s})",
                DbKind.PostgreSQL => $"NUMERIC({p},{s})",
                DbKind.SqlServer => $"DECIMAL({p},{s})",
                DbKind.Oracle => $"NUMBER({p},{s})",
                _ => "NUMERIC"
            };
        }
        if (isFloat)
        {
            return target switch
            {
                DbKind.MySQL => "DOUBLE",
                DbKind.PostgreSQL => "DOUBLE PRECISION",
                DbKind.SqlServer => "FLOAT",
                DbKind.Oracle => "BINARY_DOUBLE",
                _ => "DOUBLE"
            };
        }
        if (isDate)
        {
            bool timeOnly = type.Contains("time") && !type.Contains("date") && !type.Contains("stamp");
            bool dateOnly = type == "date";
            return target switch
            {
                DbKind.MySQL => timeOnly ? "TIME" : dateOnly ? "DATE" : type.Contains("stamp") ? "DATETIME" : "DATETIME",
                DbKind.PostgreSQL => timeOnly ? "TIME" : dateOnly ? "DATE" : type.Contains("stamp") ? "TIMESTAMP" : "TIMESTAMP",
                DbKind.SqlServer => timeOnly ? "TIME" : dateOnly ? "DATE" : type.Contains("stamp") ? "DATETIME2" : "DATETIME2",
                DbKind.Oracle => dateOnly ? "DATE" : type.Contains("stamp") ? "TIMESTAMP" : "DATE",
                _ => "DATETIME"
            };
        }
        return target switch
        {
            DbKind.MySQL => "LONGTEXT",
            DbKind.PostgreSQL => "TEXT",
            DbKind.SqlServer => "NVARCHAR(MAX)",
            DbKind.Oracle => "CLOB",
            _ => "TEXT"
        };
    }
}
