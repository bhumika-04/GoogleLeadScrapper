using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DeepLead.Core.Contracts;
using Microsoft.IdentityModel.Tokens;

namespace DeepLead.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "DeepLead";
    public string Audience { get; init; } = "DeepLead";
    /// <summary>At least 32 characters. Set via user secrets / environment, never in appsettings.json.</summary>
    public string Key { get; init; } = "";
    public int ExpiryHours { get; init; } = 12;

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));
}

public sealed class JwtTokenService(JwtOptions options)
{
    public LoginResponse CreateToken(UserDto user)
    {
        var expires = DateTime.UtcNow.AddHours(options.ExpiryHours);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(CurrentUser.TenantClaim, user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
        };

        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            expires: expires,
            signingCredentials: new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256));

        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user);
    }
}

public static class CurrentUser
{
    public const string TenantClaim = "tenant";

    public static int UserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static int TenantId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(TenantClaim)!);
}
