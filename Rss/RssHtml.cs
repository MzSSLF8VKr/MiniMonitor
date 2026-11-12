using System.Net;
using System.Text;

namespace MiniMonitor.Rss;

/// <summary>生成内置文章视图的 HTML（Fluent 排版、深浅色、字号可调、滚动到底通知）。</summary>
public static class RssHtml
{
    public static string BuildArticle(RssArticle article, RssTheme theme, int fontSize,
        bool markReadOnScroll)
    {
        string body = article.FullContent ?? article.Content ?? "";
        if (string.IsNullOrWhiteSpace(body))
        {
            body = "<p>" + WebUtility.HtmlEncode(article.Snippet ?? "（无正文）") + "</p>";
        }
        body = RssParser.SanitizeHtml(body);
        string accent = Hex(theme.Accent);
        string background = Hex(theme.Surface);
        string text = Hex(theme.Text);
        string secondary = Hex(theme.TextSecondary);
        string border = Hex(theme.Border);
        string codeBackground = theme.IsDark ? "#333333" : "#F4F4F4";
        string quoteBackground = theme.IsDark ? "#2E2E2E" : "#F8F8F8";
        string baseHref = "";
        if (!string.IsNullOrWhiteSpace(article.Link))
        {
            try
            {
                var uri = new Uri(article.Link);
                baseHref = $"<base href=\"{WebUtility.HtmlEncode(uri.GetLeftPart(UriPartial.Authority))}/\">";
            }
            catch
            {
                baseHref = "";
            }
        }
        string scrollBody = markReadOnScroll
            ? """
              (function () {
                var sent = false;
                function check() {
                  if (sent) return;
                  var doc = document.documentElement;
                  if (window.innerHeight + window.scrollY >= doc.scrollHeight - 120) {
                    sent = true;
                    if (window.chrome && window.chrome.webview) {
                      window.chrome.webview.postMessage('rss-bottom');
                    }
                  }
                }
                window.addEventListener('scroll', check, { passive: true });
                window.addEventListener('load', check);
              })();
              """
            : "";
        string scrollScript = """
            <script>
            document.addEventListener('keydown', function (e) {
              if (e.key === 'Escape' && window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage('rss-escape');
              }
            });
            </script>
            <script>
            """
            + scrollBody
            + "\n</script>";
        string title = WebUtility.HtmlEncode(article.DisplayTitle);
        string source = WebUtility.HtmlEncode(article.SourceTitle);
        string date = article.PublishedTime.ToString("yyyy-MM-dd HH:mm");
        string author = string.IsNullOrWhiteSpace(article.Author)
            ? ""
            : "<span class=\"dot\">·</span><span>" + WebUtility.HtmlEncode(article.Author) + "</span>";
        string link = string.IsNullOrWhiteSpace(article.Link)
            ? ""
            : "<a class=\"original\" href=\"" + WebUtility.HtmlEncode(article.Link)
              + "\">查看原文 ↗</a>";

        return $$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src http: https: data:; media-src http: https: data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; font-src data:;">
            {{baseHref}}
            <style>
            :root { color-scheme: {{(theme.IsDark ? "dark" : "light")}}; }
            * { box-sizing: border-box; }
            html, body { margin: 0; padding: 0; background: {{background}}; color: {{text}}; }
            body {
                font-family: "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", sans-serif;
                font-size: {{fontSize}}px;
                line-height: 1.78;
                -webkit-font-smoothing: antialiased;
                overflow-wrap: break-word;
            }
            .page { max-width: 760px; margin: 0 auto; padding: 44px 44px 96px; }
            .eyebrow {
                display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
                color: {{secondary}}; font-size: 12.5px; letter-spacing: .2px; margin-bottom: 18px;
            }
            .source {
                color: {{accent}}; font-weight: 600;
                background: {{Hex(theme.AccentSoft)}};
                border-radius: 20px; padding: 3px 12px;
            }
            .dot { color: {{secondary}}; opacity: .7; }
            h1.title {
                font-family: "Segoe UI Variable Display", "Microsoft YaHei UI", sans-serif;
                font-size: {{Math.Max(24, fontSize + 12)}}px;
                line-height: 1.35; margin: 0 0 18px; font-weight: 700; letter-spacing: -.4px;
            }
            .meta { color: {{secondary}}; font-size: 13px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
            .divider { height: 1px; background: {{border}}; margin: 26px 0 30px; }
            .content { font-size: {{fontSize}}px; }
            .content p { margin: 0 0 1.08em; }
            .content img { max-width: 100%; height: auto; border-radius: 8px; display: block; margin: 1.2em auto; }
            .content a { color: {{accent}}; text-decoration: none; border-bottom: 1px solid {{Hex(Color.FromArgb(90, theme.Accent))}}; }
            .content a:hover { border-bottom-color: {{accent}}; }
            .content h1, .content h2, .content h3, .content h4 { line-height: 1.4; margin: 1.5em 0 .6em; font-weight: 650; }
            .content blockquote {
                margin: 1.2em 0; padding: 12px 18px; color: {{secondary}};
                background: {{quoteBackground}}; border-left: 3px solid {{accent}}; border-radius: 4px;
            }
            .content pre {
                background: {{codeBackground}}; padding: 16px 18px; border-radius: 8px;
                overflow-x: auto; font-size: 13px; line-height: 1.55;
                font-family: "Cascadia Code", Consolas, "Courier New", monospace;
            }
            .content code { font-family: "Cascadia Code", Consolas, monospace; font-size: .92em; }
            .content pre code { font-size: 13px; }
            .content ul, .content ol { padding-left: 1.5em; margin: 0 0 1em; }
            .content li { margin: .35em 0; }
            .content table { width: 100%; border-collapse: collapse; margin: 1.2em 0; font-size: .94em; }
            .content th, .content td { border: 1px solid {{border}}; padding: 8px 10px; text-align: left; }
            .content th { background: {{codeBackground}}; }
            .content hr { border: none; border-top: 1px solid {{border}}; margin: 2em 0; }
            .content figure { margin: 1.4em 0; }
            .content figcaption { color: {{secondary}}; font-size: 12.5px; text-align: center; margin-top: 8px; }
            .footer { margin-top: 48px; padding-top: 18px; border-top: 1px solid {{border}}; color: {{secondary}}; font-size: 12.5px; }
            .original { color: {{accent}}; text-decoration: none; }
            @media (max-width: 620px) { .page { padding: 28px 22px 70px; } }
            </style>
            </head>
            <body>
            <div class="page">
              <div class="eyebrow">
                <span class="source">{{source}}</span>
                <span>{{date}}</span>
                {{author}}
              </div>
              <h1 class="title">{{title}}</h1>
              <div class="meta"><span>{{date}}</span>{{(link.Length > 0 ? "<span class=\"dot\">·</span>" + link : "")}}</div>
              <div class="divider"></div>
              <div class="content">{{body}}</div>
              <div class="footer">由 FluentRSS 阅读器（MiniMonitor）渲染 · 支持抓取全文与原文网页</div>
            </div>
            {{scrollScript}}
            </body>
            </html>
            """;
    }

    public static string BuildMessage(string title, string message, RssTheme theme)
    {
        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8">
            <style>
            html,body{height:100%;margin:0;background:{{Hex(theme.Surface)}};color:{{Hex(theme.Text)}};
            font-family:"Microsoft YaHei UI",sans-serif;}
            .wrap{height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;padding:40px;text-align:center;}
            h2{font-size:20px;margin:0;font-weight:600;}
            p{color:{{Hex(theme.TextSecondary)}};font-size:13.5px;line-height:1.7;margin:0;max-width:520px;}
            .glyph{font-size:46px;color:{{Hex(theme.Accent)}};}
            </style></head><body><div class="wrap">
            <div class="glyph">▤</div><h2>{{WebUtility.HtmlEncode(title)}}</h2>
            <p>{{WebUtility.HtmlEncode(message)}}</p></div></body></html>
            """;
    }

    private static string Hex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
