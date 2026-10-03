using System.Runtime.CompilerServices;
using DeepLead.Core;
using DeepLead.Core.Maps;
using DeepLead.Scrapers.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace DeepLead.Scrapers.Maps;

/// <summary>
/// Stage 1: scrolls the Google Maps result list to the end, then opens each place to read its details.
/// Permanently closed places are skipped (from the list card when visible, otherwise from the place page).
/// </summary>
public sealed class GoogleMapsScraper(BrowserSession session, ILogger<GoogleMapsScraper> logger, MapsSelectors? selectors = null) : IMapsScraper
{
    private const string Source = "GoogleMaps";
    private const int MaxIdleScrolls = 6;      // stop when the list stops growing for this many scrolls
    private const int ShortTimeoutMs = 2_500;  // for optional fields that may simply be absent

    private readonly MapsSelectors _s = selectors ?? new MapsSelectors();

    public async IAsyncEnumerable<MapsListing> SearchAsync(MapsSearchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrWhiteSpace(request.Region)
            ? $"{request.Keyword} in {request.City}, {request.CountryName}"
            : $"{request.Keyword} in {request.City}, {request.Region}, {request.CountryName}";

        var listPage = await session.Context.NewPageAsync();
        var detailPage = await session.Context.NewPageAsync();
        try
        {
            var searchUrl = MapsParsing.BuildSearchUrl(query, request.CountryIso2, request.LanguageCode);
            logger.LogInformation("Maps search: {Query}", query);
            await listPage.GotoAsync(searchUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await HandleInterstitialsAsync(listPage);

            if (!await IsVisibleWithinAsync(listPage, _s.ResultsFeed, 12_000))
            {
                // A very specific query opens a single place directly instead of a list.
                if (await IsVisibleWithinAsync(listPage, _s.PlaceName, 3_000))
                {
                    var single = await ExtractPlaceAsync(listPage, listPage.Url, rank: 1);
                    if (single.BusinessStatus != MapsBusinessStatus.PermanentlyClosed)
                        yield return single;
                }
                else
                {
                    logger.LogInformation("Maps returned no results for {Query}", query);
                }
                yield break;
            }

            var cards = await CollectResultCardsAsync(listPage, request.MaxResults, cancellationToken);
            var open = cards.Where(c => !c.PermanentlyClosed).ToList();
            logger.LogInformation("Maps list for {Query}: {Total} places, {Closed} permanently closed skipped",
                query, cards.Count, cards.Count - open.Count);

            var rank = 0;
            foreach (var card in open)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rank++;

                await HumanDelay.BetweenPagesAsync(cancellationToken);
                await detailPage.GotoAsync(MapsParsing.WithLanguage(card.Href, request.LanguageCode),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await HandleInterstitialsAsync(detailPage);

                if (!await IsVisibleWithinAsync(detailPage, _s.PlaceName, 15_000))
                {
                    logger.LogWarning("Place page did not load: {Name} ({Url})", card.Name, card.Href);
                    continue;
                }

                var listing = await ExtractPlaceAsync(detailPage, card.Href, rank);
                if (listing.BusinessStatus == MapsBusinessStatus.PermanentlyClosed)
                {
                    logger.LogDebug("Skipping permanently closed place {Name}", listing.Name);
                    continue;
                }

                yield return listing;
            }
        }
        finally
        {
            await detailPage.CloseAsync();
            await listPage.CloseAsync();
        }
    }

    private sealed record ResultCard(string Href, string Name, bool PermanentlyClosed);

    private async Task<List<ResultCard>> CollectResultCardsAsync(IPage page, int? maxResults, CancellationToken ct)
    {
        var feed = page.Locator(_s.ResultsFeed).First;
        var lastCount = 0;
        var idleScrolls = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await feed.EvaluateAsync("el => el.scrollBy(0, el.scrollHeight)");
            await HumanDelay.BetweenScrollsAsync(ct);
            ThrowIfBlocked(page);

            var count = await page.Locator(_s.ResultCard).CountAsync();
            if (maxResults is { } max && count >= max)
                break;
            if (await page.Locator(_s.EndOfList).CountAsync() > 0)
                break;

            idleScrolls = count == lastCount ? idleScrolls + 1 : 0;
            if (idleScrolls >= MaxIdleScrolls)
            {
                logger.LogInformation("Maps list stopped growing at {Count} places", count);
                break;
            }
            lastCount = count;
        }

        // Read all cards in one round-trip. Closed status is matched on exact text so review snippets can't trigger it.
        var raw = await page.EvaluateAsync<CardDto[]>(
            """
            ([cardSel, linkSel]) => Array.from(document.querySelectorAll(cardSel)).map(card => {
                const link = card.querySelector(linkSel);
                const texts = Array.from(card.querySelectorAll('span, div')).map(e => e.textContent.trim());
                return {
                    href: link ? link.href : null,
                    name: link ? (link.getAttribute('aria-label') || '') : '',
                    permanentlyClosed: texts.includes('Permanently closed')
                };
            })
            """,
            new[] { _s.ResultCard, _s.ResultLink });

        var cards = raw
            .Where(c => !string.IsNullOrEmpty(c.Href))
            .DistinctBy(c => c.Href)
            .Select(c => new ResultCard(c.Href!, c.Name ?? string.Empty, c.PermanentlyClosed))
            .ToList();

        return maxResults is { } limit ? cards.Take(limit).ToList() : cards;
    }

