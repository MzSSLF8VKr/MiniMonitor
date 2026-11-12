using System.Globalization;
using System.Text.Json.Nodes;

namespace MiniMonitor.Web.WebTodo;

/// <summary>WebTodo 数据导入导出（与 Python 版保持相同的 JSON 结构）。</summary>
public sealed class TodoTransfer
{
    public const string ExportKindFull = "todo-push-full";
    public const string ExportKindTodo = "todo-push-items";
    public const string Inbox = "收件箱";

    private readonly WebTodoDatabase _db;
    private readonly TodoConfig _config;
    private readonly ProjectStore _projects;
    private readonly TaskStore _tasks;

    public TodoTransfer(WebTodoDatabase db, TodoConfig config)
    {
        _db = db;
        _config = config;
        _projects = new ProjectStore(db);
        _tasks = new TaskStore(db);
    }

    // ------------------------------------------------------------ 导出

    public JsonObject ExportFull()
    {
        var projects = new JsonArray();
        foreach (var project in _projects.List(includeArchived: true))
        {
            projects.Add(TodoJson.ToJsonObject(project));
        }
        var tasks = new JsonArray();
        var (items, _) = _tasks.List(status: "all");
        foreach (var task in items)
        {
            tasks.Add(TodoJson.ToJsonObject(task));
        }
        var config = _config.Snapshot();
        return new JsonObject
        {
            ["kind"] = ExportKindFull,
            ["app"] = "Todo Push",
            ["schema"] = 1,
            ["version"] = "1.0.0",
            ["exportedAt"] = TodoClock.IsoNow(),
            ["config"] = config,
            ["projects"] = projects,
            ["tasks"] = tasks,
            ["stats"] = Stats()
        };
    }

