using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace MiniMonitor.Navicat;

internal static class BackupService
{
    public static int DumpDatabase(DbSession session, string? database, string path, Action<string>? log,
        ICollection<string>? selectedTables = null)
    {
        string db = string.IsNullOrWhiteSpace(database) ? session.CurrentDatabase : database;
        var tables = session.GetTables(db);
        if (selectedTables is { Count: > 0 })
        {
            tables = tables.Where(t => selectedTables.Contains(t.DisplayName) || selectedTables.Contains(t.Name)).ToList();
        }
        var views = session.GetViews(db);
        var sb = new StringBuilder();
        int totalRows = 0;

        sb.AppendLine("-- Navicat 复刻版数据库备份");
        sb.AppendLine("-- 数据库：" + db);
        sb.AppendLine("-- 类型：" + session.Kind.ShortName());
        sb.AppendLine("-- 时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();

        switch (session.Kind)
        {
            case DbKind.MySQL:
                sb.AppendLine("SET NAMES utf8mb4;");
                sb.AppendLine("SET FOREIGN_KEY_CHECKS=0;");
                break;
            case DbKind.SQLite:
                sb.AppendLine("PRAGMA foreign_keys=OFF;");
                break;
            case DbKind.PostgreSQL:
                sb.AppendLine("SET statement_timeout = 0;");
                sb.AppendLine("SET client_encoding = 'UTF8';");
                break;
        }
        sb.AppendLine();

        var pendingForeignKeys = new List<string>();
        foreach (var table in tables)
        {
            log?.Invoke("备份表 " + table.DisplayName + "…");
            string ddl = session.GetTableDdl(table);
            if (session.Kind is DbKind.PostgreSQL or DbKind.SqlServer or DbKind.Oracle)
            {
                var columns = session.GetColumns(table);
                var indexes = session.GetIndexes(table);
                var fks = session.GetForeignKeys(table);
                ddl = session.BuildCreateTable(table, columns, indexes, new List<ForeignKeyInfo>());
                foreach (var fk in fks)
                {
                    if (fk.Columns.Count == 0 || fk.RefColumns.Count == 0)
                    {
                        continue;
                    }
                    pendingForeignKeys.Add("ALTER TABLE " + session.Qualify(table) + " ADD CONSTRAINT "
                        + session.Quote(fk.Name) + " FOREIGN KEY ("
                        + string.Join(", ", fk.Columns.Select(session.Quote)) + ") REFERENCES "
                        + fk.RefTable + " (" + string.Join(", ", fk.RefColumns.Select(session.Quote)) + ")"
                        + (!string.IsNullOrWhiteSpace(fk.OnDelete) ? " ON DELETE " + fk.OnDelete : "")
                        + (!string.IsNullOrWhiteSpace(fk.OnUpdate) ? " ON UPDATE " + fk.OnUpdate : "") + ";");
                }
            }
            sb.AppendLine("-- ----------------------------");
            sb.AppendLine("-- 表结构：" + table.DisplayName);
            sb.AppendLine("-- ----------------------------");
            if (session.Kind is DbKind.MySQL or DbKind.SQLite)
            {
                sb.AppendLine("DROP TABLE IF EXISTS " + session.Qualify(table) + ";");
            }
            else if (session.Kind == DbKind.PostgreSQL)
            {
                sb.AppendLine("DROP TABLE IF EXISTS " + session.Qualify(table) + " CASCADE;");
            }
            else if (session.Kind == DbKind.SqlServer)
            {
                sb.AppendLine("IF OBJECT_ID(N'" + session.Qualify(table).Replace("'", "''") + "', N'U') IS NOT NULL DROP TABLE "
                    + session.Qualify(table) + ";");
            }
            else
            {
                sb.AppendLine("-- Oracle：请确保目标表不存在；如需覆盖请先手动删除。");
            }
            sb.AppendLine(ddl.TrimEnd().TrimEnd(';') + ";");
            sb.AppendLine();

            var columns2 = session.GetColumns(table);
            bool identity = columns2.Any(c => c.AutoIncrement);
            if (session.Kind == DbKind.SqlServer && identity)
            {
                sb.AppendLine("SET IDENTITY_INSERT " + session.Qualify(table) + " ON;");
            }

            if (columns2.Count > 0)
            {
                string columnList = string.Join(", ", columns2.Select(c => session.Quote(c.Name)));
                string prefix = "INSERT INTO " + session.Qualify(table) + " (" + columnList + ") VALUES ";
                int batchSize = session.Kind == DbKind.Oracle ? 1 : 50;
                var batch = new StringBuilder();
                int inBatch = 0;
                int tableRows = 0;
                session.StreamRows("SELECT * FROM " + session.Qualify(table), reader =>
                {
                    var values = new List<string>(reader.FieldCount);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        values.Add(NvSqlUtil.Literal(reader.GetValue(i), session.Kind));
                    }
                    string row = "(" + string.Join(", ", values) + ")";
                    if (inBatch == 0)
                    {
                        batch.Append(prefix);
                    }
                    else
                    {
                        batch.Append(", ");
                    }
                    batch.Append(row);
                    inBatch++;
                    tableRows++;
                    totalRows++;
                    if (inBatch >= batchSize)
                    {
                        batch.AppendLine(";");
                        sb.Append(batch);
                        batch.Clear();
                        inBatch = 0;
                    }
                    if (tableRows % 5000 == 0)
                    {
                        log?.Invoke("  " + table.DisplayName + " 已读取 " + tableRows + " 行…");
                    }
                }, 600);
                if (inBatch > 0)
                {
                    batch.AppendLine(";");
                    sb.Append(batch);
                }
                sb.AppendLine("-- " + table.DisplayName + "：" + tableRows + " 行");
                log?.Invoke("表 " + table.DisplayName + " 完成，共 " + tableRows + " 行。");
            }
            if (session.Kind == DbKind.SqlServer && identity)
            {
                sb.AppendLine("SET IDENTITY_INSERT " + session.Qualify(table) + " OFF;");
            }
            sb.AppendLine();
        }

        foreach (string fk in pendingForeignKeys)
        {
            sb.AppendLine(fk);
        }
        sb.AppendLine();

        foreach (var view in views)
        {
            log?.Invoke("备份视图 " + view.DisplayName + "…");
            string ddl = session.GetViewDdl(view);
            if (!string.IsNullOrWhiteSpace(ddl))
            {
                sb.AppendLine("-- 视图：" + view.DisplayName);
                sb.AppendLine(StripDefiner(ddl).TrimEnd().TrimEnd(';') + ";");
                sb.AppendLine();
            }
        }

        List<RoutineInfo> routines;
        try
        {
            routines = session.GetRoutines(db);
        }
        catch
        {
            routines = new List<RoutineInfo>();
        }
        if (routines.Count > 0)
        {
            sb.AppendLine("-- 存储过程 / 函数");
            sb.AppendLine("DELIMITER @#@");
            foreach (var routine in routines)
            {
                try
                {
                    string ddl = session.GetRoutineDdl(routine);
                    if (!NvSqlUtil.HasExecutableSql(ddl))
                    {
                        continue;
                    }
                    log?.Invoke("备份定义 " + routine.DisplayName + "…");
                    sb.AppendLine("-- " + routine.Type + "：" + routine.DisplayName);
                    sb.AppendLine(StripDefiner(ddl).TrimEnd().TrimEnd(';') + "@#@");
                    sb.AppendLine();
                }
                catch
                {
                    // 单个函数读取失败不影响整体备份
                }
            }
            sb.AppendLine("DELIMITER ;");
            sb.AppendLine();
        }

        switch (session.Kind)
        {
            case DbKind.MySQL:
                sb.AppendLine("SET FOREIGN_KEY_CHECKS=1;");
                break;
            case DbKind.SQLite:
                sb.AppendLine("PRAGMA foreign_keys=ON;");
                break;
        }
        sb.AppendLine("-- 备份完成");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        log?.Invoke("备份完成：" + path + "，共 " + totalRows + " 行。");
        return totalRows;
    }

    private static string StripDefiner(string ddl)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            ddl, @"DEFINER=\S+\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static int Restore(DbSession session, string path, Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        string script = File.ReadAllText(path, Encoding.UTF8);
        var statements = NvSqlUtil.SplitScript(script);
        log?.Invoke("共 " + statements.Count + " 条语句，开始还原…");
        int executed = 0;
        foreach (string statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                session.Execute(statement, 900);
                executed++;
            }
            catch (Exception ex)
            {
                string preview = statement.Length > 300 ? statement[..300] + "…" : statement;
                throw new InvalidOperationException(
                    "第 " + (executed + 1) + " 条语句执行失败：\n" + ex.Message + "\n\nSQL:\n" + preview, ex);
            }
            if (executed % 50 == 0)
            {
                log?.Invoke("已执行 " + executed + " / " + statements.Count + " 条语句…");
            }
        }
        log?.Invoke("还原完成，共执行 " + executed + " 条语句。");
        return executed;
    }
}
