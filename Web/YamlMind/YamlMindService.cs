using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MiniMonitor.Web;

namespace MiniMonitor.Web.YamlMind;

/// <summary>YamlMind 内置服务：在本机启动 HTTP 服务，提供与 yamlmind.py 一致的页面和 API。</summary>
public sealed class YamlMindService : IDisposable
{
    public const int DefaultPort = 50003;
    public const string AppVersion = "1.3.4";

    private readonly WebAssetStore _assets = new("ym/");
    private readonly YamlMindStore _store;
    private readonly object _sync = new();
    private SimpleHttpServer? _server;
    private Thread? _maintenance;
    private ManualResetEventSlim? _stopEvent;
    private string? _lastOptimizeDay;
    private string? _lastRelockDay;
    private DateTime _startedAt;

    public int Port { get; }
    public string Url => $"http://localhost:{Port}/";
    public bool IsRunning { get; private set; }
    public string DataDir => _store.DataDir;

    public YamlMindService(int port = DefaultPort, string? dataDir = null)
    {
        Port = port;
        _store = new YamlMindStore(dataDir);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                return;
            }
            _store.Initialize();
            var server = new SimpleHttpServer(IPAddress.Loopback, Port, "YamlMind", HandleAsync,
                maxBodyLength: 64L * 1024 * 1024);
            try
            {
                server.Start();
            }
            catch
            {
                _store.Dispose();
                throw;
            }
            _server = server;
            _startedAt = DateTime.Now;
            _stopEvent = new ManualResetEventSlim(false);
            _maintenance = new Thread(MaintenanceLoop) { IsBackground = true, Name = "YamlMind-maintenance" };
            _maintenance.Start();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning)
            {
                return;
            }
            IsRunning = false;
            try { _stopEvent?.Set(); } catch { }
            try { _server?.Stop(); } catch { }
            _server = null;
            Thread? maintenance = _maintenance;
            try { maintenance?.Join(1500); } catch { }
            _maintenance = null;
            _stopEvent?.Dispose();
            _stopEvent = null;
            // 若维护任务仍在执行（例如正好在 01:30 优化数据库），不阻塞退出：
            // 连接保留到进程结束或下次启动；正常情况则立即释放。
            if (maintenance == null || !maintenance.IsAlive)
            {
                try { _store.Dispose(); } catch { }
            }
        }
    }

    public void Dispose() => Stop();

    private async Task<HttpResponse> HandleAsync(HttpRequest request)
    {
        // 仅允许本机主机名访问（防 DNS 重绑定）+ 同源校验；此前 ACAO:* 允许任意网页读取本机数据，必须移除。
        var blocked = LocalHttpGuard.Check(request, Port);
        if (blocked != null)
        {
            return ErrorResponse(blocked.Value.Status,
                blocked.Value.Status == 403 ? "origin_blocked" : "bad_host", blocked.Value.Message);
        }
        HttpResponse response;
        try
        {
            response = await Task.Run(() => Dispatch(request));
        }
        catch (HttpApiException ex)
        {
            response = ErrorResponse(ex.Status, ex.Code, ex.Message);
        }
        catch (YamlMindStore.PermissionDeniedException)
        {
            response = ErrorResponse(423, "locked", "锁定模式已开启，禁止该操作");
        }
        catch (YamlMindStore.ValueErrorException ex)
        {
            response = ErrorResponse(400, "bad_backup", ex.Message);
        }
        catch (KeyNotFoundException)
        {
            response = ErrorResponse(404, "not_found", "文档不存在");
        }
        catch (Exception ex)
        {
            response = ErrorResponse(500, "server_error", ex.Message);
        }
        // 前端与本服务同源（127.0.0.1:50003），无需 CORS 响应头；
        // 此处的 ACAO:* 已移除，否则任意网站的 JS 都能读取本机 API 响应。
        return response;
    }

    private HttpResponse ErrorResponse(int status, string code, string message, bool noStore = true)
    {
        var payload = new JsonObject
        {
            ["ok"] = false,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        return HttpResponse.Json(status, payload, noStore);
    }

    private HttpResponse Dispatch(HttpRequest req)
    {
        string method = req.Method.ToUpperInvariant();
        string path = req.Path;

        if (method == "OPTIONS")
        {
            var opt = new HttpResponse { Status = 204, Body = Array.Empty<byte>() };
            return opt;
        }

        // ---------- 页面 ----------
        if (method is "GET" or "HEAD")
        {
            switch (path)
            {
                case "/" or "/index.html":
                    return ServePage("index.html");
                case "/editor" or "/editor.html":
                    return ServePage("editor.html");
                case "/mindmap" or "/mindmap.html":
                    return ServePage("mindmap.html");
                case "/settings" or "/settings.html":
                    return ServePage("settings.html");
            }
            if (path.StartsWith("/static/", StringComparison.Ordinal))
            {
                return ServeStatic(path["/static/".Length..], req);
            }
        }

        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            return HttpResponse.Text(404, "Not Found");
        }

        string[] seg = path["/api/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        string api = string.Join("/", seg);

        // ---------- 健康 / 引导 ----------
        if (method == "GET" && api == "health")
        {
            return HttpResponse.Json(200, new JsonObject
            {
                ["ok"] = true,
                ["app"] = "yamlmind",
                ["version"] = AppVersion,
                ["port"] = Port,
                ["db"] = _store.DbPath
            }, noStore: true);
        }

        if (method == "GET" && api == "bootstrap")
        {
            var settings = SettingsJson();
            var stats = _store.DbStats();
            return HttpResponse.Json(200, new JsonObject
            {
                ["ok"] = true,
                ["version"] = AppVersion,
                ["settings"] = settings,
                ["stats"] = stats.DeepClone(),
                ["schedule"] = NextRuns(),
                ["counts"] = new JsonObject
                {
                    ["docs"] = stats["doc_count"]?.DeepClone(),
                    ["versions"] = stats["version_count"]?.DeepClone()
                },
                ["templates"] = TemplatesJson()
            }, noStore: true);
        }

        // ---------- 文档 ----------
        if (api == "docs")
        {
            if (method == "GET")
            {
                string sort = req.Q("sort", "updated") ?? "updated";
                string search = req.Q("search", "") ?? "";
                int limit = req.QInt("limit", 40, 1, 200);
                int offset = req.QInt("offset", 0, 0);
                var rows = _store.ListDocs(sort, search, limit, offset);
                var docs = new JsonArray();
                foreach (var row in rows)
                {
                    docs.Add(RowToJson(row, convertError: true));
                }
                return HttpResponse.Json(200, new JsonObject
                {
                    ["ok"] = true,
                    ["docs"] = docs,
                    ["total"] = _store.CountDocs(search)
                }, noStore: true);
            }
            if (method == "POST")
            {
                var body = req.JsonBody();
                string title = body["title"]?.GetValue<string>() ?? "未命名导图";
                string content = body["content"]?.GetValue<string>() ?? "";
                string? templateId = body["template"]?.GetValue<string>();
                string? templateContent = TemplateContent(templateId);
                var doc = _store.CreateDoc(title, content, templateContent);
                return HttpResponse.Json(201, new JsonObject
                {
                    ["ok"] = true,
                    ["doc"] = RowToJson(doc, convertError: true)
                }, noStore: true);
            }
        }

        if (seg.Length >= 2 && seg[0] == "docs")
        {
            string docId = seg[1];
            if (seg.Length == 2)
            {
                if (method == "GET")
                {
                    bool withBody = req.QBool("body", true);
                    var doc = _store.GetDoc(docId, withBody);
                    if (doc == null) throw new HttpApiException("文档不存在", 404, "not_found");
                    _store.TouchOpened(docId);
                    return HttpResponse.Json(200, new JsonObject
                    {
                        ["ok"] = true,
                        ["doc"] = RowToJson(doc, convertError: true)
                    }, noStore: true);
                }
                if (method == "PUT")
                {
                    if (_store.GetMeta(docId) == null) throw new HttpApiException("文档不存在", 404, "not_found");
                    var body = req.JsonBody();
                    string? content = body["content"] is JsonNode c ? c.GetValue<string>() : null;
                    string? title = body["title"] is JsonNode t ? t.GetValue<string>() : null;
                    int? revision = body["revision"] is JsonNode r && int.TryParse(r.ToJsonString(), out int rv)
                        ? rv : null;
                    bool snapshot = true;
                    if (body["snapshot"] is JsonNode s)
                    {
                        snapshot = s.GetValueKind() switch
                        {
                            System.Text.Json.JsonValueKind.True => true,
                            System.Text.Json.JsonValueKind.False => false,
                            System.Text.Json.JsonValueKind.String => s.GetValue<string>().ToLowerInvariant() is "1" or "true" or "on" or "yes",
                            _ => true
                        };
                    }
                    var doc = _store.SaveDoc(docId, content, title, revision, snapshot);
                    var tree = _store.GetTree(docId) ?? new JsonObject();
                    return HttpResponse.Json(200, new JsonObject
                    {
                        ["ok"] = true,
                        ["doc"] = RowToJson(doc, convertError: true),
                        ["parsed"] = new JsonObject
                        {
                            ["ok"] = tree["ok"]?.DeepClone(),
                            ["error"] = tree["error"]?.DeepClone(),
                            ["stats"] = tree["stats"]?.DeepClone()
                        }
                    }, noStore: true);
                }
                if (method == "DELETE")
                {
                    GuardLock("删除思维导图");
                    if (!_store.DeleteDoc(docId)) throw new HttpApiException("文档不存在", 404, "not_found");
                    return HttpResponse.Json(200, new JsonObject { ["ok"] = true, ["deleted"] = docId }, noStore: true);
                }
            }
            if (seg.Length == 3 && seg[2] == "flush" && method == "POST")
            {
                string raw = req.JsonText();
                try
                {
                    var doc = _store.SaveDoc(docId, raw, null, null, false);
                    return HttpResponse.Json(200, new JsonObject
                    {
                        ["ok"] = true,
                        ["revision"] = ToNode(doc["revision"])
                    }, noStore: true);
                }
                catch (KeyNotFoundException)
                {
                    throw new HttpApiException("文档不存在", 404, "not_found");
                }
            }
            if (seg.Length == 3 && seg[2] == "duplicate" && method == "POST")
            {
                Dictionary<string, object?> doc;
                try
                {
                    doc = _store.DuplicateDoc(docId);
                }
                catch (KeyNotFoundException)
                {
                    throw new HttpApiException("文档不存在", 404, "not_found");
                }
                return HttpResponse.Json(200, new JsonObject
                {
                    ["ok"] = true,
                    ["doc"] = RowToJson(doc, convertError: true)
                }, noStore: true);
            }
            if (seg.Length == 3 && seg[2] == "rev" && method == "GET")
            {
                var meta = _store.GetMeta(docId) ?? throw new HttpApiException("文档不存在", 404, "not_found");
                return HttpResponse.Json(200, new JsonObject
                {
                    ["ok"] = true,
                    ["revision"] = ToNode(meta["revision"]),
                    ["valid"] = ToNode(meta["valid"]),
                    ["updated_at"] = ToNode(meta["updated_at"]),
                    ["title"] = ToNode(meta["title"])
                }, noStore: true);
            }
            if (seg.Length == 3 && seg[2] == "tree" && method == "GET")
            {
                bool rebuild = req.QBool("rebuild", false);
                var tree = _store.GetTree(docId, rebuild) ?? throw new HttpApiException("文档不存在", 404, "not_found");
                var meta = _store.GetMeta(docId) ?? new Dictionary<string, object?>();
                return HttpResponse.Json(200, new JsonObject
                {
                    ["ok"] = true,
                    ["tree"] = tree.DeepClone(),
                    ["meta"] = RowToJson(meta, convertError: true),
                    ["revision"] = ToNode(meta.TryGetValue("revision", out object? rv) ? rv : null),
                    ["lock"] = _store.LockModeOn()
                }, noStore: true);
            }
            if (seg.Length == 3 && seg[2] == "versions" && method == "GET")
            {
                int limit = req.QInt("limit", 30);
                var versions = new JsonArray();
                foreach (var row in _store.Versions(docId, limit))
                {
                    versions.Add(RowToJson(row, convertError: false));
                }
                return HttpResponse.Json(200, new JsonObject
                {
                    ["ok"] = true,
                    ["versions"] = versions
                }, noStore: true);
            }
        }

        if (seg.Length >= 2 && seg[0] == "versions" && seg.Length == 3 && seg[2] == "restore" && method == "POST")
        {
            if (!long.TryParse(seg[1], out long vid)) throw new HttpApiException("历史版本不存在", 404, "not_found");
            var version = _store.VersionContent(vid)
                          ?? throw new HttpApiException("历史版本不存在（可能已被轮换清理）", 404, "not_found");
            var doc = _store.SaveDoc((string)version["doc_id"]!, (string)version["content"]!,
                (string?)version["title"], null, true);
            return HttpResponse.Json(200, new JsonObject
            {
                ["ok"] = true,
                ["doc"] = RowToJson(doc, convertError: true)
            }, noStore: true);
        }

        if (api == "validate" && method == "POST")
        {
            var body = req.JsonBody();
            string content = body["content"]?.GetValue<string>() ?? "";
            string title = body["title"]?.GetValue<string>() ?? "思维导图";
            var parsed = YamlMindTree.Parse(content, title);
            var outline = new JsonArray();
            foreach (var item in YamlMindTree.Flatten(parsed))
            {
                outline.Add(item);
            }
            return HttpResponse.Json(200, new JsonObject
            {
                ["ok"] = parsed.Ok,
                ["error"] = parsed.Error?.DeepClone(),
                ["stats"] = parsed.StatsJson(),
                ["empty"] = parsed.Empty,
                ["root_title"] = parsed.Root.Title,
                ["outline"] = outline
            }, noStore: true);
        }

        // ---------- 设置 / 备份 / 维护 ----------
        if (api == "settings")
        {
            if (method == "GET")
            {
                return HttpResponse.Json(200, new JsonObject
                {
                    ["ok"] = true,
                    ["settings"] = SettingsJson(),
                    ["schedule"] = NextRuns()
                }, noStore: true);
            }
            if (method == "PUT")
            {
                var body = req.JsonBody();
                var allowed = new Dictionary<string, string[]>
                {
                    ["theme"] = new[] { "dark", "light", "auto" },
                    ["layout"] = new[] { "radial", "tree" },
                    ["lock_mode"] = new[] { "on", "off" },
                    ["auto_save"] = new[] { "on", "off" },
                    ["guide_lines"] = new[] { "on", "off" }
                };
                foreach (var pair in allowed)
                {
                    if (body[pair.Key] == null) continue;
                    string value = body[pair.Key]!.GetValue<string>();
                    if (!pair.Value.Contains(value))
                    {
                        throw new HttpApiException($"{pair.Key} 仅支持 {string.Join(", ", pair.Value)}", 400, "bad_value");
                    }
                    _store.PutSetting(pair.Key, value);
                }
                if (body["font_size"] != null)
                {
                    string raw = body["font_size"]!.ToJsonString().Trim('"');
                    if (!int.TryParse(raw, out int size)) size = 14;
                    size = Math.Max(10, Math.Min(28, size));
                    _store.PutSetting("font_size", size.ToString());
                }
                if (body["lock_mode"]?.GetValue<string>() == "on")
                {
                    long job = _store.JobStart("relock");
                    _store.JobFinish(job, "ok", "{\"manual\":true}");
                }
                return HttpResponse.Json(200, new JsonObject
                {
                    ["ok"] = true,
                    ["settings"] = SettingsJson(),
                    ["schedule"] = NextRuns()
                }, noStore: true);
            }
        }

        if (api == "settings/relock-now" && method == "POST")
        {
            JsonObject info = Relock("manual");
            long job = _store.JobStart("relock");
            _store.JobFinish(job, "ok", info.ToJsonString(HttpResponse.JsonOptions));
            info["ok"] = true;
            return HttpResponse.Json(200, info, noStore: true);
        }

        if (api == "backup" && method == "GET")
        {
            bool versions = req.QBool("versions", false);
            bool download = req.QBool("download", true);
            var payload = _store.ExportBackup(versions);
            if (!download)
            {
                return HttpResponse.Json(200, payload, noStore: true);
            }
            string name = $"yamlmind-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            byte[] raw = Encoding.UTF8.GetBytes(payload.ToJsonString(HttpResponse.JsonOptions));
            return HttpResponse.Bytes(200, raw, "application/json; charset=utf-8",
                "no-store, must-revalidate", contentDisposition: $"attachment; filename=\"{name}\"");
        }

        if (api == "import" && method == "POST")
        {
            GuardLock("导入数据");
            string raw = req.JsonText();
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new HttpApiException("请选择要导入的备份 JSON 文件", 400, "empty");
            }
            JsonObject payload;
            try
            {
                payload = JsonNode.Parse(raw) as JsonObject
                          ?? throw new HttpApiException("备份文件不是合法 JSON", 400, "bad_json");
            }
            catch (HttpApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpApiException("备份文件不是合法 JSON：" + ex.Message, 400, "bad_json");
            }
            string mode = req.Q("mode", "merge") ?? "merge";
            var result = _store.ImportBackup(payload, mode);
            result["ok"] = true;
            result["settings"] = SettingsJson();
            return HttpResponse.Json(200, result, noStore: true);
        }

        if (api == "status" && method == "GET")
        {
            var stats = _store.DbStats();
            var lastOptimize = _store.LastJob("optimize");
            var lastRelock = _store.LastJob("relock");
            return HttpResponse.Json(200, new JsonObject
            {
                ["ok"] = true,
                ["stats"] = stats,
                ["schedule"] = NextRuns(),
                ["settings"] = SettingsJson(),
                ["last_optimize"] = lastOptimize != null ? RowToJson(lastOptimize, false) : null,
                ["last_relock"] = lastRelock != null ? RowToJson(lastRelock, false) : null
            }, noStore: true);
        }

        if (api == "backups" && method == "GET")
        {
            var backups = new JsonArray();
            foreach (var backup in _store.ListBackups())
            {
                backups.Add(backup.DeepClone());
            }
            return HttpResponse.Json(200, new JsonObject
            {
                ["ok"] = true,
                ["backups"] = backups,
                ["keep"] = YamlMindStore.AutoBackupKeep,
                ["dir"] = _store.BackupDir
            }, noStore: true);
        }

        if (seg.Length == 2 && seg[0] == "backups" && method == "GET")
        {
            string name = seg[1];
            if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            {
                throw new HttpApiException("备份文件不存在", 404, "not_found");
            }
            bool exists = _store.ListBackups().Any(b => b["name"]?.GetValue<string>() == name);
            if (!exists) throw new HttpApiException("备份文件不存在", 404, "not_found");
            string full = Path.Combine(_store.BackupDir, name);
            return HttpResponse.Bytes(200, File.ReadAllBytes(full), "application/json",
                "no-store, must-revalidate", contentDisposition: $"attachment; filename=\"{name}\"");
        }

        if (api == "maintenance/optimize" && method == "POST")
        {
            var result = _store.OptimizeDb(rebuildFts: true);
            result["ok"] = result["status"]?.GetValue<string>() == "ok";
            return HttpResponse.Json(200, result, noStore: true);
        }

        if (api == "maintenance/vacuum" && method == "POST")
        {
            var result = _store.VacuumFull();
            result["ok"] = result["status"]?.GetValue<string>() == "ok";
            return HttpResponse.Json(200, result, noStore: true);
        }

        throw new HttpApiException($"接口不存在：{method} {req.Path}", 404, "not_found");
    }

    private void GuardLock(string action)
    {
        if (_store.LockModeOn())
        {
            throw new HttpApiException($"锁定模式已开启，禁止{action}。请在「设置」页关闭锁定模式后再操作。",
                423, "locked");
        }
    }

    private JsonObject SettingsJson()
    {
        var obj = new JsonObject();
        foreach (var pair in _store.GetSettings())
        {
            obj[pair.Key] = pair.Value;
        }
        return obj;
    }

    private static JsonArray TemplatesJson()
    {
        return new JsonArray
        {
            new JsonObject { ["id"] = "blank", ["title"] = "空白导图", ["preview"] = BlankYaml[..Math.Min(60, BlankYaml.Length)] },
            new JsonObject { ["id"] = "outline", ["title"] = "项目大纲", ["preview"] = OutlineYaml[..Math.Min(60, OutlineYaml.Length)] },
            new JsonObject { ["id"] = "demo", ["title"] = "示例文档", ["preview"] = DemoTemplate[..Math.Min(60, DemoTemplate.Length)] }
        };
    }

    private static string? TemplateContent(string? id)
    {
        return id switch
        {
            "blank" => BlankYaml,
            "outline" => OutlineYaml,
            "demo" => DemoTemplate,
            _ => null
        };
    }

    private const string BlankYaml = """
# 从这里开始写你的思维导图
主题:
  分支一:
    - 要点 A
    - 要点 B
  分支二:
    子项: 值
""";

    private const string OutlineYaml = """
项目大纲:
  一 背景:
    问题: 现有流程割裂
    目标: 一体化管理
  二 方案:
    架构:
      前端: 单页应用
      后端: Python 服务
      存储: SQLite (WAL)
    排期:
      - 第 1 周: 需求确认
      - 第 2 周: 开发
      - 第 3 周: 联调
  三 验收:
    - 指标: 页面秒开
    - 复盘: 双周一次
""";

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

    private HttpResponse ServePage(string name)
    {
        byte[]? data = _assets.Get(name);
        if (data == null)
        {
            return HttpResponse.Text(404, name);
        }
        return HttpResponse.Bytes(200, data, "text/html; charset=utf-8", "no-store, must-revalidate");
    }

    private HttpResponse ServeStatic(string path, HttpRequest req)
    {
        byte[]? data = _assets.Get(path);
        if (data == null)
        {
            return HttpResponse.Text(404, path);
        }
        using var sha1 = SHA1.Create();
        string tag = "\"" + Convert.ToHexString(sha1.ComputeHash(data))[..16].ToLowerInvariant() + "\"";
        if (req.Header("if-none-match") == tag)
        {
            var notModified = new HttpResponse { Status = 304, Body = Array.Empty<byte>(), ContentType = null };
            notModified.Headers["ETag"] = tag;
            return notModified;
        }
        return HttpResponse.Bytes(200, data, WebAssetStore.MimeType(path),
            "public, max-age=604800", etag: tag);
    }

    private JsonObject NextRuns()
    {
        DateTime now = DateTime.Now;
        DateTime optimize = NextRun(now, YamlMindStore.OptimizeAt.Hour, YamlMindStore.OptimizeAt.Minute);
        DateTime relock = NextRun(now, YamlMindStore.RelockAt.Hour, YamlMindStore.RelockAt.Minute);
        return new JsonObject
        {
            ["now"] = (long)((DateTimeOffset)now).ToUnixTimeMilliseconds(),
            ["optimize_at"] = optimize.ToString("yyyy-MM-dd HH:mm:ss"),
            ["relock_at"] = relock.ToString("yyyy-MM-dd HH:mm:ss"),
            ["optimize_schedule"] = $"{YamlMindStore.OptimizeAt.Hour:00}:{YamlMindStore.OptimizeAt.Minute:00}",
            ["relock_schedule"] = $"{YamlMindStore.RelockAt.Hour:00}:{YamlMindStore.RelockAt.Minute:00}"
        };
    }

    private static DateTime NextRun(DateTime now, int hour, int minute)
    {
        DateTime target = now.Date.AddHours(hour).AddMinutes(minute);
        if (target <= now)
        {
            target = target.AddDays(1);
        }
        return target;
    }

    private JsonObject Relock(string reason)
    {
        var settings = _store.GetSettings();
        string was = settings.TryGetValue("lock_mode", out string? v) ? v : "off";
        _store.PutSetting("lock_mode", "on");
        return new JsonObject
        {
            ["was"] = was,
            ["now"] = "on",
            ["reason"] = reason,
            ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private void MaintenanceLoop()
    {
        ManualResetEventSlim? stopEvent = _stopEvent;
        if (stopEvent == null)
        {
            return;
        }
        try
        {
            // 启动自检：让每个文档都有可渲染的缓存树（与 Python 版行为一致）
            if (stopEvent.Wait(400))
            {
                return;
            }
            try
            {
                // 只补齐缺失的正文/缓存树；全量优化由每日 01:30 任务负责
                _store.StartupSelfCheck();
            }
            catch
            {
                // 启动自检失败不应影响服务
            }

            while (!stopEvent.IsSet)
            {
                DateTime now = DateTime.Now;
                string day = now.ToString("yyyy-MM-dd");
                if (now.Hour == YamlMindStore.OptimizeAt.Hour && now.Minute == YamlMindStore.OptimizeAt.Minute
                    && _lastOptimizeDay != day)
                {
                    _lastOptimizeDay = day;
                    long job = _store.JobStart("optimize-auto");
                    try
                    {
                        _store.JobFinish(job, "ok", "{\"scheduled\":true}");
                        _store.OptimizeDb(rebuildFts: false);
                    }
                    catch
                    {
                        // 维护任务异常不影响服务
                    }
                    if (stopEvent.Wait(70 * 1000))
                    {
                        return;
                    }
                }
                if (now.Hour == YamlMindStore.RelockAt.Hour && now.Minute == YamlMindStore.RelockAt.Minute
                    && _lastRelockDay != day)
                {
                    _lastRelockDay = day;
                    long job = _store.JobStart("relock");
                    try
                    {
                        var info = Relock("scheduled");
                        _store.JobFinish(job, "ok", info.ToJsonString(HttpResponse.JsonOptions));
                    }
                    catch (Exception ex)
                    {
                        _store.JobFinish(job, "error", ex.Message);
                    }
                }
                if (stopEvent.Wait(1000))
                {
                    return;
                }
            }
        }
        catch
        {
            // 维护线程退出
        }
    }

    private static JsonObject RowToJson(Dictionary<string, object?> row, bool convertError)
    {
        var obj = new JsonObject();
        foreach (var pair in row)
        {
            obj[pair.Key] = ToNode(pair.Value);
        }
        if (convertError && obj.TryGetPropertyValue("error", out JsonNode? err) && err is JsonValue value
            && value.TryGetValue<string>(out string? errText) && errText.Length > 0)
        {
            try
            {
                obj["error"] = JsonNode.Parse(errText);
            }
            catch
            {
                // 保留原字符串
            }
        }
        return obj;
    }

    private static JsonNode? ToNode(object? value)
    {
        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            float f => JsonValue.Create(f),
            string s => JsonValue.Create(s),
            _ => JsonValue.Create(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
        };
    }
}
