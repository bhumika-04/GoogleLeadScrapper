using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;

// Usage: DeepLead.Migrator [--connection "<conn string>"]
// Connection string resolution order: --connection arg > env DEEPLEAD_ConnectionStrings__DeepLead > user secrets > appsettings.json
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables(prefix: "DEEPLEAD_")
    .AddCommandLine(args, new Dictionary<string, string> { ["--connection"] = "ConnectionStrings:DeepLead" })
    .Build();

var connectionString = config.GetConnectionString("DeepLead");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Connection string 'DeepLead' not configured. Use --connection, user secrets or DEEPLEAD_ConnectionStrings__DeepLead.");
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
return 0;
