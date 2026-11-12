using System.Net;
using System.Text;
using MiniMonitor.Web;

namespace MiniMonitor.Web.Drawio;

/// <summary>
/// draw.io 内置服务：在本机 50000 端口提供完整的 draw.io 编辑器页面。
/// 所有前端资源打包嵌入程序并常驻内存、文本资源预压缩，保证首次打开和交互都尽可能快。
/// </summary>
public sealed class DrawioService : IDisposable
{
    public const int DefaultPort = 50000;
    public const string AppVersion = "32.6.10";

    private const long MaxProxyBytes = 64L * 1024 * 1024;
    private const string LocalizedPreConfigVersion = "mm-zh-16b-1";

    /// <summary>
    /// 追加到 draw.io PreConfig.js 末尾的本地化默认值：
    /// 首次打开默认使用简体中文，新建图形默认字号 16px 且加粗。
    /// 用户在 Extras → Language 中修改过语言后，不再强制覆盖。
    /// </summary>
    private const string DefaultsScript = """
        /* MiniMonitor: 默认简体中文 + 16px 加粗 */
        (function () {
          var MARK = 'mm-drawio-zh-default';
          var firstRun = true;
          try { firstRun = localStorage.getItem(MARK) !== '1'; } catch (e) { firstRun = true; }

          if (firstRun) {
            try {
              if (!urlParams['lang']) {
                urlParams['lang'] = 'zh';
                window.mxLanguage = 'zh';
              }
            } catch (e) { }
          }

          function applyDefaults() {
            try {
              if (window.mxConstants) {
                mxConstants.DEFAULT_FONTSIZE = 16;
                mxConstants.DEFAULT_FONTSTYLE = mxConstants.FONT_BOLD;
              }
              if (window.mxStylesheet && mxStylesheet.prototype && !mxStylesheet.prototype.__mmFontPatched) {
                var proto = mxStylesheet.prototype;
                proto.__mmFontPatched = true;
                var dv = proto.getDefaultVertexStyle;
                proto.getDefaultVertexStyle = function () {
                  var s = dv.call(this);
                  s.fontSize = 16;
                  s.fontStyle = 1;
                  return s;
                };
                var de = proto.getDefaultEdgeStyle;
                proto.getDefaultEdgeStyle = function () {
                  var s = de.call(this);
                  s.fontSize = 16;
                  s.fontStyle = 1;
                  return s;
                };
              }
              if (window.Menus && Menus.prototype) {
                Menus.prototype.defaultFontSize = '16';
              }
              if (window.Graph && Graph.prototype) {
                Graph.prototype.defaultVertexStyle = Graph.prototype.defaultVertexStyle || {};
                Graph.prototype.defaultVertexStyle.fontSize = 16;
                Graph.prototype.defaultVertexStyle.fontStyle = 1;
                Graph.prototype.defaultEdgeStyle = Graph.prototype.defaultEdgeStyle || {};
                Graph.prototype.defaultEdgeStyle.fontSize = 16;
                Graph.prototype.defaultEdgeStyle.fontStyle = 1;
              }
              if (firstRun) {
                if (window.mxSettings && mxSettings.settings
                    && (!urlParams['lang'] || urlParams['lang'] === 'zh')) {
                  mxSettings.settings.language = 'zh';
                  try { mxSettings.save(); } catch (e) { }
                }
                try { localStorage.setItem(MARK, '1'); } catch (e) { }
                firstRun = false;
              }
            } catch (e) { }
          }

          function installHook() {
            if (window.__mmDrawioDefaultsHooked) { return; }
            if (typeof window.checkAllLoaded !== 'function') {
              setTimeout(installHook, 10);
              return;
            }
            window.__mmDrawioDefaultsHooked = true;
            var original = window.checkAllLoaded;
            window.checkAllLoaded = function () {
              if (window.App && window.App.main && !window.__mmDrawioMainPatched) {
                window.__mmDrawioMainPatched = true;
                var main = window.App.main;
                window.App.main = function () {
                  applyDefaults();
                  return main.apply(this, arguments);
                };
              }
              return original.apply(this, arguments);
            };
          }
          installHook();
        })();
        """;

