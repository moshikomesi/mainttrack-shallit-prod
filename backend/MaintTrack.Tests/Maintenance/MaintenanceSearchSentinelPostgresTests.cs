using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Maintenance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Maintenance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.Clearance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MaintTrack.Tests.Maintenance;

/// <summary>
/// Maintenance search uses PostgreSQL ILIKE, so it runs only when <c>MAINTTRACK_CLEARANCE_PG_SERVER</c>
/// points at a server that can create a throwaway database. The database is dropped afterwards.
/// </summary>
public sealed class MaintenanceSearchSentinelPostgresTests : IAsyncLifetime
{
    private readonly string? _server = Environment.GetEnvironmentVariable(ClearancePostgresConcurrencyTests.ServerVariable);
    private readonly string _database = $"mainttrack_search_test_{Guid.NewGuid():N}";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _machineId = Guid.NewGuid();
    private readonly Guid _secondMachineId = Guid.NewGuid();
    private readonly Guid _v2CustomComponentId = Guid.NewGuid();
    private readonly Guid _v2CatalogComponentId = Guid.NewGuid();
    private readonly Guid _v1CrLfId = Guid.NewGuid();
    private readonly Guid _legacyId = Guid.NewGuid();
    private readonly Guid _legacyMentioningSentinelWordId = Guid.NewGuid();
    private readonly Guid _otherTenantEntryId = Guid.NewGuid();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
            return;

        await using (var admin = new NpgsqlConnection(_server))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_server)
        {
            Database = _database,
            Pooling = false
        }.ConnectionString;

        await using var db = Open(null);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var otherTenantMachineId = Guid.NewGuid();
        db.Machines.AddRange(
            Machine(_machineId, _tenantId, "machine.press", now),
            Machine(_secondMachineId, _tenantId, "machine.cutter", now),
            Machine(otherTenantMachineId, _otherTenantId, "machine.press", now));
        db.MaintenanceEntries.AddRange(
            Entry(_v2CustomComponentId, _tenantId, _machineId, "maintenanceLog.v2\nHydraulic pump\nSeal leaking near the valve.", now),
            Entry(_v2CatalogComponentId, _tenantId, _machineId, "maintenanceLog.v2\nmaintenanceComponent.bearing\nNoisy at startup.", now.AddMinutes(1)),
            Entry(_v1CrLfId, _tenantId, _machineId, "maintenanceLog.v1\r\nBelt slipping on the roller.", now.AddMinutes(2)),
            Entry(_legacyId, _tenantId, _machineId, "Gearbox\nOil change overdue.", now.AddMinutes(3)),
            Entry(_legacyMentioningSentinelWordId, _tenantId, _machineId, "Checked the maintenanceLog binder.", now.AddMinutes(4)),
            Entry(_otherTenantEntryId, _otherTenantId, otherTenantMachineId, "maintenanceLog.v2\nHydraulic pump\nSeal leaking near the valve.", now));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
            return;

        await using var admin = new NpgsqlConnection(_server);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task SentinelText_DoesNotMatchMarkedReports()
    {
        foreach (var search in new[] { "maintenanceLog.v2", "maintenanceLog.v1", "Log.v", "v2" })
            Assert.Empty(await SearchAsync(search));

        Assert.Equal(new[] { _legacyMentioningSentinelWordId }, await SearchAsync("maintenanceLog"));
    }

    [PostgresFact]
    public async Task ComponentText_StillFindsMarkedReports()
    {
        Assert.Equal(new[] { _v2CustomComponentId }, await SearchAsync("Hydraulic pump"));
        Assert.Equal(new[] { _v2CatalogComponentId }, await SearchAsync("bearing"));
    }

    [PostgresFact]
    public async Task NarrativeText_StillFindsMarkedReports()
    {
        Assert.Equal(new[] { _v2CustomComponentId }, await SearchAsync("seal LEAKING"));
        Assert.Equal(new[] { _v2CatalogComponentId }, await SearchAsync("startup"));
        Assert.Equal(new[] { _v1CrLfId }, await SearchAsync("roller"));
    }

    [PostgresFact]
    public async Task UnmarkedReports_RemainSearchable()
    {
        Assert.Equal(new[] { _legacyId }, await SearchAsync("Gearbox"));
        Assert.Equal(new[] { _legacyId }, await SearchAsync("oil change"));
    }

    [PostgresFact]
    public async Task Search_KeepsTheMachineFilterAndTenantIsolation()
    {
        Assert.Empty(await SearchAsync("Hydraulic pump", _secondMachineId));
        Assert.Equal(new[] { _otherTenantEntryId }, await SearchAsync("Hydraulic pump", tenantId: _otherTenantId));
    }

    private async Task<Guid[]> SearchAsync(string search, Guid? machineId = null, Guid? tenantId = null)
    {
        var tenant = tenantId ?? _tenantId;
        var tenantContext = new TenantContext { TenantId = tenant };
        await using var db = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>().UseNpgsql(_connectionString).Options,
            tenantContext);
        var service = new MaintenanceEntryService(
            db,
            tenantContext,
            new StubUser(tenant),
            new NoOpFileStorage(),
            NullLogger<MaintenanceEntryService>.Instance);

        var results = await service.GetAsync(
            new GetMaintenanceEntriesRequest
            {
                MachineId = machineId,
                Search = search,
                PageNumber = 1,
                PageSize = 50
            },
            CancellationToken.None);

        return results.Select(entry => entry.Id).ToArray();
    }

    private MaintTrackDbContext Open(Guid? tenantId) =>
        new(
            new DbContextOptionsBuilder<MaintTrackDbContext>().UseNpgsql(_connectionString).Options,
            new TenantContext { TenantId = tenantId });

    private static Machine Machine(Guid id, Guid tenantId, string name, DateTime createdAt) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Code = id.ToString("N")[..8],
            IsActive = true,
            CreatedAt = createdAt
        };

    private static MaintenanceEntry Entry(Guid id, Guid tenantId, Guid machineId, string description, DateTime createdAt) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 11, 14),
            Description = description,
            EmployeeName = "Worker",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = createdAt
        };

    private sealed class StubUser : ICurrentUserContext
    {
        public StubUser(Guid tenantId) => TenantId = tenantId;

        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; }
        public int RoleId => (int)UserRole.Worker;
        public UserRole Role => UserRole.Worker;
        public string DisplayName => "Worker";
    }

    private sealed class NoOpFileStorage : IFileStorageService
    {
        public Task<string> UploadAsync(
            Stream stream,
            string fileName,
            string contentType,
            string tenantId,
            CancellationToken ct) =>
            Task.FromResult($"/uploads/{fileName}");

        public Task DeleteAsync(string fileUrl, CancellationToken ct) => Task.CompletedTask;
    }
}
