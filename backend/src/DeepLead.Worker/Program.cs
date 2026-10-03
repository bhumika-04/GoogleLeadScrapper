using DeepLead.Core.Security;
using DeepLead.Data;
using DeepLead.Enrichment.People;
using DeepLead.Enrichment.Validation;
using DeepLead.Scrapers.Search;
using DeepLead.Scrapers.Sites;
using DeepLead.Scrapers.Web;
using DeepLead.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "DeepLead Worker");
builder.Services.AddSerilog(lc => lc
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured (use user secrets in development).");

builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
builder.Services.AddSingleton<SearchRepository>();
builder.Services.AddSingleton<LeadRepository>();
builder.Services.AddSingleton<PeopleRepository>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Scraping").Get<ScrapingOptions>() ?? new ScrapingOptions());

// Stage 2 building blocks (shared so the search rate limit / circuit breaker is global to the worker).
builder.Services.AddSingleton<IWebSearch, DuckDuckGoSearch>();
builder.Services.AddSingleton<PageFetcher>();
builder.Services.AddSingleton<IndiaMartClient>();
builder.Services.AddSingleton<EmailValidator>();
builder.Services.AddSingleton<PeopleDiscovery>();

var encryptionKey = builder.Configuration["Secrets:EncryptionKey"]
    ?? throw new InvalidOperationException("Secrets:EncryptionKey is not configured (32 random bytes, base64, in user secrets).");
builder.Services.AddSingleton(new SecretBox(encryptionKey));

builder.Services.AddHostedService<SearchRunner>();
builder.Services.AddHostedService<AccountConnector>();

var host = builder.Build();
host.Run();
