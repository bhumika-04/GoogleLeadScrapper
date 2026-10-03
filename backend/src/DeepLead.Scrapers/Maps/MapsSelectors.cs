namespace DeepLead.Scrapers.Maps;

/// <summary>
/// CSS selectors for Google Maps. Google changes these without notice; kept in one place so they can later
/// be loaded from config/DB (feature F2) instead of requiring a redeploy.
/// </summary>
public sealed class MapsSelectors
{
    public string ResultsFeed { get; init; } = "div[role='feed']";
    public string ResultCard { get; init; } = "div[role='feed'] div.Nv2PK";
    public string ResultLink { get; init; } = "a.hfpxzc";
    public string EndOfList { get; init; } = "text=/reached the end of the list/i";

    public string PlaceName { get; init; } = "h1.DUwDvf";
    public string RatingValue { get; init; } = "div.F7nice > span > span[aria-hidden='true']";
    public string ReviewCount { get; init; } = "div.F7nice span[aria-label*='review']";
    public string Category { get; init; } = "button.DkEaL";
    public string Address { get; init; } = "button[data-item-id='address']";
    public string Phone { get; init; } = "button[data-item-id^='phone:tel:']";
    public string Website { get; init; } = "a[data-item-id='authority']";
    public string MainPanel { get; init; } = "div[role='main']";

    public string ConsentAccept { get; init; } = "button:has-text('Accept all')";
}
