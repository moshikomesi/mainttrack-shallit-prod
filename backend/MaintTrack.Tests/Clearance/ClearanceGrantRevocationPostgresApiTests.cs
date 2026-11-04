using System.Security.Claims;
using MaintTrack.Api.Auth;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Authentication;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Tenants;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// Runs the grant-revocation scenarios through the real API pipeline on PostgreSQL. Same opt-in
/// and throwaway-database lifecycle as <see cref="ClearancePostgresConcurrencyTests"/>: each test
/// creates its own <c>mainttrack_clearance_test_*</c> database and drops it afterwards.
/// </summary>
public sealed class ClearanceGrantRevocationPostgresApiTests : IAsyncLifetime
{
    private readonly string? _server = Environment.GetEnvironmentVariable(ClearancePostgresConcurrencyTests.ServerVariable);
    private readonly string _database = $"mainttrack_clearance_test_{Guid.NewGuid():N}";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _viewerId = Guid.NewGuid();
    private readonly Guid _approverId = Guid.NewGuid();
    private readonly Guid _entryId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private string _connectionString = string.Empty;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private MaintTrackDbContext? _dbContext;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
        {
            return;
        }

        await using (var admin = new NpgsqlConnection(_server))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, $"CREATE DATABASE \"{_database}\"");
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_server) { Database = _database, Pooling = false }.ConnectionString;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Jwt:Issuer", "MaintTrack.Test" },
                    { "Jwt:Audience", "MaintTrack.Api.Test" },
                    { "Jwt:Key", "TestSecretKeyForJwtTokenGeneration12345678901234567890" }
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<MaintTrackDbContext>));
                services.RemoveAll(typeof(MaintTrackDbContext));
                services.AddDbContext<MaintTrackDbContext>((_, options) => options.UseNpgsql(_connectionString));

                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService>(new RecordingFileStorageService());
            });
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        _dbContext = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>().UseNpgsql(_connectionString).Options,
            new TenantContext { TenantId = _tenantId });
        await _dbContext.Database.EnsureCreatedAsync();

        var machineId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant { Id = _tenantId, Name = "Clearance Factory", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        _dbContext.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        _dbContext.Users.AddRange(CreateUser(_viewerId, "revocable-viewer"), CreateUser(_approverId, "revocable-approver"));
        _dbContext.ClearanceViewers.AddRange(
            new ClearanceViewer { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _viewerId, CreatedAt = DateTime.UtcNow },
            new ClearanceViewer { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _approverId, CreatedAt = DateTime.UtcNow });
        _dbContext.ClearanceApprovers.Add(
            new ClearanceApprover { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _approverId, CreatedAt = DateTime.UtcNow });
        _dbContext.Machines.Add(new Machine
        {
            Id = machineId, TenantId = _tenantId, Name = "machine.local", Code = "L1", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        _dbContext.MaintenanceEntries.Add(new MaintenanceEntry
        {
            Id = _entryId,
            TenantId = _tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 10, 15),
            Description = "fault",
            EmployeeName = "Technician",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClearanceItems.Add(new ClearanceItem
        {
            Id = _itemId,
            TenantId = _tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = _entryId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
        {
            return;
        }

        if (_dbContext is not null) await _dbContext.DisposeAsync();
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();

        await using var admin = new NpgsqlConnection(_server);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
    }

    [PostgresFact]
    public Task RevokedViewer_OnPostgres_SameToken_LosesQueueBySourceAndAccessOnNextRequest() =>
        ClearanceGrantRevocationScenarios.RevokingViewerGrant_RemovesAllClearanceAccessOnNextRequest(Setup(_viewerId));

    [PostgresFact]
    public Task RevokedApproverWithViewerGrant_OnPostgres_SameToken_KeepsViewButCannotDecide() =>
        ClearanceGrantRevocationScenarios.RevokingApproverGrant_KeepsViewAccessButForbidsDecisions(Setup(_approverId));

    private ClearanceRevocationSetup Setup(Guid userId) =>
        new(_client!, _dbContext!, () => Authenticate(userId), _tenantId, userId, _entryId, _itemId);

    private void Authenticate(Guid userId)
    {
        using var scope = _factory!.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken(
            new[]
            {
                new Claim("user_id", userId.ToString()),
                new Claim("tenant_id", _tenantId.ToString()),
                new Claim("role_id", ((int)UserRole.Worker).ToString()),
                new Claim(ClaimTypes.Email, "clearance@example.com"),
                new Claim(ClaimTypes.Name, "clearance"),
                new Claim("display_name", "Clearance User")
            },
            DateTime.UtcNow.AddHours(8));
        _client!.DefaultRequestHeaders.Remove("Cookie");
        _client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookie.Name}={token}");
    }

    private User CreateUser(Guid id, string username) => new()
    {
        Id = id,
        TenantId = _tenantId,
        Username = username,
        Email = $"{username}@example.com",
        PasswordHash = "hash",
        DisplayName = username,
        RoleId = (int)UserRole.Worker,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
