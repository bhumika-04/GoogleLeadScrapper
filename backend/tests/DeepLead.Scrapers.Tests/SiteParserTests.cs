using DeepLead.Scrapers.Search;
using DeepLead.Scrapers.Sites;

namespace DeepLead.Scrapers.Tests;

public class SiteParserTests
{
    [Fact]
    public void WebsiteExtractor_DoesNotGlueEmailToNeighbouringText()
    {
        const string html = "<html><body><div><span>ussinghphotocopy@gmail.com</span><span>spsingh</span></div></body></html>";
        var data = WebsiteExtractor.Extract(html, "https://singhphotocopy.in/");
        Assert.Equal(["ussinghphotocopy@gmail.com"], data.Emails);
    }

    [Theory]
    [InlineData("info@yoursite.com")]
    [InlineData("yourname@gmail.com")]
    [InlineData("contact@example.com")]
    public void FindEmails_SkipsTemplatePlaceholders(string email) => Assert.Empty(WebsiteExtractor.FindEmails($"Mail us: {email}"));

    [Theory]
    [InlineData("https://www.facebook.com/WCPrints/", "https://facebook.com/wcprints")]
    [InlineData("https://m.facebook.com/wcprints?ref=page", "https://facebook.com/wcprints")]
    [InlineData("https://www.facebook.com/profile.php?id=1000123&sk=about", "https://facebook.com/profile.php?id=1000123")]
    [InlineData("https://in.linkedin.com/company/shrinathpapers/", "https://linkedin.com/company/shrinathpapers")]
    [InlineData("instagram.com/jai_ma_graphics?igsh=xyz", "https://instagram.com/jai_ma_graphics")]
    public void NormalizeSocialUrl_OneFormPerProfile(string url, string expected) =>
        Assert.Equal(expected, DeepLead.Core.Text.WebsiteClassifier.NormalizeSocialUrl(url));

    [Fact]
    public void WebsiteExtractor_ReadsMailtoTelAndSocialLinks()
    {
        const string html = """
            <a href="mailto:Info@Rangpatra.com?subject=Hi">Mail</a>
            <a href="tel:+91 72259 93533">Call</a>
            <a href="https://www.facebook.com/rangpatra/">fb</a>
            <a href="https://www.facebook.com/sharer/sharer.php?u=x">share</a>
            <a href="/contact-us">Contact</a>
            """;
        var data = WebsiteExtractor.Extract(html, "https://www.rangpatra.com/");
        Assert.Contains("info@rangpatra.com", data.Emails);
        Assert.Contains("+91 72259 93533", data.TelLinks);
        Assert.Equal(["https://www.facebook.com/rangpatra"], data.SocialLinks);
        Assert.Contains("https://www.rangpatra.com/contact-us", data.ContactPageLinks);
    }

    [Theory]
    [InlineData("Proprietor : Mr. Ramesh Jain", "Mr. Ramesh Jain", "Proprietor")]
    [InlineData("Ankit Sharma (Managing Director)", "Ankit Sharma", "Managing Director")]
    [InlineData("Priya Verma - Founder", "Priya Verma", "Founder")]
    public void FindRoleMentions_FindsNameAndRole(string text, string name, string role)
    {
        var mention = Assert.Single(WebsiteExtractor.FindRoleMentions(text));
        Assert.Equal(name, mention.Name);
        Assert.Equal(role, mention.Role);
    }

    [Fact]
    public void FindRoleMentions_JoinsLabelAndNameFromSeparateElements()
    {
        var data = WebsiteExtractor.Extract("<p><b>Proprietor:</b><span>Ramesh Jain</span></p>", "https://x.in/");
        Assert.Contains(data.RoleMentions, m => m.Name == "Ramesh Jain" && m.Role == "Proprietor");
    }

    [Fact]
    public void FindRoleMentions_NameDoesNotCrossLineBreak()
    {
        var data = WebsiteExtractor.Extract("<div><a>Contact Us</a><p>H. Badri - Founder</p></div>", "https://x.in/");
        var mention = Assert.Single(data.RoleMentions);
        Assert.Equal("H. Badri", mention.Name);
    }

    [Theory]
    [InlineData("Proprietor: H Barodawala", "H Barodawala")]
    [InlineData("Rahul K. Mehta (Director)", "Rahul K. Mehta")]
    public void FindRoleMentions_AcceptsInitials(string text, string name) =>
        Assert.Equal(name, Assert.Single(WebsiteExtractor.FindRoleMentions(text)).Name);

