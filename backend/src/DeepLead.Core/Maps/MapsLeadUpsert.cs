namespace DeepLead.Core.Maps;

/// <summary>A Maps listing prepared for saving: phone normalized, website split into real website vs social profile.</summary>
public sealed record MapsLeadUpsert(
    MapsListing Listing,
    string CountryIso2,
    int CityId,
    string NormalizedName,
    string? Website,
    string? WebsiteDomain,
    string? SocialPlatform,
    string? SocialUrl,
    string? PhoneE164,
    string? PhoneKind,
    bool? PhoneValid);
