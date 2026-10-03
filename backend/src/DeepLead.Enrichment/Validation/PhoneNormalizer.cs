using PhoneNumbers;

namespace DeepLead.Enrichment.Validation;

public sealed record NormalizedPhone(string E164, string Kind, bool IsValid);

/// <summary>Country-aware phone parsing (libphonenumber): "09685251186" in IN -> "+919685251186", Mobile.</summary>
public static class PhoneNormalizer
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

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
