using PhoneNumbers;

namespace DeepLead.Enrichment.Validation;

public sealed record NormalizedPhone(string E164, string Kind, bool IsValid);

/// <summary>Country-aware phone parsing (libphonenumber): "09685251186" in IN -> "+919685251186", Mobile.</summary>
public static class PhoneNormalizer
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    private static readonly string[] PlaceholderRuns = ["1234567", "2345678", "3456789", "0123456", "9876543"];

    /// <summary>Website-template numbers ("1800 123 4567", "98765 43210", "99999 99999") that are not real contacts.</summary>
    public static bool IsPlaceholder(string e164)
    {
        var digits = new string(e164.Where(char.IsDigit).ToArray());
        if (PlaceholderRuns.Any(digits.Contains))
            return true;
        // Seven or more identical digits in a row.
        for (int i = 0, run = 1; i < digits.Length - 1; i++)
        {
            run = digits[i] == digits[i + 1] ? run + 1 : 1;
            if (run >= 7)
                return true;
        }
        return false;
    }

    public static NormalizedPhone? Normalize(string? raw, string countryIso2)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var number = Util.Parse(raw, countryIso2.ToUpperInvariant());
            var isValid = Util.IsValidNumber(number);
            var kind = Util.GetNumberType(number) switch
            {
                PhoneNumberType.MOBILE => "Mobile",
                PhoneNumberType.FIXED_LINE => "Landline",
                PhoneNumberType.FIXED_LINE_OR_MOBILE => "LandlineOrMobile",
                PhoneNumberType.TOLL_FREE => "TollFree",
                PhoneNumberType.VOIP => "Voip",
                _ => "Unknown",
            };
            return new NormalizedPhone(Util.Format(number, PhoneNumberFormat.E164), kind, isValid);
        }
        catch (NumberParseException)
        {
            return null;
        }
    }
}
