using System.Net.Mail;
using DeepLead.Api.Auth;
using DeepLead.Core.Contracts;
using DeepLead.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepLead.Api.Controllers;

/// <summary>Customer workspaces – platform admins only.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/tenants")]
public sealed class TenantsController(UserRepository users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TenantDto>> List() => users.ListTenantsAsync();

    /// <summary>Creates a workspace together with its first TenantAdmin.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateTenantRequest request)
    {
        var email = request.AdminEmail.Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Workspace name is required."];
        if (!MailAddress.TryCreate(email, out _)) errors["adminEmail"] = ["Enter a valid email address."];
        if (string.IsNullOrWhiteSpace(request.AdminName)) errors["adminName"] = ["Admin name is required."];
        if ((request.AdminPassword ?? "").Length < UsersController.MinPasswordLength)
            errors["adminPassword"] = [$"Password must be at least {UsersController.MinPasswordLength} characters."];
        if (errors.Count == 0 && await users.EmailExistsAsync(email)) errors["adminEmail"] = ["A user with this email already exists."];
        if (errors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(errors));

        var id = await users.CreateTenantAsync(request.Name.Trim(), email, request.AdminName.Trim(), BCrypt.Net.BCrypt.HashPassword(request.AdminPassword));
        return Ok(new { id });
    }

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateTenantRequest request)
    {
        if (id == User.TenantId() && request.IsActive == false)
            return Conflict(new { message = "You can't deactivate your own workspace." });
        return await users.UpdateTenantAsync(id, string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(), request.IsActive)
            ? NoContent()
            : NotFound();
    }
}
