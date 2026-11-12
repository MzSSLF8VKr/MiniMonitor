using System.Text.RegularExpressions;

namespace MiniMonitor.Rss;

/// <summary>规则引擎：按作用范围与顺序依次执行，动作可叠加。</summary>
public static class RssRules
{
    public sealed class Outcome
    {
        public bool MarkRead { get; set; }
        public bool Star { get; set; }
        public bool Hide { get; set; }
        public bool Notify { get; set; }
        public bool Matched { get; set; }
    }

    public static bool TargetMatches(RssRule rule, RssFeed feed)
    {
        if (rule.Target == "all")
        {
            return true;
        }
        if (rule.Target.StartsWith("feed:", StringComparison.Ordinal)
            && int.TryParse(rule.Target["feed:".Length..], out int sid))
        {
            return feed.Sid == sid;
        }
        if (rule.Target.StartsWith("group:", StringComparison.Ordinal)
            && int.TryParse(rule.Target["group:".Length..], out int gid))
        {
            return feed.Gid == gid;
        }
        return false;
    }

    public static Outcome Apply(IReadOnlyList<RssRule> rules, RssFeed feed, ParsedArticle article,
        bool includeNotify = true)
    {
        var outcome = new Outcome();
        foreach (var rule in rules)
        {
            if (!TargetMatches(rule, feed) || string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }
            bool hit;
            try
            {
                var options = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                var regex = new Regex(rule.Pattern, options, TimeSpan.FromSeconds(2));
                string title = article.Title ?? "";
                string content = RssParser.HtmlToText(article.Content ?? "");
                hit = rule.Field switch
                {
                    "content" => regex.IsMatch(content),
                    "both" => regex.IsMatch(title) || regex.IsMatch(content),
                    _ => regex.IsMatch(title)
                };
            }
            catch
            {
                continue;
            }
            if (hit != rule.Match)
            {
                continue;
            }
            outcome.Matched = true;
            if (rule.MarkRead) outcome.MarkRead = true;
            if (rule.Star) outcome.Star = true;
            if (rule.Hide) outcome.Hide = true;
            if (rule.Notify && includeNotify) outcome.Notify = true;
        }
        return outcome;
    }

    /// <summary>设置页的规则测试：返回命中的规则与最终动作。</summary>
    public static (List<RssRule> Hits, Outcome Outcome) Test(IReadOnlyList<RssRule> rules, RssFeed feed,
        string title, string content)
    {
        var hits = new List<RssRule>();
        var article = new ParsedArticle { Title = title, Content = content };
        foreach (var rule in rules)
        {
            if (!TargetMatches(rule, feed) || string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }
            try
            {
                var options = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                var regex = new Regex(rule.Pattern, options, TimeSpan.FromSeconds(2));
                string plain = RssParser.HtmlToText(content);
                bool hit = rule.Field switch
                {
                    "content" => regex.IsMatch(plain),
                    "both" => regex.IsMatch(title) || regex.IsMatch(plain),
                    _ => regex.IsMatch(title)
                };
                if (hit == rule.Match)
                {
                    hits.Add(rule);
                }
            }
            catch
            {
                // 无效正则忽略
            }
        }
        var outcome = Apply(rules, feed, article);
        return (hits, outcome);
    }
}