    private static readonly HttpClient ProxyClient = CreateProxyClient();
    private static readonly SemaphoreSlim ProxySlots = new(8, 8);

    private readonly object _sync = new();
    private SimpleHttpServer? _server;
    private DrawioAssetPack? _pack;
    private Exception? _loadError;

    public int Port { get; }
    public string Url => $"http://localhost:{Port}/";
    public bool IsRunning { get; private set; }
    public string DrawioVersion => AppVersion;

    public DrawioService(int port = DefaultPort)
    {
        Port = port;
    }

    private static HttpClient CreateProxyClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                return;
            }
            var server = new SimpleHttpServer(IPAddress.Loopback, Port, "drawio", HandleAsync);
            server.Start();
            _server = server;
            IsRunning = true;
            // 后台预热资源包，避免第一次打开页面时再花时间读取/解析索引。
            if (_pack == null && _loadError == null)
            {
                _ = Task.Run(EnsurePack);
            }
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
            try { _server?.Stop(); } catch { }
            _server = null;
            // 资源包保留在内存中，重新勾选服务时可立即使用。
        }
    }

    public void Dispose() => Stop();

    private DrawioAssetPack? EnsurePack()
    {
        if (_pack != null)
        {
            return _pack;
        }
        lock (_sync)
        {
            if (_pack != null)
            {
                return _pack;
            }
            try
            {
                _pack = DrawioAssetPack.Load();
                _loadError = null;
            }
            catch (Exception ex)
            {
                _loadError = ex;
            }
            return _pack;
        }
    }

    private async Task<HttpResponse> HandleAsync(HttpRequest request)
    {
        if (request.Path.Equals("/proxy", StringComparison.OrdinalIgnoreCase))
        {
            return await ProxyAsync(request);
        }
        return Handle(request);
    }

    /// <summary>原生实现 draw.io 自托管部署中的 /proxy?url= 接口，用于从网址导入图片、图库与模板。</summary>
    private static async Task<HttpResponse> ProxyAsync(HttpRequest request)
    {
        if (!request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            HttpResponse method = HttpResponse.Text(405, "Method Not Allowed");
            method.Headers["Allow"] = "GET";
            return method;
        }
        string rawUrl = request.Q("url") ?? "";
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return HttpResponse.Text(400, "无效的 url 参数");
        }
        if (IsBlockedHost(uri.Host))
        {
            return HttpResponse.Text(400, "不允许访问本机或链路本地地址");
        }
        if (!await ProxySlots.WaitAsync(TimeSpan.FromSeconds(30)))
        {
            return HttpResponse.Text(503, "代理请求过多，请稍后重试");
        }
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            string userAgent = request.Header("User-Agent");
            if (userAgent.Length > 0)
            {
                message.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            }
            using HttpResponseMessage upstream = await ProxyClient.SendAsync(message,
                HttpCompletionOption.ResponseHeadersRead);
            if (!upstream.IsSuccessStatusCode)
            {
                return HttpResponse.Text(502, "远程资源返回 " + (int)upstream.StatusCode);
            }
            await using Stream input = await upstream.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = await input.ReadAsync(buffer);
                if (read <= 0)
                {
                    break;
                }
                if (output.Length + read > MaxProxyBytes)
                {
                    return HttpResponse.Text(502, "远程资源超过 64MB 限制");
                }
                output.Write(buffer, 0, read);
            }
            byte[] data = output.ToArray();
            var response = new HttpResponse { Status = 200 };
            if (request.Q("base64") == "1")
            {
                response.ContentType = "text/plain; charset=utf-8";
                response.Body = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(data));
            }
            else
            {
                response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                response.Body = data;
            }
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            return response;
        }
        catch (Exception ex)
        {
            return HttpResponse.Text(502, "代理请求失败：" + ex.Message);
        }
        finally
        {
            ProxySlots.Release();
        }
    }

    private static bool IsBlockedHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (IPAddress.TryParse(host, out IPAddress? address))
        {
            return IPAddress.IsLoopback(address)
                || address.IsIPv6LinkLocal
                || address.Equals(IPAddress.Any)
                || address.Equals(IPAddress.IPv6Any);
        }
        return false;
    }

    private HttpResponse Handle(HttpRequest request)
    {
        string method = request.Method.ToUpperInvariant();
        if (method != "GET" && method != "HEAD")
        {
            HttpResponse response = HttpResponse.Text(405, "Method Not Allowed");
            response.Headers["Allow"] = "GET, HEAD";
            return response;
        }

        DrawioAssetPack? pack = EnsurePack();
        if (pack == null)
        {
            return HttpResponse.Text(500, "drawio 资源加载失败：" + (_loadError?.Message ?? "未知错误"));
        }

        string path = NormalizePath(request.Path);
        if (path.Length == 0)
        {
            path = "index.html";
        }
        if (!pack.TryGet(path, out DrawioAssetPack.Entry entry))
        {
            return HttpResponse.Text(404, "Not Found");
        }

        // PreConfig.js 在 app.min.js 之前执行：在原文件末尾追加简体中文与 16px 加粗默认值。
        if (path.Equals("js/PreConfig.js", StringComparison.OrdinalIgnoreCase))
        {
            string customTag = entry.ETag + "-" + LocalizedPreConfigVersion;
            string requestTag = request.Header("If-None-Match");
            if (requestTag.Length > 0 && requestTag.Contains(customTag, StringComparison.Ordinal))
            {
                var notModified = new HttpResponse { Status = 304 };
                notModified.Headers["ETag"] = "\"" + customTag + "\"";
                notModified.Headers["Cache-Control"] = "no-cache";
                return notModified;
            }
            string originalScript = Encoding.UTF8.GetString(pack.Decompress(entry));
            var custom = new HttpResponse
            {
                Status = 200,
                ContentType = "application/javascript; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(originalScript + "\n" + DefaultsScript)
            };
            custom.Headers["ETag"] = "\"" + customTag + "\"";
            custom.Headers["Cache-Control"] = "no-cache";
            custom.Headers["X-Content-Type-Options"] = "nosniff";
            return custom;
        }

        string etag = "\"" + entry.ETag + "\"";
        string ifNoneMatch = request.Header("If-None-Match");
        if (ifNoneMatch.Length > 0 && ifNoneMatch.Contains(entry.ETag, StringComparison.Ordinal))
        {
            var notModified = new HttpResponse { Status = 304 };
            notModified.Headers["ETag"] = etag;
            notModified.Headers["Cache-Control"] = "no-cache";
            return notModified;
        }

        bool acceptBrotli = AcceptsBrotli(request.Header("Accept-Encoding"));
        var result = new HttpResponse
        {
            Status = 200,
            ContentType = entry.ContentType
        };
        if (entry.Encoding == "br" && acceptBrotli)
        {
            result.Body = pack.GetStored(entry);
            result.Headers["Content-Encoding"] = "br";
        }
        else
        {
            result.Body = pack.Decompress(entry);
        }
        result.Headers["ETag"] = etag;
        result.Headers["Cache-Control"] = "no-cache";
        result.Headers["Vary"] = "Accept-Encoding";
        result.Headers["X-Content-Type-Options"] = "nosniff";
        return result;
    }

    private static string NormalizePath(string path)
    {
        string name = (path ?? "").Replace('\\', '/').TrimStart('/');
        if (name.Contains('\0') || name.Split('/').Contains(".."))
        {
            return "";
        }
        return name;
    }

    private static bool AcceptsBrotli(string acceptEncoding)
    {
        if (acceptEncoding.Length == 0)
        {
            return false;
        }
        foreach (string token in acceptEncoding.Split(','))
        {
            string value = token.Trim();
            int semi = value.IndexOf(';');
            if (semi >= 0)
            {
                value = value[..semi].Trim();
            }
            if (value.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
