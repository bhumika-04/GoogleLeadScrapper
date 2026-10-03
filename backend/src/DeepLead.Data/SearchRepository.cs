using System.Text.Json;
using Dapper;
using DeepLead.Core.Contracts;

namespace DeepLead.Data;

public sealed class SearchRepository(SqlConnectionFactory db)
{
    // Column order must match SearchSummaryDto's constructor (Dapper maps positional records by order).
    private const string SummarySelect = """
        SELECT s.Id, s.Name, s.CountryIso2, co.Name AS CountryName, s.Status,
               (SELECT COUNT(*) FROM dbo.SearchCities sc WHERE sc.SearchId = s.Id) AS CityCount,
               (SELECT COUNT(*) FROM dbo.SearchKeywords sk WHERE sk.SearchId = s.Id) AS KeywordCount,
               a.AspectCount, a.AspectsCompleted,
               (SELECT COUNT(DISTINCT al.CompanyId) FROM dbo.AspectLeads al JOIN dbo.SearchAspects sa ON sa.Id = al.AspectId WHERE sa.SearchId = s.Id) AS LeadCount,
               s.CreatedAt
        FROM dbo.Searches s
        JOIN dbo.Countries co ON co.Iso2 = s.CountryIso2
        OUTER APPLY (SELECT COUNT(*) AS AspectCount,
                            COALESCE(SUM(CASE WHEN x.Status = 'Completed' THEN 1 ELSE 0 END), 0) AS AspectsCompleted
                     FROM dbo.SearchAspects x WHERE x.SearchId = s.Id) a
        """;

    /// <summary>
    /// Creates the session: user-typed cities, keywords, cities, and every city x keyword aspect (city-major order),
    /// all set-based through OPENJSON so 500 cities x 20 keywords is still a handful of round-trips.
    /// </summary>
    public async Task<long> CreateAsync(int tenantId, int userId, string name, CreateSearchRequest request)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();

        var cityIds = request.CityIds.ToList();
        foreach (var newCity in (request.NewCityNames ?? []).Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var id = await c.ExecuteScalarAsync<int?>("""
                SELECT TOP (1) Id FROM dbo.Cities
                WHERE CountryIso2 = @country AND (Name = @newCity OR AsciiName = @newCity)
                ORDER BY Population DESC
                """, new { country = request.CountryIso2, newCity }, tx)
                ?? await c.ExecuteScalarAsync<int>("""
                    INSERT INTO dbo.Cities (CountryIso2, Name, AsciiName, IsUserAdded)
                    OUTPUT INSERTED.Id VALUES (@country, @newCity, @newCity, 1)
                    """, new { country = request.CountryIso2, newCity }, tx);
            cityIds.Add(id);
        }

        var searchId = await c.ExecuteScalarAsync<long>("""
            INSERT INTO dbo.Searches (TenantId, CreatedByUserId, Name, CountryIso2, IcpPrompt)
            OUTPUT INSERTED.Id
            VALUES (@tenantId, @userId, @name, @country, @icp)
            """, new { tenantId, userId, name, country = request.CountryIso2, icp = string.IsNullOrWhiteSpace(request.IcpPrompt) ? null : request.IcpPrompt }, tx);

        await c.ExecuteAsync("""
            INSERT INTO dbo.SearchKeywords (SearchId, Keyword, SortOrder)
            SELECT @searchId, j.[value], CAST(j.[key] AS SMALLINT) + 1
            FROM OPENJSON(@keywords) j
            """, new { searchId, keywords = JsonSerializer.Serialize(request.Keywords) }, tx);

        // Only cities of this country; duplicates collapse to their first position.
        await c.ExecuteAsync("""
            INSERT INTO dbo.SearchCities (SearchId, CityId, SortOrder)
            SELECT @searchId, x.CityId, ROW_NUMBER() OVER (ORDER BY x.FirstPos)
            FROM (SELECT CAST(j.[value] AS INT) AS CityId, MIN(CAST(j.[key] AS INT)) AS FirstPos
                  FROM OPENJSON(@cityIds) j GROUP BY CAST(j.[value] AS INT)) x
            JOIN dbo.Cities ci ON ci.Id = x.CityId AND ci.CountryIso2 = @country
            """, new { searchId, cityIds = JsonSerializer.Serialize(cityIds), country = request.CountryIso2 }, tx);

