using DeepLead.Core.Contracts;
using DeepLead.Core.Security;
using DeepLead.Data;
using DeepLead.Enrichment.People;
using DeepLead.Scrapers.Browser;
using DeepLead.Scrapers.Sites;
using Microsoft.Playwright;

namespace DeepLead.Worker;

/// <summary>
/// LinkedIn people search for one session run, using the tenant's connected LinkedIn account.
/// Owns the logged-in browser; enforces the daily cap and human pacing; marks the account Expired on a login wall.
/// Returns null (and stops trying for this run) when LinkedIn isn't usable, so Stage 2 simply continues without it.
/// </summary>
public sealed class WorkerLinkedInLookup(
    int tenantId,
    AccountRepository accounts,
    SecretBox secrets,
    ScrapingOptions options,
    ILogger logger) : ILinkedInLookup, IAsyncDisposable
{
    private BrowserSession? _browser;
    private LinkedInPeopleSearch? _search;
    private bool _disabled;
    private int _snapshots;
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    public async Task<IReadOnlyList<LinkedInPerson>?> SearchPeopleAsync(string keywords, CancellationToken ct)
    {
        if (_disabled || !options.LinkedInEnabled)
            return null;

        // Also returns false when no LinkedIn account is connected for this tenant.
        if (!await accounts.TryConsumeDailyQuotaAsync(tenantId, Platforms.LinkedIn, options.LinkedInDailyCap, ct))
        {
            logger.LogInformation("LinkedIn not used for the rest of this run (not connected or daily cap of {Cap} reached)", options.LinkedInDailyCap);
            _disabled = true;
            return null;
        }

        if (_search is null && !await StartAsync(ct))
            return null;

        var wait = _nextAllowedUtc - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);

        try
        {
            var people = await _search!.SearchAsync(keywords, ct);
            _nextAllowedUtc = DateTime.UtcNow + TimeSpan.FromSeconds(Random.Shared.Next(options.LinkedInMinDelaySeconds, options.LinkedInMaxDelaySeconds));
            logger.LogInformation("LinkedIn '{Keywords}': {Count} people on the results page", keywords, people.Count);

            // Empty pages are either genuinely empty or a LinkedIn layout change; keep a few snapshots to tell which.
            if (people.Count == 0 && _snapshots < 3)
            {
                _snapshots++;
                await _search.SaveDebugSnapshotAsync(Path.Combine("logs", "linkedin", $"empty-{DateTime.Now:yyyyMMdd-HHmmss}"));
            }

            await SaveSessionAsync(ct);
            return people;
        }
        catch (LinkedInSessionExpiredException ex)
        {
            logger.LogWarning("{Message}; marking the LinkedIn account as expired", ex.Message);
            await accounts.MarkExpiredAsync(tenantId, Platforms.LinkedIn, "LinkedIn asked to log in again. Reconnect it in Settings.", ct);
            _disabled = true;
            return null;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            logger.LogWarning("LinkedIn search failed for '{Keywords}': {Error}", keywords, ex.Message.Split('\n')[0]);
            _nextAllowedUtc = DateTime.UtcNow + TimeSpan.FromSeconds(options.LinkedInMaxDelaySeconds);
            return null;
        }
    }

    private async Task<bool> StartAsync(CancellationToken ct)
    {
        var encrypted = await accounts.GetSessionAsync(tenantId, Platforms.LinkedIn, ct);
        if (encrypted is null)
        {
            _disabled = true;
            return false;
        }

        _browser = await BrowserSession.StartAsync(new BrowserOptions
        {
            Headless = options.LinkedInHeadless,
            StorageStateJson = secrets.Decrypt(encrypted),
        });
        _search = new LinkedInPeopleSearch(_browser);
        logger.LogInformation("LinkedIn session started for tenant {TenantId}", tenantId);
        return true;
    }

    /// <summary>LinkedIn rotates cookies; store the latest so the next run starts from a fresh session.</summary>
    private async Task SaveSessionAsync(CancellationToken ct)
    {
        if (_browser is null)
            return;
        try
        {
            await accounts.UpdateSessionAsync(tenantId, Platforms.LinkedIn, secrets.Encrypt(await _browser.ExportStateAsync()), ct);
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug("Could not export LinkedIn session: {Error}", ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await SaveSessionAsync(CancellationToken.None);
            await _browser.DisposeAsync();
        }
    }
}
