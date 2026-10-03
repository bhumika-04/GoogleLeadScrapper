namespace DeepLead.Core.Contracts;

/// <summary>One row in the leads table / export: a company as found by one aspect.</summary>
public sealed record LeadRowDto
{
    public long CompanyId { get; init; }
    public long AspectId { get; init; }
    public int? MapsRank { get; init; }
    public string Keyword { get; init; } = "";
    public string City { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Category { get; init; }
    public string? Phones { get; init; }
    public string? Emails { get; init; }
    public string? Website { get; init; }
    public string? Socials { get; init; }
    public string? Address { get; init; }
    public decimal? Rating { get; init; }
    public int? ReviewCount { get; init; }
    public string BusinessStatus { get; init; } = "";
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? MapsUrl { get; init; }
    public DateTime FoundAt { get; init; }

    // Stage 2
    public string? OwnerName { get; init; }
    /// <summary>"Name (Designation); Name (Designation)" – owner first.</summary>
    public string? People { get; init; }
    public int PeopleCount { get; init; }
    public string? TeamSize { get; init; }
    public string? Turnover { get; init; }
    public string? Gstin { get; init; }
    public DateTime? PeopleEnrichedAt { get; init; }
    /// <summary>Set by any research attempt; with PeopleEnrichedAt null it means "partial" (web search was blocked).</summary>
    public DateTime? LastEnrichedAt { get; init; }

    // Stage 5
    public int? LeadScore { get; init; }
    /// <summary>Phones/emails/people confirmed by 2+ different sites.</summary>
    public int VerifiedCount { get; init; }
    /// <summary>Not found by the previous run of this session (re-runs / repeats only).</summary>
    public bool IsNew { get; init; }
}

/// <summary>One person row for the "People" export sheet.</summary>
public sealed record PersonExportRow
{
    public string Keyword { get; init; } = "";
    public string City { get; init; } = "";
    public string Company { get; init; } = "";
    public string FullName { get; init; } = "";
    public string? Designation { get; init; }
    public bool IsOwner { get; init; }
    public bool IsDecisionMaker { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? LinkedInUrl { get; init; }
    public string? FacebookUrl { get; init; }
    public string? InstagramUrl { get; init; }
    public string? Source { get; init; }
    public string SourceUrl { get; init; } = "";
    public int SourceCount { get; init; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
