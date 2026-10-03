using System.Net.Mail;
using DeepLead.Api.Auth;
using DeepLead.Core.Contracts;
using DeepLead.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepLead.Api.Controllers;

/// <summary>
/// Users of a workspace. TenantAdmins manage their own workspace (roles TenantAdmin/User);
/// platform Admins can manage any workspace via ?tenantId= and may grant Admin only inside their own.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,TenantAdmin")]
[Route("api/users")]
public sealed class UsersController(UserRepository users) : ControllerBase
{
    public const int MinPasswordLength = 8;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> List([FromQuery] int? tenantId) =>
        ResolveTenant(tenantId) is { } t ? Ok(await users.ListAsync(t)) : Forbid();

    [HttpPost]
    public async Task<ActionResult<UserListItemDto>> Create(CreateUserRequest request)
    {
        if (ResolveTenant(request.TenantId) is not { } tenantId)
            return Forbid();

        var email = request.Email.Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string[]>();
        if (!MailAddress.TryCreate(email, out _)) errors["email"] = ["Enter a valid email address."];
        if (string.IsNullOrWhiteSpace(request.FullName)) errors["fullName"] = ["Name is required."];
        if (!CanAssignRole(request.Role, tenantId)) errors["role"] = ["You can't assign this role."];
        if ((request.Password ?? "").Length < MinPasswordLength) errors["password"] = [$"Password must be at least {MinPasswordLength} characters."];
        if (errors.Count == 0 && await users.EmailExistsAsync(email)) errors["email"] = ["A user with this email already exists."];
        if (errors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(errors));

        var id = await users.CreateAsync(tenantId, email, request.FullName.Trim(), request.Role, BCrypt.Net.BCrypt.HashPassword(request.Password));
        return Ok(await users.GetListItemAsync(id));
    }

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request)
    {
        var target = await users.GetListItemAsync(id);
        if (target is null || ResolveTenant(target.TenantId) is null || !CanManage(target))
            return NotFound();

        if (request.Role is not null && !CanAssignRole(request.Role, target.TenantId))
            return Conflict(new { message = "You can't assign this role." });

        var losesAdmin = target.IsActive && target.Role is Roles.Admin or Roles.TenantAdmin
            && (request.IsActive == false || request.Role is Roles.User);
        if (losesAdmin && target.Id == User.UserId())
            return Conflict(new { message = "You can't remove your own admin access or deactivate yourself." });
        if (losesAdmin && await users.CountActiveAdminsAsync(target.TenantId) <= 1)
            return Conflict(new { message = "A workspace needs at least one active admin." });

        await users.UpdateAsync(id, string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim(), request.Role, request.IsActive);
        return NoContent();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request)
    {
        var target = await users.GetListItemAsync(id);
        if (target is null || ResolveTenant(target.TenantId) is null || !CanManage(target))
            return NotFound();
        if (request.NewPassword.Length < MinPasswordLength)
            return Conflict(new { message = $"Password must be at least {MinPasswordLength} characters." });

        await users.SetPasswordHashAsync(id, BCrypt.Net.BCrypt.HashPassword(request.NewPassword));
        return NoContent();
    }

    private bool IsPlatformAdmin => User.IsInRole(Roles.Admin);

    /// <summary>Own tenant by default; other tenants only for platform admins.</summary>
    private int? ResolveTenant(int? requested)
    {
        var own = User.TenantId();
        if (requested is null || requested == own)
            return own;
        return IsPlatformAdmin ? requested : null;
    }

    private bool CanAssignRole(string role, int tenantId) => role switch
    {
        Roles.User or Roles.TenantAdmin => true,
        Roles.Admin => IsPlatformAdmin && tenantId == User.TenantId(),
        _ => false,
    };

    /// <summary>TenantAdmins can't edit platform admins.</summary>
    private bool CanManage(UserListItemDto target) => IsPlatformAdmin || target.Role != Roles.Admin;
}
