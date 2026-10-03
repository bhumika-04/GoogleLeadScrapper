using DeepLead.Core.Contracts;
using DeepLead.Core.Security;
using DeepLead.Data;
using DeepLead.Scrapers.Browser;
using Microsoft.Playwright;

namespace DeepLead.Worker;

/// <summary>
/// Settings → "Connect account": opens a visible browser on the login page, the user signs in by hand
/// (incl. OTP/2FA), and the resulting session (cookies) is saved encrypted. Passwords are never seen or stored.
/// Needs an interactive desktop session (run the worker in a console window, not as a Windows service, to connect).
/// </summary>
public sealed class AccountConnector(AccountRepository accounts, SecretBox secrets, ILogger<AccountConnector> logger) : BackgroundService
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);

    private sealed record PlatformLogin(string Url, string? SessionCookie);

    // SessionCookie = cookie that only exists once logged in; null = rely on the user's "save" click.
    private static readonly Dictionary<string, PlatformLogin> Logins = new()
    {
        [Platforms.LinkedIn] = new("https://www.linkedin.com/login", "li_at"),
        [Platforms.Facebook] = new("https://www.facebook.com/login", "c_user"),
        [Platforms.Instagram] = new("https://www.instagram.com/accounts/login/", "sessionid"),
        [Platforms.IndiaMart] = new("https://my.indiamart.com/", null),
        [Platforms.Justdial] = new("https://www.justdial.com/", null),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await accounts.FailStaleWaitsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await accounts.ClaimConnectRequestAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    continue;
                }
                await ConnectAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Account connector loop error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConnectAsync(ConnectJob job, CancellationToken ct)
    {
        var login = Logins[job.Platform];
        logger.LogInformation("Opening {Platform} login window for tenant {TenantId}", job.Platform, job.TenantId);

        try
        {
            // Fresh profile: nothing from scraping sessions leaks into the account, and vice versa.
            await using var browser = await BrowserSession.StartAsync(new BrowserOptions { Headless = false, StorageStatePath = null });
            var page = await browser.Context.NewPageAsync();
            await page.GotoAsync(login.Url);
            await page.BringToFrontAsync();

            var deadline = DateTime.UtcNow + LoginTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

                if (page.IsClosed)
                {
                    await accounts.FailConnectAsync(job.Id, "The login window was closed before the session was saved.", ct);
                    return;
                }

                var cookies = await browser.Context.CookiesAsync();
                var loggedIn = login.SessionCookie is not null && cookies.Any(c => c.Name == login.SessionCookie && !string.IsNullOrEmpty(c.Value));
                var userClickedSave = await accounts.GetStatusAsync(job.Id, ct) == AccountStatus.SaveRequested;

                if (loggedIn || userClickedSave)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);   // let post-login redirects set remaining cookies
                    var state = await browser.ExportStateAsync();
                    await accounts.CompleteConnectAsync(job.Id, secrets.Encrypt(state), ct);
                    logger.LogInformation("{Platform} connected for tenant {TenantId}", job.Platform, job.TenantId);
                    return;
                }
            }

            await accounts.FailConnectAsync(job.Id, "Timed out after 10 minutes waiting for login.", ct);
        }
        catch (PlaywrightException ex)
        {
            logger.LogWarning(ex, "{Platform} connect failed", job.Platform);
            await accounts.FailConnectAsync(job.Id, "Login window failed: " + ex.Message.Split('\n')[0], CancellationToken.None);
        }
    }
}
