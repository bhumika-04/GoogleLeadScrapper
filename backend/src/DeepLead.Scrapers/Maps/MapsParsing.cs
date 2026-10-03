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

    /// <summary>Search centred on a point: /maps/search/{q}/@lat,lng,{zoom}z – results come from that viewport.</summary>
    public static string BuildAreaSearchUrl(string query, decimal latitude, decimal longitude, int zoom, string countryIso2, string languageCode) =>
        string.Create(CultureInfo.InvariantCulture,
            $"https://www.google.com/maps/search/{Uri.EscapeDataString(query)}/@{latitude:0.######},{longitude:0.######},{zoom}z?hl={languageCode}&gl={countryIso2.ToLowerInvariant()}");

    /// <summary>
    /// Grid of map centres covering a city: size×size points, stepKm apart, around the city centre.
    /// Used when a single city search hits Google's ~120-results cap.
    /// </summary>
    public static IReadOnlyList<(decimal Latitude, decimal Longitude)> AreaGrid(decimal centerLat, decimal centerLng, int size, double stepKm)
    {
        var points = new List<(decimal, decimal)>();
        var latStep = stepKm / 110.574;                                              // km per degree of latitude
        var lngStep = stepKm / (111.320 * Math.Cos((double)centerLat * Math.PI / 180)); // shrinks towards the poles
        var half = (size - 1) / 2.0;

        // Centre first, then outwards, so the densest area is covered early.
        var cells = Enumerable.Range(0, size).SelectMany(r => Enumerable.Range(0, size).Select(c => (r: r - half, c: c - half)))
            .OrderBy(p => p.r * p.r + p.c * p.c);
        foreach (var (r, c) in cells)
            points.Add((Math.Round(centerLat + (decimal)(r * latStep), 6), Math.Round(centerLng + (decimal)(c * lngStep), 6)));
        return points;
    }

    /// <summary>Great-circle distance in km (haversine).</summary>
    public static double DistanceKm(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        static double Rad(decimal d) => (double)d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>Forces English labels on a place URL so parsing stays stable.</summary>
    public static string WithLanguage(string placeUrl, string languageCode)
    {
        var separator = placeUrl.Contains('?') ? '&' : '?';
        return placeUrl.Contains("hl=", StringComparison.Ordinal) ? placeUrl : $"{placeUrl}{separator}hl={languageCode}";
    }
}
