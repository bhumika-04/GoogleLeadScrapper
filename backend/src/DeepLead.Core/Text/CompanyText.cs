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

    /// <summary>
    /// One canonical form per social profile so the same page isn't stored twice:
    /// https, no www./m./web./mobile. prefix, lower-case host and path, no query (except facebook profile.php?id=), no trailing slash.
    /// </summary>
    public static string NormalizeSocialUrl(string url)
    {
        if (!Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url, UriKind.Absolute, out var uri))
            return url;
        var host = uri.Host.ToLowerInvariant();
        foreach (var prefix in new[] { "www.", "m.", "web.", "mobile.", "business." })
        {
            if (host.StartsWith(prefix, StringComparison.Ordinal))
            {
                host = host[prefix.Length..];
                break;
            }
        }
        if (host.EndsWith("linkedin.com", StringComparison.Ordinal))
            host = "linkedin.com";   // in.linkedin.com, uk.linkedin.com … are the same profile

        var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        if (host == "instagram.com" && path.StartsWith("/_u/", StringComparison.Ordinal))
            path = path[3..];   // universal-link prefix: instagram.com/_u/name == instagram.com/name
        if (host == "facebook.com" && System.Text.RegularExpressions.Regex.Match(path, @"^/p/[^/]*-(\d{6,})$") is { Success: true } fbId)
            path = "/" + fbId.Groups[1].Value;   // facebook.com/p/page-name-6156… == facebook.com/6156…

        var keepQuery = path.EndsWith("/profile.php", StringComparison.Ordinal) && System.Web.HttpUtility.ParseQueryString(uri.Query)["id"] is { } id
            ? "?id=" + id : "";
        return $"https://{host}{path}{keepQuery}";
    }

    /// <summary>
    /// True for a profile/page/channel, false for a single post, reel, video, share link or other non-profile URL.
    /// Expects a URL from <see cref="NormalizeSocialUrl"/>.
    /// </summary>
    public static bool IsProfileUrl(string normalizedUrl)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
            return false;
        var path = uri.AbsolutePath;
        if (path.Length <= 1)
            return false;

        string[] nonProfile = uri.Host switch
        {
            "instagram.com" => ["/p/", "/reel/", "/reels/", "/stories/", "/tv/", "/explore/", "/accounts/"],
            "facebook.com" => ["/sharer", "/share", "/photo", "/photos", "/videos", "/watch", "/events", "/groups/", "/hashtag/", "/story.php", "/permalink.php", "/posts/", "/login", "/dialog/"],
            "youtube.com" => ["/watch", "/shorts/", "/embed/", "/playlist", "/results"],
            "youtu.be" => ["/"],
            "linkedin.com" => ["/posts/", "/feed/", "/pulse/", "/shareArticle", "/sharing/", "/jobs/"],
            "x.com" or "twitter.com" => ["/intent", "/share", "/home", "/search", "/hashtag/"],
            _ => [],
        };
        if (nonProfile.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase) || path.Contains(p + "/", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (uri.Host is "x.com" or "twitter.com" && path.Contains("/status/", StringComparison.OrdinalIgnoreCase))
            return false;
        // facebook.com/profile.php is only a profile with its ?id=
        return !(uri.Host == "facebook.com" && path == "/profile.php" && !uri.Query.Contains("id=", StringComparison.Ordinal));
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
