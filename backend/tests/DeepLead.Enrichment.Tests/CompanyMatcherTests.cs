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

    [Fact]
    public void Extractor_FindsNumbersInText()
    {
        var found = PhoneExtractor.Find("Call us: 72259 93533 or office 0731-4047882. GST 23ABNPB6331M1ZO", "IN");
        Assert.Contains(found, p => p.E164 == "+917225993533");
        Assert.Contains(found, p => p.E164 == "+917314047882");
    }
}
