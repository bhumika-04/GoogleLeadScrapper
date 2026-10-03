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
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var hash = await users.GetPasswordHashAsync(User.UserId());
        if (hash is null || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, hash))
            return Conflict(new { message = "Current password is incorrect." });
        if (request.NewPassword.Length < UsersController.MinPasswordLength)
            return Conflict(new { message = $"New password must be at least {UsersController.MinPasswordLength} characters." });

        await users.SetPasswordHashAsync(User.UserId(), BCrypt.Net.BCrypt.HashPassword(request.NewPassword));
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await users.GetAsync(User.UserId());
        return user is null ? Unauthorized() : user;
    }
}
