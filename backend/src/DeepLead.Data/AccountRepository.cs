using Dapper;
using DeepLead.Core.Contracts;

namespace DeepLead.Data;

public sealed class AccountRepository(SqlConnectionFactory db)
{
    public async Task<IReadOnlyList<ConnectedAccountDto>> ListAsync(int tenantId)
    {
        await using var c = await db.OpenAsync();
        var rows = (await c.QueryAsync<ConnectedAccountDto>("""
            SELECT Platform, Status, AccountLabel, RequestedAt, ConnectedAt, LastUsedAt, LastError
            FROM dbo.ConnectedAccounts WHERE TenantId = @tenantId
            """, new { tenantId })).ToDictionary(r => r.Platform);

        // Always return every platform so the UI can show a card for each.
        return Platforms.All
            .Select(p => rows.TryGetValue(p, out var row)
                ? row
                : new ConnectedAccountDto(p, AccountStatus.Disconnected, null, null, null, null, null))
            .ToList();
    }

    public async Task RequestConnectAsync(int tenantId, int userId, string platform, string? label)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("""
            MERGE dbo.ConnectedAccounts AS t
            USING (SELECT @tenantId AS TenantId, @platform AS Platform) AS s
               ON t.TenantId = s.TenantId AND t.Platform = s.Platform
            WHEN MATCHED THEN UPDATE SET
                Status = 'ConnectRequested', AccountLabel = COALESCE(@label, t.AccountLabel), RequestedByUserId = @userId,
                RequestedAt = SYSUTCDATETIME(), LastError = NULL, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (TenantId, Platform, Status, AccountLabel, RequestedByUserId, RequestedAt)
                VALUES (@tenantId, @platform, 'ConnectRequested', @label, @userId, SYSUTCDATETIME());
            """, new { tenantId, userId, platform, label });
    }

    /// <summary>User says the login is done; the worker saves the session on its next check.</summary>
    public async Task<bool> RequestSaveAsync(int tenantId, string platform)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteAsync("""
            UPDATE dbo.ConnectedAccounts SET Status = 'SaveRequested', UpdatedAt = SYSUTCDATETIME()
            WHERE TenantId = @tenantId AND Platform = @platform AND Status = 'WaitingForLogin'
            """, new { tenantId, platform }) == 1;
    }

    public async Task<string?> GetStatusAsync(int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<string?>("SELECT Status FROM dbo.ConnectedAccounts WHERE Id = @id", new { id });
    }

    public async Task DisconnectAsync(int tenantId, string platform)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("""
            UPDATE dbo.ConnectedAccounts
            SET Status = 'Disconnected', SessionState = NULL, LastError = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE TenantId = @tenantId AND Platform = @platform
            """, new { tenantId, platform });
    }

    // ---------- Worker side ----------

    /// <summary>Claims the oldest connect request (moves it to WaitingForLogin so it isn't picked twice).</summary>
    public async Task<ConnectJob?> ClaimConnectRequestAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<ConnectJob>("""
            WITH next AS (
                SELECT TOP (1) * FROM dbo.ConnectedAccounts WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = 'ConnectRequested' ORDER BY RequestedAt
            )
            UPDATE next SET Status = 'WaitingForLogin', UpdatedAt = SYSUTCDATETIME()
            OUTPUT INSERTED.Id, INSERTED.TenantId, INSERTED.Platform;
            """);
    }

    /// <summary>Requests left in WaitingForLogin by a crashed worker are failed so the user can retry.</summary>
    public async Task FailStaleWaitsAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("""
            UPDATE dbo.ConnectedAccounts SET Status = 'Failed', LastError = 'Login window was closed (worker restarted). Try again.',
                UpdatedAt = SYSUTCDATETIME()
            WHERE Status IN ('WaitingForLogin', 'SaveRequested')
            """);
    }

    public async Task CompleteConnectAsync(int id, byte[] encryptedState, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("""
            UPDATE dbo.ConnectedAccounts
            SET Status = 'Connected', SessionState = @encryptedState, ConnectedAt = SYSUTCDATETIME(), LastError = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @id
            """, new { id, encryptedState });
    }

    public async Task FailConnectAsync(int id, string error, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("""
            UPDATE dbo.ConnectedAccounts SET Status = 'Failed', LastError = @error, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id
            """, new { id, error });
    }

    /// <summary>Encrypted session for a connected platform, or null. Marks it as used.</summary>
    public async Task<byte[]?> GetSessionAsync(int tenantId, string platform, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<byte[]?>("""
            UPDATE dbo.ConnectedAccounts SET LastUsedAt = SYSUTCDATETIME()
            OUTPUT INSERTED.SessionState
            WHERE TenantId = @tenantId AND Platform = @platform AND Status = 'Connected'
            """, new { tenantId, platform });
    }

    public async Task MarkExpiredAsync(int tenantId, string platform, string reason, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync("""
            UPDATE dbo.ConnectedAccounts SET Status = 'Expired', LastError = @reason, UpdatedAt = SYSUTCDATETIME()
            WHERE TenantId = @tenantId AND Platform = @platform AND Status = 'Connected'
            """, new { tenantId, platform, reason });
    }
}
