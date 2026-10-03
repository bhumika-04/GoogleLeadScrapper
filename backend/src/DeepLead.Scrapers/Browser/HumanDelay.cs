namespace DeepLead.Scrapers.Browser;

/// <summary>Randomised pauses so request timing doesn't look machine-generated (we have one IP and no proxies).</summary>
public static class HumanDelay
{
    public static Task BetweenPagesAsync(CancellationToken ct) => DelayAsync(1_500, 4_000, ct);

    public static Task BetweenScrollsAsync(CancellationToken ct) => DelayAsync(900, 2_200, ct);

    public static Task DelayAsync(int minMs, int maxMs, CancellationToken ct) =>
        Task.Delay(Random.Shared.Next(minMs, maxMs), ct);
}
