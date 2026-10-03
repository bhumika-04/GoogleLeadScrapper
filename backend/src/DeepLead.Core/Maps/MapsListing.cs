namespace DeepLead.Core.Maps;

public enum MapsBusinessStatus
{
    Operational,
    TemporarilyClosed,
    PermanentlyClosed,
}

/// <summary>One Google Maps place, as scraped in Stage 1.</summary>
public sealed record MapsListing
{
    public required string Name { get; init; }
    public required string MapsUrl { get; init; }

    /// <summary>Google feature id from the place URL (e.g. "0x3962fc...:0x1a2b..."); stable key for dedupe.</summary>
    public string? PlaceId { get; init; }

    public string? Category { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Website { get; init; }
    public decimal? Rating { get; init; }
    public int? ReviewCount { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public MapsBusinessStatus BusinessStatus { get; init; } = MapsBusinessStatus.Operational;

    /// <summary>Position in the Maps result list (1-based).</summary>
    public int Rank { get; init; }
}
