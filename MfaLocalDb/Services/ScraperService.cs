using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MfaLocalDb.Models;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using HtmlNode = HtmlAgilityPack.HtmlNode;

namespace MfaLocalDb.Services;

public sealed class ScraperService
{
    private static readonly Regex RedirectRegex = new(
        "window\\.location\\.href\\s*=\\s*[\"'](?<url>[^\"']+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CollapseWhitespaceRegex = new(
        "[ \\t\\f\\v]+",
        RegexOptions.Compiled);

    private static readonly Regex CollapseNewlineRegex = new(
        "\\n{3,}",
        RegexOptions.Compiled);

    private static readonly IReadOnlyList<(string Region, string Url)> CountryRegionPages =
    [
        ("亚洲", "https://www.mfa.gov.cn/web/gjhdq_676201/gj_676203/yz_676205/"),
        ("非洲", "https://www.mfa.gov.cn/web/gjhdq_676201/gj_676203/fz_677316/"),
        ("欧洲", "https://www.mfa.gov.cn/web/gjhdq_676201/gj_676203/oz_678770/"),
        ("北美洲", "https://www.mfa.gov.cn/web/gjhdq_676201/gj_676203/bmz_679954/"),
        ("南美洲", "https://www.mfa.gov.cn/web/gjhdq_676201/gj_676203/nmz_680924/"),
        ("大洋洲", "https://www.mfa.gov.cn/web/gjhdq_676201/gj_676203/dyz_681240/"),
    ];

    private readonly HttpClient _httpClient;

