using System.Globalization;

namespace MiniMonitor.Web;

/// <summary>
/// 内置本机服务的请求防护：
/// 1. Host 必须是 localhost / 127.0.0.1 / [::1] 且端口匹配——防止恶意网页通过 DNS 重绑定
///    （把 attacker.com 解析到 127.0.0.1）以同源身份读写本机 API；
/// 2. 非 GET/HEAD/OPTIONS 请求要求 Origin / Referer 与 Host 同源——防跨站读写。
/// </summary>
internal static class LocalHttpGuard
{
    public static (int Status, string Message)? Check(HttpRequest req, int port)
    {
        string host = (req.Header("host") ?? "").Trim().ToLowerInvariant();
        if (host.Length == 0)
        {
            return (400, "缺少 Host 请求头");
        }

        string bare;
        string portPart;
        if (host.StartsWith('['))
        {
            int end = host.IndexOf(']');
            if (end < 0)
            {
                return (400, $"Host 请求头不合法：{host}");
            }
            bare = host[1..end];
            string rest = host[(end + 1)..];
            portPart = rest.StartsWith(':') ? rest[1..] : "";
        }
        else
        {
            int colon = host.LastIndexOf(':');
            if (colon >= 0)
            {
                bare = host[..colon];
                portPart = host[(colon + 1)..];
            }
            else
            {
                bare = host;
                portPart = "";
            }
        }

        if (portPart.Length > 0 && portPart != port.ToString(CultureInfo.InvariantCulture))
        {
            return (400, $"Host 请求头与本机端口不匹配：{host}");
        }
        if (bare != "localhost" && bare != "127.0.0.1" && bare != "::1")
        {
            return (400, $"仅允许本机（localhost / 127.0.0.1）访问：{host}");
        }

        string method = req.Method.ToUpperInvariant();
        if (method is "GET" or "HEAD" or "OPTIONS")
        {
            return null;
        }
        string origin = req.Header("origin");
        if (string.IsNullOrWhiteSpace(origin))
        {
            origin = req.Header("referer");
        }
        origin = origin.Trim();
        if (origin.Length == 0)
        {
            return null;
        }
        string netloc = "";
        try
        {
            netloc = new Uri(origin).Authority.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
        if (netloc.Length > 0 && netloc != host)
        {
            return (403, $"跨源请求被拒绝：{origin}");
        }
        return null;
    }
}
