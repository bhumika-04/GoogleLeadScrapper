using Dapper;
using DeepLead.Core.Contracts;
using DeepLead.Core.People;
using DeepLead.Core.Text;

namespace DeepLead.Data;

public sealed class PeopleRepository(SqlConnectionFactory db)
{
    /// <summary>Companies of an aspect that still need Stage 2 (never researched, or older than the cache window).</summary>
    public async Task<IReadOnlyList<CompanyToResearch>> GetCompaniesToResearchAsync(long aspectId, int cacheDays, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<CompanyToResearch>("""
            SELECT co.Id AS CompanyId, co.Name, ci.AsciiName AS City, ci.Region, co.CountryIso2, co.Website,
                   (SELECT TOP (1) so.Url FROM dbo.CompanySocials so WHERE so.CompanyId = co.Id AND so.Platform = 'Facebook') AS FacebookUrl
            FROM dbo.AspectLeads al
            JOIN dbo.Companies co ON co.Id = al.CompanyId
            JOIN dbo.SearchAspects sa ON sa.Id = al.AspectId
            JOIN dbo.Cities ci ON ci.Id = sa.CityId
            WHERE al.AspectId = @aspectId
              AND (co.PeopleEnrichedAt IS NULL OR co.PeopleEnrichedAt < DATEADD(DAY, -@cacheDays, SYSUTCDATETIME()))
            ORDER BY al.MapsRank
            """, new { aspectId, cacheDays });
        return rows.AsList();
    }

    public async Task<int> CountAspectCompaniesAsync(long aspectId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AspectLeads WHERE AspectId = @aspectId", new { aspectId });
    }

