namespace DeepLead.Core;

/// <summary>
/// The source refused us (CAPTCHA, "unusual traffic" page, HTTP 429). Pipeline should pause and cool down, not retry immediately.
/// </summary>
public sealed class ScrapeBlockedException(string source, string? url, string message)
    : Exception($"{source} blocked the request: {message}")
{
    public string SourceName { get; } = source;
    public string? Url { get; } = url;
}
