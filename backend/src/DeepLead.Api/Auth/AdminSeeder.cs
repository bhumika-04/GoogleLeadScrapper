using DeepLead.Data;

namespace DeepLead.Api.Auth;

/// <summary>
/// On first start with an empty Users table, creates the first tenant and admin from config
/// (Seed:TenantName, Seed:AdminEmail, Seed:AdminPassword – keep these in user secrets).
/// </summary>
public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var users = services.GetRequiredService<UserRepository>();
        if (await users.AnyUsersAsync())
            return;

        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No users exist and Seed:AdminEmail / Seed:AdminPassword are not set – nobody can log in yet.");
            return;
        }

        await users.CreateTenantWithAdminAsync(
            config["Seed:TenantName"] ?? "Default",
            email.Trim().ToLowerInvariant(),
            config["Seed:AdminName"] ?? "Administrator",
            BCrypt.Net.BCrypt.HashPassword(password));
        logger.LogInformation("Created first admin user {Email}", email);
    }
}
