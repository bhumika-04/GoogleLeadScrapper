using DeepLead.Api.Auth;
using DeepLead.Core.Contracts;
using DeepLead.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepLead.Api.Controllers;

/// <summary>
/// Connected accounts. The API only records requests; the Worker opens the login window and stores the session,
/// so no password or cookie ever passes through the API.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,TenantAdmin")]
[Route("api/settings/accounts")]
public sealed class SettingsController(AccountRepository accounts) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ConnectedAccountDto>> List() => accounts.ListAsync(User.TenantId());

    [HttpPost("{platform}/connect")]
    public async Task<IActionResult> Connect(string platform, ConnectAccountRequest? request)
    {
        if (Normalize(platform) is not { } p)
            return NotFound();
        var label = request?.AccountLabel?.Trim();
        await accounts.RequestConnectAsync(User.TenantId(), User.UserId(), p, string.IsNullOrEmpty(label) ? null : label[..Math.Min(label.Length, 200)]);
        return Accepted();
    }

    [HttpPost("{platform}/save")]
    public async Task<IActionResult> Save(string platform)
    {
        if (Normalize(platform) is not { } p)
            return NotFound();
        return await accounts.RequestSaveAsync(User.TenantId(), p)
            ? Accepted()
            : Conflict(new { message = "No login window is open for this account." });
    }

    [HttpPost("{platform}/disconnect")]
    public async Task<IActionResult> Disconnect(string platform)
    {
        if (Normalize(platform) is not { } p)
            return NotFound();
        await accounts.DisconnectAsync(User.TenantId(), p);
        return NoContent();
    }

    private static string? Normalize(string platform) =>
        Platforms.All.FirstOrDefault(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase));
}
