namespace DeepLead.Core.Contracts;

public static class Roles
{
    public const string Admin = "Admin";             // platform admin (your team): all customers
    public const string TenantAdmin = "TenantAdmin"; // customer admin: own users + connected accounts
    public const string User = "User";               // runs searches

    public static readonly IReadOnlyList<string> All = [Admin, TenantAdmin, User];
}

public sealed record UserListItemDto
{
    public int Id { get; init; }
    public int TenantId { get; init; }
    public string Email { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Role { get; init; } = "";
    public bool IsActive { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CreateUserRequest(string Email, string FullName, string Role, string Password, int? TenantId);

public sealed record UpdateUserRequest(string? FullName, string? Role, bool? IsActive);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record TenantDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsActive { get; init; }
    public int UserCount { get; init; }
    public int SessionCount { get; init; }
    public int LeadCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CreateTenantRequest(string Name, string AdminEmail, string AdminName, string AdminPassword);

public sealed record UpdateTenantRequest(string? Name, bool? IsActive);
