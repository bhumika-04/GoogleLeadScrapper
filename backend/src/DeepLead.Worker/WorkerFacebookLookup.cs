using DeepLead.Core.Contracts;
using DeepLead.Core.Security;
using DeepLead.Data;
using DeepLead.Enrichment.People;
using DeepLead.Scrapers.Browser;
using DeepLead.Scrapers.Sites;
using Microsoft.Playwright;

namespace DeepLead.Worker;

/// <summary>
/// Facebook page reading for one session run. Uses the tenant's connected Facebook account when present,
/// otherwise browses without login (most business pages are public). Paced; stops for the run on a login wall
/// (and marks a connected account Expired).
/// </summary>
public sealed class WorkerFacebookLookup(
    int tenantId,
    AccountRepository accounts,
    SecretBox secrets,
    ScrapingOptions options,
    ILogger logger) : IFacebookLookup, IAsyncDisposable
{
    private BrowserSession? _browser;
    private FacebookPageReader? _reader;
    private bool _usingAccount;
    private bool _disabled;
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    public async Task<(FacebookReadOutcome Outcome, FacebookPage? Page)?> ReadPageAsync(string url, CancellationToken ct)
    {
        if (_disabled || !options.FacebookEnabled)
            return null;

        if (_reader is null)
            await StartAsync(ct);

        var wait = _nextAllowedUtc - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);

        try
        {
            var result = await _reader!.ReadAsync(url, ct);
            _nextAllowedUtc = DateTime.UtcNow + TimeSpan.FromSeconds(Random.Shared.Next(options.FacebookMinDelaySeconds, options.FacebookMaxDelaySeconds));

            if (result.Outcome == FacebookReadOutcome.LoginWall)
            {
                _disabled = true;
                if (_usingAccount)
                    await accounts.MarkExpiredAsync(tenantId, Platforms.Facebook, "Facebook asked to log in again. Reconnect it in Settings.", ct);
                logger.LogWarning("Facebook login wall ({Mode}); skipping Facebook for the rest of this run", _usingAccount ? "connected account" : "no login");
                return null;
            }
            return result;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            logger.LogWarning("Facebook page {Url} failed: {Error}", url, ex.Message.Split('\n')[0]);
            return (FacebookReadOutcome.NotFound, null);
        }
    }

    private async Task StartAsync(CancellationToken ct)
    {
        var encrypted = await accounts.GetSessionAsync(tenantId, Platforms.Facebook, ct);
        _usingAccount = encrypted is not null;
        _browser = await BrowserSession.StartAsync(new BrowserOptions
        {
            Headless = options.Headless,
            StorageStateJson = encrypted is null ? null : secrets.Decrypt(encrypted),
            StorageStatePath = null,   // anonymous browsing keeps no cookies on disk
            Locale = "en-US",
        });
        _reader = new FacebookPageReader(_browser);
        logger.LogInformation("Facebook reader started ({Mode})", _usingAccount ? "connected account" : "no login");
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is null)
            return;
        if (_usingAccount)
        {
            try
            {
                await accounts.UpdateSessionAsync(tenantId, Platforms.Facebook, secrets.Encrypt(await _browser.ExportStateAsync()), CancellationToken.None);
            }
            catch (PlaywrightException)
            {
            }
        }
        await _browser.DisposeAsync();
    }
}
