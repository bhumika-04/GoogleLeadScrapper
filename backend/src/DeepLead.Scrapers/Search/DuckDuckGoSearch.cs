using System.Net;
using AngleSharp.Html.Parser;
using DeepLead.Core;

namespace DeepLead.Scrapers.Search;

public sealed record SearchResult(string Url, string Title, string Snippet);

public interface IWebSearch
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, string countryIso2, CancellationToken ct);
}

/// <summary>
/// DuckDuckGo's no-JavaScript HTML endpoint. Google CAPTCHAs this IP on the first query and Brave does too;
/// DDG works but rate-limits bursts, so calls are spaced out (one IP, no proxies).
/// </summary>
public sealed class DuckDuckGoSearch : IWebSearch, IDisposable
{
    private const string Source = "DuckDuckGo";
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan BlockedBackoff = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastCallUtc = DateTime.MinValue;
    private DateTime _blockedUntilUtc = DateTime.MinValue;

    /// <summary>Circuit breaker: while true, calls fail fast without touching DuckDuckGo (hammering prolongs a block).</summary>
    public bool IsCoolingDown => DateTime.UtcNow < _blockedUntilUtc;

    public DuckDuckGoSearch()
    {
        _http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-IN,en;q=0.9");
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, string countryIso2, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsCoolingDown)
                throw new ScrapeBlockedException(Source, null, $"cooling down until {_blockedUntilUtc:HH:mm} UTC");

            var wait = _lastCallUtc + MinInterval + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 6000)) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = query,
                ["kl"] = $"{countryIso2.ToLowerInvariant()}-en",
            });
            using var response = await _http.PostAsync("https://html.duckduckgo.com/html/", content, ct);
            _lastCallUtc = DateTime.UtcNow;
            var html = await response.Content.ReadAsStringAsync(ct);

            // 202 + "anomaly" page = rate limited.
            if (response.StatusCode == HttpStatusCode.Accepted || html.Contains("anomaly-modal", StringComparison.Ordinal))
            {
                _blockedUntilUtc = DateTime.UtcNow + BlockedBackoff;
                throw new ScrapeBlockedException(Source, null, "rate limited (anomaly page)");
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"DuckDuckGo returned {(int)response.StatusCode}");

            return Parse(html);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static IReadOnlyList<SearchResult> Parse(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var results = new List<SearchResult>();
        foreach (var block in doc.QuerySelectorAll("div.result"))
        {
            if (block.ClassList.Contains("result--ad"))
                continue;
            var link = block.QuerySelector("a.result__a");
            var href = link?.GetAttribute("href");
            if (link is null || string.IsNullOrWhiteSpace(href))
                continue;

            results.Add(new SearchResult(
                UnwrapRedirect(href),
                link.TextContent.Trim(),
                block.QuerySelector(".result__snippet")?.TextContent.Trim() ?? ""));
        }
        return results;
    }

    /// <summary>DDG links look like //duckduckgo.com/l/?uddg=https%3A%2F%2Fsite.com%2F&amp;rut=...</summary>
    private static string UnwrapRedirect(string href)
    {
        var marker = href.IndexOf("uddg=", StringComparison.Ordinal);
        if (marker < 0)
            return href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
        var value = href[(marker + 5)..];
        var amp = value.IndexOf('&');
        return Uri.UnescapeDataString(amp >= 0 ? value[..amp] : value);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
