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
        MaxResults: o.TryGetValue("max", out var max) && int.TryParse(max, out var m) ? m : null);

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
