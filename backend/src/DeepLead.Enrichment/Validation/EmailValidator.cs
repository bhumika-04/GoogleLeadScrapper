using System.Collections.Concurrent;
using System.Net.Mail;
using DnsClient;

namespace DeepLead.Enrichment.Validation;

public sealed record EmailCheck(bool IsValid, string Note);

/// <summary>Syntax + disposable-domain + MX lookup. (No SMTP mailbox probe: it gets IPs blacklisted and is unreliable.)</summary>
public sealed class EmailValidator
{
    private static readonly HashSet<string> Disposable = new(StringComparer.OrdinalIgnoreCase)
        { "mailinator.com", "10minutemail.com", "guerrillamail.com", "tempmail.com", "yopmail.com", "trashmail.com", "sharklasers.com" };

    private readonly LookupClient _dns = new(new LookupClientOptions { Timeout = TimeSpan.FromSeconds(5), Retries = 1, UseCache = true });
    private readonly ConcurrentDictionary<string, bool> _mxCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<EmailCheck> CheckAsync(string email, CancellationToken ct)
    {
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            return new EmailCheck(false, "Invalid format");
        var domain = address.Host;
        if (Disposable.Contains(domain))
            return new EmailCheck(false, "Disposable domain");

        if (!_mxCache.TryGetValue(domain, out var hasMx))
        {
            try
            {
                var result = await _dns.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
                hasMx = result.Answers.MxRecords().Any();
                if (!hasMx)
                {
                    // RFC 5321: no MX falls back to the A record.
                    var a = await _dns.QueryAsync(domain, QueryType.A, cancellationToken: ct);
                    hasMx = a.Answers.ARecords().Any();
                }
            }
            catch (DnsResponseException)
            {
                return new EmailCheck(false, "DNS lookup failed");
            }
            _mxCache[domain] = hasMx;
        }

        return hasMx ? new EmailCheck(true, "Domain accepts mail (MX)") : new EmailCheck(false, "Domain has no mail server");
    }
}
