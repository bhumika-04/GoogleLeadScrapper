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

    public async Task TouchLastLoginAsync(int id)
    {
        await using var c = await db.OpenAsync();
        await c.ExecuteAsync("UPDATE dbo.Users SET LastLoginAt = SYSUTCDATETIME() WHERE Id = @id", new { id });
    }
}
