using MaintTrack.Application.Clearance;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Clearance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// Races decisions through separate connections to one shared-cache in-memory SQLite database,
/// so every request has its own DbContext and transaction like concurrent HTTP requests.
/// </summary>
public sealed class ClearanceDecisionConcurrencyTests : IAsyncLifetime
{
    private const int ApproverCount = 6;

    private readonly string _connectionString =
        $"Data Source=clearance-race-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<Guid> _approverIds = new();
    private SqliteConnection _keepAlive = null!;
    private Guid _itemId;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();

        await using var db = CreateContext(_keepAlive);
        await db.Database.EnsureCreatedAsync();

        db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        for (var i = 0; i < ApproverCount; i++)
        {
            var userId = Guid.NewGuid();
            _approverIds.Add(userId);
            db.Users.Add(new User
            {
                Id = userId,
                TenantId = _tenantId,
                Username = $"approver{i}",
                Email = $"approver{i}@example.com",
                PasswordHash = "hash",
                DisplayName = $"approver{i}",
                RoleId = (int)UserRole.Worker,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            db.ClearanceApprovers.Add(new ClearanceApprover
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        var machineId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        _itemId = Guid.NewGuid();
        db.Machines.Add(new Machine
        {
            Id = machineId, TenantId = _tenantId, Name = "machine.race", Code = "R1", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.MaintenanceEntries.Add(new MaintenanceEntry
        {
            Id = entryId,
            TenantId = _tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 10, 15),
            Description = "race",
            EmployeeName = "Technician",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        });
        db.ClearanceItems.Add(new ClearanceItem
        {
            Id = _itemId,
            TenantId = _tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = entryId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = _approverIds[0],
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _keepAlive.DisposeAsync();

    [Fact]
    public async Task ConcurrentConflictingDecisions_ExactlyOneTransitionSucceeds()
    {
        using var start = new ManualResetEventSlim(false);

        var attempts = _approverIds
            .Select((userId, index) => Task.Run(async () =>
            {
                start.Wait();
                return await DecideAsync(userId, approve: index % 2 == 0, note: $"note-{index}");
            }))
            .ToList();

        start.Set();
        var outcomes = await Task.WhenAll(attempts);

        var winners = outcomes.Where(o => o.Result is not null).ToList();
        var winner = Assert.Single(winners).Result!;
        Assert.All(outcomes.Where(o => o.Result is null),
            o => Assert.IsType<ClearanceDecisionConflictException>(o.Error));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        var stored = await db.ClearanceItems.AsNoTracking().SingleAsync(c => c.Id == _itemId);
        Assert.Equal(winner.Status, stored.Status);
        Assert.Equal(winner.DecidedByUserId, stored.DecidedByUserId);
        Assert.Equal(winner.DecidedAt, stored.DecidedAt);
        Assert.Equal(winner.DecisionNote, stored.DecisionNote);

        var audit = Assert.Single(await db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(winner.DecidedByUserId, audit.UserId);
        Assert.Equal(winner.Status == ClearanceStatus.Approved ? "clearance_approved" : "clearance_rejected", audit.Action);
    }

    private async Task<(ClearanceDecisionDto? Result, Exception? Error)> DecideAsync(
        Guid userId, bool approve, string note)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        var service = new ClearanceService(
            db,
            new TenantContext { TenantId = _tenantId },
            new StubCurrentUserContext(_tenantId, userId));

        try
        {
            var result = approve
                ? await service.ApproveAsync(_itemId, CancellationToken.None)
                : await service.RejectAsync(_itemId, note, CancellationToken.None);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private MaintTrackDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<MaintTrackDbContext>().UseSqlite(connection).Options,
            new TenantContext { TenantId = _tenantId });
}
