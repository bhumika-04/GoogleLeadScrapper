using Microsoft.Playwright;

namespace DeepLead.Scrapers.Browser;

public sealed class BrowserOptions
{
    public bool Headless { get; init; } = true;

    /// <summary>
    /// Cookies/local storage are saved here between runs so we look like a returning visitor.
    /// Null = fresh profile that is never written to disk.
    /// </summary>
    public string? StorageStatePath { get; init; } = Path.Combine("data", "browser", "state.json");

    /// <summary>A connected account's saved session (Playwright storage-state JSON). When set, StorageStatePath is ignored.</summary>
    public string? StorageStateJson { get; init; }

    internal string? PersistPath => StorageStateJson is null ? StorageStatePath : null;

    public string Locale { get; init; } = "en-IN";
    public string TimezoneId { get; init; } = "Asia/Kolkata";
}

/// <summary>
/// One Chromium instance + context shared by a scraping run. Not thread-safe: one session per worker.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly BrowserOptions _options;

    public IBrowserContext Context { get; }

    private BrowserSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, BrowserOptions options)
    {
        _playwright = playwright;
        _browser = browser;
        Context = context;
        _options = options;
    }

    public static async Task<BrowserSession> StartAsync(BrowserOptions options)
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = options.Headless,
            Args = ["--disable-blink-features=AutomationControlled"],
        });

        // Headless Chromium announces itself as "HeadlessChrome"; present a normal desktop Chrome UA instead.
        var userAgent = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{browser.Version} Safari/537.36";

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = userAgent,
            Locale = options.Locale,
            TimezoneId = options.TimezoneId,
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            StorageState = options.StorageStateJson,
            StorageStatePath = options.PersistPath is { } path && File.Exists(path) ? path : null,
        });
        context.SetDefaultTimeout(30_000);

        return new BrowserSession(playwright, browser, context, options);
    }

    /// <summary>Current cookies + local storage as JSON (for saving a connected account's login).</summary>
    public Task<string> ExportStateAsync() => Context.StorageStateAsync();

    public async ValueTask DisposeAsync()
    {
        // Connected-account sessions live encrypted in the DB, never on disk.
        if (_options.PersistPath is { } path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                await Context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = path });
            }
            catch (PlaywrightException)
            {
                // Saving cookies is best-effort; never fail a run because of it.
            }
        }

        await Context.DisposeAsync();
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }
}
