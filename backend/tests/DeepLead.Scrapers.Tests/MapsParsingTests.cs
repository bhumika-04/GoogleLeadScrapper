using DeepLead.Scrapers.Maps;

namespace DeepLead.Scrapers.Tests;

public class MapsParsingTests
{
    private const string PlaceUrl =
        "https://www.google.com/maps/place/Design+Company/data=!4m7!3m6!1s0x3962fd0d7305901d:0xe3243977dbe693d3!8m2!3d22.7217263!4d75.8634451!16s%2Fg%2F11c1q?authuser=0&hl=en";

    [Fact]
    public void ParseCoordinates_ReadsLatLngFromPlaceUrl()
    {
        var (lat, lng) = MapsParsing.ParseCoordinates(PlaceUrl);
        Assert.Equal(22.721726m, lat);
        Assert.Equal(75.863445m, lng);
    }

    [Fact]
    public void ParseCoordinates_ReturnsNullWhenAbsent() =>
        Assert.Equal((null, null), MapsParsing.ParseCoordinates("https://www.google.com/maps/search/printers"));

    [Fact]
    public void ParsePlaceId_ReadsFeatureId() =>
        Assert.Equal("0x3962fd0d7305901d:0xe3243977dbe693d3", MapsParsing.ParsePlaceId(PlaceUrl));

    [Theory]
    [InlineData("1,558 reviews", 1558)]
    [InlineData("(77)", 77)]
    [InlineData("1 review", 1)]
    public void ParseReviewCount_HandlesLabels(string label, int expected) =>
        Assert.Equal(expected, MapsParsing.ParseReviewCount(label));

    [Theory]
    [InlineData("4.8", 4.8)]
    [InlineData(" 5.0 ", 5.0)]
    public void ParseRating_ParsesValidValues(string text, double expected) =>
        Assert.Equal((decimal)expected, MapsParsing.ParseRating(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7.5")]
    public void ParseRating_RejectsInvalid(string? text) => Assert.Null(MapsParsing.ParseRating(text));

    [Fact]
    public void ParsePhoneFromItemId_StripsPrefix() =>
        Assert.Equal("09685251186", MapsParsing.ParsePhoneFromItemId("phone:tel:09685251186"));

    [Fact]
    public void StripLabelPrefix_RemovesAddressLabel() =>
        Assert.Equal("583, Mahatma Gandhi Rd, Indore", MapsParsing.StripLabelPrefix("Address: 583, Mahatma Gandhi Rd, Indore"));

    [Fact]
    public void CleanWebsite_UnwrapsGoogleRedirect() =>
        Assert.Equal("https://jaimagraphics.com/", MapsParsing.CleanWebsite("https://www.google.com/url?q=https://jaimagraphics.com/&opi=1"));

    [Fact]
    public void BuildSearchUrl_EncodesQueryAndSetsLocale() =>
        Assert.Equal("https://www.google.com/maps/search/Printing%20Companies%20in%20Indore?hl=en&gl=in",
            MapsParsing.BuildSearchUrl("Printing Companies in Indore", "IN", "en"));
}