    /// <summary>Saves Stage 2 findings. Merges into existing rows; never deletes what earlier runs found.</summary>
    public async Task SaveResearchAsync(long companyId, CompanyResearch research, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        foreach (var p in research.People)
        {
            var normalizedName = Truncate(CompanyText.NormalizeName(StripHonorific(p.FullName)), 200);
            await c.ExecuteAsync($"""
                MERGE dbo.CompanyPeople AS t
                USING (SELECT @companyId AS CompanyId, @normalizedName AS NormalizedName) AS s
                   ON t.CompanyId = s.CompanyId AND t.NormalizedName = s.NormalizedName
                WHEN MATCHED THEN UPDATE SET
                    Designation = COALESCE(t.Designation, @designation),
                    IsOwner = CASE WHEN @isOwner = 1 THEN 1 ELSE t.IsOwner END,
                    IsDecisionMaker = CASE WHEN @isDecisionMaker = 1 THEN 1 ELSE t.IsDecisionMaker END,
                    Phone = COALESCE(t.Phone, @phone), Email = COALESCE(t.Email, @email),
                    LinkedInUrl = COALESCE(t.LinkedInUrl, @linkedIn), FacebookUrl = COALESCE(t.FacebookUrl, @facebook),
                    InstagramUrl = COALESCE(t.InstagramUrl, @instagram),
                    {SourceTracking.MergeDomainsSet}
                WHEN NOT MATCHED THEN INSERT
                    (CompanyId, FullName, NormalizedName, Designation, IsOwner, IsDecisionMaker, Phone, Email, LinkedInUrl, FacebookUrl, InstagramUrl,
                     SourceUrl, Source, SourceDomains)
                    VALUES (@companyId, @fullName, @normalizedName, @designation, @isOwner, @isDecisionMaker, @phone, @email, @linkedIn, @facebook, @instagram,
                     @sourceUrl, @source, @domain);
                """, new
            {
                companyId,
                fullName = Truncate(p.FullName, 200),
                normalizedName,
                designation = Truncate(p.Designation, 200),
                isOwner = p.IsOwner,
                isDecisionMaker = p.IsDecisionMaker,
                phone = p.Phone,
                email = p.Email,
                linkedIn = p.LinkedInUrl,
                facebook = p.FacebookUrl,
                instagram = p.InstagramUrl,
                sourceUrl = Truncate(p.SourceUrl, 1000),
                source = p.Source,
                domain = SourceTracking.DomainOf(p.SourceUrl),
            }, tx);

            foreach (var extra in p.AlsoSeenAt)
                await c.ExecuteAsync($"UPDATE dbo.CompanyPeople SET {SourceTracking.MergeDomainsSet} WHERE CompanyId = @companyId AND NormalizedName = @normalizedName",
                    new { companyId, normalizedName, domain = SourceTracking.DomainOf(extra) }, tx);
        }

        foreach (var ch in research.Channels)
        {
            var key = new { companyId, type = ch.Type, normalized = Truncate(ch.NormalizedValue, 320) };
            await c.ExecuteAsync($"""
                UPDATE dbo.CompanyChannels SET
                    PhoneKind = COALESCE(PhoneKind, @kind), IsValid = COALESCE(IsValid, @valid), ValidationNote = COALESCE(ValidationNote, @note),
                    {SourceTracking.MergeDomainsSet}
                WHERE CompanyId = @companyId AND ChannelType = @type AND NormalizedValue = @normalized;

                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.CompanyChannels (CompanyId, ChannelType, Value, NormalizedValue, PhoneKind, IsValid, ValidationNote, SourceUrl, SourceDomains)
                    VALUES (@companyId, @type, @value, @normalized, @kind, @valid, @note, @source, @domain);
                """, new
            {
                key.companyId, key.type, key.normalized, value = Truncate(ch.Value, 320),
                kind = ch.PhoneKind, valid = ch.IsValid, note = Truncate(ch.Note, 200), source = Truncate(ch.SourceUrl, 1000),
                domain = SourceTracking.DomainOf(ch.SourceUrl),
            }, tx);

            foreach (var extra in ch.AlsoSeenAt)
                await c.ExecuteAsync($"UPDATE dbo.CompanyChannels SET {SourceTracking.MergeDomainsSet} WHERE CompanyId = @companyId AND ChannelType = @type AND NormalizedValue = @normalized",
                    new { key.companyId, key.type, key.normalized, domain = SourceTracking.DomainOf(extra) }, tx);
        }

        foreach (var s in research.Socials)
        {
            await c.ExecuteAsync("""
                IF NOT EXISTS (SELECT 1 FROM dbo.CompanySocials WHERE CompanyId = @companyId AND Platform = @platform AND Url = @url)
                    INSERT INTO dbo.CompanySocials (CompanyId, Platform, Url) VALUES (@companyId, @platform, @url);
                """, new { companyId, platform = s.Platform, url = Truncate(s.Url, 500) }, tx);
        }

        foreach (var f in research.Facts)
        {
            await c.ExecuteAsync("""
                IF NOT EXISTS (SELECT 1 FROM dbo.CompanyFieldValues WHERE CompanyId = @companyId AND FieldName = @field AND Value = @value AND SourceUrl = @source)
                    INSERT INTO dbo.CompanyFieldValues (CompanyId, FieldName, Value, SourceUrl, Quote, ExtractedBy, QuoteVerified)
                    VALUES (@companyId, @field, @value, @source, @quote, 'Rules', 1);
                """, new { companyId, field = f.FieldName, value = Truncate(f.Value, 500), source = Truncate(f.SourceUrl, 2000), quote = Truncate(f.Quote, 1000) }, tx);
        }

        // Headline columns on the company: latest sourced value wins, existing values kept when nothing new.
        string? Fact(string field) => research.Facts.LastOrDefault(f => f.FieldName == field)?.Value;
        var owner = research.People.FirstOrDefault(p => p.IsOwner)?.FullName ?? Fact(FactFields.OwnerName);
        var gstin = Fact(FactFields.Gstin);
        await c.ExecuteAsync("""
            UPDATE dbo.Companies SET
                OwnerName = COALESCE(@owner, OwnerName),
                TeamSize = COALESCE(@teamSize, TeamSize),
                Turnover = COALESCE(@turnover, Turnover),
                Gstin = COALESCE(@gstin, Gstin),
                FacebookFollowers = COALESCE(@followers, FacebookFollowers),
                -- Partial research (web search was blocked) leaves PeopleEnrichedAt empty so a later run retries it.
                PeopleEnrichedAt = CASE WHEN @complete = 1 THEN SYSUTCDATETIME() ELSE PeopleEnrichedAt END,
                LastEnrichedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @companyId;
            """ + LeadScore.RecomputeSql, new
        {
            companyId,
            complete = !research.SearchSkipped,
            owner = Truncate(owner, 200),
            teamSize = Truncate(Fact(FactFields.TeamSize), 100),
            turnover = Truncate(Fact(FactFields.Turnover), 100),
            gstin = gstin is { Length: 15 } ? gstin.ToUpperInvariant() : null,
            followers = int.TryParse(Fact(FactFields.FacebookFollowers), out var followerCount) ? followerCount : (int?)null,
        }, tx);

        await tx.CommitAsync(ct);
    }

