namespace DeepLead.Core.Contracts;

// Property-style records: Dapper maps these by column name, so column order doesn't matter.

public sealed record PersonDto
{
    public long Id { get; init; }
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
}

public sealed record ChannelDto
{
    public string ChannelType { get; init; } = "";
    public string NormalizedValue { get; init; } = "";
    public string? PhoneKind { get; init; }
    public bool? IsValid { get; init; }
    public string? ValidationNote { get; init; }
    public string? SourceUrl { get; init; }
}

public sealed record SocialDto
{
    public string Platform { get; init; } = "";
    public string Url { get; init; } = "";
}

public sealed record FactDto
{
    public string FieldName { get; init; } = "";
    public string Value { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string? Quote { get; init; }
    public string ExtractedBy { get; init; } = "";
    public DateTime FoundAt { get; init; }
}

public sealed record CompanyDetailDto
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Category { get; init; }
    public string? Address { get; init; }
    public string? Website { get; init; }
    public string? MapsUrl { get; init; }
    public decimal? Rating { get; init; }
    public int? ReviewCount { get; init; }
    public string? OwnerName { get; init; }
    public string? TeamSize { get; init; }
    public string? Turnover { get; init; }
    public string? Gstin { get; init; }
    public DateTime? PeopleEnrichedAt { get; init; }
    public DateTime? LastEnrichedAt { get; init; }
    public IReadOnlyList<PersonDto> People { get; init; } = [];
    public IReadOnlyList<ChannelDto> Channels { get; init; } = [];
    public IReadOnlyList<SocialDto> Socials { get; init; } = [];
    public IReadOnlyList<FactDto> Facts { get; init; } = [];
}
