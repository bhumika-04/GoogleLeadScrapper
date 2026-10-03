using PhoneNumbers;

namespace DeepLead.Enrichment.Validation;

/// <summary>Finds valid phone numbers in free page text (libphonenumber's matcher, country-aware).</summary>
public static class PhoneExtractor
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public static IReadOnlyList<NormalizedPhone> Find(string text, string countryIso2, int max = 15)
    {
        var found = new Dictionary<string, NormalizedPhone>();
        foreach (var match in Util.FindNumbers(text, countryIso2.ToUpperInvariant(), PhoneNumberUtil.Leniency.VALID, long.MaxValue))
        {
            var normalized = PhoneNormalizer.Normalize(match.RawString, countryIso2);
            if (normalized is { IsValid: true })
                found.TryAdd(normalized.E164, normalized);
            if (found.Count >= max)
                break;
        }
        return found.Values.ToList();
    }
}
