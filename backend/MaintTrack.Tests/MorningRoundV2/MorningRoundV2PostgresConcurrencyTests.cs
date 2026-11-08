using MaintTrack.Application.MorningRoundV2;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.MorningRoundV2;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.MorningRoundV2;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.Clearance;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MaintTrack.Tests.MorningRoundV2;

/// <summary>
/// Concurrent first Morning Round V2 submissions against a real PostgreSQL unique index. Runs only
/// when <c>MAINTTRACK_CLEARANCE_PG_SERVER</c> is set; uses its own throwaway database
/// (<c>mainttrack_clearance_test_*</c>) and drops it afterwards.
/// </summary>
public sealed class MorningRoundV2PostgresConcurrencyTests : IAsyncLifetime
{
    private static readonly DateOnly BusinessDay = new(2026, 10, 20);

    private readonly string? _server = Environment.GetEnvironmentVariable(ClearancePostgresConcurrencyTests.ServerVariable);
    private readonly string _database = $"mainttrack_clearance_test_{Guid.NewGuid():N}";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _blockerUserId = Guid.NewGuid();
    private readonly List<Guid> _userIds = new();
    private string _connectionString = string.Empty;
    private Guid _machineId;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
        {
            return;
        }

        await using (var admin = new NpgsqlConnection(_server))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_server) { Database = _database, Pooling = false }.ConnectionString;

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        foreach (var userId in new[] { _blockerUserId, Guid.NewGuid(), Guid.NewGuid() })
        {
            if (userId != _blockerUserId)
            {
                _userIds.Add(userId);
            }

            db.Users.Add(new User
            {
                Id = userId,
                TenantId = _tenantId,
                Username = $"user{userId:N}",
                Email = $"{userId:N}@example.com",
                PasswordHash = "hash",
                DisplayName = "Worker",
                RoleId = (int)UserRole.Worker,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        _machineId = Guid.NewGuid();
        db.Machines.Add(new Machine
        {
            Id = _machineId, TenantId = _tenantId, Name = "machine.race", Code = "R1", IsActive = true, CreatedAt = DateTime.UtcNow
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
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task TwoConcurrentFirstSubmissions_OnPostgres_OneSucceedsAndOneGetsAControlledConflict()
    {
        // An uncommitted row for the same tenant/day: both submissions pass the "already submitted?"
        // check, then queue on the unique index, so they genuinely race once it is rolled back.
        await using var blocker = CreateContext();
        await using var blockingTransaction = await blocker.Database.BeginTransactionAsync();
        blocker.MorningRoundV2Submissions.Add(new MorningRoundV2Submission
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ReportDate = BusinessDay,
            SubmittedAt = DateTime.UtcNow,
            SubmittedByUserId = _blockerUserId,
            ItemsJson = "[]",
            CreatedAt = DateTime.UtcNow
        });
        await blocker.SaveChangesAsync();

        var attempts = new[]
        {
            Task.Run(() => SubmitAsync(_userIds[0], "fail", "oil leak")),
            Task.Run(() => SubmitAsync(_userIds[1], "ok", null))
        };

        await WaitForBlockedInsertsAsync(2);
        await blockingTransaction.RollbackAsync();
        var outcomes = await Task.WhenAll(attempts);

        var winner = Assert.Single(outcomes, o => o.Result is not null);
        var loser = Assert.Single(outcomes, o => o.Result is null);
        var conflict = Assert.IsType<MorningRoundV2AlreadySubmittedException>(loser.Error);
        Assert.Equal(BusinessDay, conflict.ReportDate);

        await using var db = CreateContext();
        var stored = await db.MorningRoundV2Submissions.AsNoTracking().SingleAsync();
        Assert.Equal(winner.Result!.Id, stored.Id);
        Assert.Equal(winner.UserId, stored.SubmittedByUserId);
        Assert.Equal(BusinessDay, stored.ReportDate);
        var items = await db.ClearanceItems.AsNoTracking().ToListAsync();
        if (winner.Status == "fail")
        {
            var item = Assert.Single(items);
            Assert.Equal(stored.Id, item.MorningRoundV2SubmissionId);
            Assert.Equal(ClearanceStatus.Pending, item.Status);
            Assert.Contains("oil leak", stored.ItemsJson);
        }
        else
        {
            Assert.Empty(items);
            Assert.DoesNotContain("oil leak", stored.ItemsJson);
        }
    }

    private async Task WaitForBlockedInsertsAsync(int expected)
    {
        await using var monitor = new NpgsqlConnection(_connectionString);
        await monitor.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var count = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
                "AND wait_event_type = 'Lock' AND query ILIKE '%INSERT INTO%morning_round_v2_submissions%'", monitor);
            var blocked = Convert.ToInt32(await count.ExecuteScalarAsync());
            if (blocked >= expected)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Only {blocked} of {expected} submissions reached the unique index.");
            }

            await Task.Delay(50);
        }
    }

    private async Task<(Guid UserId, string Status, SubmitMorningRoundV2Response? Result, Exception? Error)> SubmitAsync(
        Guid userId,
        string status,
        string? notes)
    {
        await using var db = CreateContext();
        var service = new MorningRoundV2Service(
            db,
            new TenantContext { TenantId = _tenantId },
            new StubCurrentUserContext(_tenantId, userId),
            TestBusinessCalendar.Create(new MutableTimeProvider(TestBusinessCalendar.MorningOf(20))));
        try
        {
            var result = await service.SubmitAsync(
                new SubmitMorningRoundV2Request(
                    DateTime.UtcNow,
                    new[] { new SubmitMorningRoundV2ItemRequest(_machineId, status, notes) }),
                CancellationToken.None);
            return (userId, status, result, null);
        }
        catch (Exception ex)
        {
            return (userId, status, null, ex);
        }
    }

    private MaintTrackDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MaintTrackDbContext>().UseNpgsql(_connectionString).Options,
            new TenantContext { TenantId = _tenantId });
}
