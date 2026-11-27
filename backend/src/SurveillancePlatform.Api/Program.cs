using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SurveillancePlatform.Api.Background;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Options;
using SurveillancePlatform.Api.Security;
using SurveillancePlatform.Api.Services;
using SurveillancePlatform.Api.Storage;
using SurveillancePlatform.Api.Utilities;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Options (secrets must come from environment variables) ----------

builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
builder.Services.Configure<ServiceAuthOptions>(config.GetSection(ServiceAuthOptions.SectionName));
builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));
builder.Services.Configure<RetentionOptions>(config.GetSection(RetentionOptions.SectionName));
builder.Services.Configure<ReportingOptions>(config.GetSection(ReportingOptions.SectionName));
builder.Services.Configure<CacheOptions>(config.GetSection(CacheOptions.SectionName));

var jwtKey = config.GetSection(JwtOptions.SectionName)[nameof(JwtOptions.Key)];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be set to at least 32 characters (use an environment variable).");

if (string.IsNullOrWhiteSpace(config.GetSection(ServiceAuthOptions.SectionName)[nameof(ServiceAuthOptions.ApiKey)]))
    throw new InvalidOperationException("ServiceAuth:ApiKey must be set (use an environment variable).");

var connectionString = config.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

// ---------- Persistence ----------

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

// RTSP URLs are encrypted at rest. Keys must persist across container restarts.
builder.Services.AddDataProtection()
    .SetApplicationName("surveillance-platform")
    .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeyPath"] ?? "keys"));

var redisConnection = config.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// ---------- Snapshot storage ----------

var storageOptions = config.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
if (storageOptions.Provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAmazonS3>(sp =>
    {
        var s3 = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value.S3;
        var s3Config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Region) };
        if (!string.IsNullOrWhiteSpace(s3.ServiceUrl))
        {
            s3Config.ServiceURL = s3.ServiceUrl;
            s3Config.ForcePathStyle = true;
        }
        return new AmazonS3Client(s3Config);
    });
    builder.Services.AddSingleton<ISnapshotStorage, S3SnapshotStorage>();
}
else
{
    builder.Services.AddSingleton<ISnapshotStorage, LocalSnapshotStorage>();
}

// ---------- Application services ----------

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<RtspUrlProtector>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<PersonService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddHostedService<RetentionService>();

// ---------- Authentication & authorization ----------

var jwtOptions = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ServiceKeyAuthenticationHandler>(ServiceKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.Admin, p => p.RequireRole("Admin"));

// ---------- Reverse proxy (nginx) ----------
// Only proxies listed in Proxy:TrustedProxies (IPs or CIDRs) may set X-Forwarded-For / X-Forwarded-Proto.
// Without this, every client behind nginx shares one rate-limit bucket.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var entry in config.GetSection("Proxy:TrustedProxies").Get<string[]>() ?? [])
    {
        if (entry.Contains('/'))
        {
            o.KnownNetworks.Add(IPNetwork.Parse(entry));
        }
        else
        {
            o.KnownProxies.Add(System.Net.IPAddress.Parse(entry));
        }
    }
});

// ---------- Rate limiting (per client IP on auth endpoints) ----------

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy<string>(RateLimitPolicies.Auth, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------- Errors ----------

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// ---------- Web ----------

var corsOrigins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy("web", p =>
    p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Surveillance Platform Admin API",
        Version = "v1",
        Description = "Multi-tenant admin API for homes, cameras, attendance and dashboards. Use the Authorize button with a JWT access token from POST /api/auth/login. Internal endpoints are documented separately."
    });
    c.SwaggerDoc("internal", new OpenApiInfo
    {
        Title = "Monitoring Engine (internal)",
        Version = "v1",
        Description = "Endpoints used by the Monitoring Engine. Requires the X-Service-Key header."
    });

    c.DocInclusionPredicate((docName, api) =>
        docName == "internal"
            ? string.Equals(api.GroupName, "internal", StringComparison.Ordinal)
            : !string.Equals(api.GroupName, "internal", StringComparison.Ordinal));

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT access token from POST /api/auth/login"
    });
    c.AddSecurityDefinition(ServiceKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ServiceKeyAuthenticationHandler.HeaderName,
        Description = "Service key for the Monitoring Engine"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile), includeControllerXmlComments: true);
});

var app = builder.Build();

// ---------- Startup tasks ----------

if (config.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// ---------- Pipeline ----------

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || config.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "Admin API");
        o.SwaggerEndpoint("/swagger/internal/swagger.json", "Monitoring Engine (internal)");
    });
}

app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

app.Run();

// Exposed for integration tests.
public partial class Program;
