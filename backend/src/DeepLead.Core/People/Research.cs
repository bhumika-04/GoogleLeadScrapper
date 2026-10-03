namespace DeepLead.Core.People;

/// <summary>A company queued for Stage 2 (people discovery).</summary>
public sealed record CompanyToResearch(
    long CompanyId,
    string Name,
    string City,
    string? Region,
    string CountryIso2,
    string? Website,
    string? FacebookUrl = null);

public sealed record PersonFinding
{
    public required string FullName { get; init; }
    public string? Designation { get; init; }
    public bool IsOwner { get; init; }
    public bool IsDecisionMaker { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? LinkedInUrl { get; init; }
    public string? FacebookUrl { get; init; }
    public string? InstagramUrl { get; init; }
    public required string SourceUrl { get; init; }
    /// <summary>'Website', 'IndiaMart', 'LinkedInSearch', ...</summary>
    public required string Source { get; init; }
    /// <summary>Other pages that showed the same person in this run (evidence for "Verified").</summary>
    public IReadOnlyList<string> AlsoSeenAt { get; init; } = [];
}

public sealed record ChannelFinding(string Type, string Value, string NormalizedValue, string? PhoneKind, bool? IsValid, string? Note, string SourceUrl)
{
    /// <summary>Other pages that showed the same phone/email in this run (evidence for "Verified").</summary>
    public IReadOnlyList<string> AlsoSeenAt { get; init; } = [];
}

public sealed record SocialFinding(string Platform, string Url);

/// <summary>A company fact with its proof ("real figures only"): value + page + exact text it came from.</summary>
public sealed record FactFinding(string FieldName, string Value, string SourceUrl, string? Quote, string ExtractedBy);

public sealed class CompanyResearch
{
    public List<PersonFinding> People { get; } = [];
    public List<ChannelFinding> Channels { get; } = [];
    public List<SocialFinding> Socials { get; } = [];
    public List<FactFinding> Facts { get; } = [];
    public int PagesVisited { get; set; }
    public int SearchesRun { get; set; }

    /// <summary>True when web search was blocked/skipped: results are partial and the company should be retried later.</summary>
    public bool SearchSkipped { get; set; }
}

public static class FactFields
{
    public const string TeamSize = "TeamSize";
    public const string Turnover = "Turnover";
    public const string OwnerName = "OwnerName";
    public const string Gstin = "Gstin";
    public const string LegalStatus = "LegalStatus";
    public const string YearEstablished = "YearEstablished";
    public const string NatureOfBusiness = "NatureOfBusiness";
    public const string FacebookFollowers = "FacebookFollowers";
}
