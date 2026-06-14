using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using MaintTrack.Api.Auth;
using Microsoft.AspNetCore.Http;
using MaintTrack.Api.Endpoints;
using MaintTrack.Api.Middleware;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Authentication;
using MaintTrack.Application.Configuration;
using MaintTrack.Application.Machines;
using MaintTrack.Infrastructure.Authentication;
using MaintTrack.Infrastructure.Machines;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MaintTrack.Infrastructure.MorningRound;
using MaintTrack.Application.MorningRound;
using MaintTrack.Infrastructure.Storage;
using MaintTrack.Application.Forklifts;
using MaintTrack.Application.Maintenance;
using MaintTrack.Application.MaintenanceTasks;
using MaintTrack.Application.Reports.ForkliftReports;
using MaintTrack.Application.Reports.MaintenanceTasks;
using MaintTrack.Application.Treatments;
using MaintTrack.Application.AnnualPlans;
using MaintTrack.Infrastructure.Forklifts;
using MaintTrack.Infrastructure.Maintenance;
using MaintTrack.Infrastructure.MaintenanceTasks;
using MaintTrack.Infrastructure.Reports;
using MaintTrack.Infrastructure.Treatments;
using MaintTrack.Infrastructure.AnnualPlans;
using Amazon.S3;
using Amazon.Extensions.NETCore.Setup;
using Amazon;
using Amazon.Runtime;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Configuration
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// Core services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();

// Authentication services
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ILoginService, LoginService>();

// Machines
builder.Services.AddScoped<
    IMachineService,
    MaintTrack.Infrastructure.Machines.MachineService>();

// Morning Round
builder.Services.AddScoped<
    IMorningRoundService,
    MaintTrack.Infrastructure.MorningRound.MorningRoundService>();

builder.Services.AddScoped<
    IMorningRoundTemplateService,
    MorningRoundTemplateService>();
// Maintenance
builder.Services.AddScoped<
    IMaintenanceEntryService,
    MaintTrack.Infrastructure.Maintenance.MaintenanceEntryService>();

builder.Services.AddScoped<IMaintenanceTypeService, MaintTrack.Infrastructure.Maintenance.MaintenanceTypeService>();
builder.Services.AddScoped<IMaintenanceTasksRepository, MaintenanceTasksRepository>();
builder.Services.AddScoped<IMaintenanceTasksService, MaintenanceTasksService>();

builder.Services.AddScoped<ITreatmentService, TreatmentService>();

builder.Services.AddScoped<IAnnualPlanService, AnnualPlanService>();

builder.Services.AddScoped<IForkliftReportService, ForkliftReportService>();
builder.Services.AddScoped<IForkliftService, ForkliftService>();
builder.Services.AddScoped<IForkliftReportsQueryService, ForkliftReportsQueryService>();
builder.Services.AddScoped<IMaintenanceTasksReportService, MaintenanceTasksReportService>();
if (string.Equals(builder.Configuration["Storage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
{
var s3Section = builder.Configuration.GetSection("S3");

var accessKey = s3Section["AccessKey"];
var secretKey = s3Section["SecretKey"];
var region = s3Section["Region"];

if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
{
    throw new Exception("S3 credentials are missing in configuration");
}

var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);

builder.Services.AddSingleton<IAmazonS3>(_ =>
    new AmazonS3Client(credentials, Amazon.RegionEndpoint.GetBySystemName(region))
);

var storageProvider = builder.Configuration["Storage:Provider"];

if (string.Equals(storageProvider, "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IFileStorageService, S3FileStorageService>();
}
}
else
{
    builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
}


// Database / EF Core
builder.Services.AddDbContext<MaintTrackDbContext>((sp, options) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    // Prefer environment variables, but fall back to appsettings
    var connectionString =
        Environment.GetEnvironmentVariable("MAINTTRACK_DB_CONNECTION") ??
        configuration.GetConnectionString("Default") ??
        throw new InvalidOperationException("Database connection string is not configured.");

    options.UseNpgsql(connectionString);
});

// Authentication / Authorization
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtOptions = builder.Configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? throw new InvalidOperationException("JWT configuration is missing.");

        if (!builder.Environment.IsDevelopment() &&
            (string.IsNullOrEmpty(jwtOptions.Key) ||
             string.Equals(jwtOptions.Key, "CHANGE_ME_SUPER_SECRET_KEY", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "JWT signing key must be set to a secure value in production. Do not use the default key (CHANGE_ME_SUPER_SECRET_KEY). " +
                "Set Jwt:Key via configuration or environment.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            NameClaimType = "unique_name",
        };

        // Map claim names to standard claim types
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = ClaimTypes.Name;

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthCookie.Name, out var token)
                    && !string.IsNullOrWhiteSpace(token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
         limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit = 10000;

    });

    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
         limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit = 100;

    });
});

// API + Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var productionFrontendOrigin = "https://mt.shallit.co.il";
var stagingFrontendOrigin = "https://mt-test.shallit.co.il";

var localDevOrigins = new[]
{
    "http://localhost:3000",
    "http://localhost:5173",
    "http://localhost:4173",
    "http://localhost:5062",
};

var productionOrigins = new[] { productionFrontendOrigin };
var stagingOrigins = new[] { stagingFrontendOrigin };

string[] originValidationOrigins;
string corsPolicyName;

if (builder.Environment.IsDevelopment())
{
    originValidationOrigins = productionOrigins
        .Concat(stagingOrigins)
        .Concat(localDevOrigins)
        .ToArray();
    corsPolicyName = "dev";
}
else if (builder.Environment.IsStaging())
{
    originValidationOrigins = stagingOrigins;
    corsPolicyName = "staging";
}
else
{
    originValidationOrigins = productionOrigins;
    corsPolicyName = "production";
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("dev",
        policy => policy
            .WithOrigins(originValidationOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());

    options.AddPolicy("staging",
        policy => policy
            .WithOrigins(stagingOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());

    options.AddPolicy("production", policy =>
        policy
            .WithOrigins(productionOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
//    db.Database.Migrate();
//}

app.UseForwardedHeaders();

// Middleware pipeline
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(corsPolicyName);

app.UseMiddleware<SecurityHeadersMiddleware>();
//app.UseMiddleware<OriginValidationMiddleware>(originValidationOrigins);
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

// Health endpoint
app.MapGet("/health", async (MaintTrackDbContext dbContext) =>
    {
        var canConnect = await dbContext.Database.CanConnectAsync();

        return Results.Ok(new
        {
            status = canConnect ? "Healthy" : "Degraded",
            checks = new
            {
                database = canConnect ? "Healthy" : "Unhealthy"
            }
        });
    })
    .WithName("Health")
    .WithOpenApi();

app.MapAuth();
app.MapMachines();
app.MapMorningRoundEndpoints();
app.MapMaintenanceEntryEndpoints();
app.MapMaintenanceTasksController();
app.MapMaintenanceTypeEndpoints();
app.MapTreatmentEndpoints();
app.MapForkliftEndpoints();
app.MapAnnualPlanEndpoints();
app.MapReportsEndpoints();
app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
