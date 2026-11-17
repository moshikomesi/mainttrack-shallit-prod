using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using MaintTrack.Api.Auth;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Authentication;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Tenants;
using MaintTrack.Domain.Translations;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Translations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MaintTrack.Tests.Translations;

public sealed class MaintenanceTranslationsEndpointTests : IClassFixture<MaintenanceTranslationsReadFixture>
{
    private readonly MaintenanceTranslationsReadFixture _fixture;

    public MaintenanceTranslationsEndpointTests(MaintenanceTranslationsReadFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task Worker_CanReadTranslations_WithoutAClearanceGrant()
    {
        var before = _fixture.NarrativeReads;
        var response = await _fixture.Client.GetAsync($"/api/maintenance/{_fixture.EntryId}/translations");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_fixture.NarrativeReads > before);
        Assert.Contains("Seal leaking. Replaced the gasket.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("maintenanceComponent.bearing", ExtractOriginalText(body), StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret-do-not-leak", body, StringComparison.Ordinal);
        Assert.DoesNotContain(_fixture.SecretClaimToken.ToString(), body, StringComparison.Ordinal);
        Assert.DoesNotContain("claimToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lastError", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attemptCount", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jobId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("processing", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", body, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(body);
        Assert.Equal("Seal leaking. Replaced the gasket.", document.RootElement.GetProperty("originalText").GetString());
        var translations = document.RootElement.GetProperty("translations");
        Assert.Equal(3, translations.GetArrayLength());
        AssertStatus(translations[0], "he", "pending", null);
        AssertStatus(translations[1], "en", "completed", "Translated seal note");
        AssertStatus(translations[2], "th", "unavailable", null);
    }

    [Fact]
    public async Task MissingReport_Returns404_WithoutReadingTranslations()
    {
        var before = _fixture.NarrativeReads;
        var response = await _fixture.Client.GetAsync($"/api/maintenance/{Guid.NewGuid()}/translations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, _fixture.NarrativeReads);
    }

    [Fact]
    public async Task TokenWithoutTenant_Returns404_WithoutReadingTranslations()
    {
        var before = _fixture.NarrativeReads;
        using var client = _fixture.CreateClientWithoutTenant();

        var response = await client.GetAsync($"/api/maintenance/{_fixture.EntryId}/translations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, _fixture.NarrativeReads);
    }

    [Fact]
    public async Task CrossTenantReport_Returns404_WithoutReadingTranslations()
    {
        var before = _fixture.NarrativeReads;
        var response = await _fixture.Client.GetAsync($"/api/maintenance/{_fixture.OtherTenantEntryId}/translations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, _fixture.NarrativeReads);
    }

    private static string ExtractOriginalText(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("originalText").GetString() ?? string.Empty;
    }

    private static void AssertStatus(JsonElement item, string language, string status, string? text)
    {
        Assert.Equal(language, item.GetProperty("language").GetString());
        Assert.Equal(status, item.GetProperty("status").GetString());
        if (text is null)
            Assert.Equal(JsonValueKind.Null, item.GetProperty("text").ValueKind);
        else
            Assert.Equal(text, item.GetProperty("text").GetString());
    }
}

public sealed class MaintenanceTranslationsReadFixture : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;
    private readonly NarrativeReadCounter _counter = new();

    public HttpClient Client { get; private set; } = null!;
    public Guid EntryId { get; private set; }
    public Guid OtherTenantEntryId { get; private set; }
    public Guid SecretClaimToken { get; } = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    public int NarrativeReads => _counter.Count;

    public HttpClient CreateClientWithoutTenant()
    {
        var client = _factory.CreateClient();
        TranslationApiHost.Authenticate(client, _factory.Services, null, Guid.NewGuid(), (int)UserRole.Worker);
        return client;
    }

    public async Task InitializeAsync()
    {
        _connection.Open();
        _factory = TranslationApiHost.Create(_connection, enqueueEnabled: true, configure: services =>
        {
            services.RemoveAll<ITranslationRepository>();
            services.AddScoped<TranslationRepository>();
            services.AddScoped<ITranslationRepository>(sp =>
                new CountingTranslationRepository(sp.GetRequiredService<TranslationRepository>(), _counter));
        });
        Client = _factory.CreateClient();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        EntryId = Guid.NewGuid();
        OtherTenantEntryId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var otherMachineId = Guid.NewGuid();
        const string narrative = "Seal leaking. Replaced the gasket.";
        const string description = "maintenanceLog.v2\nmaintenanceComponent.bearing\n" + narrative;
        var hash = SourceTextHasher.Compute(narrative);
        var now = DateTime.UtcNow;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.AddRange(
            new Tenant { Id = tenantId, Name = "Tenant A", Status = TenantStatus.Active, CreatedAt = now },
            new Tenant { Id = otherTenantId, Name = "Tenant B", Status = TenantStatus.Active, CreatedAt = now });
        db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        db.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Username = "worker",
            Email = "worker@example.com",
            PasswordHash = "hash",
            DisplayName = "Worker",
            RoleId = (int)UserRole.Worker,
            IsActive = true,
            CreatedAt = now
        });
        db.Machines.AddRange(
            new Machine
            {
                Id = machineId,
                TenantId = tenantId,
                Name = "machine.a",
                Code = "A",
                IsActive = true,
                CreatedAt = now
            },
            new Machine
            {
                Id = otherMachineId,
                TenantId = otherTenantId,
                Name = "machine.b",
                Code = "B",
                IsActive = true,
                CreatedAt = now
            });
        db.MaintenanceEntries.AddRange(
            new MaintenanceEntry
            {
                Id = EntryId,
                TenantId = tenantId,
                MachineId = machineId,
                Date = new DateOnly(2026, 11, 13),
                Description = description,
                EmployeeName = "Worker",
                WorkHours = 1,
                IsSafeToOperate = true,
                CreatedByUserId = userId,
                CreatedAt = now
            },
            new MaintenanceEntry
            {
                Id = OtherTenantEntryId,
                TenantId = otherTenantId,
                MachineId = otherMachineId,
                Date = new DateOnly(2026, 11, 13),
                Description = description,
                EmployeeName = "Other",
                WorkHours = 1,
                IsSafeToOperate = true,
                CreatedByUserId = userId,
                CreatedAt = now
            });
        db.TranslationJobs.AddRange(
            Job(tenantId, EntryId, hash, narrative, TranslationLanguages.Hebrew, TranslationJobStatus.Processing, SecretClaimToken, null, now),
            Job(tenantId, EntryId, hash, narrative, TranslationLanguages.English, TranslationJobStatus.Completed, null, null, now),
            Job(tenantId, EntryId, hash, narrative, TranslationLanguages.Thai, TranslationJobStatus.Failed, Guid.NewGuid(), "provider-secret-do-not-leak", now));
        db.TranslatedTexts.Add(new TranslatedText
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = TranslatableFields.MaintenanceEntry,
            EntityId = EntryId,
            FieldName = TranslatableFields.Description,
            SourceHash = hash,
            TargetLanguage = TranslationLanguages.English,
            Text = "Translated seal note",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        TranslationApiHost.Authenticate(Client, _factory.Services, tenantId, userId, (int)UserRole.Worker);
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static TranslationJob Job(
        Guid tenantId,
        Guid entryId,
        string hash,
        string narrative,
        string language,
        string status,
        Guid? claimToken,
        string? lastError,
        DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = TranslatableFields.MaintenanceEntry,
            EntityId = entryId,
            FieldName = TranslatableFields.Description,
            SourceText = narrative,
            SourceHash = hash,
            TargetLanguage = language,
            Status = status,
            AttemptCount = status == TranslationJobStatus.Failed ? 4 : 1,
            ClaimToken = claimToken,
            LastError = lastError,
            CreatedAt = createdAt
        };
}

public sealed class MaintenanceTranslationsDisabledEndpointTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _entryId;

    public async Task InitializeAsync()
    {
        _connection.Open();
        _factory = TranslationApiHost.Create(_connection, enqueueEnabled: false, configure: services =>
        {
            services.RemoveAll<ITranslationRepository>();
            services.AddSingleton<ITranslationRepository, ThrowingTranslationRepository>();
        });
        _client = _factory.CreateClient();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        _entryId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        db.Machines.Add(new Machine
        {
            Id = machineId,
            TenantId = tenantId,
            Name = "machine.disabled",
            Code = "D",
            IsActive = true,
            CreatedAt = now
        });
        db.MaintenanceEntries.Add(new MaintenanceEntry
        {
            Id = _entryId,
            TenantId = tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 11, 13),
            Description = "maintenanceLog.v2\nHydraulic pump\nSeal leaking. Replaced the gasket.",
            EmployeeName = "Worker",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = userId,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        TranslationApiHost.Authenticate(_client, _factory.Services, tenantId, userId, (int)UserRole.Worker);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task DisabledFlag_DoesNotReadTranslationTables()
    {
        var response = await _client.GetAsync($"/api/maintenance/{_entryId}/translations");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Seal leaking. Replaced the gasket.", document.RootElement.GetProperty("originalText").GetString());
        Assert.All(document.RootElement.GetProperty("translations").EnumerateArray(), item =>
            Assert.Equal("unavailable", item.GetProperty("status").GetString()));
    }
}

public sealed class MaintenanceTranslationsRoleTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection.Open();
        _factory = TranslationApiHost.Create(_connection, configure: services =>
        {
            services.RemoveAll<ICurrentUserContext>();
            services.AddScoped<ICurrentUserContext>(_ => new FixedRoleUser(0));
        });
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        await db.Database.EnsureCreatedAsync();
        TranslationApiHost.Authenticate(_client, _factory.Services, Guid.NewGuid(), Guid.NewGuid(), (int)UserRole.Worker);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RoleBelowOne_Returns403()
    {
        var response = await _client.GetAsync($"/api/maintenance/{Guid.NewGuid()}/translations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

public sealed class MaintenanceTranslationEnqueueFailureEndpointTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TrackingFileStorage _files = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Guid _machineId;
    private Guid _typeId;

    public async Task InitializeAsync()
    {
        _connection.Open();
        _factory = TranslationApiHost.Create(_connection, enqueueEnabled: true, configure: services =>
        {
            services.RemoveAll<ITranslationRepository>();
            services.AddSingleton<ITranslationRepository, ThrowingTranslationRepository>();
            services.RemoveAll<IFileStorageService>();
            services.AddSingleton<IFileStorageService>(_files);
        });
        _client = _factory.CreateClient();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _machineId = Guid.NewGuid();
        _typeId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        db.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Username = "worker",
            Email = "worker@example.com",
            PasswordHash = "hash",
            DisplayName = "Worker",
            RoleId = (int)UserRole.Worker,
            IsActive = true,
            CreatedAt = now
        });
        db.Machines.Add(new Machine
        {
            Id = _machineId,
            TenantId = tenantId,
            Name = "machine.upload",
            Code = "U",
            IsActive = true,
            CreatedAt = now
        });
        db.MaintenanceTypes.Add(new MaintenanceType
        {
            Id = _typeId,
            TenantId = tenantId,
            Code = "other",
            IsActive = true,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        TranslationApiHost.Authenticate(_client, _factory.Services, tenantId, userId, (int)UserRole.Worker);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EnqueueFailure_DoesNotFailCreationOrDeleteUploadedPhotos()
    {
        const string description = "maintenanceLog.v2\nOther: Hydraulic pump\nSeal leaking. Replaced the gasket.";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_machineId.ToString()), "machineId");
        form.Add(new StringContent("2026-11-13"), "date");
        form.Add(new StringContent(_typeId.ToString()), "maintenanceTypeId");
        form.Add(new StringContent(description), "description");
        form.Add(new StringContent("1"), "workHours");
        form.Add(new StringContent("true"), "isSafeToOperate");
        var primary = new ByteArrayContent(new byte[] { 1, 2, 3 });
        primary.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(primary, "file", "primary.png");
        var extra = new ByteArrayContent(new byte[] { 4, 5, 6 });
        extra.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(extra, "additionalFiles", "extra.png");

        var response = await _client.PostAsync("/api/maintenance", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        Assert.Empty(_files.Deleted);
        Assert.Equal(2, _files.Uploaded.Count);

        using var created = JsonDocument.Parse(body);
        var createdId = created.RootElement.GetProperty("id").GetGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        var entry = await db.MaintenanceEntries
            .Include(item => item.AdditionalImages)
            .SingleAsync(item => item.Id == createdId);

        Assert.Equal(description, entry.Description);
        Assert.Equal(_files.Uploaded[0], entry.ImageUrl);
        Assert.Equal(_files.Uploaded[1], Assert.Single(entry.AdditionalImages).ImageUrl);
        Assert.Equal(
            1,
            await db.AuditLogs.CountAsync(log => log.EntityId == entry.Id && log.Action == "maintenance_created"));
    }
}

internal static class TranslationApiHost
{
    public static WebApplicationFactory<Program> Create(
        SqliteConnection connection,
        Action<IServiceCollection>? configure = null,
        bool enqueueEnabled = false)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "MaintTrack.Test",
                    ["Jwt:Audience"] = "MaintTrack.Api.Test",
                    ["Jwt:Key"] = "TestSecretKeyForJwtTokenGeneration12345678901234567890",
                    ["Translation:EnqueueEnabled"] = enqueueEnabled ? "true" : "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<MaintTrackDbContext>));
                services.RemoveAll(typeof(MaintTrackDbContext));
                services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(connection));
                configure?.Invoke(services);
            });
        });
    }

    public static void Authenticate(
        HttpClient client,
        IServiceProvider services,
        Guid? tenantId,
        Guid userId,
        int roleId)
    {
        using var scope = services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new("role_id", roleId.ToString()),
            new(ClaimTypes.Email, "worker@example.com"),
            new(ClaimTypes.Name, "worker"),
            new("display_name", "Worker")
        };
        if (tenantId is Guid tenant)
            claims.Add(new Claim("tenant_id", tenant.ToString()));

        var token = jwtService.GenerateToken(claims, DateTime.UtcNow.AddHours(1));
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookie.Name}={token}");
    }
}

