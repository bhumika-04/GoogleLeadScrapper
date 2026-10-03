using DeepLead.Core.Text;

namespace DeepLead.Data;

/// <summary>
/// "Verified" bookkeeping: every phone/email/person row keeps the distinct sites (domains) that showed it.
/// Two or more different sites = verified.
/// </summary>
internal static class SourceTracking
{
    /// <summary>SET fragment: add @domain to SourceDomains once and bump SourceCount when it's a new site. Needs @domain.</summary>
    public const string MergeDomainsSet = """
        SourceDomains = CASE WHEN SourceDomains IS NULL THEN @domain
                             WHEN ',' + SourceDomains + ',' LIKE '%,' + @domain + ',%' THEN SourceDomains
                             ELSE LEFT(SourceDomains + ',' + @domain, 400) END,
        SourceCount = CASE WHEN SourceDomains IS NULL OR ',' + SourceDomains + ',' LIKE '%,' + @domain + ',%' THEN SourceCount
                           WHEN SourceCount < 250 THEN SourceCount + 1 ELSE SourceCount END
        """;

    public static string DomainOf(string? url) => WebsiteClassifier.GetDomain(url) ?? "unknown";
}

/// <summary>
/// Lead score 0–100 = how complete and trustworthy a lead is (not an AI guess):
/// valid mobile 20 (other valid phone 12) · valid email 15 · owner known 20 · a person with direct contact/LinkedIn 10 ·
/// website 10 · any social profile 5 · rating ≥4 with ≥20 reviews 10 (any rating 4) · anything verified by 2+ sites 10.
/// </summary>
internal static class LeadScore
{
    public const string RecomputeSql = """
        UPDATE c SET LeadScore = s.Score, LeadScoreAt = SYSUTCDATETIME()
        FROM dbo.Companies c
        CROSS APPLY (SELECT CAST(
              CASE WHEN EXISTS (SELECT 1 FROM dbo.CompanyChannels x WHERE x.CompanyId = c.Id AND x.ChannelType = 'Phone' AND x.IsValid = 1 AND x.PhoneKind = 'Mobile') THEN 20
                   WHEN EXISTS (SELECT 1 FROM dbo.CompanyChannels x WHERE x.CompanyId = c.Id AND x.ChannelType = 'Phone' AND x.IsValid = 1) THEN 12 ELSE 0 END
            + CASE WHEN EXISTS (SELECT 1 FROM dbo.CompanyChannels x WHERE x.CompanyId = c.Id AND x.ChannelType = 'Email' AND x.IsValid = 1) THEN 15 ELSE 0 END
            + CASE WHEN c.OwnerName IS NOT NULL THEN 20 ELSE 0 END
            + CASE WHEN EXISTS (SELECT 1 FROM dbo.CompanyPeople p WHERE p.CompanyId = c.Id
                                AND (p.Phone IS NOT NULL OR p.Email IS NOT NULL OR p.LinkedInUrl IS NOT NULL)) THEN 10 ELSE 0 END
            + CASE WHEN c.Website IS NOT NULL THEN 10 ELSE 0 END
            + CASE WHEN EXISTS (SELECT 1 FROM dbo.CompanySocials so WHERE so.CompanyId = c.Id) THEN 5 ELSE 0 END
            + CASE WHEN c.Rating >= 4 AND c.ReviewCount >= 20 THEN 10 WHEN c.Rating IS NOT NULL THEN 4 ELSE 0 END
            + CASE WHEN EXISTS (SELECT 1 FROM dbo.CompanyChannels x WHERE x.CompanyId = c.Id AND x.SourceCount >= 2)
                     OR EXISTS (SELECT 1 FROM dbo.CompanyPeople p WHERE p.CompanyId = c.Id AND p.SourceCount >= 2) THEN 10 ELSE 0 END
            AS TINYINT) AS Score) s
        WHERE c.Id = @companyId;
        """;
}
