namespace DeepLead.Core.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(int Id, int TenantId, string TenantName, string Email, string FullName, string Role);

public sealed record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);

/// <summary>Row used only inside the API for password checks; never returned to clients.</summary>
public sealed record UserCredentials(int Id, int TenantId, string TenantName, string Email, string FullName, string Role, string PasswordHash, bool IsActive);
