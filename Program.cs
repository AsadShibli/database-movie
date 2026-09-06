using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Npgsql;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Services.Ingestion;
using ReactionVideoAggregator.Services.Matching;
using ReactionVideoAggregator.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Swagger "Authorize" button sends x-api-key on subsequent requests.
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "x-api-key",
        Description = "Admin API key for POST endpoints"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Chrome extension content scripts call this API from other origins.
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExtensionPolicy", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// 60 requests / minute / IP for public catalog and extension endpoints.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("PublicApiPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Register EF Core with the PostgreSQL (Npgsql) provider.
// Connection string comes from configuration, not from code.
// EnableDynamicJson lets POCOs (PatreonData, ExternalLinks) map to jsonb.
var connectionString = NormalizeNpgsqlConnectionString(
    builder.Configuration.GetConnectionString("DefaultConnection"));
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(dataSource));

// Strategy Pattern: each platform has its own IVideoIngestor (scoped per request).
builder.Services.AddScoped<IVideoIngestor, YouTubeIngestor>();
builder.Services.AddScoped<IVideoIngestor, PatreonIngestor>();
builder.Services.AddScoped<PlatformIngestorFactory>();

// IHttpClientFactory for TMDB (and later real HTTP). Matching is scoped; the worker is a hosted singleton.
builder.Services.AddHttpClient();
builder.Services.Configure<TmdbOptions>(builder.Configuration.GetSection("TmdbOptions"));
builder.Services.AddScoped<TmdbMatchingService>();
builder.Services.AddHostedService<VideoMatchingWorker>();
builder.Services.AddHostedService<RssMonitoringWorker>();

var app = builder.Build();

// Apply pending EF migrations at boot (needed on Render free — no shell).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// Log full stack traces for unhandled 500s and return JSON to the client.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[CRITICAL API ERROR] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = ex.Message,
                detail = ex.InnerException?.Message
            });
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // Interactive dashboard: http://localhost:5000/swagger
    app.UseSwagger();
    app.UseSwaggerUI();
}

// HTTPS redirect breaks behind Render's reverse proxy; only use it locally.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("ExtensionPolicy");
app.UseRateLimiter();
app.UseAuthorization();
// Default files must run first so GET / maps to wwwroot/index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();

// Render Blueprint connection strings are often postgres:// URIs; Npgsql prefers keywords + SSL.
static string NormalizeNpgsqlConnectionString(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");

    if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return connectionString;

    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':', 2);
    var user = Uri.UnescapeDataString(userInfo[0]);
    var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var db = uri.AbsolutePath.Trim('/');
    var port = uri.Port > 0 ? uri.Port : 5432;
    return $"Host={uri.Host};Port={port};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
}
