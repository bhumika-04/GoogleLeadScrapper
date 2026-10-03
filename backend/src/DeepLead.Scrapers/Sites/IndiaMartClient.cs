using System.Net;
using System.Text.RegularExpressions;
using DeepLead.Core;

namespace DeepLead.Scrapers.Sites;

/// <summary>
/// Paced HTTP access to IndiaMART seller pages. IndiaMART answers 429 after a short burst, and its own search
/// returns nothing to automated browsers, so sellers are found by guessing the profile slug from the company name
/// (indiamart.com/{slug}/ usually mirrors the name) and verifying name + city on the page.
/// </summary>
public sealed partial class IndiaMartClient : IDisposable
{
    private const string Source = "IndiaMART";
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan RateLimitBackoff = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastCallUtc = DateTime.MinValue;
    private DateTime _blockedUntilUtc = DateTime.MinValue;

    public IndiaMartClient()
    {
        _http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-IN,en;q=0.9");
    }

    public bool IsCoolingDown => DateTime.UtcNow < _blockedUntilUtc;

    /// <summary>Profile page HTML, or null when the seller doesn't exist (404). Throws ScrapeBlockedException on 429.</summary>
    public async Task<(string Url, string Html)?> GetProfileAsync(string profileUrl, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsCoolingDown)
                throw new ScrapeBlockedException(Source, profileUrl, $"cooling down until {_blockedUntilUtc:HH:mm} UTC");

            var wait = _lastCallUtc + MinInterval + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 3000)) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            using var response = await _http.GetAsync(profileUrl, ct);
            _lastCallUtc = DateTime.UtcNow;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _blockedUntilUtc = DateTime.UtcNow + RateLimitBackoff;
                throw new ScrapeBlockedException(Source, profileUrl, "429 Too Many Requests");
            }
            if (!response.IsSuccessStatusCode)
                return null;

            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? profileUrl;
            return (finalUrl, await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    /// <summary>
    /// Likely slugs, most likely first: "Jai Ma Graphics" -> jai-ma-graphics, jaimagraphics, jai-ma-graphics-indore.
    /// Trailing "pvt ltd", city names and Maps keyword-stuffing ("… | offset printing | …") are dropped first.
    /// </summary>
    public static IReadOnlyList<string> GuessProfileUrls(string companyName, string city, int max = 3)
    {
        var name = companyName.Split('|', '–', '—', ',')[0];
        name = Regex.Replace(name, @"\b(pvt\.?|private|ltd\.?|limited|llp)\b", " ", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, $@"\b{Regex.Escape(city)}\b", " ", RegexOptions.IgnoreCase).Replace("&", " and ");

        var hyphen = NonSlug().Replace(name.ToLowerInvariant(), "-").Trim('-');
        var citySlug = NonSlug().Replace(city.ToLowerInvariant(), "-").Trim('-');
        if (hyphen.Length < 3)
            return [];

        var slugs = new List<string> { hyphen };
        if (hyphen.Count(c => c == '-') is > 0 and <= 3)
            slugs.Add(hyphen.Replace("-", ""));
        slugs.Add($"{hyphen}-{citySlug}");

        return slugs.Distinct().Take(max).Select(s => $"https://www.indiamart.com/{s}/profile.html").ToList();
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
