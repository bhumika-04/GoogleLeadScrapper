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
