using MaintTrack.Application.Clearance;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Clearance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// Runs only when <c>MAINTTRACK_CLEARANCE_PG_SERVER</c> holds a connection string to a local
/// PostgreSQL server whose user may CREATE DATABASE. Each test creates its own uniquely named
/// throwaway database (<c>mainttrack_clearance_test_*</c>), and drops it afterwards; no other
/// database is read or modified.
/// </summary>
public sealed class ClearancePostgresConcurrencyTests : IAsyncLifetime
{
    public const string ServerVariable = "MAINTTRACK_CLEARANCE_PG_SERVER";
    private const int Contenders = 8;

    private readonly string? _server = Environment.GetEnvironmentVariable(ServerVariable);
    private readonly string _database = $"mainttrack_clearance_test_{Guid.NewGuid():N}";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<Guid> _approverIds = new();
    private string _connectionString = string.Empty;
    private Guid _entryId;
    private Guid _itemId;

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
            // Proves the explicit READ COMMITTED wins: under SERIALIZABLE the losing decisions would
            // fail with serialization errors (40001) instead of matching zero rows.
            await ExecuteAsync(admin, $"ALTER DATABASE \"{_database}\" SET default_transaction_isolation TO 'serializable'");
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_server) { Database = _database, Pooling = false }.ConnectionString;

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        for (var i = 0; i < Contenders; i++)
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
                Id = Guid.NewGuid(), TenantId = _tenantId, UserId = userId, CreatedAt = DateTime.UtcNow
            });
        }

        var machineId = Guid.NewGuid();
        _entryId = Guid.NewGuid();
        _itemId = Guid.NewGuid();
        db.Machines.Add(new Machine
        {
            Id = machineId, TenantId = _tenantId, Name = "machine.race", Code = "R1", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.MaintenanceEntries.Add(new MaintenanceEntry
        {
            Id = _entryId,
            TenantId = _tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 10, 20),
            Description = "race",
            EmployeeName = "Technician",
            WorkHours = 1,
            IsSafeToOperate = false,
            CreatedByUserId = _approverIds[0],
            CreatedAt = DateTime.UtcNow
        });
        db.ClearanceItems.Add(new ClearanceItem
        {
            Id = _itemId,
            TenantId = _tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = _entryId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = _approverIds[0],
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
        {
            return;
        }

        await using var admin = new NpgsqlConnection(_server);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
    }

    [PostgresFact]
    public async Task ConcurrentDecisions_OnPostgres_ExactlyOneTransitionAndOneAudit()
    {
        await using var blocker = new NpgsqlConnection(_connectionString);
        await blocker.OpenAsync();
        await using var blockingTransaction = await blocker.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand("SELECT id FROM clearance_items WHERE id = @id FOR UPDATE", blocker, blockingTransaction))
        {
            lockRow.Parameters.AddWithValue("id", _itemId);
            await lockRow.ExecuteNonQueryAsync();
        }

        var attempts = _approverIds
            .Select((userId, index) => Task.Run(() => DecideAsync(userId, approve: index % 2 == 0, note: $"note-{index}")))
            .ToList();

        // Release the row only once every decision is queued on it, so all of them race the same pending row.
        await WaitForBlockedUpdatesAsync(Contenders);
        await blockingTransaction.CommitAsync();
        var outcomes = await Task.WhenAll(attempts);

        var winner = Assert.Single(outcomes, o => o.Result is not null).Result!;
        Assert.All(outcomes.Where(o => o.Result is null),
            o => Assert.IsType<ClearanceDecisionConflictException>(o.Error));

        await using var db = CreateContext();
        var stored = await db.ClearanceItems.AsNoTracking().SingleAsync(c => c.Id == _itemId);
        Assert.Equal(winner.Status, stored.Status);
        Assert.Equal(winner.DecidedByUserId, stored.DecidedByUserId);
        Assert.Equal(winner.DecisionNote, stored.DecisionNote);

        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.EntityName == nameof(ClearanceItem)).ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(winner.DecidedByUserId, audit.UserId);
        Assert.Equal(_itemId, audit.EntityId);
        Assert.Equal(winner.Status == ClearanceStatus.Approved ? "clearance_approved" : "clearance_rejected", audit.Action);

        var entry = await db.MaintenanceEntries.AsNoTracking().SingleAsync(e => e.Id == _entryId);
        Assert.False(entry.IsSafeToOperate);
    }

    [PostgresFact]
    public async Task FailedAuditInsert_OnPostgres_RollsBackTheDecision()
    {
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, """
                CREATE FUNCTION fail_clearance_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.action LIKE 'clearance_%' THEN
                        RAISE EXCEPTION 'injected audit failure';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER fail_clearance_audit BEFORE INSERT ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION fail_clearance_audit();
                """);
        }

        var outcome = await DecideAsync(_approverIds[0], approve: true, note: null);

        Assert.Null(outcome.Result);
        Assert.IsType<DbUpdateException>(outcome.Error);
        await using var db = CreateContext();
        var stored = await db.ClearanceItems.AsNoTracking().SingleAsync(c => c.Id == _itemId);
        Assert.Equal(ClearanceStatus.Pending, stored.Status);
        Assert.Null(stored.DecidedByUserId);
        Assert.Null(stored.DecidedAt);
        Assert.False(await db.AuditLogs.AnyAsync());
    }

    private async Task WaitForBlockedUpdatesAsync(int expected)
    {
        await using var monitor = new NpgsqlConnection(_connectionString);
        await monitor.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var count = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
                "AND wait_event_type = 'Lock' AND query ILIKE 'UPDATE%clearance_items%'", monitor);
            var blocked = Convert.ToInt32(await count.ExecuteScalarAsync());
            if (blocked >= expected)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Only {blocked} of {expected} decisions reached the row lock.");
            }

            await Task.Delay(50);
        }
    }

    private async Task<(ClearanceDecisionDto? Result, Exception? Error)> DecideAsync(Guid userId, bool approve, string? note)
    {
        await using var db = CreateContext();
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

    private MaintTrackDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MaintTrackDbContext>().UseNpgsql(_connectionString).Options,
            new TenantContext { TenantId = _tenantId });

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Skips the test unless a PostgreSQL server for throwaway test databases is configured.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClearancePostgresConcurrencyTests.ServerVariable)))
        {
            Skip = $"Set {ClearancePostgresConcurrencyTests.ServerVariable} to a local PostgreSQL server to run.";
        }
    }
}
