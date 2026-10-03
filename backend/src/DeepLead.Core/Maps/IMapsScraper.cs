namespace DeepLead.Core.Maps;

public sealed record MapsSearchRequest(
    string Keyword,
    string City,
    string? Region,
    string CountryName,
    string CountryIso2,
    string LanguageCode = "en",
    int? MaxResults = null);

public interface IMapsScraper
{
    /// <summary>
    /// Searches Google Maps for "keyword in city" and returns every listing until the end of the result list.
    /// Permanently closed listings are dropped here and never returned.
    /// </summary>
    IAsyncEnumerable<MapsListing> SearchAsync(MapsSearchRequest request, CancellationToken cancellationToken = default);
}