    private sealed class CardDto
    {
        public string? Href { get; set; }
        public string? Name { get; set; }
        public bool PermanentlyClosed { get; set; }
    }

    private async Task<MapsListing> ExtractPlaceAsync(IPage page, string placeUrl, int rank)
    {
        var name = (await page.Locator(_s.PlaceName).First.InnerTextAsync()).Trim();

        var (lat, lng) = MapsParsing.ParseCoordinates(placeUrl);
        if (lat is null)
            (lat, lng) = MapsParsing.ParseCoordinates(page.Url);

        var status = await page.EvaluateAsync<string>(
            """
            (mainSel) => {
                const main = document.querySelector(mainSel) || document.body;
                const texts = Array.from(main.querySelectorAll('span, div')).map(e => e.textContent.trim());
                if (texts.includes('Permanently closed')) return 'PermanentlyClosed';
                if (texts.includes('Temporarily closed')) return 'TemporarilyClosed';
                return 'Operational';
            }
            """,
            _s.MainPanel);

        return new MapsListing
        {
            Name = name,
            MapsUrl = placeUrl,
            PlaceId = MapsParsing.ParsePlaceId(placeUrl) ?? MapsParsing.ParsePlaceId(page.Url),
            Category = await TextOrNullAsync(page, _s.Category),
            Address = MapsParsing.StripLabelPrefix(await AttributeOrNullAsync(page, _s.Address, "aria-label")),
            Phone = MapsParsing.ParsePhoneFromItemId(await AttributeOrNullAsync(page, _s.Phone, "data-item-id")),
            Website = MapsParsing.CleanWebsite(await AttributeOrNullAsync(page, _s.Website, "href")),
            Rating = MapsParsing.ParseRating(await TextOrNullAsync(page, _s.RatingValue)),
            ReviewCount = MapsParsing.ParseReviewCount(await AttributeOrNullAsync(page, _s.ReviewCount, "aria-label")),
            Latitude = lat,
            Longitude = lng,
            BusinessStatus = Enum.Parse<MapsBusinessStatus>(status),
            Rank = rank,
        };
    }

    private async Task HandleInterstitialsAsync(IPage page)
    {
        ThrowIfBlocked(page);

        if (page.Url.Contains("consent.google", StringComparison.OrdinalIgnoreCase))
        {
            var accept = page.Locator(_s.ConsentAccept).First;
            if (await accept.CountAsync() > 0)
            {
                await accept.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            }
        }
    }

    private static void ThrowIfBlocked(IPage page)
    {
        if (page.Url.Contains("/sorry/", StringComparison.OrdinalIgnoreCase))
            throw new ScrapeBlockedException(Source, page.Url, "unusual traffic / CAPTCHA page");
    }

    private static async Task<bool> IsVisibleWithinAsync(IPage page, string selector, int timeoutMs)
    {
        try
        {
            await page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<string?> TextOrNullAsync(IPage page, string selector)
    {
        var locator = page.Locator(selector).First;
        if (await locator.CountAsync() == 0)
            return null;
        var text = await locator.InnerTextAsync(new LocatorInnerTextOptions { Timeout = ShortTimeoutMs });
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static async Task<string?> AttributeOrNullAsync(IPage page, string selector, string attribute)
    {
        var locator = page.Locator(selector).First;
        if (await locator.CountAsync() == 0)
            return null;
        var value = await locator.GetAttributeAsync(attribute, new LocatorGetAttributeOptions { Timeout = ShortTimeoutMs });
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
