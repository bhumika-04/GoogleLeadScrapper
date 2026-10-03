using System.Net;
using System.Text;

namespace DeepLead.Scrapers.Web;

public sealed record FetchedPage(string RequestedUrl, string FinalUrl, string Html);

/// <summary>Plain HTTP fetch for static pages (company websites, IndiaMART). No JavaScript; returns null on any failure.</summary>
public sealed class PageFetcher : IDisposable
{
    private const int MaxBytes = 3 * 1024 * 1024;

    private readonly HttpClient _http;

    public PageFetcher()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-IN,en;q=0.9");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
    }

    public async Task<FetchedPage?> GetAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                    break;
            }

            var charset = response.Content.Headers.ContentType?.CharSet;
            var encoding = TryGetEncoding(charset) ?? Encoding.UTF8;
            return new FetchedPage(url, response.RequestMessage?.RequestUri?.ToString() ?? url, encoding.GetString(buffer.ToArray()));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (ct.IsCancellationRequested)
                throw;
            return null;
        }
    }

    private static Encoding? TryGetEncoding(string? charset)
    {
        try
        {
            return string.IsNullOrWhiteSpace(charset) ? null : Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
