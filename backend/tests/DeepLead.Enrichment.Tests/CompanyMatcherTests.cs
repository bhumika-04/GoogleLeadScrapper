using DeepLead.Enrichment.People;
using DeepLead.Enrichment.Validation;

namespace DeepLead.Enrichment.Tests;

public class CompanyMatcherTests
{
    [Fact]
    public void DistinctiveName_MatchesWithoutCity() =>
        Assert.True(new CompanyMatcher("Burhani Offset Printers", "Indore").Matches("Burhani Offset Printers - Manufacturer from India"));

    [Fact]
    public void GenericName_NeedsCity()
    {
        var m = new CompanyMatcher("Design Company", "Indore");
        Assert.False(m.Matches("Design Company | LinkedIn"));
        Assert.True(m.Matches("Design Company, Indore - Offset printing"));
    }

    [Fact]
    public void DifferentCompany_DoesNotMatch() =>
        Assert.False(new CompanyMatcher("Jai Ma Graphics", "Indore").Matches("Jai Steel International Services pvt ltd Indore"));

    [Fact]
    public void LegalSuffixAndPunctuation_AreIgnored() =>
        Assert.True(new CompanyMatcher("Allied Paper and Print Solution Pvt. Ltd.", "Indore").NameMatches("ALLIED PAPER AND PRINT SOLUTION"));
}

public class SocialPickTests
{
    [Fact]
    public void KeepsProfilesMatchingTheCompany()
    {
        var tokens = PeopleDiscovery.NameTokens("WC Prints", "https://wcprints.in/");
        var picked = PeopleDiscovery.PickCompanyProfiles(
            [new("Instagram", "https://instagram.com/smifslimited"), new("Instagram", "https://instagram.com/wc_prints")], tokens).ToList();
        Assert.Equal("https://instagram.com/wc_prints", Assert.Single(picked).Url);
    }

    [Fact]
    public void KeepsFirstWhenNothingMatches()
    {
        var picked = PeopleDiscovery.PickCompanyProfiles(
            [new("Facebook", "https://facebook.com/a"), new("Facebook", "https://facebook.com/b")], ["zzzz"]).ToList();
        Assert.Equal("https://facebook.com/a", Assert.Single(picked).Url);
    }

    [Theory]
    [InlineData("https://instagram.com/reel/ddellg2al1u", false)]
    [InlineData("https://instagram.com/bahetiprintworld", true)]
    [InlineData("https://facebook.com/profile.php", false)]
    [InlineData("https://facebook.com/61561314508583", true)]
    [InlineData("https://youtube.com/watch", false)]
    [InlineData("https://youtube.com/@indiamart", true)]
    public void ProfileUrls(string url, bool profile) => Assert.Equal(profile, DeepLead.Core.Text.WebsiteClassifier.IsProfileUrl(url));

    [Fact]
    public void FacebookPageIdForms_Collapse() =>
        Assert.Equal("https://facebook.com/61561314508583",
            DeepLead.Core.Text.WebsiteClassifier.NormalizeSocialUrl("https://www.facebook.com/p/Mahakal-Flex-Printing-Bhamori-61561314508583/"));
}

public class PhoneTests
{
    [Theory]
    [InlineData("09685251186", "+919685251186", "Mobile")]
    [InlineData("0731 404 7882", "+917314047882", "Landline")]
    public void Normalize_IndianNumbers(string raw, string e164, string kind)
    {
        var p = PhoneNormalizer.Normalize(raw, "IN");
        Assert.NotNull(p);
        Assert.Equal(e164, p.E164);
        Assert.Equal(kind, p.Kind);
    }

    [Theory]
    [InlineData("+9118001234567", true)]    // "1800 123 4567" template number
    [InlineData("+919999999999", true)]
    [InlineData("+919876543210", true)]
    [InlineData("+919630959679", false)]
    [InlineData("+917314047882", false)]
    public void Placeholder_Numbers(string e164, bool placeholder) => Assert.Equal(placeholder, PhoneNormalizer.IsPlaceholder(e164));

    [Fact]
    public void Extractor_SkipsTemplateNumbers() =>
        Assert.DoesNotContain(PhoneExtractor.Find("Call 1800 123 4567 or 99999 99999", "IN"), p => true);

    [Fact]
    public void Extractor_FindsNumbersInText()
    {
        var found = PhoneExtractor.Find("Call us: 72259 93533 or office 0731-4047882. GST 23ABNPB6331M1ZO", "IN");
        Assert.Contains(found, p => p.E164 == "+917225993533");
        Assert.Contains(found, p => p.E164 == "+917314047882");
    }
}
