using DeepLead.Scrapers.Sites;

namespace DeepLead.Enrichment.People;

/// <summary>
/// LinkedIn people search through a connected account. Implemented by the worker, which owns the logged-in browser,
/// the daily quota, pacing and session-expiry handling.
/// </summary>
public interface ILinkedInLookup
{
    /// <summary>Results for the keywords, or null when LinkedIn can't be used right now (quota reached, session expired).</summary>
    Task<IReadOnlyList<LinkedInPerson>?> SearchPeopleAsync(string keywords, CancellationToken ct);
}
