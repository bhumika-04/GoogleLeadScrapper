using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "DeepLead Worker");
builder.Services.AddSerilog(lc => lc
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day));

// Pipeline stages and Hangfire server are registered here as they are built (Stage 1: Google Maps first).

var host = builder.Build();
host.Run();
