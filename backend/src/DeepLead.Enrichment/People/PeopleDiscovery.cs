using System.Text.RegularExpressions;
using DeepLead.Core;
using DeepLead.Core.People;
using DeepLead.Core.Text;
using DeepLead.Enrichment.Validation;
using DeepLead.Scrapers.Search;
using DeepLead.Scrapers.Sites;
using DeepLead.Scrapers.Web;
using Microsoft.Extensions.Logging;

namespace DeepLead.Enrichment.People;

/// <summary>
/// Stage 2: finds the owner and core team of a company plus contact channels and social handles.
/// Every finding carries the URL it came from; nothing is guessed. Sources, in order:
///  1. company website (home + contact/about/team pages) – or its IndiaMART page when that is the "website";
///  2. IndiaMART seller profile, located by slug guessing (no search engine needed);
///  3. LinkedIn people search through a connected account (when the tenant connected one);
///  4. general web search, best-effort, only while nobody has been found (engines block this IP quickly).
/// </summary>
public sealed class PeopleDiscovery(IWebSearch search, PageFetcher fetcher, IndiaMartClient indiaMart, EmailValidator emails, ILogger<PeopleDiscovery> logger)
{
    private const int MaxWebsitePages = 5;

    private static readonly string[] OwnerRoles = ["owner", "proprietor", "proprietress", "founder", "ceo", "chief executive", "managing director", "chairman", "chairperson", "managing partner"];
    private static readonly string[] DecisionRoles = ["director", "partner", "head", "president", "vice president", "vp", "general manager", "gm", "cto", "cfo", "coo", "manager"];

    public async Task<CompanyResearch> ResearchAsync(CompanyToResearch company, CancellationToken ct,
        ILinkedInLookup? linkedIn = null, IFacebookLookup? facebook = null)
    {
        var research = new CompanyResearch();
        var match = new CompanyMatcher(company.Name, company.City);

        try
        {
            // 1. Many small businesses list their IndiaMART page as "website" on Maps: read it as the IndiaMART profile.
            if (company.Website is not null && IndiaMartParser.ToProfileUrl(company.Website) is { } ownIndiaMart)
                await ReadIndiaMartAsync(company, match, ownIndiaMart, research, ct);
            else if (company.Website is not null)
                await CrawlWebsiteAsync(company, research, ct);

            // 2. IndiaMART by slug guessing.
            if (!research.Facts.Any(f => f.ExtractedBy == "IndiaMart"))
                await GuessIndiaMartAsync(company, match, research, ct);
        }
        catch (ScrapeBlockedException ex)
        {
            research.SearchSkipped = true;
            logger.LogWarning("IndiaMART skipped for {Company}: {Reason}", company.Name, ex.Message);
        }

        // 2b. Facebook business page: intro phone/email/website + follower count.
        if (facebook is not null)
            await ReadFacebookAsync(company, match, facebook, research, ct);

        // 3. LinkedIn people search (connected account).
        if (linkedIn is not null)
            await SearchLinkedInAsync(company, match, linkedIn, research, ct);

        // 4. General web search, only while we still have nobody.
        if (research.People.Count == 0)
        {
            try
            {
                var results = await RunSearchAsync($"\"{company.Name}\" {company.City}", company.CountryIso2, research, ct);
                await UseSearchResultsAsync(company, match, results, research, ct);
            }
            catch (ScrapeBlockedException ex)
            {
                research.SearchSkipped = true;
                logger.LogDebug("Web search skipped for {Company}: {Reason}", company.Name, ex.Message);
            }
        }

        await ValidateEmailsAsync(research, ct);
        Deduplicate(research);
        logger.LogInformation("People research {Company}: {People} people, {Channels} channels, {Socials} socials, {Facts} facts ({Pages} pages, {Searches} searches)",
            company.Name, research.People.Count, research.Channels.Count, research.Socials.Count, research.Facts.Count, research.PagesVisited, research.SearchesRun);
        return research;
    }

    private async Task<IReadOnlyList<SearchResult>> RunSearchAsync(string query, string countryIso2, CompanyResearch research, CancellationToken ct)
    {
        research.SearchesRun++;
        return await search.SearchAsync(query, countryIso2, ct);
    }

    // ---------------- Website ----------------