        await c.ExecuteAsync("""
            INSERT INTO dbo.SearchAspects (SearchId, Sequence, CityId, KeywordId)
            SELECT @searchId, ROW_NUMBER() OVER (ORDER BY sc.SortOrder, sk.SortOrder), sc.CityId, sk.Id
            FROM dbo.SearchCities sc CROSS JOIN dbo.SearchKeywords sk
            WHERE sc.SearchId = @searchId AND sk.SearchId = @searchId;

            INSERT INTO dbo.AspectStages (AspectId, Stage)
            SELECT a.Id, s.Stage FROM dbo.SearchAspects a CROSS JOIN (VALUES (CAST(1 AS TINYINT)), (CAST(2 AS TINYINT))) s(Stage)
            WHERE a.SearchId = @searchId;
            """, new { searchId }, tx);

        await tx.CommitAsync();
        return searchId;
    }

    public async Task<IReadOnlyList<SearchSummaryDto>> ListAsync(int tenantId)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<SearchSummaryDto>(
            SummarySelect + "\nWHERE s.TenantId = @tenantId ORDER BY s.CreatedAt DESC", new { tenantId });
        return rows.AsList();
    }

    public async Task<SearchDetailDto?> GetAsync(int tenantId, long id)
    {
        await using var c = await db.OpenAsync();
        var summary = await c.QuerySingleOrDefaultAsync<SearchSummaryDto>(
            SummarySelect + "\nWHERE s.TenantId = @tenantId AND s.Id = @id", new { tenantId, id });
        if (summary is null)
            return null;

        var icp = await c.ExecuteScalarAsync<string?>("SELECT IcpPrompt FROM dbo.Searches WHERE Id = @id", new { id });
        var keywords = await c.QueryAsync<string>(
            "SELECT Keyword FROM dbo.SearchKeywords WHERE SearchId = @id ORDER BY SortOrder", new { id });
        var aspects = await c.QueryAsync<AspectDto>("""
            SELECT a.Id, a.Sequence, ci.AsciiName AS City, ci.Region, k.Keyword, a.Status, a.LeadCount,
                   st.ItemsTotal, COALESCE(st.ItemsDone, 0) AS ItemsDone, st.StartedAt, COALESCE(st2.FinishedAt, st.FinishedAt) AS FinishedAt,
                   COALESCE(st2.LastError, st.LastError) AS LastError,
                   st2.Status AS PeopleStatus, st2.ItemsTotal AS PeopleTotal, COALESCE(st2.ItemsDone, 0) AS PeopleDone
            FROM dbo.SearchAspects a
            JOIN dbo.Cities ci ON ci.Id = a.CityId
            JOIN dbo.SearchKeywords k ON k.Id = a.KeywordId
            LEFT JOIN dbo.AspectStages st ON st.AspectId = a.Id AND st.Stage = 1
            LEFT JOIN dbo.AspectStages st2 ON st2.AspectId = a.Id AND st2.Stage = 2
            WHERE a.SearchId = @id
            ORDER BY a.Sequence
            """, new { id });

        return new SearchDetailDto(summary, icp, keywords.AsList(), aspects.AsList());
    }

    /// <summary>User actions. Returns false when the session doesn't exist for this tenant or the transition isn't allowed.</summary>
    public async Task<bool> SetStatusAsync(int tenantId, long id, string newStatus, IReadOnlyCollection<string> allowedFrom)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.ExecuteAsync("""
            UPDATE dbo.Searches SET Status = @newStatus
            WHERE Id = @id AND TenantId = @tenantId AND Status IN @allowedFrom
            """, new { id, tenantId, newStatus, allowedFrom });
        return rows == 1;
    }

    // ---------- Worker side ----------

    /// <summary>Claims the next session to run: an interrupted 'Running' one first (worker restart), then oldest 'Pending'.</summary>
    public async Task<SearchRunInfo?> ClaimNextAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<SearchRunInfo>("""
            DECLARE @claimed TABLE (Id BIGINT);
            WITH next AS (
                SELECT TOP (1) Id, Status FROM dbo.Searches WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status IN ('Running', 'Pending')
                ORDER BY CASE Status WHEN 'Running' THEN 0 ELSE 1 END, CreatedAt
            )
            UPDATE next SET Status = 'Running' OUTPUT INSERTED.Id INTO @claimed;

            SELECT s.Id, s.TenantId, s.CountryIso2, co.Name AS CountryName
            FROM dbo.Searches s JOIN dbo.Countries co ON co.Iso2 = s.CountryIso2
            WHERE s.Id IN (SELECT Id FROM @claimed);
            """);
    }

    public async Task<string?> GetStatusAsync(long id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<string?>("SELECT Status FROM dbo.Searches WHERE Id = @id", new { id });
    }

    public async Task<IReadOnlyList<AspectRunInfo>> GetAspectsToRunAsync(long searchId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<AspectRunInfo>("""
            SELECT a.Id, a.Sequence, a.CityId, ci.AsciiName AS City, ci.Region, k.Keyword, a.Status, st.Status AS MapsStatus,
                   ci.Latitude, ci.Longitude, ci.Population
            FROM dbo.SearchAspects a
            JOIN dbo.Cities ci ON ci.Id = a.CityId
            JOIN dbo.SearchKeywords k ON k.Id = a.KeywordId
            LEFT JOIN dbo.AspectStages st ON st.AspectId = a.Id AND st.Stage = 1
            WHERE a.SearchId = @searchId AND a.Status NOT IN ('Completed', 'Cancelled')
            ORDER BY a.Sequence
            """, new { searchId });
        return rows.AsList();
    }

    /// <summary>Sets the aspect status and one stage row (created if missing). Started/finished timestamps follow the status.</summary>
    public async Task SetAspectStateAsync(long aspectId, string aspectStatus, string stageStatus, string? error = null,
        CancellationToken ct = default, byte stage = 1)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("""
            UPDATE dbo.SearchAspects SET Status = @aspectStatus WHERE Id = @aspectId;

            IF NOT EXISTS (SELECT 1 FROM dbo.AspectStages WHERE AspectId = @aspectId AND Stage = @stage)
                INSERT INTO dbo.AspectStages (AspectId, Stage) VALUES (@aspectId, @stage);

            UPDATE dbo.AspectStages
            SET Status = @stageStatus,
                StartedAt = CASE WHEN @stageStatus = 'Running' AND StartedAt IS NULL THEN SYSUTCDATETIME() ELSE StartedAt END,
                FinishedAt = CASE WHEN @stageStatus IN ('Completed', 'Failed', 'Cancelled') THEN SYSUTCDATETIME() ELSE NULL END,
                LastError = @error
            WHERE AspectId = @aspectId AND Stage = @stage;
            """, new { aspectId, aspectStatus, stageStatus, error, stage });
    }

    /// <summary>Marks the aspect itself done without touching its stage rows (keeps their notes/errors).</summary>
    public async Task CompleteAspectAsync(long aspectId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE dbo.SearchAspects SET Status = 'Completed' WHERE Id = @aspectId", new { aspectId });
    }

    public async Task SetAspectProgressAsync(long aspectId, int? itemsTotal, int itemsDone, CancellationToken ct = default, byte stage = 1)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("""
            UPDATE dbo.AspectStages SET ItemsTotal = COALESCE(@itemsTotal, ItemsTotal), ItemsDone = @itemsDone
            WHERE AspectId = @aspectId AND Stage = @stage;

            UPDATE dbo.SearchAspects SET LeadCount = (SELECT COUNT(*) FROM dbo.AspectLeads WHERE AspectId = @aspectId)
            WHERE Id = @aspectId;
            """, new { aspectId, itemsTotal, itemsDone, stage });
    }

    /// <summary>Only moves Running -> final, so a Pause/Cancel the user clicked meanwhile is not overwritten.</summary>
    public async Task FinishSearchAsync(long id, string finalStatus, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("UPDATE dbo.Searches SET Status = @finalStatus WHERE Id = @id AND Status = 'Running'", new { id, finalStatus });
    }
}
