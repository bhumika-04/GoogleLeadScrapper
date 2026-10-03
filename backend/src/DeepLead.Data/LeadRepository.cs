using Dapper;
using DeepLead.Core.Contracts;
using DeepLead.Core.Maps;

namespace DeepLead.Data;

public sealed class LeadRepository(SqlConnectionFactory db)
{
    /// <summary>
    /// Saves a Stage 1 listing into the global company cache and links it to the aspect.
    /// Matches an existing company by Maps place id, otherwise by normalized name in the same city.
    /// </summary>
    public async Task<long> UpsertMapsLeadAsync(long aspectId, MapsLeadUpsert lead, CancellationToken ct = default)
    {
        var l = lead.Listing;
        await using var c = await db.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        var companyId = await c.ExecuteScalarAsync<long?>("""
            SELECT TOP (1) Id FROM dbo.Companies
            WHERE (@placeId IS NOT NULL AND MapsPlaceId = @placeId)
               OR (MapsPlaceId IS NULL AND CountryIso2 = @country AND CityId = @cityId AND NormalizedName = @normalizedName)
            ORDER BY CASE WHEN MapsPlaceId = @placeId THEN 0 ELSE 1 END
            """, new { placeId = l.PlaceId, country = lead.CountryIso2, cityId = lead.CityId, normalizedName = lead.NormalizedName }, tx);

        var p = new
        {
            id = companyId,
            name = l.Name,
            normalizedName = lead.NormalizedName,
            country = lead.CountryIso2,
            cityId = lead.CityId,
            address = l.Address,
            lat = l.Latitude,
            lng = l.Longitude,
            website = lead.Website,
            websiteDomain = lead.WebsiteDomain,
            category = l.Category,
            placeId = l.PlaceId,
            mapsUrl = l.MapsUrl,
            rating = l.Rating,
            reviewCount = l.ReviewCount,
            status = l.BusinessStatus.ToString(),
        };

        if (companyId is null)
        {
            companyId = await c.ExecuteScalarAsync<long>("""
                INSERT INTO dbo.Companies (Name, NormalizedName, CountryIso2, CityId, Address, Latitude, Longitude, Website, WebsiteDomain,
                                           Category, MapsPlaceId, MapsUrl, Rating, ReviewCount, BusinessStatus)
                OUTPUT INSERTED.Id
                VALUES (@name, @normalizedName, @country, @cityId, @address, @lat, @lng, @website, @websiteDomain,
                        @category, @placeId, @mapsUrl, @rating, @reviewCount, @status)
                """, p, tx);
        }
        else
        {
            // Maps data is the freshest source for these fields; keep existing values when Maps has none.
            await c.ExecuteAsync("""
                UPDATE dbo.Companies SET
                    Name = @name, NormalizedName = @normalizedName,
                    Address = COALESCE(@address, Address), Latitude = COALESCE(@lat, Latitude), Longitude = COALESCE(@lng, Longitude),
                    Website = COALESCE(@website, Website), WebsiteDomain = COALESCE(@websiteDomain, WebsiteDomain),
                    Category = COALESCE(@category, Category), MapsPlaceId = COALESCE(@placeId, MapsPlaceId), MapsUrl = @mapsUrl,
                    Rating = COALESCE(@rating, Rating), ReviewCount = COALESCE(@reviewCount, ReviewCount),
                    BusinessStatus = @status, UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @id
                """, p, tx);
        }

        await c.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM dbo.AspectLeads WHERE AspectId = @aspectId AND CompanyId = @companyId)
                INSERT INTO dbo.AspectLeads (AspectId, CompanyId, MapsRank, FoundBy) VALUES (@aspectId, @companyId, @rank, 'Maps');
            """, new { aspectId, companyId, rank = l.Rank }, tx);

        if (lead.PhoneE164 is not null)
        {
            await c.ExecuteAsync($"""
                UPDATE dbo.CompanyChannels SET {SourceTracking.MergeDomainsSet}
                WHERE CompanyId = @companyId AND ChannelType = 'Phone' AND NormalizedValue = @normalized;

                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.CompanyChannels (CompanyId, ChannelType, Value, NormalizedValue, PhoneKind, IsValid, SourceUrl, SourceDomains)
                    VALUES (@companyId, 'Phone', @raw, @normalized, @kind, @valid, @source, @domain);
                """, new
            {
                companyId, raw = l.Phone, normalized = lead.PhoneE164, kind = lead.PhoneKind, valid = lead.PhoneValid, source = l.MapsUrl,
                domain = SourceTracking.DomainOf(l.MapsUrl),
            }, tx);
        }

        if (lead.SocialUrl is not null)
        {
            await c.ExecuteAsync("""
                IF NOT EXISTS (SELECT 1 FROM dbo.CompanySocials WHERE CompanyId = @companyId AND Platform = @platform AND Url = @url)
                    INSERT INTO dbo.CompanySocials (CompanyId, Platform, Url) VALUES (@companyId, @platform, @url);
                """, new { companyId, platform = lead.SocialPlatform, url = lead.SocialUrl }, tx);
        }

        await c.ExecuteAsync(LeadScore.RecomputeSql, new { companyId }, tx);
        await tx.CommitAsync(ct);
        return companyId.Value;
    }

    private const string LeadFrom = """
        FROM dbo.AspectLeads al
        JOIN dbo.SearchAspects sa ON sa.Id = al.AspectId
        JOIN dbo.Searches s ON s.Id = sa.SearchId
        JOIN dbo.SearchKeywords k ON k.Id = sa.KeywordId
        JOIN dbo.Cities ci ON ci.Id = sa.CityId
        JOIN dbo.Companies co ON co.Id = al.CompanyId
        WHERE s.TenantId = @tenantId AND s.Id = @searchId
          AND (@aspectId IS NULL OR al.AspectId = @aspectId)
          AND (@q IS NULL OR co.Name LIKE @q OR co.Category LIKE @q OR co.Address LIKE @q OR co.Website LIKE @q)
          AND (@onlyNew = 0 OR (s.ParentSearchId IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM dbo.AspectLeads al2 JOIN dbo.SearchAspects sa2 ON sa2.Id = al2.AspectId
                WHERE sa2.SearchId = s.ParentSearchId AND al2.CompanyId = co.Id)))
        """;

    private const string LeadSelect = """
        SELECT co.Id AS CompanyId, al.AspectId, al.MapsRank, k.Keyword, ci.AsciiName AS City,
               co.Name, co.Category,
               (SELECT STRING_AGG(ch.NormalizedValue, ', ') FROM dbo.CompanyChannels ch WHERE ch.CompanyId = co.Id AND ch.ChannelType = 'Phone') AS Phones,
               (SELECT STRING_AGG(ch.NormalizedValue, ', ') FROM dbo.CompanyChannels ch WHERE ch.CompanyId = co.Id AND ch.ChannelType = 'Email') AS Emails,
               co.Website,
               (SELECT STRING_AGG(so.Url, ', ') FROM dbo.CompanySocials so WHERE so.CompanyId = co.Id) AS Socials,
               co.Address, co.Rating, co.ReviewCount, co.BusinessStatus, co.Latitude, co.Longitude, co.MapsUrl, al.FoundAt,
               co.OwnerName, co.TeamSize, co.Turnover, co.Gstin, co.PeopleEnrichedAt, co.LastEnrichedAt,
               (SELECT STRING_AGG(p.FullName + COALESCE(' (' + p.Designation + ')', ''), '; ')
                       WITHIN GROUP (ORDER BY p.IsOwner DESC, p.IsDecisionMaker DESC, p.FullName)
                FROM dbo.CompanyPeople p WHERE p.CompanyId = co.Id) AS People,
               (SELECT COUNT(*) FROM dbo.CompanyPeople p WHERE p.CompanyId = co.Id) AS PeopleCount,
               CAST(co.LeadScore AS INT) AS LeadScore,
               CAST(CASE WHEN s.ParentSearchId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM dbo.AspectLeads al2 JOIN dbo.SearchAspects sa2 ON sa2.Id = al2.AspectId
                        WHERE sa2.SearchId = s.ParentSearchId AND al2.CompanyId = co.Id) THEN 1 ELSE 0 END AS BIT) AS IsNew,
               (SELECT COUNT(*) FROM dbo.CompanyChannels ch WHERE ch.CompanyId = co.Id AND ch.SourceCount >= 2)
             + (SELECT COUNT(*) FROM dbo.CompanyPeople p WHERE p.CompanyId = co.Id AND p.SourceCount >= 2) AS VerifiedCount
        """;

    public async Task<IReadOnlyList<PersonExportRow>> GetPeopleForExportAsync(int tenantId, long searchId, long? aspectId)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<PersonExportRow>("""
            SELECT k.Keyword, ci.AsciiName AS City, co.Name AS Company,
                   p.FullName, p.Designation, p.IsOwner, p.IsDecisionMaker, p.Phone, p.Email,
                   p.LinkedInUrl, p.FacebookUrl, p.InstagramUrl, p.Source, p.SourceUrl, CAST(p.SourceCount AS INT) AS SourceCount
            FROM dbo.AspectLeads al
            JOIN dbo.SearchAspects sa ON sa.Id = al.AspectId
            JOIN dbo.Searches s ON s.Id = sa.SearchId
            JOIN dbo.SearchKeywords k ON k.Id = sa.KeywordId
            JOIN dbo.Cities ci ON ci.Id = sa.CityId
            JOIN dbo.Companies co ON co.Id = al.CompanyId
            JOIN dbo.CompanyPeople p ON p.CompanyId = co.Id
            WHERE s.TenantId = @tenantId AND s.Id = @searchId AND (@aspectId IS NULL OR al.AspectId = @aspectId)
            ORDER BY sa.Sequence, al.MapsRank, p.IsOwner DESC, p.IsDecisionMaker DESC
            """, new { tenantId, searchId, aspectId }, commandTimeout: 300);
        return rows.AsList();
    }

    public async Task<PagedResult<LeadRowDto>> GetLeadsAsync(int tenantId, long searchId, long? aspectId, string? search, int page, int pageSize, string? sort = null, bool onlyNew = false)
    {
        var q = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim() + "%";
        await using var c = await db.OpenAsync();
        var p = new { tenantId, searchId, aspectId, q, onlyNew, offset = (page - 1) * pageSize, pageSize };

        // Whitelisted sort orders only (never interpolate user input into SQL).
        var orderBy = sort switch
        {
            "score" => "ORDER BY co.LeadScore DESC, sa.Sequence, al.MapsRank",
            "reviews" => "ORDER BY co.ReviewCount DESC, co.Rating DESC, sa.Sequence",
            _ => "ORDER BY sa.Sequence, al.MapsRank",
        };

        var total = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) " + LeadFrom, p);
        var rows = await c.QueryAsync<LeadRowDto>(
            LeadSelect + "\n" + LeadFrom + $" {orderBy} OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY", p);
        return new PagedResult<LeadRowDto>(rows.AsList(), total, page, pageSize);
    }

    public async Task<IReadOnlyList<LeadRowDto>> GetAllLeadsAsync(int tenantId, long searchId, long? aspectId)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<LeadRowDto>(
            LeadSelect + "\n" + LeadFrom +" ORDER BY sa.Sequence, al.MapsRank",
            new { tenantId, searchId, aspectId, q = (string?)null, onlyNew = false },
            commandTimeout: 300);
        return rows.AsList();
    }
}