    private async Task CrawlWebsiteAsync(CompanyToResearch company, CompanyResearch research, CancellationToken ct)
    {
        var queue = new Queue<string>([company.Website!]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0 && seen.Count < MaxWebsitePages)
        {
            var url = queue.Dequeue();
            if (!seen.Add(url.TrimEnd('/')))
                continue;

            var page = await fetcher.GetAsync(url, ct);
            if (page is null)
                continue;
            research.PagesVisited++;

            var data = WebsiteExtractor.Extract(page.Html, page.FinalUrl);
            foreach (var link in data.ContactPageLinks)
                queue.Enqueue(link);

            foreach (var email in data.Emails)
                research.Channels.Add(new ChannelFinding("Email", email, email, null, null, null, page.FinalUrl));

            foreach (var raw in data.TelLinks)
                if (PhoneNormalizer.Normalize(raw, company.CountryIso2) is { IsValid: true } p)
                    research.Channels.Add(new ChannelFinding("Phone", raw, p.E164, p.Kind, true, "tel: link on website", page.FinalUrl));

            foreach (var p in PhoneExtractor.Find(data.VisibleText, company.CountryIso2))
                research.Channels.Add(new ChannelFinding("Phone", p.E164, p.E164, p.Kind, true, "Found in website text", page.FinalUrl));

            foreach (var social in data.SocialLinks)
                if (SocialPlatformOf(social) is { } platform && !social.Contains("/in/", StringComparison.OrdinalIgnoreCase))
                    research.Socials.Add(new SocialFinding(platform, social));

            foreach (var mention in data.RoleMentions)
            {
                research.People.Add(new PersonFinding
                {
                    FullName = mention.Name,
                    Designation = mention.Role,
                    IsOwner = IsOwnerRole(mention.Role),
                    IsDecisionMaker = IsDecisionRole(mention.Role),
                    SourceUrl = page.FinalUrl,
                    Source = "Website",
                });
            }
        }
    }

    // ---------------- Search results ----------------

    private async Task UseSearchResultsAsync(CompanyToResearch company, CompanyMatcher match, IReadOnlyList<SearchResult> results, CompanyResearch research, CancellationToken ct)
    {
        var indiaMartDone = research.Facts.Any(f => f.ExtractedBy == "IndiaMart");

        foreach (var r in results)
        {
            if (LinkedInResultParser.Parse(r.Url, r.Title, r.Snippet) is { } person)
            {
                // Only keep people whose title/snippet names this company (avoids namesakes from other firms).
                if (match.Matches(person.MatchText))
                {
                    research.People.Add(new PersonFinding
                    {
                        FullName = person.FullName,
                        Designation = person.Designation,
                        IsOwner = IsOwnerRole(person.Designation),
                        IsDecisionMaker = IsDecisionRole(person.Designation),
                        LinkedInUrl = person.ProfileUrl,
                        SourceUrl = person.ProfileUrl,
                        Source = "LinkedInSearch",
                    });
                }
                continue;
            }

            if (!indiaMartDone && IndiaMartParser.ToProfileUrl(r.Url) is { } profileUrl && match.NameMatches($"{r.Title} {r.Snippet}"))
            {
                indiaMartDone = await ReadIndiaMartAsync(company, match, profileUrl, research, ct);
                continue;
            }

            if (SocialPlatformOf(r.Url) is { } platform && !LinkedInResultParser.IsProfile(r.Url) && match.Matches(r.Title))
                research.Socials.Add(new SocialFinding(platform, CleanSocialUrl(r.Url)));
        }
    }

    // ---------------- IndiaMART ----------------

    private async Task GuessIndiaMartAsync(CompanyToResearch company, CompanyMatcher match, CompanyResearch research, CancellationToken ct)
    {
        foreach (var url in IndiaMartClient.GuessProfileUrls(company.Name, company.City))
        {
            if (await ReadIndiaMartAsync(company, match, url, research, ct))
                return;
        }
    }

