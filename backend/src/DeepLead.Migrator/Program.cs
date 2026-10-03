using System.Reflection;
using DbUp;
using DeepLead.Migrator;
using Microsoft.Extensions.Configuration;

// Usage:
//   DeepLead.Migrator                         apply pending DbUp scripts (creates the database if missing)
//   DeepLead.Migrator --import-cities IN      apply scripts, then load/refresh cities for a country from GeoNames
//   --connection "<conn string>"              override the configured connection string
// Connection string resolution order: --connection > env DEEPLEAD_ConnectionStrings__DefaultConnection > user secrets > appsettings.json
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables(prefix: "DEEPLEAD_")
    .AddCommandLine(args, new Dictionary<string, string> { ["--connection"] = "ConnectionStrings:DefaultConnection" })
    .Build();

var connectionString = config.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Connection string 'DefaultConnection' not configured. Use --connection, user secrets or DEEPLEAD_ConnectionStrings__DefaultConnection.");
    return 1;
}

EnsureDatabase.For.SqlDatabase(connectionString);

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), name => name.Contains(".Scripts."))
    .WithTransactionPerScript()
    .JournalToSqlTable("dbo", "SchemaVersions")
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(result.Error);
    Console.ResetColor();
    return 2;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("Database is up to date.");
Console.ResetColor();

var importCountry = config["import-cities"];
if (!string.IsNullOrWhiteSpace(importCountry))
{
    var count = await GeoNamesImporter.ImportCitiesAsync(connectionString, importCountry.Trim().ToUpperInvariant());
    Console.WriteLine($"Cities for {importCountry.ToUpperInvariant()}: {count} inserted/updated.");
}

return 0;
