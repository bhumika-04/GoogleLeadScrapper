using Dapper;
using DeepLead.Core.Contracts;

namespace DeepLead.Data;

public sealed class UserRepository(SqlConnectionFactory db)
{
    private const string UserColumns = """
        u.Id, u.TenantId, t.Name AS TenantName, u.Email, u.FullName, u.Role
        """;

    public async Task<UserCredentials?> GetCredentialsByEmailAsync(string email)
    {
        await using var c = await db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<UserCredentials>($"""
            SELECT {UserColumns}, u.PasswordHash, CAST(CASE WHEN u.IsActive = 1 AND t.IsActive = 1 THEN 1 ELSE 0 END AS BIT) AS IsActive
            FROM dbo.Users u JOIN dbo.Tenants t ON t.Id = u.TenantId
            WHERE u.Email = @email
            """, new { email });
    }

    public async Task<UserDto?> GetAsync(int id)
    {
        await using var c = await db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<UserDto>($"""
            SELECT {UserColumns}
            FROM dbo.Users u JOIN dbo.Tenants t ON t.Id = u.TenantId
            WHERE u.Id = @id
            """, new { id });
    }

    public async Task<bool> AnyUsersAsync()
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Users) THEN 1 ELSE 0 END AS BIT)");
    }

    public async Task CreateTenantWithAdminAsync(string tenantName, string email, string fullName, string passwordHash)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        var tenantId = await c.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Tenants (Name) OUTPUT INSERTED.Id VALUES (@tenantName)", new { tenantName }, tx);
        await c.ExecuteAsync("""
            INSERT INTO dbo.Users (TenantId, Email, FullName, PasswordHash, Role)
            VALUES (@tenantId, @email, @fullName, @passwordHash, 'Admin')
            """, new { tenantId, email, fullName, passwordHash }, tx);
        await tx.CommitAsync();
    }

    /// <summary>Used on every authenticated request so deactivation takes effect immediately.</summary>
    public async Task<bool> IsActiveAsync(int id)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<bool>("""
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Users u JOIN dbo.Tenants t ON t.Id = u.TenantId
                WHERE u.Id = @id AND u.IsActive = 1 AND t.IsActive = 1) THEN 1 ELSE 0 END AS BIT)
            """, new { id });
    }

    public async Task<IReadOnlyList<UserListItemDto>> ListAsync(int tenantId)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<UserListItemDto>("""
            SELECT Id, TenantId, Email, FullName, Role, IsActive, LastLoginAt, CreatedAt
            FROM dbo.Users WHERE TenantId = @tenantId ORDER BY IsActive DESC, FullName
            """, new { tenantId });
        return rows.AsList();
    }

    public async Task<UserListItemDto?> GetListItemAsync(int id)
    {
        await using var c = await db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<UserListItemDto>("""
            SELECT Id, TenantId, Email, FullName, Role, IsActive, LastLoginAt, CreatedAt FROM dbo.Users WHERE Id = @id
            """, new { id });
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<bool>(
            "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE Email = @email) THEN 1 ELSE 0 END AS BIT)", new { email });
    }

    public async Task<int> CreateAsync(int tenantId, string email, string fullName, string role, string passwordHash)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<int>("""
            INSERT INTO dbo.Users (TenantId, Email, FullName, PasswordHash, Role)
            OUTPUT INSERTED.Id VALUES (@tenantId, @email, @fullName, @passwordHash, @role)
            """, new { tenantId, email, fullName, passwordHash, role });
    }

    public async Task UpdateAsync(int id, string? fullName, string? role, bool? isActive)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("""
            UPDATE dbo.Users SET
                FullName = COALESCE(@fullName, FullName),
                Role = COALESCE(@role, Role),
                IsActive = COALESCE(@isActive, IsActive)
            WHERE Id = @id
            """, new { id, fullName, role, isActive });
    }

    public async Task SetPasswordHashAsync(int id, string passwordHash)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("UPDATE dbo.Users SET PasswordHash = @passwordHash WHERE Id = @id", new { id, passwordHash });
    }

    public async Task<string?> GetPasswordHashAsync(int id)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<string?>("SELECT PasswordHash FROM dbo.Users WHERE Id = @id", new { id });
    }

    /// <summary>Active admins (Admin or TenantAdmin) in a tenant – used to prevent locking a tenant out.</summary>
    public async Task<int> CountActiveAdminsAsync(int tenantId)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Users WHERE TenantId = @tenantId AND IsActive = 1 AND Role IN ('Admin', 'TenantAdmin')", new { tenantId });
    }

    // ---------- Tenants (platform admin) ----------

    public async Task<IReadOnlyList<TenantDto>> ListTenantsAsync()
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<TenantDto>("""
            SELECT t.Id, t.Name, t.IsActive, t.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.Users u WHERE u.TenantId = t.Id) AS UserCount,
                   (SELECT COUNT(*) FROM dbo.Searches s WHERE s.TenantId = t.Id) AS SessionCount,
                   (SELECT COUNT(DISTINCT al.CompanyId) FROM dbo.AspectLeads al
                      JOIN dbo.SearchAspects sa ON sa.Id = al.AspectId JOIN dbo.Searches s ON s.Id = sa.SearchId
                    WHERE s.TenantId = t.Id) AS LeadCount
            FROM dbo.Tenants t ORDER BY t.Name
            """);
        return rows.AsList();
    }

    public async Task<int> CreateTenantAsync(string name, string adminEmail, string adminName, string passwordHash)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        var tenantId = await c.ExecuteScalarAsync<int>("INSERT INTO dbo.Tenants (Name) OUTPUT INSERTED.Id VALUES (@name)", new { name }, tx);
        await c.ExecuteAsync("""
            INSERT INTO dbo.Users (TenantId, Email, FullName, PasswordHash, Role)
            VALUES (@tenantId, @adminEmail, @adminName, @passwordHash, 'TenantAdmin')
            """, new { tenantId, adminEmail, adminName, passwordHash }, tx);
        await tx.CommitAsync();
        return tenantId;
    }

    public async Task<bool> UpdateTenantAsync(int id, string? name, bool? isActive)
    {
        await using var c = await db.OpenAsync();
        return await c.ExecuteAsync("""
            UPDATE dbo.Tenants SET Name = COALESCE(@name, Name), IsActive = COALESCE(@isActive, IsActive) WHERE Id = @id
            """, new { id, name, isActive }) == 1;
    }

    public async Task TouchLastLoginAsync(int id)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("UPDATE dbo.Users SET LastLoginAt = SYSUTCDATETIME() WHERE Id = @id", new { id });
    }
}
