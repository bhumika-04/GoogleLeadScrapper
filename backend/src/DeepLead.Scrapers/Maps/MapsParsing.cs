using System.Globalization;
using System.Text.RegularExpressions;

namespace DeepLead.Scrapers.Maps;

/// <summary>Pure parsing helpers for Google Maps URLs and labels (kept separate so they can be unit-tested).</summary>
public static partial class MapsParsing
{
    [GeneratedRegex(@"!3d(-?\d+(?:\.\d+)?)!4d(-?\d+(?:\.\d+)?)")]
    private static partial Regex CoordinatesRegex();

    [GeneratedRegex(@"!1s(0x[0-9a-f]+:0x[0-9a-f]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceIdRegex();

    [GeneratedRegex(@"\d[\d,\.]*")]
    private static partial Regex NumberRegex();

    public static (decimal? Latitude, decimal? Longitude) ParseCoordinates(string url)
    {
        var match = CoordinatesRegex().Match(url);
        if (!match.Success)
            return (null, null);

        return (Math.Round(decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), 6),
                Math.Round(decimal.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), 6));
    }

    public static string? ParsePlaceId(string url)
    {
        var match = PlaceIdRegex().Match(Uri.UnescapeDataString(url));
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>"4.5" -> 4.5. Expects English labels (we always request hl=en).</summary>
    public static decimal? ParseRating(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 5
            ? value
            : null;
    }

    /// <summary>"1,234 reviews", "(1,234)", "1 review" -> 1234 / 1.</summary>
    public static int? ParseReviewCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = NumberRegex().Match(text);
        if (!match.Success)
            return null;
        var digits = match.Value.Replace(",", string.Empty).Replace(".", string.Empty);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>Button data-item-id "phone:tel:+917312345678" -> "+917312345678".</summary>
    public static string? ParsePhoneFromItemId(string? dataItemId)
    {
        const string prefix = "phone:tel:";
        return dataItemId is not null && dataItemId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? dataItemId[prefix.Length..].Trim()
            : null;
    }

    /// <summary>aria-label "Address: 12, MG Road, Indore" -> "12, MG Road, Indore".</summary>
    public static string? StripLabelPrefix(string? ariaLabel)
    {
        if (string.IsNullOrWhiteSpace(ariaLabel))
            return null;
        var colon = ariaLabel.IndexOf(':');
        return (colon >= 0 ? ariaLabel[(colon + 1)..] : ariaLabel).Trim();
    }

    /// <summary>Google sometimes wraps outbound links: /url?q=https://site.com&amp;... -> https://site.com</summary>
    public static string? CleanWebsite(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/url")
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["q"] ?? query["url"] ?? href;
        }
        return href;
    }

    public static string BuildSearchUrl(string query, string countryIso2, string languageCode) =>
        $"https://www.google.com/maps/search/{Uri.EscapeDataString(query)}?hl={languageCode}&gl={countryIso2.ToLowerInvariant()}";

    /// <summary>Forces English labels on a place URL so parsing stays stable.</summary>
    public static string WithLanguage(string placeUrl, string languageCode)
    {
        var separator = placeUrl.Contains('?') ? '&' : '?';
        return placeUrl.Contains("hl=", StringComparison.Ordinal) ? placeUrl : $"{placeUrl}{separator}hl={languageCode}";
    }
}