    public JsonObject ExportTodo()
    {
        var projects = _projects.List(includeArchived: true);
        var (tasks, _) = _tasks.List(status: "all");
        var byProject = tasks.GroupBy(t => Convert.ToString(t.GetValueOrDefault("projectId"), CultureInfo.InvariantCulture) ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());
        var projectsJson = new JsonArray();
        foreach (var project in projects)
        {
            var item = TodoJson.ToJsonObject(project);
            string id = Convert.ToString(project.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
            var nested = new JsonArray();
            if (byProject.TryGetValue(id, out var list))
            {
                foreach (var task in list) nested.Add(TodoJson.ToJsonObject(task));
            }
            item["tasks"] = nested;
            projectsJson.Add(item);
        }
        var tasksJson = new JsonArray();
        foreach (var task in tasks) tasksJson.Add(TodoJson.ToJsonObject(task));
        return new JsonObject
        {
            ["kind"] = ExportKindTodo,
            ["app"] = "Todo Push",
            ["schema"] = 1,
            ["version"] = "1.0.0",
            ["exportedAt"] = TodoClock.IsoNow(),
            ["projects"] = projectsJson,
            ["tasks"] = tasksJson,
            ["stats"] = new JsonObject { ["projects"] = projects.Count, ["tasks"] = tasks.Count }
        };
    }

    public JsonObject Preview(string kind)
    {
        JsonObject data = kind == "full" ? ExportFull() : ExportTodo();
        data["_preview"] = true;
        return data;
    }

    public JsonObject Stats()
    {
        return new JsonObject
        {
            ["projects"] = _db.Scalar("SELECT COUNT(*) FROM projects"),
            ["tasks"] = _db.Scalar("SELECT COUNT(*) FROM tasks"),
            ["openTasks"] = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE status='open'")
        };
    }

    // ------------------------------------------------------------ 导入

    public JsonObject ImportTodo(JsonNode? payload, string mode = "merge")
    {
        mode = mode == "replace" ? "replace" : "merge";
        var warnings = new List<string>();
        var specs = NormalizeSpecs(payload, warnings);
        var result = ImportResult(mode, warnings);
        if (mode == "replace")
        {
            _db.Execute("DELETE FROM tasks");
            _db.Execute("DELETE FROM projects");
        }
        var projectCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            var projectSpec = spec.Project.DeepClone().AsObject();
            string projectName = (projectSpec["name"]?.GetValue<string>() ?? "").Trim();
            if (projectName.Length == 0)
            {
                projectName = Inbox;
                projectSpec["name"] = Inbox;
            }
            var existing = mode == "merge" ? _projects.FindByName(projectName) : null;
            string projectId;
            if (existing != null)
            {
                projectId = Convert.ToString(existing.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                var changes = new JsonObject();
                foreach (string key in new[] { "description", "color", "colorMode", "icon", "expireAt", "pinned", "archived" })
                {
                    if (projectSpec.TryGetPropertyValue(key, out JsonNode? node) && node != null)
                    {
                        changes[key] = node.DeepClone();
                    }
                }
                if (changes.Count > 0)
                {
                    _projects.Update(projectId, changes);
                    Increment(result, "projects", "updated");
                }
            }
            else
            {
                var created = _projects.Create(projectSpec);
                projectId = Convert.ToString(created.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                Increment(result, "projects", "created");
            }
            projectCache[projectName.ToLowerInvariant()] = projectId;
            foreach (var taskSpec in spec.Tasks)
            {
                ImportTask(taskSpec, projectId, projectName, mode, result, warnings, projectCache);
            }
        }
        if (mode == "replace" && specs.Count > 0)
        {
            foreach (var (row, index) in _db.Query("SELECT id FROM projects ORDER BY created_at, name")
                         .Select((r, i) => (r, i)))
            {
                _db.Execute("UPDATE projects SET sort_order=@s WHERE id=@id",
                    ("@s", (index + 1) * 1000), ("@id", row.GetValueOrDefault("id")));
            }
        }
        result["warnings"] = ToJsonArray(warnings);
        return result;
    }

    private void ImportTask(JsonObject taskSpec, string projectId, string projectName, string mode,
        JsonObject result, List<string> warnings, Dictionary<string, string> projectCache)
    {
        string title = (taskSpec["title"]?.GetValue<string>() ?? "").Trim();
        if (title.Length == 0)
        {
            Increment(result, "tasks", "skipped");
            warnings.Add("有一条任务缺少标题，已跳过");
            return;
        }
        string targetProject = projectId;
        string? linked = taskSpec["project"]?.GetValue<string>() ?? taskSpec["projectName"]?.GetValue<string>();
        bool inherited = taskSpec["_inherited"] != null && TodoClock.ToBool(taskSpec["_inherited"]);
        if (!string.IsNullOrWhiteSpace(linked) && !inherited)
        {
            string key = linked.Trim().ToLowerInvariant();
            targetProject = projectCache.TryGetValue(key, out string? cached) ? cached : "";
            if (string.IsNullOrEmpty(targetProject))
            {
                var fallback = _projects.FindByName(linked.Trim());
                if (fallback != null)
                {
                    targetProject = Convert.ToString(fallback.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                    projectCache[key] = targetProject;
                }
                else
                {
                    var created = _projects.Create(new JsonObject { ["name"] = linked.Trim() });
                    targetProject = Convert.ToString(created.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                    projectCache[key] = targetProject;
                    Increment(result, "projects", "created");
                }
            }
        }
        else if (string.IsNullOrEmpty(projectId))
        {
            warnings.Add($"任务「{TodoClock.Clip(title, 40)}」缺少项目，已放入「{Inbox}」");
            var inbox = projectCache.TryGetValue(Inbox, out string? cachedInbox)
                ? cachedInbox
                : Convert.ToString(_projects.FindByName(Inbox)?.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
            if (string.IsNullOrEmpty(inbox))
            {
                var created = _projects.Create(new JsonObject { ["name"] = Inbox, ["color"] = "#06b6d4" });
                inbox = Convert.ToString(created.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                Increment(result, "projects", "created");
            }
            targetProject = inbox;
        }

        var payload = new JsonObject();
        foreach (var pair in taskSpec)
        {
            if (pair.Key is "project" or "projectName" or "_inherited" or "id") continue;
            payload[pair.Key] = pair.Value?.DeepClone();
        }
        payload["title"] = title;
        payload["projectId"] = targetProject;
        if (mode == "merge")
        {
            var duplicate = _db.QueryOne("SELECT id FROM tasks WHERE project_id=@p AND title=@t AND status != 'done'",
                ("@p", targetProject), ("@t", title));
            if (duplicate != null)
            {
                string dupId = Convert.ToString(duplicate.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                _tasks.Update(dupId, payload);
                Increment(result, "tasks", "updated");
                return;
            }
        }
        try
        {
            _tasks.Create(payload);
            Increment(result, "tasks", "created");
        }
        catch (ArgumentException ex)
        {
            Increment(result, "tasks", "skipped");
            warnings.Add($"任务「{TodoClock.Clip(title, 40)}」导入失败：{ex.Message}");
        }
    }

    public JsonObject ImportFull(JsonNode? payload, string mode = "merge")
    {
        var data = payload as JsonObject ?? new JsonObject();
        var warnings = new List<string>();
        var result = ImportResult(mode == "replace" ? "replace" : "merge", warnings);
        result["config"] = false;
        if (data["config"] is JsonObject config)
        {
            if (config["settings"] is JsonObject settings)
            {
                _config.UpdateSettings(settings);
            }
            result["config"] = true;
        }

        int projectCount = (data["projects"] as JsonArray)?.Count ?? 0;
        int taskCount = (data["tasks"] as JsonArray)?.Count ?? 0;
        if (projectCount > 0 || taskCount > 0)
        {
            var merged = ImportEntities(data, (string)result["mode"]!, warnings);
            result["projects"] = merged["projects"]?.DeepClone();
            result["tasks"] = merged["tasks"]?.DeepClone();
        }
        else if (data["kind"]?.GetValue<string>() is ExportKindFull or ExportKindTodo)
        {
            warnings.Add("文件中没有可导入的项目或任务");
        }

        result["warnings"] = ToJsonArray(warnings);
        return result;
    }

    private JsonObject ImportEntities(JsonObject data, string mode, List<string> warnings)
    {
        var summary = new JsonObject
        {
            ["projects"] = new JsonObject { ["created"] = 0, ["updated"] = 0 },
            ["tasks"] = new JsonObject { ["created"] = 0, ["updated"] = 0, ["skipped"] = 0 }
        };
        if (mode == "replace")
        {
            _db.Execute("DELETE FROM tasks");
            _db.Execute("DELETE FROM projects");
        }
        string nowIso = TodoClock.IsoNow();
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonNode? node in data["projects"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject row) continue;
            var spec = ProjectSpecFromExport(row);
            string? projectId = row["id"]?.GetValue<string>();
            if (mode == "merge" && !string.IsNullOrEmpty(projectId)
                && _db.QueryOne("SELECT id FROM projects WHERE id=@id", ("@id", projectId)) != null)
            {
                _projects.Update(projectId, spec);
                Increment(summary, "projects", "updated");
                idMap[projectId] = projectId;
                continue;
            }
            var created = _projects.Create(spec);
            Increment(summary, "projects", "created");
            if (!string.IsNullOrEmpty(projectId))
            {
                idMap[projectId] = Convert.ToString(created.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
            }
        }
        foreach (JsonNode? node in data["tasks"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject row) continue;
            var spec = TaskSpecFromExport(row);
            string? originProject = row["projectId"]?.GetValue<string>();
            string? target = originProject != null && idMap.TryGetValue(originProject, out string? mapped) ? mapped : null;
            if (target == null && !string.IsNullOrEmpty(originProject))
            {
                var found = _db.QueryOne("SELECT id FROM projects WHERE id=@id", ("@id", originProject));
                target = found == null ? null : Convert.ToString(found.GetValueOrDefault("id"), CultureInfo.InvariantCulture);
            }
            if (target == null)
            {
                string name = (row["projectName"]?.GetValue<string>() ?? row["project"]?.GetValue<string>() ?? Inbox).Trim();
                if (name.Length == 0) name = Inbox;
                var existing = _projects.FindByName(name);
                if (existing != null)
                {
                    target = Convert.ToString(existing.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                }
                else
                {
                    var created = _projects.Create(new JsonObject { ["name"] = name });
                    target = Convert.ToString(created.GetValueOrDefault("id"), CultureInfo.InvariantCulture) ?? "";
                    Increment(summary, "projects", "created");
                }
                warnings.Add($"任务「{TodoClock.Clip(row["title"]?.GetValue<string>() ?? "", 40)}」的项目不存在，已归入「{name}」");
            }
            spec["projectId"] = target;
            try
            {
                _tasks.Create(spec);
                Increment(summary, "tasks", "created");
            }
            catch (ArgumentException ex)
            {
                Increment(summary, "tasks", "skipped");
                warnings.Add("任务导入失败：" + ex.Message);
            }
        }
        foreach (var group in _db.Query("SELECT project_id, COUNT(*) FROM tasks WHERE status='done' GROUP BY project_id"))
        {
            _db.Execute("UPDATE tasks SET completed_at=COALESCE(completed_at,@now) WHERE project_id=@p AND status='done'",
                ("@now", nowIso), ("@p", group.GetValueOrDefault("project_id")));
        }
        return summary;
    }

    // ------------------------------------------------------------ 导入格式解析

    private sealed class ProjectSpec
    {
        public JsonObject Project = new();
        public List<JsonObject> Tasks = new();
    }

    private static List<ProjectSpec> NormalizeSpecs(JsonNode? payload, List<string> warnings)
    {
        if (payload is JsonValue value && value.TryGetValue<string>(out string? text))
        {
            try
            {
                payload = JsonNode.Parse(text);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("JSON 解析失败：" + ex.Message);
            }
        }
        if (payload == null)
        {
            throw new ArgumentException("导入内容为空");
        }
        if (payload is JsonArray array)
        {
            payload = new JsonObject { ["projects"] = array.DeepClone() };
        }
        if (payload is not JsonObject root)
        {
            throw new ArgumentException("导入内容必须是 JSON 对象或数组");
        }
        foreach (string key in new[] { "items", "data" })
        {
            if (root[key] is JsonObject or JsonArray)
            {
                JsonNode inner = root[key]!;
                root = inner as JsonObject ?? new JsonObject { ["projects"] = inner.DeepClone() };
                break;
            }
        }
        var specs = new List<ProjectSpec>();
        if (root["projects"] is JsonArray projects)
        {
            foreach (JsonNode? entry in projects)
            {
                if (entry is not JsonObject project)
                {
                    warnings.Add("项目条目必须是对象，已忽略一个条目");
                    continue;
                }
                var projectSpec = new JsonObject();
                foreach (var pair in project)
                {
                    if (pair.Key == "tasks") continue;
                    projectSpec[pair.Key] = pair.Value?.DeepClone();
                }
                if (projectSpec["name"] == null)
                {
                    projectSpec["name"] = project["title"]?.DeepClone() ?? "";
                }
                var nested = new List<JsonObject>();
                if (project["tasks"] is JsonArray childTasks)
                {
                    foreach (JsonNode? taskNode in childTasks)
                    {
                        if (taskNode is JsonObject taskObj)
                        {
                            var copy = taskObj.DeepClone().AsObject();
                            copy["_inherited"] = true;
                            nested.Add(copy);
                        }
                        else if (taskNode is JsonValue tv && tv.TryGetValue<string>(out string? taskText))
                        {
                            nested.Add(new JsonObject { ["title"] = taskText, ["_inherited"] = true });
                        }
                    }
                }
                specs.Add(new ProjectSpec { Project = projectSpec, Tasks = nested });
            }
        }

        bool hasNested = specs.Any(s => s.Tasks.Count > 0);
        if (!hasNested && root["tasks"] is JsonArray standaloneTasks)
        {
            var standalone = new List<JsonObject>();
            foreach (JsonNode? taskNode in standaloneTasks)
            {
                if (taskNode is JsonObject taskObj)
                {
                    standalone.Add(taskObj.DeepClone().AsObject());
                }
                else if (taskNode is JsonValue tv && tv.TryGetValue<string>(out string? taskText))
                {
                    standalone.Add(new JsonObject { ["title"] = taskText });
                }
            }
            var grouped = new Dictionary<string, List<JsonObject>>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in standalone)
            {
                string key = (task["projectName"]?.GetValue<string>() ?? task["project"]?.GetValue<string>() ?? "").Trim();
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<JsonObject>();
                    grouped[key] = list;
                }
                list.Add(task);
            }
            foreach (var pair in grouped)
            {
                var matched = specs.FirstOrDefault(s =>
                    (s.Project["name"]?.GetValue<string>() ?? "").Trim()
                    .Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(pair.Key) && matched != null)
                {
                    matched.Tasks.AddRange(pair.Value);
                    continue;
                }
                var spec = new ProjectSpec { Project = new JsonObject { ["name"] = string.IsNullOrEmpty(pair.Key) ? Inbox : pair.Key } };
                spec.Tasks.AddRange(pair.Value);
                specs.Add(spec);
            }
        }

        if (specs.Count == 0)
        {
            throw new ArgumentException("没有识别到可导入的项目或任务，请参考 /api/export/todo 的输出格式");
        }
        return specs;
    }

    private static JsonObject ProjectSpecFromExport(JsonObject row)
    {
        var spec = new JsonObject();
        foreach (string key in new[] { "name", "description", "color", "colorMode", "icon", "expireAt", "pinned", "archived", "sortOrder" })
        {
            if (row.TryGetPropertyValue(key, out JsonNode? node) && node != null)
            {
                spec[key] = node.DeepClone();
            }
        }
        if (spec["name"] == null) spec["name"] = "导入项目";
        return spec;
    }

    private static JsonObject TaskSpecFromExport(JsonObject row)
    {
        var spec = new JsonObject();
        foreach (string key in new[] { "title", "notes", "priority", "status", "dueAt", "sortOrder" })
        {
            if (row.TryGetPropertyValue(key, out JsonNode? node) && node != null)
            {
                spec[key] = node.DeepClone();
            }
        }
        return spec;
    }

    private static JsonObject ImportResult(string mode, List<string> warnings)
    {
        return new JsonObject
        {
            ["mode"] = mode,
            ["projects"] = new JsonObject { ["created"] = 0, ["updated"] = 0 },
            ["tasks"] = new JsonObject { ["created"] = 0, ["updated"] = 0, ["skipped"] = 0 },
            ["warnings"] = new JsonArray(),
            ["config"] = false
        };
    }

    private static void Increment(JsonObject result, string group, string key)
    {
        if (result[group] is JsonObject obj)
        {
            obj[key] = (obj[key]?.GetValue<int>() ?? 0) + 1;
        }
    }

    private static JsonArray ToJsonArray(List<string> items)
    {
        var arr = new JsonArray();
        foreach (string item in items) arr.Add(item);
        return arr;
    }
}