    /// <summary>Reads an IndiaMART seller profile; true only when it is verifiably this company (name + city).</summary>
    private async Task<bool> ReadIndiaMartAsync(CompanyToResearch company, CompanyMatcher match, string profileUrl, CompanyResearch research, CancellationToken ct)
    {
        var page = await indiaMart.GetProfileAsync(profileUrl, ct);
        if (page is null)
            return false;
        research.PagesVisited++;
        profileUrl = page.Value.Url;   // sellers with custom pages redirect, e.g. to /aboutus.html

        var profile = IndiaMartParser.Parse(page.Value.Html);
        // Same company? Name must match, and the city too when IndiaMART states one.
        if (profile.CompanyName is null || !match.NameMatches(profile.CompanyName)
            || (profile.City is not null && !profile.City.Contains(company.City, StringComparison.OrdinalIgnoreCase)
                                         && !company.City.Contains(profile.City, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogDebug("IndiaMART profile {Url} is a different company ({Name}, {City})", profileUrl, profile.CompanyName, profile.City);
            return false;
        }

        void Fact(string field, string? value, string label)
        {
            if (!string.IsNullOrWhiteSpace(value))
                research.Facts.Add(new FactFinding(field, value, profileUrl, $"{label}: {value}", "IndiaMart"));
        }

        Fact(FactFields.TeamSize, profile.Employees, "Total Number of Employees");
        Fact(FactFields.Turnover, profile.AnnualTurnover, "Annual Turnover");
        Fact(FactFields.LegalStatus, profile.LegalStatus, "Legal Status of Firm");
        Fact(FactFields.Gstin, profile.Gstin, "GST No.");
        Fact(FactFields.NatureOfBusiness, profile.NatureOfBusiness, "Nature of Business");
        Fact(FactFields.YearEstablished, profile.YearEstablished, "Year of Establishment");

        if (profile.CeoName is { } ceo)
        {
            Fact(FactFields.OwnerName, ceo, profile.CeoRole ?? "Company CEO");
            var isProprietorship = profile.LegalStatus?.Contains("Proprietor", StringComparison.OrdinalIgnoreCase) == true;
            research.People.Add(new PersonFinding
            {
                FullName = ceo,
                Designation = profile.CeoRole ?? (isProprietorship ? "Proprietor (CEO)" : "CEO"),
                IsOwner = true,
                IsDecisionMaker = true,
                SourceUrl = profileUrl,
                Source = "IndiaMart",
            });
        }

        if (profile.ForwardingNumber is { } pns && PhoneNormalizer.Normalize(pns, company.CountryIso2) is { } phone)
            research.Channels.Add(new ChannelFinding("Phone", pns, phone.E164, "Virtual", phone.IsValid, "IndiaMART call-forwarding number", profileUrl));

        return true;
    }

    // ---------------- Facebook page ----------------

    private async Task ReadFacebookAsync(CompanyToResearch company, CompanyMatcher match, IFacebookLookup facebook, CompanyResearch research, CancellationToken ct)
    {
        // Pages linked from the website / Maps are trusted; guessed addresses must show this company's name.
        var linked = research.Socials.Where(s => s.Platform == "Facebook").Select(s => s.Url)
            .Concat(company.FacebookUrl is { } known ? [known] : [])
            .Where(u => !Regex.IsMatch(u, @"/(groups|posts|events|photos|watch|sharer)/", RegexOptions.IgnoreCase))
            .Select(u => (Url: u, Trusted: true));
        var guessed = FacebookPageReader.GuessPageUrls(company.Name, company.City).Select(u => (Url: u, Trusted: false));

        foreach (var (url, trusted) in linked.Concat(guessed).DistinctBy(c => c.Url.TrimEnd('/').ToLowerInvariant()).Take(3))
        {
            var result = await facebook.ReadPageAsync(url, ct);
            if (result is null)
                return;   // Facebook unavailable for this run (login wall)
            var (outcome, page) = result.Value;
            research.PagesVisited++;
            if (outcome != FacebookReadOutcome.Ok || page is null)
                continue;
            if (!trusted && !match.NameMatches(page.Name))
                continue;

            var pageUrl = Regex.Replace(page.Url, @"[?&]locale=[^&]*", "").TrimEnd('?', '/');
            research.Socials.Add(new SocialFinding("Facebook", pageUrl));

            foreach (var p in PhoneExtractor.Find(page.IntroText, company.CountryIso2))
                research.Channels.Add(new ChannelFinding("Phone", p.E164, p.E164, p.Kind, true, "Facebook page intro", pageUrl));
            foreach (var email in WebsiteExtractor.FindEmails(page.IntroText))
                research.Channels.Add(new ChannelFinding("Email", email, email, null, null, null, pageUrl));
            foreach (var link in page.ExternalLinks)
                if (SocialPlatformOf(link) is { } platform and not "Facebook")
                    research.Socials.Add(new SocialFinding(platform, CleanSocialUrl(link)));
            if (page.Followers is { } followers)
                research.Facts.Add(new FactFinding(FactFields.FacebookFollowers, followers.ToString(), pageUrl, $"{followers} followers", "Facebook"));
            return;
        }
    }

    // ---------------- LinkedIn (connected account) ----------------

    private async Task SearchLinkedInAsync(CompanyToResearch company, CompanyMatcher match, ILinkedInLookup linkedIn, CompanyResearch research, CancellationToken ct)
    {
        // Company name only: LinkedIn people search matches it against current-company/headline;
        // the card's location line ("Indore, Madhya Pradesh") then satisfies the city check for generic names.
        var keywords = company.Name.Split('|', '–', '—')[0].Trim();
        var people = await linkedIn.SearchPeopleAsync(keywords, ct);
        if (people is null)
            return;
        research.SearchesRun++;

        foreach (var p in people)
        {
            // Keep only people whose card names this company (avoids namesakes and other firms).
            if (!match.Matches($"{p.Headline} {p.CardText}"))
                continue;
            var designation = LinkedInPeopleSearch.DesignationFromHeadline(p.Headline);
            research.People.Add(new PersonFinding
            {
                FullName = p.FullName,
                Designation = designation,
                IsOwner = IsOwnerRole(designation),
                IsDecisionMaker = IsDecisionRole(designation),
                LinkedInUrl = p.ProfileUrl,
                SourceUrl = p.ProfileUrl,
                Source = "LinkedIn",
            });
        }
    }

    // ---------------- Helpers ----------------

    private async Task ValidateEmailsAsync(CompanyResearch research, CancellationToken ct)
    {
        for (var i = 0; i < research.Channels.Count; i++)
        {
            var c = research.Channels[i];
            if (c.Type != "Email" || c.IsValid is not null)
                continue;
            var check = await emails.CheckAsync(c.NormalizedValue, ct);
            research.Channels[i] = c with { IsValid = check.IsValid, Note = check.Note };
        }
    }

    /// <summary>Same person from several sources → one entry, keeping the most complete fields.</summary>
    private static void Deduplicate(CompanyResearch research)
    {
        var merged = research.People
            .GroupBy(p => CompanyText.NormalizeName(StripHonorific(p.FullName)))
            .Select(g => g.Aggregate((a, b) => a with
            {
                Designation = a.Designation ?? b.Designation,
                IsOwner = a.IsOwner || b.IsOwner,
                IsDecisionMaker = a.IsDecisionMaker || b.IsDecisionMaker,
                LinkedInUrl = a.LinkedInUrl ?? b.LinkedInUrl,
                FacebookUrl = a.FacebookUrl ?? b.FacebookUrl,
                InstagramUrl = a.InstagramUrl ?? b.InstagramUrl,
                Phone = a.Phone ?? b.Phone,
                Email = a.Email ?? b.Email,
                AlsoSeenAt = a.AlsoSeenAt.Append(b.SourceUrl).Concat(b.AlsoSeenAt).Where(u => u != a.SourceUrl).Distinct().ToList(),
            }))
            .ToList();
        research.People.Clear();
        research.People.AddRange(merged);

        var channels = research.Channels
            .GroupBy(c => (c.Type, c.NormalizedValue))
            .Select(g => g.First() with
            {
                AlsoSeenAt = g.Skip(1).Select(c => c.SourceUrl).Where(u => u != g.First().SourceUrl).Distinct().ToList(),
            })
            .ToList();
        research.Channels.Clear();
        research.Channels.AddRange(channels);

        var socials = research.Socials.DistinctBy(s => s.Url.ToLowerInvariant()).ToList();
        research.Socials.Clear();
        research.Socials.AddRange(socials);
    }

    public static string StripHonorific(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"^(Mr|Mrs|Ms|Dr|Shri|Smt|Er)\.?\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    private static bool IsOwnerRole(string? role) =>
        role is not null && OwnerRoles.Any(r => role.Contains(r, StringComparison.OrdinalIgnoreCase)) || role?.Trim().Equals("MD", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDecisionRole(string? role) =>
        IsOwnerRole(role) || (role is not null && DecisionRoles.Any(r => System.Text.RegularExpressions.Regex.IsMatch(role, $@"\b{r}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));

    private static string? SocialPlatformOf(string url) => WebsiteClassifier.GetSocialPlatform(url)?.ToString();

    private static string CleanSocialUrl(string url) =>
        Uri.TryCreate(WebsiteClassifier.StripTracking(url), UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Path).TrimEnd('/') : url;
}

/// <summary>Decides whether a piece of text is about this company (normalized name contained; generic short names also need the city).</summary>
public sealed class CompanyMatcher(string companyName, string city)
{
    private readonly string _name = CompanyText.NormalizeName(companyName);
    private readonly string _city = CompanyText.NormalizeName(city);
    private readonly bool _generic = CompanyText.NormalizeName(companyName).Split(' ').Length <= 2;

    public bool Matches(string text)
    {
        if (!NameMatches(text))
            return false;
        var t = CompanyText.NormalizeName(text);
        return !_generic || t.Contains(_city, StringComparison.Ordinal) || _name.Split(' ').Length == 2 && _name.Length >= 12;
    }

    /// <summary>Name only – for pages whose location is checked separately (e.g. IndiaMART profile city).</summary>
    public bool NameMatches(string text)
    {
        var t = CompanyText.NormalizeName(text);
        return _name.Length >= 3 && $" {t} ".Contains($" {_name} ", StringComparison.Ordinal);
    }
}