    [Fact]
    public void FindRoleMentions_IgnoresMenuText() =>
        Assert.Empty(WebsiteExtractor.FindRoleMentions("Contact Us - Founder"));

    [Fact]
    public void IndiaMart_ParsesFactsheetAndJsonLd()
    {
        const string html = """
            <script type="application/ld+json">[{"@context":"https://schema.org","@type":"Organization","name":"Burhani Offset Printers",
              "address":{"@type":"PostalAddress","addressLocality":"Indore"},"foundingDate":"1997"}]</script>
            <div class="factsheet-table__row"><dt class="factsheet-table__label">Company CEO</dt><dd class="factsheet-table__value"><span>H Barodawala</span></dd></div>
            <div class="factsheet-table__row"><dt class="factsheet-table__label">Total Number of Employees</dt><dd class="factsheet-table__value"><span>Upto 10 People</span></dd></div>
            <div class="factsheet-table__row"><dt class="factsheet-table__label">Legal Status of Firm</dt><dd class="factsheet-table__value"><span>Proprietorship</span></dd></div>
            <div class="factsheet-table__row"><dt class="factsheet-table__label">GST No.</dt><dd class="factsheet-table__value"><span>23ABNPB6331M1ZO</span></dd></div>
            <script>var x = {"pnsNumber":"+91-8047309871"};</script>
            """;
        var p = IndiaMartParser.Parse(html);
        Assert.Equal("Burhani Offset Printers", p.CompanyName);
        Assert.Equal("H Barodawala", p.CeoName);
        Assert.Equal("Upto 10 People", p.Employees);
        Assert.Equal("Proprietorship", p.LegalStatus);
        Assert.Equal("23ABNPB6331M1ZO", p.Gstin);
        Assert.Equal("1997", p.YearEstablished);
        Assert.Equal("Indore", p.City);
        Assert.Equal("+91-8047309871", p.ForwardingNumber);
    }

    [Fact]
    public void IndiaMart_ReadsOwnerFromVerifiedSupplierBlock()
    {
        const string html = """
            <div data-props="{&quot;gstNumber&quot;:&quot;23BYWPP1952R1ZE&quot;,&quot;directorProprietor&quot;:&quot;Rinku Sharma (Owner)&quot;,&quot;sellerPns&quot;:&quot;+91-8047822032&quot;}"></div>
            """;
        var p = IndiaMartParser.Parse(html);
        Assert.Equal("Rinku Sharma", p.CeoName);
        Assert.Equal("Owner", p.CeoRole);
        Assert.Equal("+91-8047822032", p.ForwardingNumber);
    }

    [Theory]
    [InlineData("Jai Ma Graphics", "Indore", "jai-ma-graphics", "jaimagraphics")]
    [InlineData("Aadinath Print O Pack – packaging printing | offset printing", "Indore", "aadinath-print-o-pack", "aadinathprintopack")]
    [InlineData("Shree Graphics & Printing Press", "Indore", "shree-graphics-and-printing-press", "shree-graphics-and-printing-press-indore")]
    [InlineData("Saraswati Printing Press, Indore", "Indore", "saraswati-printing-press", "saraswatiprintingpress")]
    public void IndiaMart_GuessesSlugs(string name, string city, string first, string second)
    {
        var urls = IndiaMartClient.GuessProfileUrls(name, city);
        Assert.Equal($"https://www.indiamart.com/{first}/profile.html", urls[0]);
        Assert.Equal($"https://www.indiamart.com/{second}/profile.html", urls[1]);
    }

    [Theory]
    [InlineData("Owner at Jai Ma Graphics", "Owner")]
    [InlineData("Managing Director @ Allied Paper | Print solutions", "Managing Director")]
    [InlineData("Printing & packaging entrepreneur", "Printing & packaging entrepreneur")]
    public void LinkedIn_DesignationFromHeadline(string headline, string expected) =>
        Assert.Equal(expected, LinkedInPeopleSearch.DesignationFromHeadline(headline));

    [Fact]
    public void LinkedIn_CardLinesSkipNoise()
    {
        var p = LinkedInPeopleSearch.ToPerson("https://www.linkedin.com/in/rinku-sharma", "Rinku Sharma",
            ["Rinku Sharma", "View Rinku Sharma’s profile", "• 2nd", "2nd degree connection", "Owner at Jai Ma Graphics", "Indore, Madhya Pradesh, India", "Connect"]);
        Assert.Equal("Owner at Jai Ma Graphics", p.Headline);
        Assert.Equal("Indore, Madhya Pradesh, India", p.Location);
    }

