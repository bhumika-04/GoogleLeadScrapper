using DeepLead.Data;
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
builder.Services.AddSingleton(builder.Configuration.GetSection("Scraping").Get<ScrapingOptions>() ?? new ScrapingOptions());
builder.Services.AddHostedService<SearchRunner>();

var host = builder.Build();
host.Run();
