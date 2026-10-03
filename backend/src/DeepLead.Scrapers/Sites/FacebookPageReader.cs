using System.Globalization;
using System.Text.RegularExpressions;
using DeepLead.Scrapers.Browser;
using Microsoft.Playwright;

namespace DeepLead.Scrapers.Sites;

public sealed record FacebookPage(
    string Url,
    string Name,
    int? Followers,
    string IntroText,
    IReadOnlyList<string> ExternalLinks);

public enum FacebookReadOutcome { Ok, NotFound, LoginWall }

/// <summary>
/// Reads a public Facebook business page (works without login for most pages): name, follower count, and the
/// "Intro" block, which often carries phone, email, website and other social links. Forces English UI text.
/// </summary>
public sealed partial class FacebookPageReader(BrowserSession session)
{
    private IPage? _page;

    public async Task<(FacebookReadOutcome Outcome, FacebookPage? Page)> ReadAsync(string url, CancellationToken ct)
    {
        _page ??= await session.Context.NewPageAsync();
        var target = WithEnglishLocale(url);
        await _page.GotoAsync(target, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
        await HumanDelay.DelayAsync(3_000, 5_000, ct);

        if (_page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase) || _page.Url.Contains("/checkpoint", StringComparison.OrdinalIgnoreCase))
            return (FacebookReadOutcome.LoginWall, null);

        var raw = await _page.EvaluateAsync<RawPage>(
            """
            () => {
              const og = document.querySelector("meta[property='og:title']");
              const links = Array.from(document.querySelectorAll("a[href]")).map(a => a.href)
                .map(h => { try { const u = new URL(h); return u.hostname === 'l.facebook.com' ? (u.searchParams.get('u') || '') : h; } catch { return ''; } })
                .filter(h => h && !/facebook\.com|fbcdn|fb\.com|fb\.me|messenger\.com/.test(h));
              return {
                title: og ? og.content : document.title,
                text: (document.body.innerText || '').slice(0, 8000),
                links: Array.from(new Set(links)).slice(0, 30)
              };
            }
            """);

        return Parse(_page.Url, raw.Title ?? "", raw.Text ?? "", raw.Links ?? []);
    }

    private sealed class RawPage
    {
        public string? Title { get; set; }
        public string? Text { get; set; }
        public string[]? Links { get; set; }
    }

    [GeneratedRegex(@"([\d][\d.,]*)\s*([KkMm])?\s+(?:followers|likes)", RegexOptions.IgnoreCase)]
    private static partial Regex Followers();

    internal static (FacebookReadOutcome, FacebookPage?) Parse(string url, string title, string text, IReadOnlyList<string> links)
    {
        if (text.Contains("This content isn't available", StringComparison.OrdinalIgnoreCase)
            || text.Contains("content isn't available right now", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Page not found", StringComparison.OrdinalIgnoreCase))
            return (FacebookReadOutcome.NotFound, null);

        var name = Regex.Replace(title, @"\s*\|\s*Facebook\s*$", "", RegexOptions.IgnoreCase).Trim();
        if (name.Length == 0 || name.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
            return (FacebookReadOutcome.NotFound, null);

        // The Intro block sits between "Intro" and the next section ("Photos"/"Posts"/"See all photos"); posts below it are noise.
        var intro = text;
        var start = text.IndexOf("Intro", StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = new[] { "See all photos", "\nPhotos", "\nPosts", "Privacy  · " }
                .Select(m => text.IndexOf(m, start + 5, StringComparison.Ordinal))
                .Where(i => i > start).DefaultIfEmpty(Math.Min(text.Length, start + 1500)).Min();
            intro = text[start..end];
        }

        return (FacebookReadOutcome.Ok, new FacebookPage(url, name, ParseFollowers(text), intro.Trim(), links));
    }

    internal static int? ParseFollowers(string text)
    {
        var m = Followers().Match(text);
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
            return null;
        var factor = m.Groups[2].Value.ToUpperInvariant() switch { "K" => 1_000m, "M" => 1_000_000m, _ => 1m };
        return (int)Math.Min(int.MaxValue, n * factor);
    }

    /// <summary>Likely page addresses: "Jai Ma Graphics", Indore -> jaimagraphics, jaimagraphicsindore.</summary>
    public static IReadOnlyList<string> GuessPageUrls(string companyName, string city)
    {
        var name = Regex.Replace(companyName.Split('|', '–', '—', ',')[0], @"\b(pvt\.?|private|ltd\.?|limited|llp)\b", " ", RegexOptions.IgnoreCase);
        var joined = Regex.Replace(name.ToLowerInvariant().Replace("&", "and"), @"[^a-z0-9]", "");
        var citySlug = Regex.Replace(city.ToLowerInvariant(), @"[^a-z0-9]", "");
        if (joined.Length < 5)
            return [];
        var guesses = new List<string> { joined };
        if (!joined.EndsWith(citySlug, StringComparison.Ordinal))
            guesses.Add(joined + citySlug);
        return guesses.Select(g => $"https://www.facebook.com/{g}").ToList();
    }

    private static string WithEnglishLocale(string url) =>
        url.Contains("locale=", StringComparison.Ordinal) ? url : url + (url.Contains('?') ? "&" : "?") + "locale=en_US";
}
