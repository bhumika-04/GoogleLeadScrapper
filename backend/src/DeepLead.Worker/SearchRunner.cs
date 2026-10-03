using DeepLead.Core;
using DeepLead.Core.Contracts;
using DeepLead.Core.Maps;
using DeepLead.Core.Security;
using DeepLead.Core.Text;
using DeepLead.Data;
using DeepLead.Enrichment.People;
using DeepLead.Enrichment.Validation;
using DeepLead.Scrapers.Browser;
using DeepLead.Scrapers.Maps;

namespace DeepLead.Worker;

public sealed class ScrapingOptions
{
    public bool Headless { get; init; } = true;
    public int BlockCooldownMinutes { get; init; } = 20;
    public int IdlePollSeconds { get; init; } = 5;

    /// <summary>Testing aid only; null = scrape until the end of the Maps list.</summary>
    public int? MaxResultsPerAspect { get; init; }

    /// <summary>Stage 2 (owner, core team, contacts, socials). Companies researched within this many days are reused.</summary>
    public bool PeopleStageEnabled { get; init; } = true;
    public int PeopleCacheDays { get; init; } = 30;

    /// <summary>LinkedIn people search with the tenant's connected account (Settings).</summary>
    public bool LinkedInEnabled { get; init; } = true;
    /// <summary>Searches per account per UTC day. Free LinkedIn accounts get restricted when they search heavily.</summary>
    public int LinkedInDailyCap { get; init; } = 60;
    public int LinkedInMinDelaySeconds { get; init; } = 25;
    public int LinkedInMaxDelaySeconds { get; init; } = 50;
    /// <summary>A visible (non-headless) browser is less likely to be flagged by LinkedIn.</summary>
    public bool LinkedInHeadless { get; init; }
}

