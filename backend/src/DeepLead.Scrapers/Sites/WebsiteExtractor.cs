using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace DeepLead.Scrapers.Sites;

public sealed record RoleMention(string Name, string Role, string Quote);

public sealed record WebsitePageData(
    string Url,
    IReadOnlyList<string> Emails,
    IReadOnlyList<string> TelLinks,
    IReadOnlyList<string> SocialLinks,
    IReadOnlyList<string> ContactPageLinks,
    IReadOnlyList<RoleMention> RoleMentions,
    string VisibleText);

/// <summary>Rule-based extraction from a company web page: emails, tel: links, social links, "Director: Mr X" style mentions.</summary>
public static partial class WebsiteExtractor
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,24}")]
    private static partial Regex Email();

    // Role then name ("Proprietor : Mr. Ramesh Jain") or name then role ("Ramesh Jain (Director)", "Ramesh Jain - Founder").
    private const string Roles = @"(?:Managing\s+Director|Director|Proprietor|Proprietress|Owner|Founder|Co-?Founder|CEO|Chief\s+Executive\s+Officer|Chairman|Chairperson|Partner|Managing\s+Partner|MD)";
    // A name never crosses a line break ([ \t] only): "Contact Us\nH. Badri - Founder" must give "H. Badri", not "Us H. Badri".
    // First part: an initial ("H." / "H ") or a capitalised word; optional middle initial; then a surname (+ optional second surname).
    private const string Name = @"(?:(?:Mr|Mrs|Ms|Dr|Shri|Smt|Er)\.?[ \t]+)?(?:[A-Z](?:\.[ \t]*|[ \t]+)|[A-Z][a-z]+[ \t]+)(?:[A-Z]\.?[ \t]+)?[A-Z][a-zA-Z]+(?:[ \t]+[A-Z][a-zA-Z]+)?";

    [GeneratedRegex(@"(?<role>" + Roles + @")\s*[:\-–]\s*(?<name>" + Name + ")")]
    private static partial Regex RoleThenName();

    [GeneratedRegex(@"(?<name>" + Name + @")\s*(?:[,\-–]\s*|\(\s*)(?<role>" + Roles + @")\b")]
    private static partial Regex NameThenRole();

    private static readonly string[] SocialHosts = ["linkedin.com", "facebook.com", "instagram.com", "youtube.com", "youtu.be", "twitter.com", "x.com"];
    private static readonly string[] ContactKeywords = ["contact", "about", "team", "management", "leadership", "director", "founder", "who-we-are", "our-story", "company-profile", "profile"];
    private static readonly string[] IgnoredEmailSuffixes = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", "example.com", "sentry.io", "wixpress.com", "domain.com"];
    // Menu/boilerplate words: a "name" containing any of these is page furniture, not a person.
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Our", "The", "Us", "Contact", "About", "Read", "View", "More", "Home", "Call", "Email", "Mail", "Message", "Click", "Team",
        "Company", "Private", "Limited", "Welcome", "Menu", "Services", "Service", "Products", "Product", "Gallery", "Follow", "Get",
        "Office", "Address", "Phone", "Copyright", "All", "Rights", "Reserved", "Best", "Top", "Leading", "Printing", "Press", "Quick", "Links",
    };

    public static WebsitePageData Extract(string html, string pageUrl)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var baseUri = new Uri(pageUrl);

        foreach (var junk in doc.QuerySelectorAll("script:not([type='application/ld+json']), style, noscript, svg"))
            junk.Remove();

        // TextContent glues neighbouring elements together ("info@site.com" + "Call us" -> "info@site.comCall us"),
        // which corrupts emails and names; put a line break after every block/inline-block-ish element first.
        foreach (var el in doc.QuerySelectorAll("p, div, li, td, th, tr, dd, dt, h1, h2, h3, h4, h5, h6, section, article, header, footer, address, span, a, strong, b, label, br"))
            el.After(doc.CreateTextNode("\n"));

        var text = WebUtility.HtmlDecode(doc.Body?.TextContent ?? "");
        text = Regex.Replace(text, @"[ \t ]+", " ");
        text = Regex.Replace(text, @"\s*\n\s*", "\n").Trim();

        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tels = new HashSet<string>();
        var socials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contactLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href")!.Trim();
            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                // UnescapeDataString, not UrlDecode: '+' must survive ("tel:+91…", "name+tag@…").
                AddEmail(emails, SafeUnescape(href[7..].Split('?')[0]));
            }
            else if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            {
                tels.Add(SafeUnescape(href[4..]).Trim());
            }
            else if (Uri.TryCreate(baseUri, href, out var abs) && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            {
                var host = abs.Host.ToLowerInvariant();
                if (SocialHosts.Any(s => host == s || host.EndsWith("." + s, StringComparison.Ordinal)))
                {
                    if (abs.AbsolutePath.Length > 1 && !IsShareLink(abs))
                        socials.Add(abs.GetLeftPart(UriPartial.Path).TrimEnd('/'));
                }
                else if (SameSite(host, baseUri.Host) && abs.AbsolutePath.Length > 1)
                {
                    var path = abs.AbsolutePath.ToLowerInvariant();
                    var label = a.TextContent.ToLowerInvariant();
                    if (ContactKeywords.Any(k => path.Contains(k) || label.Contains(k)))
                        contactLinks.Add(abs.GetLeftPart(UriPartial.Path));
                }
            }
        }

        foreach (Match m in Email().Matches(text))
            AddEmail(emails, m.Value);

        // JSON-LD Organization.sameAs often lists the official social profiles.
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
            foreach (var url in SameAsLinks(script.TextContent))
                if (SocialHosts.Any(s => url.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    socials.Add(url.TrimEnd('/'));

        return new WebsitePageData(pageUrl, emails.ToList(), tels.ToList(), socials.ToList(), contactLinks.Take(10).ToList(),
            FindRoleMentions(text), text.Length > 60_000 ? text[..60_000] : text);
    }

    public static IReadOnlyList<RoleMention> FindRoleMentions(string text)
    {
        var found = new Dictionary<string, RoleMention>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Single lines, plus each pair of adjacent lines ("Proprietor:" and "Ramesh Jain" in separate elements).
        // Pairs keep their line break, and Name can't cross it.
        var candidates = lines.Concat(lines.Zip(lines.Skip(1), (a, b) => $"{a}\n{b}"));
        foreach (var line in candidates)
        {
            if (line.Length > 400)
                continue;
            foreach (var regex in new[] { RoleThenName(), NameThenRole() })
            {
                foreach (Match m in regex.Matches(line))
                {
                    var name = Regex.Replace(m.Groups["name"].Value.Trim(), @"\s+", " ");
                    var words = Regex.Replace(name, @"^(Mr|Mrs|Ms|Dr|Shri|Smt|Er)\.?\s+", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Any(w => NotNames.Contains(w.TrimEnd('.'))))
                        continue;
                    found.TryAdd(name, new RoleMention(name, Regex.Replace(m.Groups["role"].Value, @"\s+", " "), line.Trim()));
                }
            }
        }
        return found.Values.ToList();
    }

    /// <summary>Valid-looking emails in free text (same filters as page extraction).</summary>
    public static IReadOnlyList<string> FindEmails(string text)
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Email().Matches(text))
            AddEmail(emails, m.Value);
        return emails.ToList();
    }

    private static string SafeUnescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static void AddEmail(HashSet<string> emails, string raw)
    {
        var e = raw.Trim().Trim('.', ',', ';').ToLowerInvariant();
        if (e.Length is < 6 or > 254 || !Email().IsMatch(e) || IgnoredEmailSuffixes.Any(s => e.EndsWith(s, StringComparison.Ordinal)))
            return;
        emails.Add(e);
    }

    private static bool SameSite(string host, string baseHost)
    {
        static string Bare(string h) => h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..].ToLowerInvariant() : h.ToLowerInvariant();
        var a = Bare(host);
        var b = Bare(baseHost);
        return a == b || a.EndsWith("." + b, StringComparison.Ordinal);
    }

    private static bool IsShareLink(Uri uri)
    {
        var p = uri.AbsolutePath.ToLowerInvariant();
        return p.Contains("/sharer") || p.Contains("/share") || p.StartsWith("/intent") || p.Contains("/dialog/") || p == "/home";
    }

    private static IEnumerable<string> SameAsLinks(string json)
    {
        var links = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            Collect(doc.RootElement, links);
        }
        catch (JsonException)
        {
        }
        return links;

        static void Collect(JsonElement e, List<string> into)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Collect(item, into);
                    break;
                case JsonValueKind.Object:
                    foreach (var prop in e.EnumerateObject())
                    {
                        if (prop.NameEquals("sameAs"))
                        {
                            if (prop.Value.ValueKind == JsonValueKind.String) into.Add(prop.Value.GetString()!);
                            else if (prop.Value.ValueKind == JsonValueKind.Array)
                                into.AddRange(prop.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!));
                        }
                        else Collect(prop.Value, into);
                    }
                    break;
            }
        }
    }
}