internal sealed class NarrativeReadCounter
{
    public int Count { get; set; }
}

internal sealed class CountingTranslationRepository : ITranslationRepository
{
    private readonly TranslationRepository _inner;
    private readonly NarrativeReadCounter _counter;

    public CountingTranslationRepository(TranslationRepository inner, NarrativeReadCounter counter)
    {
        _inner = inner;
        _counter = counter;
    }

    public Task<IReadOnlyList<NarrativeTranslationRow>> GetNarrativeTranslationsAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        CancellationToken cancellationToken)
    {
        _counter.Count++;
        return _inner.GetNarrativeTranslationsAsync(entityId, entityType, fieldName, sourceHash, cancellationToken);
    }

    public Task<EnqueueTranslationResult> EnqueueAsync(
        EnqueueTranslationRequest request,
        CancellationToken cancellationToken) =>
        _inner.EnqueueAsync(request, cancellationToken);

    public Task<IReadOnlyList<EnqueueTranslationResult>> EnqueueAllAsync(
        IReadOnlyList<EnqueueTranslationRequest> requests,
        CancellationToken cancellationToken) =>
        _inner.EnqueueAllAsync(requests, cancellationToken);

    public Task<IReadOnlyList<TranslationJob>> ClaimPendingAsync(
        int batchSize,
        TimeSpan processingLease,
        CancellationToken cancellationToken) =>
        _inner.ClaimPendingAsync(batchSize, processingLease, cancellationToken);

    public Task RenewClaimsAsync(
        IReadOnlyCollection<TranslationClaim> claims,
        CancellationToken cancellationToken) =>
        _inner.RenewClaimsAsync(claims, cancellationToken);

    public Task<TranslationCompletionResult> CompleteAsync(
        Guid jobId,
        Guid claimToken,
        string sourceHash,
        string translatedText,
        CancellationToken cancellationToken) =>
        _inner.CompleteAsync(jobId, claimToken, sourceHash, translatedText, cancellationToken);

    public Task<bool> FailAsync(
        Guid jobId,
        Guid claimToken,
        string error,
        int maxAttempts,
        CancellationToken cancellationToken) =>
        _inner.FailAsync(jobId, claimToken, error, maxAttempts, cancellationToken);

    public Task<TranslatedText?> FindTranslationAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        _inner.FindTranslationAsync(entityId, entityType, fieldName, sourceHash, targetLanguage, cancellationToken);
}