/// <summary>
/// Picks up one session at a time and runs its aspects sequentially:
/// Stage 1 Google Maps, then Stage 2 people discovery for that aspect's companies.
/// Pause/Cancel from the UI are honoured between items; a Google Maps block pauses for a cool-down and retries the aspect.
/// </summary>
public sealed class SearchRunner(
    SearchRepository searches,
    LeadRepository leads,
    PeopleRepository people,
    PeopleDiscovery discovery,
    AccountRepository accounts,
    SecretBox secrets,
    ScrapingOptions options,
    ILoggerFactory loggerFactory,
    ILogger<SearchRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Search runner started (headless: {Headless})", options.Headless);

        while (!stoppingToken.IsCancellationRequested)
        {
            SearchRunInfo? search = null;
            try
            {
                search = await searches.ClaimNextAsync(stoppingToken);
                if (search is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.IdlePollSeconds), stoppingToken);
                    continue;
                }

                await RunSearchAsync(search, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // service stopping; the session stays 'Running' and is resumed on next start
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Session {SearchId} crashed", search?.Id);
                if (search is not null)
                    await searches.FinishSearchAsync(search.Id, SearchStatus.Failed, CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(options.IdlePollSeconds), stoppingToken);
            }
        }
    }

    private async Task RunSearchAsync(SearchRunInfo search, CancellationToken ct)
    {
        var aspects = await searches.GetAspectsToRunAsync(search.Id, ct);
        logger.LogInformation("Session {SearchId}: {Count} aspects to run", search.Id, aspects.Count);

        await using var browser = await BrowserSession.StartAsync(new BrowserOptions { Headless = options.Headless });
        var scraper = new GoogleMapsScraper(browser, loggerFactory.CreateLogger<GoogleMapsScraper>());

        // The tenant's LinkedIn account (if connected) is opened lazily on the first Stage 2 lookup.
        await using var linkedIn = new WorkerLinkedInLookup(search.TenantId, accounts, secrets, options, logger);
        _linkedIn = linkedIn;

        var failed = 0;
        foreach (var aspect in aspects)
        {
            var outcome = await RunAspectWithRetryAsync(search, aspect, scraper, ct);
            if (outcome == AspectOutcome.Stopped)
            {
                logger.LogInformation("Session {SearchId} stopped by user", search.Id);
                return;
            }
            if (outcome == AspectOutcome.Failed)
                failed++;
        }

        await searches.FinishSearchAsync(search.Id, failed == aspects.Count && failed > 0 ? SearchStatus.Failed : SearchStatus.Completed, ct);
        logger.LogInformation("Session {SearchId} finished ({Failed} failed aspects)", search.Id, failed);
    }

    private enum AspectOutcome { Completed, Failed, Stopped }

    // The runner handles one session at a time, so the current run's LinkedIn lookup can live in a field.
    private WorkerLinkedInLookup? _linkedIn;

    private async Task<AspectOutcome> RunAspectWithRetryAsync(SearchRunInfo search, AspectRunInfo aspect, GoogleMapsScraper scraper, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                return await RunAspectAsync(search, aspect, scraper, ct);
            }
            catch (ScrapeBlockedException ex)
            {
                var until = DateTime.UtcNow.AddMinutes(options.BlockCooldownMinutes);
                logger.LogWarning("Google blocked us on aspect {AspectId}; cooling down until {Until:HH:mm} UTC", aspect.Id, until);
                await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Running, SearchStatus.Blocked,
                    $"Blocked by Google ({ex.Message}). Retrying at {until:HH:mm} UTC.", ct);

                // Wait out the cool-down, but react to Pause/Cancel while waiting.
                while (DateTime.UtcNow < until)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    if (await IsStoppedAsync(search.Id, ct))
                    {
                        await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Paused, SearchStatus.Paused, null, ct);
                        return AspectOutcome.Stopped;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Aspect {AspectId} failed", aspect.Id);
                await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Failed, SearchStatus.Failed, ex.Message, ct);
                return AspectOutcome.Failed;
            }
        }
    }

    private async Task<AspectOutcome> RunAspectAsync(SearchRunInfo search, AspectRunInfo aspect, GoogleMapsScraper scraper, CancellationToken ct)
    {
        if (await IsStoppedAsync(search.Id, ct))
            return AspectOutcome.Stopped;

        logger.LogInformation("Aspect #{Seq}: {Keyword} — {City}", aspect.Sequence, aspect.Keyword, aspect.City);

        // Stage 1 – Google Maps (skipped when a previous run already finished it, e.g. paused during Stage 2).
        if (aspect.MapsStatus != SearchStatus.Completed)
        {
            await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Running, SearchStatus.Running, null, ct);
            await searches.SetAspectProgressAsync(aspect.Id, null, 0, ct);

            var request = new MapsSearchRequest(aspect.Keyword, aspect.City, aspect.Region, search.CountryName, search.CountryIso2,
                MaxResults: options.MaxResultsPerAspect);
            var done = 0;
            await foreach (var listing in scraper.SearchAsync(request, ct))
            {
                await leads.UpsertMapsLeadAsync(aspect.Id, Prepare(listing, search.CountryIso2, aspect.CityId), ct);
                done++;
                await searches.SetAspectProgressAsync(aspect.Id, null, done, ct);

                if (await IsStoppedAsync(search.Id, ct))
                {
                    await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Paused, SearchStatus.Paused, null, ct);
                    return AspectOutcome.Stopped;
                }
            }

            await searches.SetAspectProgressAsync(aspect.Id, done, done, ct);
            await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Running, SearchStatus.Completed, null, ct);
        }
        if (!options.PeopleStageEnabled)
        {
            await searches.CompleteAspectAsync(aspect.Id, ct);
            return AspectOutcome.Completed;
        }

        // Stage 2 – people discovery for this aspect's companies.
        if (!await RunPeopleStageAsync(search, aspect, ct))
            return AspectOutcome.Stopped;

        await searches.CompleteAspectAsync(aspect.Id, ct);
        return AspectOutcome.Completed;
    }

    /// <summary>Returns false when the user paused/cancelled during the stage.</summary>
    private async Task<bool> RunPeopleStageAsync(SearchRunInfo search, AspectRunInfo aspect, CancellationToken ct)
    {
        var companies = await people.GetCompaniesToResearchAsync(aspect.Id, options.PeopleCacheDays, ct);
        var total = await people.CountAspectCompaniesAsync(aspect.Id, ct);
        var done = total - companies.Count;   // already researched recently (company cache)

        await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Running, SearchStatus.Running, null, ct, stage: 2);
        await searches.SetAspectProgressAsync(aspect.Id, total, done, ct, stage: 2);

        var partial = 0;
        foreach (var company in companies)
        {
            if (await IsStoppedAsync(search.Id, ct))
            {
                await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Paused, SearchStatus.Paused, null, ct, stage: 2);
                return false;
            }

            try
            {
                var research = await discovery.ResearchAsync(company, ct, _linkedIn);
                await people.SaveResearchAsync(company.CompanyId, research, ct);
                if (research.SearchSkipped)
                    partial++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad website must not stop the run.
                logger.LogWarning(ex, "People research failed for {Company}", company.Name);
            }

            done++;
            await searches.SetAspectProgressAsync(aspect.Id, total, done, ct, stage: 2);
        }

        var note = partial > 0
            ? $"Web search was rate-limited for {partial} compan{(partial == 1 ? "y" : "ies")} (website and IndiaMART still checked); owners/teams may be incomplete."
            : null;
        await searches.SetAspectStateAsync(aspect.Id, SearchStatus.Running, SearchStatus.Completed, note, ct, stage: 2);
        return true;
    }

    private async Task<bool> IsStoppedAsync(long searchId, CancellationToken ct)
    {
        var status = await searches.GetStatusAsync(searchId, ct);
        return status is SearchStatus.Paused or SearchStatus.Cancelled;
    }

    /// <summary>Normalizes the phone and moves social-profile "websites" (Instagram etc.) to socials.</summary>
    private static MapsLeadUpsert Prepare(MapsListing listing, string countryIso2, int cityId)
    {
        string? website = null, websiteDomain = null, socialPlatform = null, socialUrl = null;
        if (listing.Website is { } url)
        {
            var clean = WebsiteClassifier.StripTracking(url);
            if (WebsiteClassifier.GetSocialPlatform(clean) is { } platform)
            {
                socialPlatform = platform.ToString();
                socialUrl = clean;
            }
            else
            {
                website = clean;
                websiteDomain = WebsiteClassifier.GetDomain(clean);
            }
        }

        var phone = PhoneNormalizer.Normalize(listing.Phone, countryIso2);
        return new MapsLeadUpsert(listing, countryIso2, cityId, CompanyText.NormalizeName(listing.Name),
            website, websiteDomain, socialPlatform, socialUrl,
            phone?.E164 ?? listing.Phone, phone?.Kind, phone?.IsValid);
    }
}
