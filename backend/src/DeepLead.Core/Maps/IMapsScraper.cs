namespace DeepLead.Core.Maps;

public sealed record MapsSearchRequest(
    string Keyword,
    string City,
    string? Region,
    string CountryName,
    string CountryIso2,
    string LanguageCode = "en",
    int? MaxResults = null)
{
    /// <summary>
    /// Area search: centre the map here and search the keyword alone, so results come from this part of the city.
    /// Used to get past Google's ~120-results-per-list cap in big cities.
    /// </summary>
    public (decimal Latitude, decimal Longitude)? Center { get; init; }
    public int Zoom { get; init; } = 15;

    /// <summary>Places already saved for this aspect: skipped at list level without opening their page.</summary>
    public IReadOnlySet<string>? SkipPlaceIds { get; init; }
}

/// <summary>What the last list scroll saw: cards in the list and whether Google showed "end of the list".</summary>
public sealed record MapsListStats(int Cards, bool ReachedEnd, int SkippedKnown, int PermanentlyClosed);

public interface IMapsScraper
{
    /// <summary>
    /// Searches Google Maps for "keyword in city" and returns every listing until the end of the result list.
    /// Permanently closed listings are dropped here and never returned.
    /// </summary>
    IAsyncEnumerable<MapsListing> SearchAsync(MapsSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stats of the most recent SearchAsync list (valid once enumeration finished).</summary>
    MapsListStats? LastListStats { get; }
}
