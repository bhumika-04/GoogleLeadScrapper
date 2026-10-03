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
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
