using System.Text.Json;
using System.Text.Json.Serialization;
using DeepLead.Core.Maps;
using DeepLead.Scrapers.Browser;
using DeepLead.Scrapers.Maps;
using Microsoft.Extensions.Logging;

// Developer tool for running scrapers by hand, outside the Worker pipeline.
//   DeepLead.Cli install-browsers
//   DeepLead.Cli maps --keyword "Printing Companies" --city Indore [--region "Madhya Pradesh"] [--country India] [--iso2 IN] [--max 20] [--headful] [--out leads.json]

if (args.Length == 0)
{
    Console.WriteLine("Commands: install-browsers | maps --keyword <k> --city <c> [--region <r>] [--country India] [--iso2 IN] [--max N] [--headful] [--out file.json]");
    return 1;
}

switch (args[0])
{
    case "install-browsers":
        return Microsoft.Playwright.Program.Main(["install", "chromium"]);

    case "maps":
        return await RunMapsAsync(ParseOptions(args[1..]));

    case "people":
        return await RunPeopleAsync(ParseOptions(args[1..]));

    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 1;
}

static async Task<int> RunMapsAsync(Dictionary<string, string?> o)
{
    if (!o.TryGetValue("keyword", out var keyword) || !o.TryGetValue("city", out var city) || keyword is null || city is null)
    {
        Console.Error.WriteLine("--keyword and --city are required.");
        return 1;
    }

    using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(c => c.SingleLine = true).SetMinimumLevel(LogLevel.Information));

    var request = new MapsSearchRequest(
        Keyword: keyword,
        City: city,
        Region: o.GetValueOrDefault("region"),
        CountryName: o.GetValueOrDefault("country") ?? "India",
        CountryIso2: o.GetValueOrDefault("iso2") ?? "IN",
        MaxResults: o.TryGetValue("max", out var max) && int.TryParse(max, out var m) ? m : null)
    {
        // --lat/--lng: area search centred on that point (as the worker does when a city list is capped).
        Center = decimal.TryParse(o.GetValueOrDefault("lat"), System.Globalization.CultureInfo.InvariantCulture, out var lat)
                 && decimal.TryParse(o.GetValueOrDefault("lng"), System.Globalization.CultureInfo.InvariantCulture, out var lng)
            ? (lat, lng) : null,
    };

    await using var session = await BrowserSession.StartAsync(new BrowserOptions { Headless = !o.ContainsKey("headful") });
    var scraper = new GoogleMapsScraper(session, loggerFactory.CreateLogger<GoogleMapsScraper>());

    var results = new List<MapsListing>();
    var started = DateTime.UtcNow;
    await foreach (var listing in scraper.SearchAsync(request))
    {
        results.Add(listing);
        Console.WriteLine($"{listing.Rank,4}. {listing.Name} | {listing.Phone ?? "-"} | {listing.Website ?? "-"} | {listing.Rating?.ToString() ?? "-"} ({listing.ReviewCount?.ToString() ?? "0"}) | {listing.BusinessStatus}");
    }

    var outPath = o.GetValueOrDefault("out") ?? $"maps-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    var json = JsonSerializer.Serialize(results, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    });
    await File.WriteAllTextAsync(outPath, json);

    Console.WriteLine($"{results.Count} listings in {(DateTime.UtcNow - started).TotalSeconds:N0}s -> {Path.GetFullPath(outPath)}");
    if (scraper.LastListStats is { } stats)
        Console.WriteLine($"List: {stats.Cards} cards, {(stats.ReachedEnd ? "reached end of list" : "list capped / stopped growing")}, {stats.PermanentlyClosed} permanently closed");
    return 0;
}

// DeepLead.Cli people --name "Burhani Offset Printers" --city Indore [--website https://...] [--iso2 IN]
static async Task<int> RunPeopleAsync(Dictionary<string, string?> o)
{
    if (o.GetValueOrDefault("name") is not { } name || o.GetValueOrDefault("city") is not { } city)
    {
        Console.Error.WriteLine("--name and --city are required.");
        return 1;
    }

    using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(c => c.SingleLine = true).SetMinimumLevel(LogLevel.Information));
    using var search = new DeepLead.Scrapers.Search.DuckDuckGoSearch();
    using var fetcher = new DeepLead.Scrapers.Web.PageFetcher();
    using var indiaMart = new DeepLead.Scrapers.Sites.IndiaMartClient();
    var discovery = new DeepLead.Enrichment.People.PeopleDiscovery(search, fetcher, indiaMart, new DeepLead.Enrichment.Validation.EmailValidator(),
        loggerFactory.CreateLogger<DeepLead.Enrichment.People.PeopleDiscovery>());

    var research = await discovery.ResearchAsync(
        new DeepLead.Core.People.CompanyToResearch(0, name, city, null, o.GetValueOrDefault("iso2") ?? "IN", o.GetValueOrDefault("website")),
        CancellationToken.None);

    Console.WriteLine($"\nPEOPLE ({research.People.Count})");
    foreach (var p in research.People)
        Console.WriteLine($"  {(p.IsOwner ? "[OWNER] " : p.IsDecisionMaker ? "[DM] " : "")}{p.FullName} — {p.Designation ?? "?"} | {p.LinkedInUrl ?? ""} | via {p.Source}: {p.SourceUrl}");
    Console.WriteLine($"CHANNELS ({research.Channels.Count})");
    foreach (var c in research.Channels)
        Console.WriteLine($"  {c.Type}: {c.NormalizedValue} [{c.PhoneKind}{(c.IsValid is { } v ? (v ? " valid" : " INVALID") : "")}] {c.Note} | {c.SourceUrl}");
    Console.WriteLine($"SOCIALS ({research.Socials.Count})");
    foreach (var s in research.Socials)
        Console.WriteLine($"  {s.Platform}: {s.Url}");
    Console.WriteLine($"FACTS ({research.Facts.Count})");
    foreach (var f in research.Facts)
        Console.WriteLine($"  {f.FieldName} = {f.Value} | {f.SourceUrl}");
    return 0;
}

static Dictionary<string, string?> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;
        var key = args[i][2..];
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        options[key] = hasValue ? args[++i] : null;
    }
    return options;
}
