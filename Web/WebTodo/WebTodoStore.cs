using System.Globalization;
using System.Text.Json.Nodes;

namespace MiniMonitor.Web.WebTodo;

public sealed class TodoNotFoundException : Exception
{
    public TodoNotFoundException(string id) : base(id) { }
}

public sealed class ProjectStore
{
    private const string Columns =
        "id,name,description,color,color_mode,icon,expire_at,expire_notified,pinned,archived,sort_order,created_at,updated_at";

    private static readonly Dictionary<string, (string Column, string Kind)> ProjectFields = new()
    {
        ["name"] = ("name", "str"),
        ["description"] = ("description", "text"),
        ["color"] = ("color", "color"),
        ["colorMode"] = ("color_mode", "color_mode"),
        ["icon"] = ("icon", "str"),
        ["expireAt"] = ("expire_at", "dt"),
        ["pinned"] = ("pinned", "bool"),
        ["archived"] = ("archived", "bool"),
        ["sortOrder"] = ("sort_order", "int")
    };

    private readonly WebTodoDatabase _db;
    public ProjectStore(WebTodoDatabase db) => _db = db;

    public List<Dictionary<string, object?>> List(bool includeArchived = false, string? q = null)
    {
        string sql = "SELECT p.*, " +
                     "(SELECT COUNT(*) FROM tasks t WHERE t.project_id = p.id) AS task_count, " +
                     "(SELECT COUNT(*) FROM tasks t WHERE t.project_id = p.id AND t.status = 'open') AS open_count, " +
                     "(SELECT COUNT(*) FROM tasks t WHERE t.project_id = p.id AND t.status = 'done') AS done_count " +
                     "FROM projects p";
        var clauses = new List<string>();
        var args = new List<(string, object?)>();
        if (!includeArchived)
        {
            clauses.Add("p.archived = 0");
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            clauses.Add("(p.name LIKE @q OR p.description LIKE @q)");
            args.Add(("@q", "%" + q + "%"));
        }
        if (clauses.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", clauses);
        }
        sql += " ORDER BY p.pinned DESC, p.sort_order ASC, p.created_at ASC";
        var rows = _db.Query(sql, args.ToArray());
        var output = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var item = WebTodoSerializer.ProjectFromRow(row);
            item["taskCount"] = row.GetValueOrDefault("task_count") ?? 0L;
            item["openCount"] = row.GetValueOrDefault("open_count") ?? 0L;
            item["doneCount"] = row.GetValueOrDefault("done_count") ?? 0L;
            output.Add(item);
        }
        return output;
    }

    public Dictionary<string, object?> GetRow(string projectId)
    {
        return _db.QueryOne($"SELECT {Columns} FROM projects WHERE id = @id", ("@id", projectId))
               ?? throw new TodoNotFoundException(projectId);
    }

    public Dictionary<string, object?> Get(string projectId)
    {
        var row = GetRow(projectId);
        var item = WebTodoSerializer.ProjectFromRow(row);
        item["taskCount"] = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE project_id=@id", ("@id", projectId));
        item["openCount"] = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE project_id=@id AND status='open'", ("@id", projectId));
        item["doneCount"] = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE project_id=@id AND status='done'", ("@id", projectId));
        return item;
    }

    public Dictionary<string, object?>? FindByName(string name)
    {
        return _db.QueryOne("SELECT id FROM projects WHERE name = @n COLLATE NOCASE", ("@n", name));
    }

    public Dictionary<string, object?> Create(JsonObject payload)
    {
        string name = TodoClock.Clip(Str(payload["name"])?.Trim() ?? "", 200);
        if (name.Length == 0)
        {
            throw new ArgumentException("项目名称不能为空");
        }
        var fields = new List<(string Column, object? Value)>();
        foreach (var pair in ProjectFields)
        {
            if (payload.TryGetPropertyValue(pair.Key, out JsonNode? node))
            {
                fields.Add((pair.Value.Column, ConvertValue(pair.Value.Kind, node)));
            }
        }
        if (!fields.Any(f => f.Column == "color"))
        {
            fields.Add(("color", "#6366f1"));
        }
        string now = TodoClock.IsoNow();
        string id = TodoClock.NewId("p");
        var cols = new List<string> { "id", "created_at", "updated_at" };
        var vals = new List<object?> { id, now, now };
        foreach (var field in fields)
        {
            cols.Add(field.Column);
            vals.Add(field.Value);
        }
        if (!cols.Contains("sort_order"))
        {
            cols.Add("sort_order");
            vals.Add(_db.NextSortOrder("projects"));
        }
        var args = cols.Select((c, i) => ("@" + c, vals[i])).ToArray();
        _db.Insert($"INSERT INTO projects({string.Join(",", cols)}) VALUES({string.Join(",", cols.Select(c => "@" + c))})", args);
        return Get(id);
    }

    public (Dictionary<string, object?> Project, Dictionary<string, object?> Changed) Update(string projectId, JsonObject changes)
    {
        var current = GetRow(projectId);
        var cols = new List<string>();
        var vals = new List<object?>();
        foreach (var pair in changes)
        {
            if (!ProjectFields.TryGetValue(pair.Key, out var spec))
            {
                continue;
            }
            object? converted = ConvertValue(spec.Kind, pair.Value);
            object? currentValue = current.GetValueOrDefault(spec.Column);
            if (!ValuesEqual(currentValue, converted))
            {
                cols.Add(spec.Column);
                vals.Add(converted);
            }
        }
        if (cols.Count == 0)
        {
            return (Get(projectId), new Dictionary<string, object?>());
        }
        cols.Add("updated_at");
        vals.Add(TodoClock.IsoNow());
        var args = cols.Select((c, i) => ("@" + c, vals[i])).ToList();
        args.Add(("@id", projectId));
        _db.Execute($"UPDATE projects SET {string.Join(",", cols.Select(c => c + " = @" + c))} WHERE id=@id", args.ToArray());
        var changed = new Dictionary<string, object?>();
        for (int i = 0; i < cols.Count; i++)
        {
            object? before = current.GetValueOrDefault(cols[i]);
            if (!ValuesEqual(before, vals[i]))
            {
                changed[cols[i]] = new Dictionary<string, object?> { ["from"] = before, ["to"] = vals[i] };
            }
        }
        return (Get(projectId), changed);
    }

    public int Delete(string projectId, bool cascade)
    {
        GetRow(projectId);
        long taskCount = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE project_id=@id", ("@id", projectId));
        if (taskCount > 0 && !cascade)
        {
            throw new ArgumentException($"项目下仍有 {taskCount} 个任务，确认后将一并删除（cascade=1）");
        }
        _db.BeginTransaction();
        try
        {
            _db.Execute("DELETE FROM tasks WHERE project_id=@id", ("@id", projectId));
            int changed = _db.Execute("DELETE FROM projects WHERE id=@id", ("@id", projectId));
            _db.Commit();
            return changed;
        }
        catch
        {
            _db.Rollback();
            throw;
        }
    }

    public int Reorder(List<string> ids)
    {
        foreach (var (id, index) in ids.Select((v, i) => (v, i)))
        {
            _db.Execute("UPDATE projects SET sort_order=@s, updated_at=@u WHERE id=@id",
                ("@s", (index + 1) * 1000), ("@u", TodoClock.IsoNow()), ("@id", id));
        }
        return ids.Count;
    }

    public static object? ConvertValue(string kind, JsonNode? node)
    {
        switch (kind)
        {
            case "str":
                return node == null ? "" : TodoClock.Clip(Str(node)?.Trim() ?? "", 200);
            case "text":
                return node == null ? "" : Str(node) ?? "";
            case "bool":
                return TodoClock.ToBool(node) ? 1L : 0L;
            case "int":
                return (long)TodoClock.ToInt(node, 0);
            case "priority":
                return (long)TodoClock.ToInt(node, 0, 0, 3);
            case "dt":
                DateTime? dt = TodoClock.ParseDt(node);
                return dt == null ? null : TodoClock.Iso(dt);
            case "color":
            {
                string text = node == null ? "" : Str(node)?.Trim() ?? "";
                if (text.Length == 0) return "#6366f1";
                if (text.Length > 64 || text.IndexOfAny(new[] { '"', '\'', '<', '>', ';', '&', '(', ')', '{', '}' }) >= 0)
                {
                    throw new ArgumentException($"颜色值不合法：'{text}'");
                }
                return text;
            }
            case "color_mode":
                return (Str(node) ?? "").Trim().ToLowerInvariant() == "rainbow" ? "rainbow" : "solid";
            case "status":
            {
                string text = (Str(node) ?? "open").ToLowerInvariant();
                if (text is not ("open" or "done" or "canceled"))
                {
                    throw new ArgumentException($"任务状态不合法：'{text}'（可选 open/done/canceled）");
                }
                return text;
            }
            default:
                throw new ArgumentException($"未知转换器：{kind}");
        }
    }

    public static string? Str(JsonNode? node)
    {
        if (node == null) return null;
        return node.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => node.GetValue<string>(),
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            System.Text.Json.JsonValueKind.Number => node.ToJsonString(),
            _ => null
        };
    }

    public static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a is long la && b is int ib) return la == ib;
        if (a is int ia && b is long lb) return ia == lb;
        if (a is long l1 && b is long l2) return l1 == l2;
        if (a is double d1 && b is double d2) return Math.Abs(d1 - d2) < 0.000001;
        if (a is bool ba) return b is bool bb ? ba == bb : Convert.ToInt64(b) == (ba ? 1 : 0);
        if (b is bool bbb) return Convert.ToInt64(a) == (bbb ? 1 : 0);
        return string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture),
            Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}