internal sealed class ThrowingTranslationRepository : ITranslationRepository
{
    public Task<EnqueueTranslationResult> EnqueueAsync(
        EnqueueTranslationRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");

    public Task<IReadOnlyList<EnqueueTranslationResult>> EnqueueAllAsync(
        IReadOnlyList<EnqueueTranslationRequest> requests,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");

    public Task<IReadOnlyList<NarrativeTranslationRow>> GetNarrativeTranslationsAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");

    public Task<IReadOnlyList<TranslationJob>> ClaimPendingAsync(
        int batchSize,
        TimeSpan processingLease,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");

    public Task RenewClaimsAsync(
        IReadOnlyCollection<TranslationClaim> claims,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");

    public Task<TranslationCompletionResult> CompleteAsync(
        Guid jobId,
        Guid claimToken,
        string sourceHash,
        string translatedText,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");

    public Task<bool> FailAsync(
        Guid jobId,
        Guid claimToken,
        string error,
        int maxAttempts,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");

    public Task<TranslatedText?> FindTranslationAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("translation storage was accessed");
}

internal sealed class FixedRoleUser : ICurrentUserContext
{
    public FixedRoleUser(int roleId) => RoleId = roleId;

    public Guid UserId { get; } = Guid.NewGuid();
    public Guid TenantId { get; } = Guid.NewGuid();
    public int RoleId { get; }
    public UserRole Role => UserRole.Worker;
    public string DisplayName => "Blocked";
}

internal sealed class TrackingFileStorage : IFileStorageService
{
    public List<string> Uploaded { get; } = new();
    public List<string> Deleted { get; } = new();

    public Task<string> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string tenantId,
        CancellationToken ct)
    {
        var url = $"/uploads/{Guid.NewGuid():N}-{fileName}";
        Uploaded.Add(url);
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string fileUrl, CancellationToken ct)
    {
        Deleted.Add(fileUrl);
        return Task.CompletedTask;
    }
}