    /// <summary>Full company card for the UI; null unless the company was found by one of this tenant's sessions.</summary>
    public async Task<CompanyDetailDto?> GetCompanyDetailAsync(int tenantId, long companyId)
    {
        await using var c = await db.OpenAsync();
        var company = await c.QuerySingleOrDefaultAsync<CompanyDetailDto>("""
            SELECT co.Id, co.Name, co.Category, co.Address, co.Website, co.MapsUrl, co.Rating, co.ReviewCount,
                   co.OwnerName, co.TeamSize, co.Turnover, co.Gstin, co.PeopleEnrichedAt, co.LastEnrichedAt,
                   CAST(co.LeadScore AS INT) AS LeadScore, co.FacebookFollowers
            FROM dbo.Companies co
            WHERE co.Id = @companyId
              AND EXISTS (SELECT 1 FROM dbo.AspectLeads al JOIN dbo.SearchAspects sa ON sa.Id = al.AspectId
                          JOIN dbo.Searches s ON s.Id = sa.SearchId
                          WHERE al.CompanyId = co.Id AND s.TenantId = @tenantId)
            """, new { tenantId, companyId });
        if (company is null)
            return null;

        var people = await c.QueryAsync<PersonDto>("""
            SELECT Id, FullName, Designation, IsOwner, IsDecisionMaker, Phone, Email, LinkedInUrl, FacebookUrl, InstagramUrl, Source, SourceUrl,
                   CAST(SourceCount AS INT) AS SourceCount, SourceDomains
            FROM dbo.CompanyPeople WHERE CompanyId = @companyId
            ORDER BY IsOwner DESC, IsDecisionMaker DESC, FullName
            """, new { companyId });
        var channels = await c.QueryAsync<ChannelDto>("""
            SELECT ChannelType, NormalizedValue, PhoneKind, IsValid, ValidationNote, SourceUrl,
                   CAST(SourceCount AS INT) AS SourceCount, SourceDomains
            FROM dbo.CompanyChannels WHERE CompanyId = @companyId ORDER BY ChannelType, SourceCount DESC, Id
            """, new { companyId });
        var socials = await c.QueryAsync<SocialDto>(
            "SELECT Platform, Url FROM dbo.CompanySocials WHERE CompanyId = @companyId ORDER BY Platform", new { companyId });
        var facts = await c.QueryAsync<FactDto>("""
            SELECT FieldName, Value, SourceUrl, Quote, ExtractedBy, FoundAt
            FROM dbo.CompanyFieldValues WHERE CompanyId = @companyId ORDER BY FieldName, FoundAt DESC
            """, new { companyId });

        // A fact is verified when the same value (case-insensitive) is stated on 2+ different sites.
        var factList = facts.AsList();
        var verifiedKeys = factList
            .GroupBy(f => (f.FieldName, Value: f.Value.Trim().ToLowerInvariant()))
            .Where(g => g.Select(f => SourceTracking.DomainOf(f.SourceUrl)).Distinct().Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet();
        var markedFacts = factList.Select(f => f with { Verified = verifiedKeys.Contains((f.FieldName, f.Value.Trim().ToLowerInvariant())) }).ToList();

        return company with { People = people.AsList(), Channels = channels.AsList(), Socials = socials.AsList(), Facts = markedFacts };
    }

    private static string StripHonorific(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"^(Mr|Mrs|Ms|Dr|Shri|Smt|Er)\.?\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
