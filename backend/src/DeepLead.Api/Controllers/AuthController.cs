using DeepLead.Api.Auth;
using DeepLead.Core.Contracts;
using DeepLead.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepLead.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserRepository users, JwtTokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var creds = await users.GetCredentialsByEmailAsync(request.Email.Trim().ToLowerInvariant());

        // Same response for unknown email and wrong password, so emails can't be probed.
        if (creds is null || !creds.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, creds.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        await users.TouchLastLoginAsync(creds.Id);
        var user = new UserDto(creds.Id, creds.TenantId, creds.TenantName, creds.Email, creds.FullName, creds.Role);
        return tokens.CreateToken(user);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await users.GetAsync(User.UserId());
        return user is null ? Unauthorized() : user;
    }
}
