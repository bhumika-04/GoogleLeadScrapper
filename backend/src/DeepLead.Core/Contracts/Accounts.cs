namespace DeepLead.Core.Contracts;

public static class Platforms
{
    public const string LinkedIn = "LinkedIn";
    public const string Facebook = "Facebook";
    public const string Instagram = "Instagram";
    public const string IndiaMart = "IndiaMart";
    public const string Justdial = "Justdial";

    public static readonly IReadOnlyList<string> All = [LinkedIn, Facebook, Instagram, IndiaMart, Justdial];
}

public static class AccountStatus
{
    public const string ConnectRequested = "ConnectRequested";
    public const string WaitingForLogin = "WaitingForLogin";
    public const string SaveRequested = "SaveRequested";   // user clicked "I've logged in – save" in the UI
    public const string Connected = "Connected";
    public const string Expired = "Expired";
    public const string Failed = "Failed";
    public const string Disconnected = "Disconnected";
}

public sealed record ConnectedAccountDto(
    string Platform,
    string Status,
    string? AccountLabel,
    DateTime? RequestedAt,
    DateTime? ConnectedAt,
    DateTime? LastUsedAt,
    string? LastError,
    int UsageToday);

public sealed record ConnectAccountRequest(string? AccountLabel);

/// <summary>Worker side: a connect request to fulfil.</summary>
public sealed record ConnectJob(int Id, int TenantId, string Platform);