    public ScraperService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
    }

    public async Task<ScrapeResult> ScrapeAllAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("正在读取国家列表...");
        var targets = new List<ScrapeTarget>();
        foreach (var (region, url) in CountryRegionPages)
        {
            targets.AddRange(await ReadCountryTargetsAsync(region, url, cancellationToken));
        }

        progress?.Report("正在读取国际组织列表...");
        targets.AddRange(await ReadOrganizationTargetsAsync(cancellationToken));

        var dedupedTargets = targets
            .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var entries = new ConcurrentBag<ScrapedEntry>();
        var failures = new ConcurrentBag<string>();
        var completed = 0;

        await Parallel.ForEachAsync(
            dedupedTargets,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 6,
            },
            async (target, token) =>
            {
                try
                {
                    var entry = await ReadEntryAsync(target, token);
                    entries.Add(entry);
                    var current = Interlocked.Increment(ref completed);
                    progress?.Report($"已同步 {current}/{dedupedTargets.Count}: {entry.Name}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{target.Name} | {target.Url} | {ex.Message}");
                    var current = Interlocked.Increment(ref completed);
                    progress?.Report($"跳过 {current}/{dedupedTargets.Count}: {target.Name}");
                }
            });

        return new ScrapeResult
        {
            Entries = entries
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Region, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToList(),
            Failures = failures.OrderBy(item => item, StringComparer.Ordinal).ToList(),
        };
    }

    private async Task<IReadOnlyList<ScrapeTarget>> ReadCountryTargetsAsync(string region, string regionUrl, CancellationToken cancellationToken)
    {
        var document = await LoadDocumentAsync(regionUrl, cancellationToken);
        var container = document.DocumentNode.SelectSingleNode("//div[contains(@class,'linkList2')]");
        if (container is null)
        {
            return [];
        }

        IEnumerable<HtmlNode> anchors = container.SelectNodes(".//a[@href]")?.Cast<HtmlNode>() ?? Array.Empty<HtmlNode>();
        var targets = new List<ScrapeTarget>();
        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            if (!href.StartsWith("./", StringComparison.Ordinal) || !href.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var name = CleanInlineText(anchor.InnerText);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            targets.Add(new ScrapeTarget
            {
                Kind = "国家",
                Region = region,
                Name = name,
                Url = CombineUrl(regionUrl, href),
            });
        }

        return targets;
    }

    private async Task<IReadOnlyList<ScrapeTarget>> ReadOrganizationTargetsAsync(CancellationToken cancellationToken)
    {
        const string orgUrl = "https://www.mfa.gov.cn/web/gjhdq_676201/gjhdqzz_681964/";
        var document = await LoadDocumentAsync(orgUrl, cancellationToken);
        var container = document.DocumentNode.SelectSingleNode("//div[contains(@class,'linkList2')]");
        if (container is null)
        {
            return [];
        }

        IEnumerable<HtmlNode> anchors = container.SelectNodes(".//a[@href]")?.Cast<HtmlNode>() ?? Array.Empty<HtmlNode>();
        var targets = new List<ScrapeTarget>();
        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            if (!href.StartsWith("./", StringComparison.Ordinal) || !href.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var name = CleanInlineText(anchor.InnerText);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            targets.Add(new ScrapeTarget
            {
                Kind = "组织",
                Region = "国际和地区组织",
                Name = name,
                Url = CombineUrl(orgUrl, href),
            });
        }

        return targets;
    }

    private async Task<ScrapedEntry> ReadEntryAsync(ScrapeTarget target, CancellationToken cancellationToken)
    {
        var resolved = await ResolveContentPageAsync(target.Url, cancellationToken);
        var titleNode = resolved.Document.DocumentNode.SelectSingleNode("//div[contains(@class,'news-title')]//h1");
        var contentNode = resolved.Document.DocumentNode.SelectSingleNode("//div[contains(@class,'news-main')]");

        if (titleNode is null || contentNode is null)
        {
            throw new InvalidOperationException("未找到正文结构。");
        }

        var title = CleanInlineText(titleNode.InnerText);
        var contentHtml = contentNode.InnerHtml.Trim();
        var contentText = ExtractReadableText(contentNode);

        if (string.IsNullOrWhiteSpace(contentText))
        {
            throw new InvalidOperationException("正文为空。");
        }

        return new ScrapedEntry
        {
            Kind = target.Kind,
            Region = target.Region,
            Name = target.Name,
            Title = string.IsNullOrWhiteSpace(title) ? target.Name : title,
            SourceUrl = resolved.Url,
            ContentHtml = contentHtml,
            ContentText = contentText,
            SyncedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
    }

    private async Task<(HtmlDocument Document, string Url)> ResolveContentPageAsync(string url, CancellationToken cancellationToken)
    {
        var currentUrl = url;

        for (var depth = 0; depth < 6; depth++)
        {
            var html = await _httpClient.GetStringAsync(currentUrl, cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            if (document.DocumentNode.SelectSingleNode("//div[contains(@class,'news-main')]") is not null)
            {
                return (document, currentUrl);
            }

            var redirectUrl = TryGetRedirectUrl(document, currentUrl);
            if (!string.IsNullOrWhiteSpace(redirectUrl))
            {
                currentUrl = redirectUrl;
                continue;
            }

            var listContentUrl = TryGetListContentLink(document, currentUrl);
            if (!string.IsNullOrWhiteSpace(listContentUrl))
            {
                currentUrl = listContentUrl;
                continue;
            }

            var fallbackUrl = TryGetFallbackContentLink(document, currentUrl);
            if (!string.IsNullOrWhiteSpace(fallbackUrl) &&
                !string.Equals(fallbackUrl, currentUrl, StringComparison.OrdinalIgnoreCase))
            {
                currentUrl = fallbackUrl;
                continue;
            }

            throw new InvalidOperationException($"无法定位到内容页：{currentUrl}");
        }

        throw new InvalidOperationException("跳转层级过深。");
    }

    private async Task<HtmlDocument> LoadDocumentAsync(string url, CancellationToken cancellationToken)
    {
        var html = await _httpClient.GetStringAsync(url, cancellationToken);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document;
    }

    private static string? TryGetRedirectUrl(HtmlDocument document, string baseUrl)
    {
        IEnumerable<HtmlNode> scriptNodes = document.DocumentNode.SelectNodes("//script")?.Cast<HtmlNode>() ?? Array.Empty<HtmlNode>();
        foreach (var scriptNode in scriptNodes)
        {
            var match = RedirectRegex.Match(scriptNode.InnerText);
            if (match.Success)
            {
                var url = CombineUrl(baseUrl, match.Groups["url"].Value);
                if (IsMfaContentUrl(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    private static string? TryGetFallbackContentLink(HtmlDocument document, string baseUrl)
    {
        IEnumerable<HtmlNode> anchors = document.DocumentNode.SelectNodes("//a[@href]")?.Cast<HtmlNode>() ?? Array.Empty<HtmlNode>();
        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var text = CleanInlineText(anchor.InnerText);
            if (text.Contains("概况", StringComparison.Ordinal) ||
                text.Contains("简介", StringComparison.Ordinal) ||
                text.Contains("基本情况", StringComparison.Ordinal))
            {
                var url = CombineUrl(baseUrl, href);
                if (string.Equals(url, baseUrl, StringComparison.OrdinalIgnoreCase) ||
                    !IsMfaContentUrl(url))
                {
                    continue;
                }

                return url;
            }
        }

        anchors = document.DocumentNode.SelectNodes("//a[@href]")?.Cast<HtmlNode>() ?? Array.Empty<HtmlNode>();
        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            if (!href.StartsWith("./", StringComparison.Ordinal) ||
                !href.Contains(".shtml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = CombineUrl(baseUrl, href);
            if (IsMfaContentUrl(url))
            {
                return url;
            }
        }

        return null;
    }

    private static string? TryGetListContentLink(HtmlDocument document, string baseUrl)
    {
        var anchors = document.DocumentNode
            .SelectNodes("//div[contains(@class,'newsList')]//div[contains(@class,'newsBd')]//a[@href]")
            ?.Cast<HtmlNode>() ?? Array.Empty<HtmlNode>();

        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var url = CombineUrl(baseUrl, href);
            if (IsMfaContentUrl(url))
            {
                return url;
            }
        }

        return null;
    }

    private static string ExtractReadableText(HtmlNode contentNode)
    {
        var builder = new StringBuilder();
        WriteNodeText(contentNode, builder);
        var text = HtmlEntity.DeEntitize(builder.ToString()).Replace("\r", string.Empty);
        text = Regex.Replace(text, "[\u00A0\u3000]", " ");
        text = CollapseWhitespaceRegex.Replace(text, " ");
        text = Regex.Replace(text, " *\n *", "\n");
        text = CollapseNewlineRegex.Replace(text, "\n\n");
        return text.Trim();
    }

    private static void WriteNodeText(HtmlNode node, StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(((HtmlTextNode)node).Text);
            return;
        }

        if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            return;
        }

        var block = node.Name is "p" or "div" or "li" or "tr" or "table" or "section" or "h1" or "h2" or "h3" or "h4";
        foreach (var child in node.ChildNodes)
        {
            WriteNodeText(child, builder);
        }

        if (block)
        {
            builder.AppendLine();
            builder.AppendLine();
        }
    }

    private static string CleanInlineText(string input)
    {
        return HtmlEntity.DeEntitize(input)
            .Replace("\r", string.Empty)
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Trim();
    }

    private static string CombineUrl(string baseUrl, string relativeOrAbsoluteUrl)
    {
        relativeOrAbsoluteUrl = HtmlEntity.DeEntitize(relativeOrAbsoluteUrl).Trim();
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri) &&
            IsHttpUrl(absoluteUri.AbsoluteUri))
        {
            return absoluteUri.AbsoluteUri;
        }

        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        var combinedUri = new Uri(baseUri, relativeOrAbsoluteUrl);
        if (!IsHttpUrl(combinedUri.AbsoluteUri))
        {
            throw new InvalidOperationException($"不支持的链接协议：{combinedUri.Scheme}");
        }

        return combinedUri.AbsoluteUri;
    }

    private static bool IsHttpUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsMfaContentUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            (uri.Host.EndsWith("mfa.gov.cn", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith("fmprc.gov.cn", StringComparison.OrdinalIgnoreCase)) &&
            !uri.AbsolutePath.Contains("/irs-c-web/", StringComparison.OrdinalIgnoreCase);
    }
}
