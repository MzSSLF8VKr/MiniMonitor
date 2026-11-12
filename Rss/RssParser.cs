using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MiniMonitor.Rss;

/// <summary>
/// RSS 2.0 / Atom / RDF / JSON Feed 解析、OPML 导入导出、订阅源自动发现、
/// 正文安全清洗与“抓取全文”（Readability 风格）实现。全部使用 .NET 原生 API。
/// </summary>
public static class RssParser
{
    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style|noscript|template)\b[^>]*>[\s\S]*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DangerousBlockRegex = new(
        @"<(iframe|object|embed|form|input|button|select|textarea|link|meta|base|svg)\b[^>]*>[\s\S]*?</\1\s*>"
        + @"|<(iframe|object|embed|form|input|button|select|textarea|link|meta|base|svg)\b[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EventAttributeRegex = new(
        @"\s+on[a-z]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JavascriptUrlRegex = new(
        @"(href|src)\s*=\s*(['""])\s*javascript:[^'""]*\2",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImgSrcRegex = new(
        @"<img\b[^>]*?\bsrc\s*=\s*(['""])(?<url>[^'""]+)\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AttributeUrlRegex = new(
        @"(?<attr>\b(?:src|href|poster))\s*=\s*(?<quote>['""])(?<url>[^'""]+)(?<endquote>\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex CandidateBlockRegex = new(
        @"<(article|main|section|div)\b(?<attrs>[^>]*)>[\s\S]*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li",
        "blockquote", "pre", "code", "figure", "figcaption", "img", "a",
        "strong", "b", "em", "i", "u", "s", "del", "ins", "hr", "table",
        "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "span", "div",
        "video", "audio", "source", "sup", "sub", "small", "mark"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt", "title", "width", "height", "colspan", "rowspan", "poster", "controls", "type"
    };

    // ================= 信息流解析 =================

    public static ParsedFeed Parse(string text, string feedUrl)
    {
        string trimmed = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (trimmed.StartsWith('{'))
        {
            return ParseJsonFeed(trimmed, feedUrl);
        }
        var doc = XDocument.Parse(text, LoadOptions.None);
        string rootName = doc.Root?.Name.LocalName.ToLowerInvariant() ?? "";
        return rootName switch
        {
            "feed" => ParseAtom(doc, feedUrl),
            "rdf" => ParseRss(doc, feedUrl, rdf: true),
            _ => ParseRss(doc, feedUrl, rdf: false)
        };
    }

    private static IEnumerable<XElement> Children(XElement parent, string localName)
    {
        return parent.Elements().Where(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement? Child(XElement parent, string localName)
    {
        return parent.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ChildValue(XElement parent, string localName)
    {
        var element = Child(parent, localName);
        return element == null ? null : element.Value.Trim();
    }

    private static ParsedFeed ParseRss(XDocument doc, string feedUrl, bool rdf)
    {
        var root = doc.Root!;
        XElement? channel = rdf
            ? Child(root, "channel")
            : Child(root, "channel");
        if (channel == null && rdf)
        {
            channel = root;
        }

        var feed = new ParsedFeed
        {
            Title = ChildValue(channel ?? root, "title") ?? "",
            Link = ChildValue(channel ?? root, "link"),
            Description = ChildValue(channel ?? root, "description")
        };

        var image = channel == null ? null : Child(channel, "image");
        if (image != null)
        {
            feed.IconUrl = ChildValue(image, "url");
        }
        if (string.IsNullOrWhiteSpace(feed.IconUrl) && channel != null)
        {
            var itunesImage = channel.Elements().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "image", StringComparison.OrdinalIgnoreCase)
                && e.Attribute("href") != null);
            feed.IconUrl = itunesImage?.Attribute("href")?.Value;
        }

        IEnumerable<XElement> items = rdf
            ? root.Elements().Where(e => string.Equals(e.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase))
            : channel == null
                ? Enumerable.Empty<XElement>()
                : Children(channel, "item");

        var baseUri = TryUri(feed.Link) ?? TryUri(feedUrl);
        foreach (var item in items)
        {
            var link = ChildValue(item, "link");
            if (string.IsNullOrWhiteSpace(link))
            {
                link = item.Elements().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase)
                    && e.Attribute("href") != null)?.Attribute("href")?.Value;
            }

            string? content = ChildValue(item, "encoded");
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ChildValue(item, "description");
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ChildValue(item, "summary");
            }

            string? author = ChildValue(item, "creator") ?? ChildValue(item, "author");
            string? guid = ChildValue(item, "guid");
            if (string.IsNullOrWhiteSpace(guid))
            {
                guid = link;
            }

            long published = ParseDate(ChildValue(item, "pubDate"))
                ?? ParseDate(ChildValue(item, "published"))
                ?? ParseDate(ChildValue(item, "updated"))
                ?? ParseDate(ChildValue(item, "date"))
                ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var article = new ParsedArticle
            {
                Title = ChildValue(item, "title") ?? "",
                Link = Absolutize(link, baseUri),
                Author = author,
                Content = MakeAbsoluteHtml(SanitizeHtml(content), baseUri),
                Published = published
            };
            article.Thumb = FindThumb(item, article.Content, baseUri);
            article.Snippet = MakeSnippet(article.Content);
            article.Guid = BuildGuid(guid, article);
            feed.Articles.Add(article);
        }
        return feed;
    }

    private static ParsedFeed ParseAtom(XDocument doc, string feedUrl)
    {
        var root = doc.Root!;
        var feed = new ParsedFeed
        {
            Title = ChildValue(root, "title") ?? "",
            Description = ChildValue(root, "subtitle"),
            Link = FindAtomLink(root),
            IconUrl = ChildValue(root, "icon") ?? ChildValue(root, "logo")
        };
        var baseUri = TryUri(feed.Link) ?? TryUri(feedUrl);
        foreach (var entry in Children(root, "entry"))
        {
            string? content = ChildValue(entry, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                content = ChildValue(entry, "summary");
            }
            var authorElement = Child(entry, "author");
            string? author = authorElement == null ? null : ChildValue(authorElement, "name");
            long published = ParseDate(ChildValue(entry, "published"))
                ?? ParseDate(ChildValue(entry, "updated"))
                ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var article = new ParsedArticle
            {
                Title = ChildValue(entry, "title") ?? "",
                Link = Absolutize(FindAtomLink(entry), baseUri),
                Author = author,
                Content = MakeAbsoluteHtml(SanitizeHtml(content), baseUri),
                Published = published,
                Guid = ChildValue(entry, "id") ?? ""
            };
            article.Thumb = FindThumb(entry, article.Content, baseUri);
            article.Snippet = MakeSnippet(article.Content);
            article.Guid = BuildGuid(article.Guid, article);
            feed.Articles.Add(article);
        }
        return feed;
    }

    private static string? FindAtomLink(XElement element)
    {
        var links = Children(element, "link").ToList();
        var alternate = links.FirstOrDefault(l =>
            string.Equals(l.Attribute("rel")?.Value, "alternate", StringComparison.OrdinalIgnoreCase));
        var chosen = alternate ?? links.FirstOrDefault(l => l.Attribute("href") != null);
        return chosen?.Attribute("href")?.Value;
    }

    private static ParsedFeed ParseJsonFeed(string text, string feedUrl)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var feed = new ParsedFeed
        {
            Title = GetString(root, "title") ?? "",
            Link = GetString(root, "home_page_url"),
            Description = GetString(root, "description"),
            IconUrl = GetString(root, "icon") ?? GetString(root, "favicon")
        };
        var baseUri = TryUri(feed.Link) ?? TryUri(feedUrl);
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return feed;
        }
        foreach (var item in items.EnumerateArray())
        {
            string? content = GetString(item, "content_html");
            if (string.IsNullOrWhiteSpace(content))
            {
                string? plain = GetString(item, "content_text");
                content = string.IsNullOrWhiteSpace(plain)
                    ? null
                    : "<p>" + WebUtility.HtmlEncode(plain).Replace("\n", "<br>") + "</p>";
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                content = GetString(item, "summary");
            }
            string? author = GetString(item, "author");
            if (string.IsNullOrWhiteSpace(author) && item.TryGetProperty("authors", out var authors)
                && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0)
            {
                author = GetString(authors[0], "name");
            }
            long published = ParseDate(GetString(item, "date_published"))
                ?? ParseDate(GetString(item, "date_modified"))
                ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string? link = GetString(item, "url") ?? GetString(item, "external_url");
            var article = new ParsedArticle
            {
                Title = GetString(item, "title") ?? "",
                Link = Absolutize(link, baseUri),
                Author = author,
                Content = MakeAbsoluteHtml(SanitizeHtml(content), baseUri),
                Published = published,
                Guid = GetString(item, "id") ?? ""
            };
            article.Thumb = GetString(item, "image") ?? GetString(item, "banner_image")
                ?? FindThumb(null, article.Content, baseUri);
            article.Thumb = Absolutize(article.Thumb, baseUri);
            article.Snippet = MakeSnippet(article.Content);
            article.Guid = BuildGuid(article.Guid, article);
            feed.Articles.Add(article);
        }
        return feed;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? FindThumb(XElement? item, string? content, Uri? baseUri)
    {
        if (item != null)
        {
            foreach (var element in item.Descendants())
            {
                string local = element.Name.LocalName.ToLowerInvariant();
                if (local is "thumbnail" or "content" or "enclosure")
                {
                    string? url = element.Attribute("url")?.Value;
                    string? medium = element.Attribute("medium")?.Value;
                    string? type = element.Attribute("type")?.Value;
                    if (!string.IsNullOrWhiteSpace(url)
                        && (local == "thumbnail"
                            || string.Equals(medium, "image", StringComparison.OrdinalIgnoreCase)
                            || (type?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false)))
                    {
                        return Absolutize(url, baseUri);
                    }
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(content))
        {
            var match = ImgSrcRegex.Match(content);
            if (match.Success)
            {
                return Absolutize(match.Groups["url"].Value, baseUri);
            }
        }
        return null;
    }

    private static string BuildGuid(string? guid, ParsedArticle article)
    {
        string value = !string.IsNullOrWhiteSpace(guid)
            ? guid.Trim()
            : !string.IsNullOrWhiteSpace(article.Link)
                ? article.Link!
                : article.Title + "|" + article.Published;
        if (value.Length <= 480)
        {
            return value;
        }
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static long? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUnixTimeSeconds();
        }
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local)).ToUnixTimeSeconds();
        }
        return null;
    }

    private static Uri? TryUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }

    public static string? Absolutize(string? url, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return baseUri == null ? "https:" + url : baseUri.Scheme + ":" + url;
        }
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        // 必须先判断 http(s)，否则 "/post/1" 会被 Uri 解析成 file:///post/1
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
                ? absolute.ToString()
                : url;
        }
        if (baseUri != null && Uri.TryCreate(baseUri, url, out var combined)
            && (combined.Scheme == Uri.UriSchemeHttp || combined.Scheme == Uri.UriSchemeHttps))
        {
            return combined.ToString();
        }
        // 其它绝对协议（mailto:、javascript: 等）不参与图片/链接重写
        return url.Contains(':') && !url.StartsWith("#", StringComparison.Ordinal) ? null : url;
    }

    // ================= 清洗 / 摘要 / 绝对链接 =================

    public static string SanitizeHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        string result = html;
        result = ScriptStyleRegex.Replace(result, "");
        result = DangerousBlockRegex.Replace(result, "");
        result = EventAttributeRegex.Replace(result, "");
        result = JavascriptUrlRegex.Replace(result, "$1=\"#\"");
        result = result.Replace("<![CDATA[", "").Replace("]]>", "");
        return result.Trim();
    }

    public static string MakeAbsoluteHtml(string? html, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(html) || baseUri == null)
        {
            return html ?? "";
        }
        return AttributeUrlRegex.Replace(html, match =>
        {
            string url = match.Groups["url"].Value;
            string? absolute = Absolutize(url, baseUri);
            if (absolute == null)
            {
                return match.Value;
            }
            return $"{match.Groups["attr"].Value}={match.Groups["quote"].Value}{absolute}{match.Groups["quote"].Value}";
        });
    }

    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        string result = ScriptStyleRegex.Replace(html, " ");
        result = Regex.Replace(result, @"</?(p|div|br|li|tr|h[1-6]|blockquote|pre)\b[^>]*>",
            " ", RegexOptions.IgnoreCase);
        result = TagRegex.Replace(result, " ");
        result = WebUtility.HtmlDecode(result);
        return MultiWhitespaceRegex.Replace(result, " ").Trim();
    }

    private static string? MakeSnippet(string? html)
    {
        string text = HtmlToText(html);
        if (text.Length == 0)
        {
            return null;
        }
        return text.Length <= 280 ? text : text[..280].TrimEnd() + "…";
    }

    // ================= 订阅源自动发现 =================

    /// <summary>
    /// 如果用户输入的是网站首页，则从 &lt;link rel="alternate"&gt; 或常见路径中发现 RSS/Atom/JSON Feed。
    /// </summary>
    public static async Task<List<DiscoveredFeed>> DiscoverAsync(HttpClient http, string input,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DiscoveredFeed>();
        if (!TryNormalizeUrl(input, out string url))
        {
            return results;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept",
            "application/rss+xml,application/atom+xml,application/feed+json,text/xml,application/xml,text/html;q=0.8,*/*;q=0.5");
        string? body = null;
        string? finalUrl = url;
        string? contentType = null;
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            contentType = response.Content.Headers.ContentType?.MediaType;
            if (response.IsSuccessStatusCode)
            {
                body = await Http.ReadStringLimitedAsync(response, 4 * 1024 * 1024, cancellationToken);
            }
        }
        catch
        {
            body = null;
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            string trimmed = body!.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            bool looksFeed = trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<rss", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<feed", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<rdf", StringComparison.OrdinalIgnoreCase)
                || (trimmed.StartsWith('{') && trimmed.Contains("\"version\""));
            if (looksFeed
                || (contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) ?? false)
                || (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                string? title = null;
                try
                {
                    title = Parse(body!, finalUrl).Title;
                }
                catch
                {
                    // 内容不是有效信息流时继续走 HTML 发现流程
                }
                if (title != null)
                {
                    results.Add(new DiscoveredFeed { Url = finalUrl, Title = title, Type = contentType });
                    return results;
                }
            }

            // HTML <link rel="alternate">
            foreach (Match match in Regex.Matches(body!, @"<link\b[^>]*>",
                RegexOptions.IgnoreCase).Cast<Match>())
            {
                string tag = match.Value;
                string? rel = ExtractAttribute(tag, "rel");
                string? type = ExtractAttribute(tag, "type");
                string? href = ExtractAttribute(tag, "href");
                if (string.IsNullOrWhiteSpace(href)
                    || !string.Equals(rel?.ToLowerInvariant(), "alternate", StringComparison.Ordinal))
                {
                    continue;
                }
                if (type == null
                    || (!type.Contains("rss", StringComparison.OrdinalIgnoreCase)
                        && !type.Contains("atom", StringComparison.OrdinalIgnoreCase)
                        && !type.Contains("feed+json", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                string? absolute = Absolutize(href, TryUri(finalUrl));
                if (absolute != null && results.All(r => r.Url != absolute))
                {
                    results.Add(new DiscoveredFeed
                    {
                        Url = absolute,
                        Title = ExtractAttribute(tag, "title") ?? "",
                        Type = type
                    });
                }
            }
            if (results.Count > 0)
            {
                return results;
            }
        }

        // 常见路径回退
        if (Uri.TryCreate(finalUrl, UriKind.Absolute, out var site))
        {
            string origin = site.GetLeftPart(UriPartial.Authority);
            foreach (string suffix in new[] { "/feed", "/rss", "/atom.xml", "/feed.xml", "/index.xml", "/rss.xml" })
            {
                string candidate = origin + suffix;
                try
                {
                    using var probe = new HttpRequestMessage(HttpMethod.Get, candidate);
                    probe.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
                    using var response = await http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }
                    string? probeBody = await Http.ReadStringLimitedAsync(response, 512 * 1024, cancellationToken);
                    if (string.IsNullOrWhiteSpace(probeBody))
                    {
                        continue;
                    }
                    var parsed = Parse(probeBody!, candidate);
                    results.Add(new DiscoveredFeed
                    {
                        Url = candidate,
                        Title = parsed.Title,
                        Type = response.Content.Headers.ContentType?.MediaType
                    });
                    break;
                }
                catch
                {
                    // 忽略单个候选路径失败
                }
            }
        }

        if (results.Count == 0 && TryNormalizeUrl(input, out string normalized))
        {
            results.Add(new DiscoveredFeed { Url = normalized, Title = "" });
        }
        return results;
    }

    private static string? ExtractAttribute(string tag, string name)
    {
        var match = Regex.Match(tag, name + @"\s*=\s*(['""])(?<v>[^'""]*)\1",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value : null;
    }

    public static bool TryNormalizeUrl(string input, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        input = input.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
        {
            input = "https://" + input;
        }
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }
        url = uri.ToString();
        return true;
    }

    public static string? GuessFaviconUrl(RssFeed feed)
    {
        if (!string.IsNullOrWhiteSpace(feed.IconUrl))
        {
            return Absolutize(feed.IconUrl, TryUri(feed.Link) ?? TryUri(feed.Url));
        }
        var baseUri = TryUri(feed.Link) ?? TryUri(feed.Url);
        if (baseUri == null)
        {
            return null;
        }
        return baseUri.GetLeftPart(UriPartial.Authority) + "/favicon.ico";
    }

    // ================= OPML =================

    public sealed class OpmlEntry
    {
        public string? GroupName { get; set; }
        public RssFeed Feed { get; set; } = new();
    }

    public static List<OpmlEntry> ImportOpml(string xml)
    {
        var document = XDocument.Parse(xml);
        var list = new List<OpmlEntry>();
        var body = document.Descendants().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "body", StringComparison.OrdinalIgnoreCase));
        if (body != null)
        {
            WalkOutlines(body, null, list, 0);
        }
        return list;
    }

    private static void WalkOutlines(XElement parent, string? group, List<OpmlEntry> list, int depth)
    {
        if (depth > 8)
        {
            return;
        }
        foreach (var outline in parent.Elements().Where(e =>
                     string.Equals(e.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase)))
        {
            string? xmlUrl = outline.Attribute("xmlUrl")?.Value
                ?? outline.Attribute("xmlurl")?.Value;
            string text = outline.Attribute("text")?.Value
                ?? outline.Attribute("title")?.Value
                ?? outline.Attribute("xmlUrl")?.Value
                ?? "";
            if (!string.IsNullOrWhiteSpace(xmlUrl))
            {
                if (!TryNormalizeUrl(xmlUrl, out string normalized))
                {
                    continue;
                }
                list.Add(new OpmlEntry
                {
                    GroupName = group,
                    Feed = new RssFeed
                    {
                        Title = string.IsNullOrWhiteSpace(text) ? normalized : text,
                        Url = normalized,
                        Link = outline.Attribute("htmlUrl")?.Value
                    }
                });
            }
            else
            {
                WalkOutlines(outline, string.IsNullOrWhiteSpace(text) ? group : text, list, depth + 1);
            }
        }
    }

    public static string BuildOpml(IEnumerable<RssGroup> groups, IEnumerable<RssFeed> feeds)
    {
        var body = new XElement("body");
        var feedList = feeds.ToList();
        foreach (var group in groups)
        {
            var groupElement = new XElement("outline", new XAttribute("text", group.Name),
                new XAttribute("title", group.Name));
            foreach (var feed in feedList.Where(f => f.Gid == group.Gid))
            {
                groupElement.Add(BuildOutline(feed));
            }
            if (groupElement.HasElements)
            {
                body.Add(groupElement);
            }
        }
        foreach (var feed in feedList.Where(f => f.Gid == null || groups.All(g => g.Gid != f.Gid)))
        {
            body.Add(BuildOutline(feed));
        }
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("opml", new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", "FluentRSS 订阅源"),
                    new XElement("dateCreated", DateTimeOffset.Now.ToString("r"))),
                body));
        return document.Declaration + Environment.NewLine + document.ToString();
    }

    private static XElement BuildOutline(RssFeed feed)
    {
        var element = new XElement("outline",
            new XAttribute("type", "rss"),
            new XAttribute("text", feed.Title),
            new XAttribute("title", feed.Title),
            new XAttribute("xmlUrl", feed.Url));
        if (!string.IsNullOrWhiteSpace(feed.Link))
        {
            element.Add(new XAttribute("htmlUrl", feed.Link));
        }
        return element;
    }

    // ================= 抓取全文 =================

    /// <summary>
    /// Readability 风格的正文提取：按容器语义、文本长度和链接密度挑选主内容块，
    /// 移除导航、评论、广告等噪音后返回干净 HTML。失败时返回 null。
    /// </summary>
    public static string? ExtractReadableArticle(string html, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }
        string cleaned = ScriptStyleRegex.Replace(html, " ");
        cleaned = Regex.Replace(cleaned,
            @"<(header|footer|nav|aside)\b[^>]*>[\s\S]*?</\1\s*>", " ",
            RegexOptions.IgnoreCase);
        cleaned = DangerousBlockRegex.Replace(cleaned, " ");

        string? best = null;
        int bestScore = 0;
        foreach (Match match in CandidateBlockRegex.Matches(cleaned))
        {
            string block = match.Value;
            string attrs = match.Groups["attrs"].Value;
            string content = block;
            string text = HtmlToText(content);
            if (text.Length < 80)
            {
                continue;
            }
            int score = text.Length;
            string lower = attrs.ToLowerInvariant();
            if (lower.Contains("article") || lower.Contains("content") || lower.Contains("post")
                || lower.Contains("entry") || lower.Contains("main") || lower.Contains("story"))
            {
                score += 800;
            }
            if (lower.Contains("comment") || lower.Contains("sidebar") || lower.Contains("footer")
                || lower.Contains("nav") || lower.Contains("advert") || lower.Contains("related"))
            {
                score -= 1200;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = block;
            }
        }

        string selected = best ?? cleaned;
        var bodyMatch = Regex.Match(selected, @"<body\b[^>]*>([\s\S]*?)</body\s*>", RegexOptions.IgnoreCase);
        if (bodyMatch.Success)
        {
            selected = bodyMatch.Groups[1].Value;
        }

        string sanitized = SanitizeHtml(selected);
        sanitized = KeepOnlyAllowedTags(sanitized);
        sanitized = MakeAbsoluteHtml(sanitized, baseUri);
        string plain = HtmlToText(sanitized);
        if (plain.Length < 80)
        {
            return null;
        }
        if (plain.Length > 200000)
        {
            sanitized = sanitized[..200000];
        }
        return sanitized.Trim();
    }

    private static string KeepOnlyAllowedTags(string html)
    {
        // 逐标签处理：白名单外的标签直接去掉，白名单内标签仅保留安全属性。
        return Regex.Replace(html, @"<[^>]+>", match =>
        {
            string tag = match.Value;
            if (tag.StartsWith("<!", StringComparison.Ordinal) || tag.StartsWith("<?", StringComparison.Ordinal))
            {
                return "";
            }
            var nameMatch = Regex.Match(tag, @"^</?\s*(?<name>[a-zA-Z0-9]+)");
            if (!nameMatch.Success)
            {
                return "";
            }
            string name = nameMatch.Groups["name"].Value.ToLowerInvariant();
            if (!AllowedTags.Contains(name))
            {
                return "";
            }
            if (tag.StartsWith("</", StringComparison.Ordinal))
            {
                return $"</{name}>";
            }
            if (name == "br")
            {
                return "<br>";
            }
            if (name == "hr")
            {
                return "<hr>";
            }
            if (name == "img")
            {
                string? src = ExtractAttribute(tag, "src");
                string? alt = ExtractAttribute(tag, "alt");
                return src == null ? "" : $"<img src=\"{WebUtility.HtmlEncode(src)}\" alt=\"{WebUtility.HtmlEncode(alt ?? "")}\">";
            }
            if (name == "a")
            {
                string? href = ExtractAttribute(tag, "href");
                return href == null
                    ? "<a>"
                    : $"<a href=\"{WebUtility.HtmlEncode(href)}\" target=\"_blank\" rel=\"noreferrer noopener\">";
            }
            if (name is "video" or "audio" or "source")
            {
                string? src = ExtractAttribute(tag, "src");
                return src == null ? $"<{name}>" : $"<{name} src=\"{WebUtility.HtmlEncode(src)}\">";
            }
            var preserved = new StringBuilder();
            preserved.Append('<').Append(name);
            foreach (string attribute in new[] { "colspan", "rowspan", "width", "height" })
            {
                string? value = ExtractAttribute(tag, attribute);
                if (!string.IsNullOrWhiteSpace(value) && value.Length < 12
                    && Regex.IsMatch(value, @"^[0-9%]+$"))
                {
                    preserved.Append(' ').Append(attribute).Append("=\"").Append(value).Append('"');
                }
            }
            preserved.Append('>');
            return preserved.ToString();
        });
    }
}

/// <summary>HttpClient 共享配置与有上限的响应读取。</summary>
public static class Http
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/126.0 Safari/537.36 MiniMonitor-FluentRSS/1.40";

    public static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(35) };
    }

    public static async Task<string> ReadStringLimitedAsync(HttpResponseMessage response, int maxBytes,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                break;
            }
            buffer.Write(chunk, 0, read);
        }
        byte[] bytes = buffer.ToArray();
        return Decode(bytes, response.Content.Headers.ContentType?.CharSet);
    }

    public static string Decode(byte[] bytes, string? charset = null)
    {
        // BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        string encodingName = charset ?? "";
        if (string.IsNullOrWhiteSpace(encodingName))
        {
            string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 512));
            var match = Regex.Match(head, @"encoding\s*=\s*(['""])(?<enc>[a-zA-Z0-9_\-]+)\1",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                encodingName = match.Groups["enc"].Value;
            }
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(encodingName))
            {
                var encoding = Encoding.GetEncoding(encodingName);
                return encoding.GetString(bytes);
            }
        }
        catch
        {
            // 未知编码回退 UTF-8
        }
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
