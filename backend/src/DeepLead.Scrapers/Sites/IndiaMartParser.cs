using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace DeepLead.Scrapers.Sites;

public sealed record IndiaMartProfile(
    string? CompanyName,
    string? CeoName,
    /// <summary>Role IndiaMART states with the name ("Owner", "Director"); null when it's the factsheet "Company CEO".</summary>
    string? CeoRole,
    string? Employees,
    string? AnnualTurnover,
    string? LegalStatus,
    string? Gstin,
    string? NatureOfBusiness,
    string? YearEstablished,
    string? City,
    string? ForwardingNumber,
    IReadOnlyDictionary<string, string> Factsheet);

/// <summary>Parses an IndiaMART seller "profile.html" page (public, no login): the factsheet table + JSON-LD.</summary>
public static partial class IndiaMartParser
{
    [GeneratedRegex(@"^https?://(?:www\.)?indiamart\.com/([a-z0-9\-]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex SellerUrl();

    [GeneratedRegex("\"(?:pnsNumber|sellerPns)\"\\s*:\\s*\"([+\\d\\- ]{8,20})\"")]
    private static partial Regex PnsNumber();

    // Verified-supplier block on custom seller pages: "directorProprietor":"Rinku Sharma (Owner)" (often HTML-escaped).
    [GeneratedRegex("\"directorProprietor\"\\s*:\\s*\"([^\"]{3,120})\"")]
    private static partial Regex DirectorProprietor();

    [GeneratedRegex(@"^(?<name>.+?)\s*\((?<role>[^)]{2,40})\)\s*$")]
    private static partial Regex NameWithRole();

    private static readonly HashSet<string> NonSellerSlugs = new(StringComparer.OrdinalIgnoreCase)
        { "proddetail", "search", "impcat", "city", "catalog", "cgi", "enquiry" };

    /// <summary>"https://www.indiamart.com/burhani-offset-printers/anything" -> ".../burhani-offset-printers/profile.html".</summary>
    public static string? ToProfileUrl(string url)
    {
        var m = SellerUrl().Match(url);
        if (!m.Success || NonSellerSlugs.Contains(m.Groups[1].Value))
            return null;
        return $"https://www.indiamart.com/{m.Groups[1].Value.ToLowerInvariant()}/profile.html";
    }

    public static IndiaMartProfile Parse(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);

        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in doc.QuerySelectorAll(".factsheet-table__row"))
        {
            var label = row.QuerySelector(".factsheet-table__label")?.TextContent.Trim();
            var value = row.QuerySelector(".factsheet-table__value")?.TextContent.Trim();
            if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value))
                facts.TryAdd(label, Clean(value));
        }

        string? companyName = null, city = null, founded = null;
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var items = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().ToList() : [json.RootElement];
                foreach (var item in items.Where(i => i.ValueKind == JsonValueKind.Object))
                {
                    if (!item.TryGetProperty("@type", out var type) || type.GetString() != "Organization")
                        continue;
                    companyName ??= item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    founded ??= item.TryGetProperty("foundingDate", out var f) ? f.GetString() : null;
                    if (item.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("addressLocality", out var loc))
                        city ??= loc.GetString();
                }
            }
            catch (JsonException)
            {
                // Malformed JSON-LD is common; the factsheet is the main source anyway.
            }
        }

        string? Get(params string[] labels) => labels.Select(l => facts.GetValueOrDefault(l)).FirstOrDefault(v => v is not null);

        var decoded = System.Net.WebUtility.HtmlDecode(html);
        var pns = PnsNumber().Match(decoded);

        string? ownerRole = null;
        var ceo = Get("Company CEO", "CEO", "Proprietor", "Owner", "Partner", "Director", "Managing Director");
        if (ceo is null && DirectorProprietor().Match(decoded) is { Success: true } dp)
        {
            var value = dp.Groups[1].Value.Trim();
            var nr = NameWithRole().Match(value);
            ceo = nr.Success ? nr.Groups["name"].Value.Trim() : value;
            ownerRole = nr.Success ? nr.Groups["role"].Value.Trim() : null;
        }

        return new IndiaMartProfile(
            CompanyName: companyName,
            CeoName: ceo,
            CeoRole: ownerRole,
            Employees: Get("Total Number of Employees", "Number of Employees"),
            AnnualTurnover: Get("Annual Turnover"),
            LegalStatus: Get("Legal Status of Firm"),
            Gstin: Get("GST No.", "GST Number", "GSTIN"),
            NatureOfBusiness: Get("Nature of Business"),
            YearEstablished: Get("Year of Establishment") ?? founded,
            City: city,
            ForwardingNumber: pns.Success ? pns.Groups[1].Value.Trim() : null,
            Factsheet: facts);
    }

    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
