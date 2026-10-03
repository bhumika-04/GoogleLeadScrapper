using Dapper;
using DeepLead.Core.Contracts;

namespace DeepLead.Data;

public sealed class GeoRepository(SqlConnectionFactory db)
{
    private const string CityColumns = "Id, AsciiName AS Name, Region, Population, IsUserAdded";

    public async Task<IReadOnlyList<CountryDto>> GetCountriesAsync()
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<CountryDto>(
            "SELECT Iso2, Name, PhoneCode FROM dbo.Countries WHERE IsEnabled = 1 ORDER BY Name");
        return rows.AsList();
    }

    /// <summary>Type-ahead: prefix matches first, then contains, biggest cities first.</summary>
    public async Task<IReadOnlyList<CityDto>> SearchCitiesAsync(string countryIso2, string? query, int take)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<CityDto>($"""
            SELECT TOP (@take) {CityColumns}
            FROM dbo.Cities
            WHERE CountryIso2 = @countryIso2
              AND (@query IS NULL OR AsciiName LIKE '%' + @query + '%' OR Name LIKE '%' + @query + '%')
            ORDER BY CASE WHEN @query IS NOT NULL AND (AsciiName LIKE @query + '%' OR Name LIKE @query + '%') THEN 0 ELSE 1 END,
                     Population DESC
            """, new { countryIso2, query = string.IsNullOrWhiteSpace(query) ? null : query.Trim(), take });
        return rows.AsList();
    }

    /// <summary>"Top 50/100/200/500" quick-add, by GeoNames population.</summary>
    public async Task<IReadOnlyList<CityDto>> GetTopCitiesAsync(string countryIso2, int count)
    {
        await using var c = await db.OpenAsync();
        var rows = await c.QueryAsync<CityDto>($"""
            SELECT TOP (@count) {CityColumns}
            FROM dbo.Cities
            WHERE CountryIso2 = @countryIso2 AND Population IS NOT NULL
            ORDER BY Population DESC
            """, new { countryIso2, count });
        return rows.AsList();
    }
}
