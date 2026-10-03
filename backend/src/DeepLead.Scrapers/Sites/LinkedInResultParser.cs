using System.Text.RegularExpressions;

namespace DeepLead.Scrapers.Sites;

public sealed record LinkedInPersonHit(string FullName, string? Designation, string ProfileUrl, string MatchText);

/// <summary>
/// Reads people from search-result titles/snippets of public LinkedIn profiles, e.g.
/// "Rahul Sharma - Owner - Jai Ma Graphics | LinkedIn" or snippet "Owner at Jai Ma Graphics · Indore".
/// No LinkedIn page is opened, so no login is needed.
/// </summary>
public static partial class LinkedInResultParser
{
    [GeneratedRegex(@"^https?://([a-z]{2,3}\.)?(www\.)?linkedin\.com/in/[^/?#]+", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileUrl();

    [GeneratedRegex(@"\s+[\-–—|]\s+")]
    private static partial Regex TitleSeparator();

    [GeneratedRegex(@"^[\p{L}][\p{L}\.'’\-]*(\s+[\p{L}][\p{L}\.'’\-]*){1,4}$")]
    private static partial Regex PersonName();

    [GeneratedRegex(@"(?<role>[\p{L}&/ ,\-]{2,60}?)\s+(?:at|@)\s+(?<company>[^·|\n]{2,80})", RegexOptions.IgnoreCase)]
    private static partial Regex RoleAtCompany();

    public static bool IsProfile(string url) => ProfileUrl().IsMatch(url);

    public static string CanonicalProfileUrl(string url)
    {
        var m = ProfileUrl().Match(url);
        return m.Success ? "https://www.linkedin.com/in/" + m.Value.Split("/in/")[1] : url;
    }

    public static LinkedInPersonHit? Parse(string url, string title, string snippet)
    {
        if (!IsProfile(url))
            return null;

        var cleanTitle = Regex.Replace(title, @"\s*[|\-–]\s*LinkedIn\s*$", "", RegexOptions.IgnoreCase).Trim();
        var parts = TitleSeparator().Split(cleanTitle).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        if (parts.Length == 0)
            return null;

        var name = parts[0];
        if (!PersonName().IsMatch(name) || name.Length > 60)
            return null;

        // "Name - Designation - Company" or "Name - Company"; else try "Designation at Company" in the snippet.
        string? designation = parts.Length >= 3 ? parts[1] : null;
        if (designation is null)
        {
            var m = RoleAtCompany().Match(snippet);
            if (m.Success)
                designation = m.Groups["role"].Value.Trim(' ', ',', '·');
        }

        return new LinkedInPersonHit(name, string.IsNullOrWhiteSpace(designation) ? null : designation, CanonicalProfileUrl(url), $"{title} {snippet}");
    }
}