public sealed class TaskStore
{
    private const string Columns =
        "id,project_id,title,notes,priority,status,due_at,completed_at,sort_order,created_at,updated_at";

    private static readonly Dictionary<string, (string Column, string Kind)> TaskFields = new()
    {
        ["title"] = ("title", "str"),
        ["notes"] = ("notes", "text"),
        ["priority"] = ("priority", "priority"),
        ["status"] = ("status", "status"),
        ["dueAt"] = ("due_at", "dt"),
        ["completedAt"] = ("completed_at", "dt"),
        ["sortOrder"] = ("sort_order", "int"),
        ["projectId"] = ("project_id", "str")
    };

    private static readonly Dictionary<string, string?> Views = new()
    {
        ["all"] = null,
        ["today"] = "date(due_at) = date('now','localtime') AND status = 'open'",
        ["upcoming"] = "date(due_at) > date('now','localtime') AND status = 'open'",
        ["overdue"] = "datetime(due_at) < datetime('now','localtime') AND status = 'open'",
        ["nodate"] = "due_at IS NULL AND status = 'open'"
    };

    private readonly WebTodoDatabase _db;
    public TaskStore(WebTodoDatabase db) => _db = db;

    public (List<Dictionary<string, object?>> Items, long Total) List(string? projectId = null,
        string status = "open", string? view = null, string? q = null, int? limit = null, int offset = 0)
    {
        var where = new List<string>();
        var args = new List<(string, object?)>();
        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            where.Add("t.status = @status");
            args.Add(("@status", status));
        }
        if (!string.IsNullOrEmpty(projectId))
        {
            where.Add("t.project_id = @project");
            args.Add(("@project", projectId));
        }
        if (view != null && Views.TryGetValue(view, out string? viewSql) && viewSql != null)
        {
            where.Add("(" + viewSql + ")");
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            where.Add("(t.title LIKE @q OR t.notes LIKE @q)");
            args.Add(("@q", "%" + q + "%"));
        }
        string clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        long total = _db.Scalar($"SELECT COUNT(*) FROM tasks t {clause}", args.ToArray());
        string sql = $"SELECT t.*, p.name AS project_name, p.color AS project_color FROM tasks t " +
                     $"LEFT JOIN projects p ON p.id = t.project_id {clause} " +
                     "ORDER BY CASE WHEN t.due_at IS NULL THEN 1 ELSE 0 END, t.due_at ASC, t.sort_order ASC, t.created_at DESC";
        if (limit.HasValue && limit.Value > 0)
        {
            sql += " LIMIT @limit OFFSET @offset";
            args.Add(("@limit", limit.Value));
            args.Add(("@offset", Math.Max(0, offset)));
        }
        var rows = _db.Query(sql, args.ToArray());
        var items = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var item = WebTodoSerializer.TaskFromRow(row);
            item["projectName"] = row.GetValueOrDefault("project_name");
            item["projectColor"] = row.GetValueOrDefault("project_color");
            items.Add(item);
        }
        return (items, total);
    }

    public Dictionary<string, object?> GetRow(string taskId)
    {
        return _db.QueryOne($"SELECT {Columns} FROM tasks WHERE id=@id", ("@id", taskId))
               ?? throw new TodoNotFoundException(taskId);
    }

    public Dictionary<string, object?> Get(string taskId)
    {
        var row = GetRow(taskId);
        var item = WebTodoSerializer.TaskFromRow(row);
        var project = _db.QueryOne("SELECT name, color FROM projects WHERE id=@id",
            ("@id", row.GetValueOrDefault("project_id")));
        item["projectName"] = project?.GetValueOrDefault("name");
        item["projectColor"] = project?.GetValueOrDefault("color");
        return item;
    }

    public Dictionary<string, object?> Create(JsonObject payload)
    {
        string title = TodoClock.Clip((ProjectStore.Str(payload["title"]) ?? "").Trim(), 200);
        if (title.Length == 0) throw new ArgumentException("任务标题不能为空");
        string? projectId = ProjectStore.Str(payload["projectId"]);
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("必须指定 projectId");
        new ProjectStore(_db).GetRow(projectId);
        string now = TodoClock.IsoNow();
        string taskId = TodoClock.NewId("t");
        var values = new List<(string Column, object? Value)>
        {
            ("id", taskId), ("project_id", projectId), ("title", title),
            ("created_at", now), ("updated_at", now),
            ("sort_order", _db.NextSortOrder("tasks", "project_id = @p", ("@p", projectId)))
        };
        foreach (var pair in TaskFields)
        {
            if (!payload.TryGetPropertyValue(pair.Key, out JsonNode? node)) continue;
            if (pair.Key is "projectId" or "title" or "sortOrder") continue;
            values.Add((pair.Value.Column, ProjectStore.ConvertValue(pair.Value.Kind, node)));
        }
        string? status = values.FirstOrDefault(v => v.Column == "status").Value as string;
        if (status == "done" && !values.Any(v => v.Column == "completed_at"))
        {
            values.Add(("completed_at", now));
        }
        var cols = values.Select(v => v.Column).ToList();
        _db.Insert($"INSERT INTO tasks({string.Join(",", cols)}) VALUES({string.Join(",", cols.Select(c => "@" + c))})",
            values.Select(v => ("@" + v.Column, v.Value)).ToArray());
        return Get(taskId);
    }

    public (Dictionary<string, object?> Task, Dictionary<string, object?> Changed) Update(string taskId, JsonObject changes)
    {
        var current = GetRow(taskId);
        var cols = new List<string>();
        var vals = new List<object?>();
        foreach (var pair in changes)
        {
            if (!TaskFields.TryGetValue(pair.Key, out var spec)) continue;
            object? converted = ProjectStore.ConvertValue(spec.Kind, pair.Value);
            object? currentValue = current.GetValueOrDefault(spec.Column);
            if (spec.Column == "project_id")
            {
                string? projectId = converted as string;
                if (string.IsNullOrEmpty(projectId) || ProjectStore.ValuesEqual(currentValue, converted))
                {
                    continue;
                }
                new ProjectStore(_db).GetRow(projectId);
            }
            if (!ProjectStore.ValuesEqual(currentValue, converted))
            {
                cols.Add(spec.Column);
                vals.Add(converted);
            }
            if (pair.Key == "status")
            {
                if ((converted as string) == "done" && !cols.Contains("completed_at"))
                {
                    cols.Add("completed_at");
                    vals.Add(TodoClock.IsoNow());
                }
                else if ((converted as string) != "done" && !cols.Contains("completed_at"))
                {
                    cols.Add("completed_at");
                    vals.Add(null);
                }
            }
        }
        if (cols.Count == 0)
        {
            return (Get(taskId), new Dictionary<string, object?>());
        }
        cols.Add("updated_at");
        vals.Add(TodoClock.IsoNow());
        var args = cols.Select((c, i) => ("@" + c, vals[i])).ToList();
        args.Add(("@id", taskId));
        _db.Execute($"UPDATE tasks SET {string.Join(",", cols.Select(c => c + " = @" + c))} WHERE id=@id", args.ToArray());
        var changed = new Dictionary<string, object?>();
        for (int i = 0; i < cols.Count; i++)
        {
            object? before = current.GetValueOrDefault(cols[i]);
            if (!ProjectStore.ValuesEqual(before, vals[i]))
            {
                changed[cols[i]] = new Dictionary<string, object?> { ["from"] = before, ["to"] = vals[i] };
            }
        }
        return (Get(taskId), changed);
    }

    public (Dictionary<string, object?> Task, Dictionary<string, object?> Changed) SetStatus(string taskId, string status)
    {
        return Update(taskId, new JsonObject { ["status"] = status });
    }

    public void Delete(string taskId)
    {
        GetRow(taskId);
        _db.Execute("DELETE FROM tasks WHERE id=@id", ("@id", taskId));
    }

    public int Move(List<string> ids, string projectId)
    {
        new ProjectStore(_db).GetRow(projectId);
        int moved = 0;
        foreach (string id in ids)
        {
            GetRow(id);
            _db.Execute("UPDATE tasks SET project_id=@p, updated_at=@u WHERE id=@id",
                ("@p", projectId), ("@u", TodoClock.IsoNow()), ("@id", id));
            moved++;
        }
        return moved;
    }

    public int Reorder(string? projectId, List<string> ids)
    {
        foreach (var (id, index) in ids.Select((v, i) => (v, i)))
        {
            _db.Execute("UPDATE tasks SET sort_order=@s WHERE id=@id", ("@s", (index + 1) * 1000), ("@id", id));
        }
        return ids.Count;
    }
}

/// <summary>把 Dictionary 行数据转换为 JSON 节点的公共工具（原 TodoAudit 的静态序列化部分）。</summary>
internal static class TodoJson
{
    public static JsonObject ToJsonObject(Dictionary<string, object?> source)
    {
        var obj = new JsonObject();
        foreach (var pair in source)
        {
            obj[pair.Key] = ToNode(pair.Value);
        }
        return obj;
    }

    internal static JsonArray ToJsonArray(IEnumerable<object?> values)
    {
        var arr = new JsonArray();
        foreach (object? value in values)
        {
            arr.Add(ToNode(value));
        }
        return arr;
    }

    internal static JsonNode? ToNode(object? value)
    {
        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            float f => JsonValue.Create(f),
            string s => JsonValue.Create(s),
            List<object?> list => ToJsonArray(list),
            List<string> ls => ToJsonArray(ls.Cast<object?>()),
            Dictionary<string, object?> dict => ToJsonObject(dict),
            _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }
}
