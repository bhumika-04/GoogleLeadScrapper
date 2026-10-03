using System.Data;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.SqlClient;

namespace DeepLead.Migrator;

/// <summary>
/// Loads populated places for one country from the free GeoNames dump (https://download.geonames.org/export/dump/).
/// Upserts into dbo.Cities by GeoNameId, so it can be re-run to refresh.
/// </summary>
public static class GeoNamesImporter
{
    private const string DumpBaseUrl = "https://download.geonames.org/export/dump/";

    // Populated places only; skips sections of places (PPLX), historical (PPLH), abandoned (PPLQ), destroyed (PPLW), etc.
    private static readonly HashSet<string> IncludedFeatureCodes =
        ["PPL", "PPLA", "PPLA2", "PPLA3", "PPLA4", "PPLA5", "PPLC", "PPLG", "PPLS", "PPLF", "PPLL", "PPLR"];

    public static async Task<int> ImportCitiesAsync(string connectionString, string countryIso2)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DeepLead-Migrator/1.0");

        var regions = await LoadRegionNamesAsync(http, countryIso2);
        var table = await LoadCitiesAsync(http, countryIso2, regions);
        Console.WriteLine($"GeoNames: {table.Rows.Count} populated places for {countryIso2}.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var create = new SqlCommand("""
            CREATE TABLE #Cities
            (
                GeoNameId INT NOT NULL PRIMARY KEY,
                CountryIso2 CHAR(2) NOT NULL,
                Name NVARCHAR(200) NOT NULL,
                AsciiName NVARCHAR(200) NOT NULL,
                Region NVARCHAR(200) NULL,
                Latitude DECIMAL(9,6) NULL,
                Longitude DECIMAL(9,6) NULL,
                Population INT NULL
            );
            """, connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        using (var bulk = new SqlBulkCopy(connection) { DestinationTableName = "#Cities", BulkCopyTimeout = 600 })
        {
            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(table);
        }

        await using var merge = new SqlCommand("""
            MERGE dbo.Cities AS target
            USING #Cities AS source ON target.GeoNameId = source.GeoNameId
            WHEN MATCHED THEN UPDATE SET
                Name = source.Name, AsciiName = source.AsciiName, Region = source.Region,
                Latitude = source.Latitude, Longitude = source.Longitude, Population = source.Population
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (GeoNameId, CountryIso2, Name, AsciiName, Region, Latitude, Longitude, Population)
                VALUES (source.GeoNameId, source.CountryIso2, source.Name, source.AsciiName, source.Region, source.Latitude, source.Longitude, source.Population);
            """, connection) { CommandTimeout = 600 };

        return await merge.ExecuteNonQueryAsync();
    }

    // admin1CodesASCII.txt: "IN.35<TAB>Madhya Pradesh<TAB>Madhya Pradesh<TAB>1264542"
    private static async Task<Dictionary<string, string>> LoadRegionNamesAsync(HttpClient http, string countryIso2)
    {
        var text = await http.GetStringAsync(DumpBaseUrl + "admin1CodesASCII.txt");
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && parts[0].StartsWith(countryIso2 + ".", StringComparison.Ordinal))
                regions[parts[0][(countryIso2.Length + 1)..]] = parts[1].Trim();
        }
        return regions;
    }

    private static async Task<DataTable> LoadCitiesAsync(HttpClient http, string countryIso2, Dictionary<string, string> regions)
    {
        var table = new DataTable();
        table.Columns.Add("GeoNameId", typeof(int));
        table.Columns.Add("CountryIso2", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("AsciiName", typeof(string));
        table.Columns.Add("Region", typeof(string));
        table.Columns.Add("Latitude", typeof(decimal));
        table.Columns.Add("Longitude", typeof(decimal));
        table.Columns.Add("Population", typeof(int));

        await using var zipStream = await http.GetStreamAsync($"{DumpBaseUrl}{countryIso2}.zip");
        using var buffer = new MemoryStream();
        await zipStream.CopyToAsync(buffer);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        var entry = archive.GetEntry($"{countryIso2}.txt")
            ?? throw new InvalidOperationException($"{countryIso2}.txt not found in GeoNames archive.");

        using var reader = new StreamReader(entry.Open());
        while (await reader.ReadLineAsync() is { } line)
        {
            // geonameid, name, asciiname, alternatenames, lat, lng, feature class, feature code, country, cc2, admin1, admin2, admin3, admin4, population, ...
            var f = line.Split('\t');
            if (f.Length < 15 || f[6] != "P" || !IncludedFeatureCodes.Contains(f[7]))
                continue;

            var population = long.TryParse(f[14], out var p) ? (int)Math.Min(p, int.MaxValue) : 0;
            if (population <= 0)
                continue;   // unnamed hamlets etc.; users can still type any city

            table.Rows.Add(
                int.Parse(f[0], CultureInfo.InvariantCulture),
                countryIso2,
                Truncate(f[1], 200),
                Truncate(string.IsNullOrWhiteSpace(f[2]) ? f[1] : f[2], 200),
                regions.TryGetValue(f[10], out var region) ? region : null,
                Math.Round(decimal.Parse(f[4], CultureInfo.InvariantCulture), 6),
                Math.Round(decimal.Parse(f[5], CultureInfo.InvariantCulture), 6),
                population);
        }

        return table;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
