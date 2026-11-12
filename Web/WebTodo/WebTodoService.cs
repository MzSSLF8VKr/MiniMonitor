using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MiniMonitor.Web;

namespace MiniMonitor.Web.WebTodo;

/// <summary>WebTodo 内置服务：原生 C# 复刻 Python 单文件版（Todo Push），默认端口 50004。
/// 仅提供任务/项目管理、NTP 校时与数据导入导出，不含提醒推送、邮件与操作日志。</summary>
public sealed class WebTodoService : IDisposable
{
    public const int DefaultPort = TodoConfig.DefaultPort;
    public const string AppVersion = "1.1.0";

    private readonly WebAssetStore _assets = new("wt/");
    private readonly object _sync = new();
    private readonly string? _dataDirOverride;
    private SimpleHttpServer? _server;
    private TodoConfig? _config;
    private WebTodoDatabase? _db;
    private ProjectStore? _projects;
    private TaskStore? _tasks;
    private NtpClock? _ntp;
    private TodoTransfer? _transfer;
    private string _startedAt = "";

    public int Port { get; }
    public string Url => $"http://localhost:{Port}/";
    public bool IsRunning { get; private set; }
    public string DataDir => _config?.DataDir ?? (_dataDirOverride ?? TodoConfig.ResolveDefaultDataDir());

