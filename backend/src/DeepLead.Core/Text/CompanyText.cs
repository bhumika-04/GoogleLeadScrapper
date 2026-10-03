using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DeepLead.Core.Text;

public static partial class CompanyText
{
    private static readonly string[] LegalSuffixes =
        ["private limited", "pvt ltd", "pvt. ltd.", "pvt", "limited", "ltd", "llp", "llc", "inc", "co", "company", "enterprises"];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphaNumeric();

    /// <summary>Lower-case, accents/punctuation removed, trailing legal suffixes dropped – used for matching, never displayed.</summary>
    public static string NormalizeName(string name)
    {
        var lowered = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        var cleaned = NonAlphaNumeric().Replace(sb.ToString(), " ").Trim();
        foreach (var suffix in LegalSuffixes)
        {
            var s = NonAlphaNumeric().Replace(suffix, " ").Trim();
            if (cleaned.EndsWith(" " + s, StringComparison.Ordinal))
                cleaned = cleaned[..^(s.Length + 1)].Trim();
        }

        return cleaned.Length > 300 ? cleaned[..300] : cleaned;
    }
}

public enum SocialPlatform
{
    LinkedIn,
    Facebook,
    Instagram,
    YouTube,
    X,
}

public static class WebsiteClassifier
{
    private static readonly (string Domain, SocialPlatform Platform)[] SocialDomains =
    [
        ("linkedin.com", SocialPlatform.LinkedIn),
        ("facebook.com", SocialPlatform.Facebook),
        ("fb.com", SocialPlatform.Facebook),
        ("instagram.com", SocialPlatform.Instagram),
        ("youtube.com", SocialPlatform.YouTube),
        ("youtu.be", SocialPlatform.YouTube),
        ("twitter.com", SocialPlatform.X),
        ("x.com", SocialPlatform.X),
    ];

    // Tracking parameters Google/Meta add to listing links; they make the same site look like different URLs.
    private static readonly string[] TrackingParams = ["utm_", "igsh", "fbclid", "gclid"];

    /// <summary>Host without "www.", e.g. "jaimagraphics.com"; null for invalid URLs.</summary>
    public static string? GetDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public static SocialPlatform? GetSocialPlatform(string? url)
    {
        var domain = GetDomain(url);
        if (domain is null)
            return null;
        foreach (var (d, platform) in SocialDomains)
        {
            if (domain == d || domain.EndsWith("." + d, StringComparison.Ordinal))
                return platform;
        }
        return null;
    }

    /// <summary>Removes utm_*/igsh/fbclid/gclid query parameters.</summary>
    public static string StripTracking(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
            return url;

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !TrackingParams.Any(t => p.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var builder = new UriBuilder(uri) { Query = string.Join('&', kept) };
        var result = builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
        return result;
    }
}
