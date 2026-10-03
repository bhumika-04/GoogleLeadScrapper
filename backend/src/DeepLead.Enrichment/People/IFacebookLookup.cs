using DeepLead.Scrapers.Sites;

namespace DeepLead.Enrichment.People;

/// <summary>
/// Reads public Facebook pages. Implemented by the worker (owns the browser; uses the tenant's connected
/// Facebook account when there is one, otherwise browses without login).
/// </summary>
public interface IFacebookLookup
{
    /// <summary>Null when Facebook can't be used right now (login wall, quota); NotFound pages return (NotFound, null).</summary>
    Task<(FacebookReadOutcome Outcome, FacebookPage? Page)?> ReadPageAsync(string url, CancellationToken ct);
}
