using DeepLead.Core.Contracts;
using DeepLead.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepLead.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/geo")]
public sealed class GeoController(GeoRepository geo) : ControllerBase
{
    [HttpGet("countries")]
    public Task<IReadOnlyList<CountryDto>> Countries() => geo.GetCountriesAsync();

    [HttpGet("countries/{iso2}/cities")]
    public Task<IReadOnlyList<CityDto>> Cities(string iso2, [FromQuery] string? q, [FromQuery] int take = 20) =>
        geo.SearchCitiesAsync(iso2.ToUpperInvariant(), q, Math.Clamp(take, 1, 100));

    [HttpGet("countries/{iso2}/cities/top")]
    public Task<IReadOnlyList<CityDto>> TopCities(string iso2, [FromQuery] int n = 50) =>
        geo.GetTopCitiesAsync(iso2.ToUpperInvariant(), Math.Clamp(n, 1, 500));
}
