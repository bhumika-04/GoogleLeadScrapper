using System.Text.RegularExpressions;
using DeepLead.Scrapers.Browser;
using Microsoft.Playwright;

namespace DeepLead.Scrapers.Sites;

public sealed record LinkedInPerson(string FullName, string? Headline, string? Location, string ProfileUrl, string CardText);

/// <summary>The saved LinkedIn login is no longer valid (login wall / checkpoint).</summary>
public sealed class LinkedInSessionExpiredException(string url) : Exception($"LinkedIn session expired (redirected to {url})");

/// <summary>
/// People search inside LinkedIn using a connected account's session. One results page per company,
/// human-paced by the caller. Extraction is markup-agnostic: profile links + the text lines of their result card.
/// </summary>
public sealed partial class LinkedInPeopleSearch(BrowserSession session)
{
    private IPage? _page;

    public async Task<IReadOnlyList<LinkedInPerson>> SearchAsync(string keywords, CancellationToken ct)
    {
        _page ??= await session.Context.NewPageAsync();
        var url = $"https://www.linkedin.com/search/results/people/?keywords={Uri.EscapeDataString(keywords)}&origin=GLOBAL_SEARCH_HEADER";
        await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45_000 });
        ThrowIfLoggedOut(_page.Url);

        // Results render client-side; wait for profile links or the "no results" state, then a human-like pause.
        try
        {
            await _page.WaitForSelectorAsync("main a[href*='/in/'], main h2:has-text('No results')", new PageWaitForSelectorOptions { Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            ThrowIfLoggedOut(_page.Url);
            return [];
        }
        await HumanDelay.DelayAsync(1_500, 3_500, ct);
        await _page.Mouse.WheelAsync(0, 1200);
        await HumanDelay.DelayAsync(800, 1_800, ct);

        var raw = await _page.EvaluateAsync<RawCard[]>(
            """
            () => {
              const out = [], seen = new Set();
              for (const a of document.querySelectorAll("main a[href*='/in/']")) {
                const url = a.href.split('?')[0].replace(/\/$/, '');
                if (seen.has(url)) continue;
                const card = a.closest('li') || a.closest('[data-chameleon-result-urn]') || a.closest('div[data-view-name]');
                if (!card) continue;
                const nameEl = a.querySelector("span[aria-hidden='true']") || a;
                const name = (nameEl.innerText || '').trim().split('\n')[0];
                if (!name) continue;
                seen.add(url);
                out.push({ url, name, lines: (card.innerText || '').split('\n').map(s => s.trim()).filter(Boolean).slice(0, 14) });
              }
              return out;
            }
            """);

        return raw
            .Where(r => r.Url is not null && r.Name is not null && !r.Name.Equals("LinkedIn Member", StringComparison.OrdinalIgnoreCase))
            .Select(r => ToPerson(r.Url!, r.Name!, r.Lines ?? []))
            .ToList();
    }

    private sealed class RawCard
    {
        public string? Url { get; set; }
        public string? Name { get; set; }
        public string[]? Lines { get; set; }
    }

    /// <summary>Screenshot + HTML of the current results page (for diagnosing layout changes). Stays on this machine.</summary>
    public async Task SaveDebugSnapshotAsync(string pathWithoutExtension)
    {
        if (_page is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pathWithoutExtension))!);
        await _page.ScreenshotAsync(new PageScreenshotOptions { Path = pathWithoutExtension + ".png", FullPage = true });
        await File.WriteAllTextAsync(pathWithoutExtension + ".html", await _page.ContentAsync());
    }

    [GeneratedRegex(@"^(•\s*)?(1st|2nd|3rd\+?|3rd)(\s+degree connection)?$|^View .+profile$|^(Connect|Follow|Message|Pending)$|^Status is|^Current:|^Past:|mutual connection|^\d+\s+followers", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseLine();

    /// <summary>Card lines look like: Name / View Name's profile / • 2nd / Headline / Location / [Current: …] / Connect.</summary>
    internal static LinkedInPerson ToPerson(string url, string name, string[] lines)
    {
        var meaningful = lines
            .Where(l => !NoiseLine().IsMatch(l) && !l.Equals(name, StringComparison.OrdinalIgnoreCase) && !l.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var headline = meaningful.ElementAtOrDefault(0);
        var location = meaningful.ElementAtOrDefault(1);
        return new LinkedInPerson(CleanName(name), headline, location, url, string.Join(" · ", lines));
    }

    /// <summary>"Owner at Jai Ma Graphics" -> "Owner"; headlines without " at " are kept as they are (trimmed).</summary>
    public static string? DesignationFromHeadline(string? headline)
    {
        if (string.IsNullOrWhiteSpace(headline))
            return null;
        var m = Regex.Match(headline, @"^(?<role>.{2,80}?)\s+(?:at|@)\s+", RegexOptions.IgnoreCase);
        var role = m.Success ? m.Groups["role"].Value : headline.Split('|')[0];
        role = role.Trim(' ', '-', '–', ',', '·');
        return role.Length > 120 ? role[..120] : role;
    }

    private static string CleanName(string name) => Regex.Replace(name, @"\s*,?\s*(Ph\.?D|MBA|CA|CFA|PMP)\.?$", "", RegexOptions.IgnoreCase).Trim();

    private static void ThrowIfLoggedOut(string url)
    {
        if (url.Contains("/login", StringComparison.OrdinalIgnoreCase) || url.Contains("/authwall", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/checkpoint", StringComparison.OrdinalIgnoreCase) || url.Contains("/uas/", StringComparison.OrdinalIgnoreCase))
            throw new LinkedInSessionExpiredException(url);
    }
}
