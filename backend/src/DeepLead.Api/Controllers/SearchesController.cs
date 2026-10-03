using System.Text.RegularExpressions;
using DeepLead.Api.Auth;
using DeepLead.Core.Contracts;
using DeepLead.Data;
using DeepLead.Export;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepLead.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/searches")]
public sealed partial class SearchesController(
    SearchRepository searches,
    LeadRepository leads,
    PeopleRepository people,
    IValidator<CreateSearchRequest> validator) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SearchSummaryDto>> List() => searches.ListAsync(User.TenantId());

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SearchDetailDto>> Get(long id) =>
        await searches.GetAsync(User.TenantId(), id) is { } detail ? detail : NotFound();

    [HttpPost]
    public async Task<ActionResult<SearchDetailDto>> Create(CreateSearchRequest request)
    {
        request = request with
        {
            CountryIso2 = request.CountryIso2.Trim().ToUpperInvariant(),
            Keywords = request.Keywords.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };

        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var name = string.IsNullOrWhiteSpace(request.Name) ? AutoName(request) : request.Name.Trim();
        var id = await searches.CreateAsync(User.TenantId(), User.UserId(), name, request);
        return CreatedAtAction(nameof(Get), new { id }, await searches.GetAsync(User.TenantId(), id));
    }

    [HttpPost("{id:long}/pause")]
    public Task<IActionResult> Pause(long id) =>
        Transition(id, SearchStatus.Paused, SearchStatus.Pending, SearchStatus.Running);

    [HttpPost("{id:long}/resume")]
    public Task<IActionResult> Resume(long id) =>
        Transition(id, SearchStatus.Pending, SearchStatus.Paused, SearchStatus.Failed);

    [HttpPost("{id:long}/cancel")]
    public Task<IActionResult> Cancel(long id) =>
        Transition(id, SearchStatus.Cancelled, SearchStatus.Pending, SearchStatus.Running, SearchStatus.Paused);

    [HttpGet("{id:long}/leads")]
    public async Task<PagedResult<LeadRowDto>> Leads(long id, [FromQuery] long? aspectId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? sort = null) =>
        await leads.GetLeadsAsync(User.TenantId(), id, aspectId, q, Math.Max(page, 1), Math.Clamp(pageSize, 10, 500), sort);

    /// <summary>Company card: people (owner first), phones/emails, socials, sourced facts.</summary>
    [HttpGet("{id:long}/companies/{companyId:long}")]
    public async Task<ActionResult<CompanyDetailDto>> Company(long id, long companyId) =>
        await people.GetCompanyDetailAsync(User.TenantId(), companyId) is { } detail ? detail : NotFound();

    [HttpGet("{id:long}/export")]
    public async Task<IActionResult> Export(long id, [FromQuery] string format = "excel", [FromQuery] long? aspectId = null)
    {
        var detail = await searches.GetAsync(User.TenantId(), id);
        if (detail is null)
            return NotFound();

        var rows = await leads.GetAllLeadsAsync(User.TenantId(), id, aspectId);
        var fileBase = SafeFileName(detail.Summary.Name);

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            return File(LeadExporter.ToCsv(rows), "text/csv", $"{fileBase}.csv");

        var persons = await leads.GetPeopleForExportAsync(User.TenantId(), id, aspectId);
        return File(LeadExporter.ToExcel(detail.Summary.Name, rows, persons),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{fileBase}.xlsx");
    }

    private async Task<IActionResult> Transition(long id, string to, params string[] allowedFrom) =>
        await searches.SetStatusAsync(User.TenantId(), id, to, allowedFrom)
            ? NoContent()
            : Conflict(new { message = $"Session cannot be moved to {to} from its current state." });

    /// <summary>"Paper Trader +6 — India, 50 cities" / "Printing Companies — Indore".</summary>
    private static string AutoName(CreateSearchRequest r)
    {
        var keywords = r.Keywords.Count == 1 ? r.Keywords[0] : $"{r.Keywords[0]} +{r.Keywords.Count - 1}";
        var cityCount = r.CityIds.Count + (r.NewCityNames?.Count ?? 0);
        var where = cityCount == 1 && r.NewCityNames is { Count: 1 } ? r.NewCityNames[0]
            : cityCount == 1 ? $"{r.CountryIso2}, 1 city"
            : $"{r.CountryIso2}, {cityCount} cities";
        var name = $"{keywords} — {where}";
        return name.Length > 200 ? name[..200] : name;
    }

    [GeneratedRegex(@"[^\w\- ]+")]
    private static partial Regex UnsafeFileChars();

    private static string SafeFileName(string name)
    {
        var cleaned = UnsafeFileChars().Replace(name, "").Trim();
        return string.IsNullOrEmpty(cleaned) ? "leads" : cleaned[..Math.Min(cleaned.Length, 80)];
    }
}

public sealed class CreateSearchRequestValidator : AbstractValidator<CreateSearchRequest>
{
    public CreateSearchRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.CountryIso2).NotEmpty().Length(2);
        RuleFor(r => r.Keywords).NotEmpty().WithMessage("Enter at least one keyword.");
        RuleFor(r => r.Keywords.Count).LessThanOrEqualTo(50).WithName("Keywords");
        RuleForEach(r => r.Keywords).MaximumLength(200);
        RuleFor(r => r.CityIds.Count + (r.NewCityNames == null ? 0 : r.NewCityNames.Count))
            .InclusiveBetween(1, 1000).WithName("Cities").WithMessage("Select between 1 and 1000 cities.");
        RuleForEach(r => r.NewCityNames).MaximumLength(200);
        RuleFor(r => r.IcpPrompt).MaximumLength(20_000);
    }
}