    [Theory]
    [InlineData("https://www.indiamart.com/burhani-offset-printers/", "https://www.indiamart.com/burhani-offset-printers/profile.html")]
    [InlineData("https://www.indiamart.com/burhani-offset-printers/calendar-printing.html", "https://www.indiamart.com/burhani-offset-printers/profile.html")]
    [InlineData("https://www.indiamart.com/proddetail/box-123.html", null)]
    [InlineData("https://dir.indiamart.com/indore/printers.html", null)]
    public void IndiaMart_ProfileUrl(string url, string? expected) => Assert.Equal(expected, IndiaMartParser.ToProfileUrl(url));

    [Fact]
    public void LinkedIn_ParsesNameDesignationFromTitle()
    {
        var hit = LinkedInResultParser.Parse("https://in.linkedin.com/in/rahul-sharma-12ab?trk=x",
            "Rahul Sharma - Owner - Jai Ma Graphics | LinkedIn", "Indore · Owner at Jai Ma Graphics");
        Assert.NotNull(hit);
        Assert.Equal("Rahul Sharma", hit.FullName);
        Assert.Equal("Owner", hit.Designation);
        Assert.Equal("https://www.linkedin.com/in/rahul-sharma-12ab", hit.ProfileUrl);
    }

    [Fact]
    public void LinkedIn_FallsBackToSnippetForDesignation()
    {
        var hit = LinkedInResultParser.Parse("https://www.linkedin.com/in/priya-v", "Priya Verma – Jai Ma Graphics | LinkedIn",
            "Managing Partner at Jai Ma Graphics · Experience");
        Assert.Equal("Managing Partner", hit?.Designation);
    }

    [Fact]
    public void LinkedIn_IgnoresCompanyPages() =>
        Assert.Null(LinkedInResultParser.Parse("https://www.linkedin.com/company/jai-ma-graphics", "Jai Ma Graphics | LinkedIn", ""));

    [Fact]
    public void Facebook_ParsesIntroAndFollowers()
    {
        const string text = "Log in\nIndiaMART\n340K followers • 10 following\nPosts\nAbout\nIntro\nIndia's largest online B2B marketplace.\nPage · App page\n+91 96969 69696\nmarketing@indiamart.com\nindiamart.com\nSee all photos\nSome post text 0731-4047882";
        var (outcome, page) = FacebookPageReader.Parse("https://www.facebook.com/IndiaMART/", "IndiaMART | Facebook", text, ["https://www.youtube.com/@indiamart"]);
        Assert.Equal(FacebookReadOutcome.Ok, outcome);
        Assert.Equal("IndiaMART", page!.Name);
        Assert.Equal(340_000, page.Followers);
        Assert.Contains("+91 96969 69696", page.IntroText);
        Assert.DoesNotContain("0731-4047882", page.IntroText);   // post text below the Intro block is excluded
    }

    [Fact]
    public void Facebook_DetectsMissingPage() =>
        Assert.Equal(FacebookReadOutcome.NotFound,
            FacebookPageReader.Parse("https://www.facebook.com/x", "Facebook", "This content isn't available right now", []).Item1);

    [Theory]
    [InlineData("1,234 followers", 1234)]
    [InlineData("2.5K followers", 2500)]
    [InlineData("1.2M likes", 1_200_000)]
    public void Facebook_ParsesFollowerCounts(string text, int expected) => Assert.Equal(expected, FacebookPageReader.ParseFollowers(text));

    [Fact]
    public void Facebook_GuessesPageUrls() =>
        Assert.Equal(["https://www.facebook.com/jaimagraphics", "https://www.facebook.com/jaimagraphicsindore"],
            FacebookPageReader.GuessPageUrls("Jai Ma Graphics", "Indore"));

    [Fact]
    public void DuckDuckGo_ParsesResultsAndUnwrapsRedirects()
    {
        const string html = """
            <div class="result results_links"><a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.indiamart.com%2Fburhani-offset-printers%2F&amp;rut=abc">Burhani Offset Printers, Indore</a>
            <a class="result__snippet">About Us At Mechanic Nagar, Indore</a></div>
            <div class="result result--ad"><a class="result__a" href="https://ads.example.com">Ad</a></div>
            """;
        var r = Assert.Single(DuckDuckGoSearch.Parse(html));
        Assert.Equal("https://www.indiamart.com/burhani-offset-printers/", r.Url);
        Assert.Equal("Burhani Offset Printers, Indore", r.Title);
        Assert.Contains("Mechanic Nagar", r.Snippet);
    }
}