    public WebTodoService(int port = DefaultPort, string? dataDir = null)
    {
        Port = port;
        _dataDirOverride = dataDir;
    }

    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning) return;
            var config = new TodoConfig(_dataDirOverride);
            var db = new WebTodoDatabase(config.DbPath);
            db.Initialize();
            var projects = new ProjectStore(db);
            var tasks = new TaskStore(db);
            var ntp = new NtpClock(config);
            var transfer = new TodoTransfer(db, config);
            var server = new SimpleHttpServer(IPAddress.Loopback, Port, "WebTodo", HandleAsync);
            _config = config;
            _db = db;
            _projects = projects;
            _tasks = tasks;
            _ntp = ntp;
            _transfer = transfer;
            _startedAt = TodoClock.IsoNow();
            try
            {
                server.Start();
            }
            catch
            {
                db.Dispose();
                throw;
            }
            _server = server;
            ntp.Start();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { _ntp?.Stop(); } catch { }
            try { _server?.Stop(); } catch { }
            _server = null;
            try { _db?.Dispose(); } catch { }
            _db = null;
            _config = null;
            _projects = null;
            _tasks = null;
            _ntp = null;
            _transfer = null;
        }
    }

    public void Dispose() => Stop();

    private async Task<HttpResponse> HandleAsync(HttpRequest request)
    {
        bool isApi = request.Path.StartsWith("/api/", StringComparison.Ordinal);
        if (!isApi)
        {
            return HandleStatic(request);
        }

        var blocked = CheckOrigin(request);
        if (blocked != null)
        {
            var (status, message) = blocked.Value;
            var payload = new JsonObject
            {
                ["ok"] = false,
                ["error"] = message,
                ["code"] = status == 403 ? "ORIGIN_BLOCKED" : "BAD_HOST"
            };
            return HttpResponse.Json(status, payload);
        }

        if (request.BufferLengthExceeded())
        {
            return ErrorResponse(413, "请求体过大（上限 8 MB）", "PAYLOAD_TOO_LARGE");
        }

        try
        {
            return (await Task.Run(() => DispatchApi(request))).Response;
        }
        catch (TodoNotFoundException ex)
        {
            return ErrorResponse(404, $"记录不存在：{ex.Message}", "NOT_FOUND");
        }
        catch (HttpApiException ex)
        {
            return ErrorResponse(ex.Status, ex.Message, ex.Code);
        }
        catch (ArgumentException ex)
        {
            return ErrorResponse(400, ex.Message, "VALIDATION_ERROR");
        }
        catch (Exception ex)
        {
            return ErrorResponse(500, $"服务器内部错误：{ex.GetType().Name}: {ex.Message}", "INTERNAL_ERROR");
        }
    }

    private HttpResponse ErrorResponse(int status, string message, string code)
    {
        return HttpResponse.Json(status, new JsonObject
        {
            ["ok"] = false,
            ["error"] = message,
            ["code"] = code
        });
    }

    private static HttpResponse Data(JsonNode? data, int status = 200)
    {
        return HttpResponse.Json(status, new JsonObject { ["ok"] = true, ["data"] = data });
    }

    private (int Status, string Message)? CheckOrigin(HttpRequest req)
    {
        // 仅允许本机主机名访问（防 DNS 重绑定）+ 同源校验，统一实现在 LocalHttpGuard。
        return LocalHttpGuard.Check(req, Port);
    }

    private sealed class RouteResult
    {
        public HttpResponse Response = HttpResponse.Json(200, new JsonObject());
        public string Action = "api.unknown";
        public string? TargetType;
        public string? TargetId;
    }

    private RouteResult Route(HttpResponse response, string action, string? targetType = null, string? targetId = null)
    {
        return new RouteResult { Response = response, Action = action, TargetType = targetType, TargetId = targetId };
    }

    private RouteResult DispatchApi(HttpRequest req)
    {
        string method = req.Method.ToUpperInvariant();
        string api = req.Path["/api/".Length..].Trim('/');
        string[] seg = api.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (method == "OPTIONS")
        {
            var opt = new HttpResponse { Status = 204, Body = Array.Empty<byte>() };
            opt.Headers["Allow"] = "GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS";
            return Route(opt, "api.options");
        }

        // ---------------- 状态 ----------------
        if (method == "GET" && api == "health")
        {
            return Route(Data(new JsonObject { ["status"] = "alive", ["serverTime"] = TodoClock.IsoNow() }),
                "system.health");
        }
        if (method == "GET" && api == "state")
        {
            return Route(Data(StateJson()), "state.view");
        }
        if (method == "GET" && api == "stats/overview")
        {
            return Route(Data(StatsOverview(req.QInt("days", 14, 1, 90))), "stats.view");
        }

        // ---------------- 项目 ----------------
        if (api == "projects")
        {
            if (method == "GET")
            {
                var items = _projects!.List(req.QBool("includeArchived", false), req.Q("q"));
                return Route(Data(new JsonObject
                {
                    ["items"] = TodoJson.ToJsonArray(items.Select(i => (object?)i)),
                    ["total"] = items.Count
                }), "project.list");
            }
            if (method == "POST")
            {
                var project = _projects!.Create(req.JsonBody());
                return Route(Data(TodoJson.ToJsonObject(project)), "project.create",
                    "project", Convert.ToString(project.GetValueOrDefault("id")));
            }
        }
        if (api == "projects/reorder" && method == "POST")
        {
            var body = req.JsonBody();
            var ids = StringList(body["ids"]);
            if (body["ids"] is not JsonArray)
            {
                throw new HttpApiException("ids 必须是数组", 400, "VALIDATION_ERROR");
            }
            return Route(Data(new JsonObject { ["reordered"] = _projects!.Reorder(ids) }), "project.reorder");
        }
        if (seg.Length == 2 && seg[0] == "projects")
        {
            string id = seg[1];
            if (method == "GET")
            {
                return Route(Data(TodoJson.ToJsonObject(_projects!.Get(id))), "project.view", "project", id);
            }
            if (method == "PATCH")
            {
                var (project, changed) = _projects!.Update(id, req.JsonBody());
                return Route(Data(new JsonObject
                {
                    ["project"] = TodoJson.ToJsonObject(project),
                    ["changed"] = TodoJson.ToJsonObject(changed)
                }), "project.update", "project", id);
            }
            if (method == "DELETE")
            {
                bool cascade = req.QBool("cascade", false);
                long taskCount = _db!.Scalar("SELECT COUNT(*) FROM tasks WHERE project_id=@id", ("@id", id));
                if (taskCount > 0 && !cascade)
                {
                    throw new HttpApiException($"项目下仍有 {taskCount} 个任务，确认后可连带删除（cascade=1）",
                        409, "PROJECT_NOT_EMPTY");
                }
                _projects!.Delete(id, cascade);
                return Route(Data(new JsonObject
                {
                    ["deleted"] = id,
                    ["tasksRemoved"] = cascade ? taskCount : 0
                }), "project.delete", "project", id);
            }
        }

        // ---------------- 任务 ----------------
        if (api == "tasks")
        {
            if (method == "GET")
            {
                int limit = req.QInt("limit", 0);
                string status = req.Q("status", "open") ?? "open";
                var (items, total) = _tasks!.List(req.Q("projectId"), status, req.Q("view"), req.Q("q"),
                    limit > 0 ? limit : null, req.QInt("offset", 0, 0));
                return Route(Data(new JsonObject
                {
                    ["items"] = TodoJson.ToJsonArray(items.Select(i => (object?)i)),
                    ["total"] = total,
                    ["limit"] = limit > 0 ? limit : null,
                    ["offset"] = req.QInt("offset", 0)
                }), "task.list");
            }
            if (method == "POST")
            {
                return Route(Data(TodoJson.ToJsonObject(CreateTask(req.JsonBody())), 200), "task.create");
            }
        }
        if (api == "tasks/reorder" && method == "POST")
        {
            var body = req.JsonBody();
            return Route(Data(new JsonObject
            {
                ["reordered"] = _tasks!.Reorder(ProjectStore.Str(body["projectId"]), StringList(body["ids"]))
            }), "task.reorder");
        }
        if (api == "tasks/batch" && method == "POST")
        {
            var body = req.JsonBody();
            string action = (ProjectStore.Str(body["action"]) ?? "").Trim();
            var idsNode = body["ids"] as JsonArray;
            var ids = StringList(idsNode);
            if (idsNode == null || ids.Count == 0)
            {
                throw new HttpApiException("ids 不能为空", 400, "VALIDATION_ERROR");
            }
            if (action is not ("complete" or "reopen" or "delete" or "move" or "priority"))
            {
                throw new HttpApiException($"不支持的批量操作：{action}", 400, "VALIDATION_ERROR");
            }
            int done = 0;
            foreach (string taskId in ids)
            {
                try
                {
                    switch (action)
                    {
                        case "delete":
                            _tasks!.Delete(taskId);
                            break;
                        case "move":
                            continue;
                        default:
                            _tasks!.SetStatus(taskId, action == "complete" ? "done" : "open");
                            break;
                    }
                    done++;
                }
                catch (TodoNotFoundException)
                {
                    continue;
                }
            }
            if (action == "move")
            {
                string? projectId = ProjectStore.Str(body["projectId"]);
                if (string.IsNullOrEmpty(projectId))
                {
                    throw new HttpApiException("移动任务需要 projectId", 400, "VALIDATION_ERROR");
                }
                done = _tasks!.Move(ids, projectId);
            }
            return Route(Data(new JsonObject { ["action"] = action, ["affected"] = done }), "task.batch");
        }
        if (seg.Length == 2 && seg[0] == "tasks")
        {
            string id = seg[1];
            if (method == "GET")
            {
                return Route(Data(TodoJson.ToJsonObject(_tasks!.Get(id))), "task.view", "task", id);
            }
            if (method == "PATCH")
            {
                var (task, changed) = _tasks!.Update(id, req.JsonBody());
                return Route(Data(new JsonObject
                {
                    ["task"] = TodoJson.ToJsonObject(task),
                    ["changed"] = TodoJson.ToJsonObject(changed)
                }), "task.update", "task", id);
            }
            if (method == "DELETE")
            {
                string title = Convert.ToString(_tasks!.Get(id).GetValueOrDefault("title"), CultureInfo.InvariantCulture) ?? "";
                _tasks.Delete(id);
                return Route(Data(new JsonObject { ["deleted"] = id, ["title"] = title }), "task.delete", "task", id);
            }
        }
        if (seg.Length == 3 && seg[0] == "tasks" && method == "POST")
        {
            string id = seg[1];
            string action = seg[2];
            if (action == "complete")
            {
                var task = _tasks!.SetStatus(id, "done");
                return Route(Data(TodoJson.ToJsonObject(task.Item1)), "task.complete", "task", id);
            }
            if (action == "reopen")
            {
                var task = _tasks!.SetStatus(id, "open");
                return Route(Data(TodoJson.ToJsonObject(task.Item1)), "task.reopen", "task", id);
            }
        }

        // ---------------- 校时 ----------------
        if (api == "clock" && method == "GET")
        {
            return Route(Data(_ntp!.Status()), "clock.view");
        }
        if (api == "clock/sync" && method == "POST")
        {
            bool synced = _ntp!.Sync();
            var status = _ntp.Status();
            if (!synced)
            {
                string error = status["lastError"]?.GetValue<string>() ?? "所有 NTP 服务器均不可达";
                throw new HttpApiException(error, 502, "NTP_ERROR");
            }
            return Route(Data(status), "clock.sync");
        }

        // ---------------- 设置 ----------------
        if (api == "settings")
        {
            if (method == "GET")
            {
                return Route(Data(SettingsJson()), "settings.view");
            }
            if (method == "PATCH")
            {
                var body = req.JsonBody();
                var changes = body.DeepClone().AsObject();
                changes.Remove("server");
                var settings = _config!.UpdateSettings(changes);
                if (changes["clock"] != null)
                {
                    _ntp?.Trigger();
                }
                return Route(Data(SettingsJson()), "settings.update");
            }
        }

        // ---------------- 导入导出 ----------------
        if (method == "GET" && api.StartsWith("export/", StringComparison.Ordinal))
        {
            string kind = api["export/".Length..];
            if (kind == "preview")
            {
                return Route(Data(_transfer!.Preview(req.Q("kind", "todo") ?? "todo")), "data.preview");
            }
            JsonObject data = kind == "full" ? _transfer!.ExportFull() : _transfer!.ExportTodo();
            string stem = kind == "full" ? "todo-full" : "todo-items";
            string filename = $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            byte[] raw = Encoding.UTF8.GetBytes(data.ToJsonString(TodoClock.JsonOptions));
            var response = HttpResponse.Bytes(200, raw, "application/json; charset=utf-8",
                contentDisposition: $"attachment; filename=\"{filename}\"");
            response.Headers["X-Export-Records"] =
                ((data["projects"] as JsonArray)?.Count ?? 0) + ((data["tasks"] as JsonArray)?.Count ?? 0) + "";
            return Route(response, kind == "full" ? "data.export.full" : "data.export.todo");
        }
        if (method == "POST" && (api == "import/full" || api == "import/todo"))
        {
            var body = req.JsonBody();
            JsonNode? payload = body["payload"] ?? body.DeepClone();
            string mode = (ProjectStore.Str(body["mode"]) ?? "merge").ToLowerInvariant();
            if (mode is not ("merge" or "replace"))
            {
                throw new HttpApiException("mode 只能是 merge 或 replace", 400, "VALIDATION_ERROR");
            }
            if (payload is JsonValue val && val.TryGetValue<string>(out string? text))
            {
                try { payload = JsonNode.Parse(text); }
                catch (Exception ex) { throw new HttpApiException("payload 不是合法 JSON：" + ex.Message, 400, "VALIDATION_ERROR"); }
            }
            JsonObject result = api == "import/full"
                ? _transfer!.ImportFull(payload, mode)
                : _transfer!.ImportTodo(payload, mode);
            return Route(Data(result), api == "import/full" ? "data.import.full" : "data.import.todo");
        }

        throw new HttpApiException($"接口不存在：{method} {req.Path}", 404, "NOT_FOUND");
    }

    // ============================== API 数据 ==============================

    private JsonObject StateJson()
    {
        var (tzName, offsetMinutes) = TodoClock.TzLabel();
        JsonObject settings = _config!.Settings;
        string host = "127.0.0.1";
        int port = Port;
        string nowIso = TodoClock.IsoNow();
        return new JsonObject
        {
            ["app"] = new JsonObject
            {
                ["name"] = "Todo Push",
                ["version"] = AppVersion,
                ["host"] = host,
                ["port"] = port,
                ["tz"] = tzName,
                ["utcOffsetMinutes"] = offsetMinutes,
                ["serverTime"] = nowIso,
                ["clockSynced"] = _ntp!.Status()["synced"]?.GetValue<bool>() ?? false,
                ["startedAt"] = _startedAt,
                ["platform"] = $"Windows {Environment.OSVersion.Version}",
                ["python"] = ".NET 8 原生 C# 复刻",
                ["dataDir"] = _config.DataDir,
                ["url"] = $"http://{host}:{port}/"
            },
            ["counts"] = new JsonObject
            {
                ["projects"] = _db!.Scalar("SELECT COUNT(*) FROM projects WHERE archived = 0"),
                ["tasks"] = _db.Scalar("SELECT COUNT(*) FROM tasks"),
                ["openTasks"] = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE status = 'open'"),
                ["dueToday"] = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE status = 'open' AND date(due_at) = date('now','localtime')"),
                ["overdue"] = _db.Scalar("SELECT COUNT(*) FROM tasks WHERE status = 'open' AND due_at IS NOT NULL AND datetime(due_at) < datetime(@now)",
                    ("@now", nowIso))
            },
            ["clock"] = _ntp.Status()
        };
    }

    private JsonObject StatsOverview(int days)
    {
        var dueByDay = new JsonArray();
        foreach (var row in _db!.Query("SELECT substr(due_at, 1, 10) AS day, COUNT(*) AS n FROM tasks " +
                                      "WHERE due_at IS NOT NULL AND substr(due_at,1,10) >= date('now','localtime',@d) " +
                                      "GROUP BY day ORDER BY day", ("@d", $"-{days} days")))
        {
            dueByDay.Add(new JsonObject
            {
                ["day"] = TodoJson.ToNode(row.GetValueOrDefault("day")),
                ["n"] = Convert.ToInt64(row.GetValueOrDefault("n") ?? 0L)
            });
        }
        return new JsonObject
        {
            ["dueByDay"] = dueByDay
        };
    }

    private JsonObject SettingsJson()
    {
        JsonObject settings = _config!.Settings;
        settings["server"] = new JsonObject
        {
            ["host"] = "127.0.0.1",
            ["port"] = Port
        };
        return settings;
    }

    private Dictionary<string, object?> CreateTask(JsonObject body)
    {
        var settings = _config!.Settings;
        var defaults = settings["defaults"] as JsonObject ?? new JsonObject();
        var payload = body.DeepClone().AsObject();
        if (payload["priority"] == null)
        {
            payload["priority"] = TodoClock.ToInt(defaults["priority"], 0);
        }
        var task = _tasks!.Create(payload);
        return task;
    }

    private static List<string> StringList(JsonNode? node)
    {
        var output = new List<string>();
        if (node is JsonArray arr)
        {
            foreach (JsonNode? item in arr)
            {
                if (item == null) continue;
                output.Add(item.GetValueKind() switch
                {
                    System.Text.Json.JsonValueKind.String => item.GetValue<string>(),
                    _ => item.ToJsonString().Trim('"')
                });
            }
        }
        return output;
    }

    // ============================== 静态页面 ==============================

    private static readonly Dictionary<string, string> Mime = new()
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".webp"] = "image/webp",
        [".woff2"] = "font/woff2",
        [".txt"] = "text/plain; charset=utf-8",
        [".map"] = "application/json; charset=utf-8"
    };

    private HttpResponse HandleStatic(HttpRequest req)
    {
        string method = req.Method.ToUpperInvariant();
        if (method is not ("GET" or "HEAD"))
        {
            return HttpResponse.Text(405, "Method Not Allowed");
        }
        string rel = req.Path.TrimStart('/');
        if (rel.Length == 0) rel = "index.html";
        string normalized = WebAssetStore.Normalize(rel);
        if (normalized.Length == 0)
        {
            return HttpResponse.Text(403, "forbidden");
        }
        byte[]? data = _assets.Get(normalized);
        if (data == null)
        {
            int slash = normalized.LastIndexOf('/');
            string baseName = slash >= 0 ? normalized[(slash + 1)..] : normalized;
            if (!baseName.Contains('.'))
            {
                data = _assets.Get("index.html");
                normalized = "index.html";
            }
        }
        if (data == null)
        {
            return HttpResponse.Text(404, "404 Not Found");
        }
        int dot = normalized.LastIndexOf('.');
        string ext = dot >= 0 ? normalized[dot..].ToLowerInvariant() : "";
        string contentType = Mime.TryGetValue(ext, out string? mime) ? mime : "application/octet-stream";
        string cache = ext == ".html" ? "no-store" : "no-cache";
        var response = HttpResponse.Bytes(200, data, contentType, cache);
        response.Headers["X-Content-Type-Options"] = "nosniff";
        return response;
    }
}

internal static class WebTodoRequestExtensions
{
    public static bool BufferLengthExceeded(this HttpRequest request)
    {
        return request.Body.LongLength > 8L * 1024 * 1024;
    }
}
