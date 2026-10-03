using DeepLead.Api.Auth;
using DeepLead.Api.Controllers;
using DeepLead.Core.Contracts;
using DeepLead.Data;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// Services start in System32; relative paths (logs/) must resolve next to the exe.
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // As a Windows service the working directory is System32; read appsettings and write logs next to the exe.
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});
builder.Host.UseWindowsService(o => o.ServiceName = "DeepLead API");

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured (use user secrets in development).");

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters (set it in user secrets).");

builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<GeoRepository>();
builder.Services.AddSingleton<SearchRepository>();
builder.Services.AddSingleton<LeadRepository>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<PeopleRepository>();
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IValidator<CreateSearchRequest>, CreateSearchRequestValidator>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.SigningKey,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        // Deactivating a user or workspace takes effect immediately, not when the token expires.
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var repo = ctx.HttpContext.RequestServices.GetRequiredService<UserRepository>();
                if (!await repo.IsActiveAsync(ctx.Principal!.UserId()))
                    ctx.Fail("User or workspace is deactivated.");
            },
        };
    });
builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Content-Disposition")));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

await AdminSeeder.SeedAsync(app.Services, app.Configuration, app.Logger);

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();
