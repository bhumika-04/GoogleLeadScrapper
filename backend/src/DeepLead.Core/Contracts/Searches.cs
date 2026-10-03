namespace DeepLead.Core.Contracts;

public static class SearchStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Blocked = "Blocked";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>Search Console submit. Cities are existing ids plus free-typed names (saved as user-added cities).</summary>
public sealed record CreateSearchRequest(
    string? Name,
    string CountryIso2,
    IReadOnlyList<int> CityIds,
    IReadOnlyList<string>? NewCityNames,
    IReadOnlyList<string> Keywords,
    string? IcpPrompt);

public sealed record SearchSummaryDto(
    long Id,
    string Name,
    string CountryIso2,
    string CountryName,
    string Status,
    int CityCount,
    int KeywordCount,
    int AspectCount,
    int AspectsCompleted,
    int LeadCount,
    DateTime CreatedAt,
    long? ParentSearchId,
    int RunNumber,
    string? RepeatFrequency,
    DateTime? NextRunAt);

public sealed record SetRepeatRequest(string? Frequency);

public sealed record AspectDto(
    long Id,
    int Sequence,
    string City,
    string? Region,
    string Keyword,
    string Status,
    int LeadCount,
    int? ItemsTotal,
    int ItemsDone,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    string? LastError,
    string? PeopleStatus,
    int? PeopleTotal,
    int PeopleDone);

public sealed record SearchDetailDto(
    SearchSummaryDto Summary,
    string? IcpPrompt,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<AspectDto> Aspects);

/// <summary>A search the worker has claimed, with everything needed to run its aspects.</summary>
public sealed record SearchRunInfo(long Id, int TenantId, string CountryIso2, string CountryName);

public sealed record AspectRunInfo(
    long Id, int Sequence, int CityId, string City, string? Region, string Keyword, string Status, string? MapsStatus,
    decimal? Latitude, decimal? Longitude, int? Population);
