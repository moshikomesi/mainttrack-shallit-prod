using MaintTrack.Application.MorningRoundV2;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.MorningRoundV2;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.MorningRoundV2;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MaintTrack.Tests.MorningRoundV2;

/// <summary>
/// Morning Round V2 allows exactly one submission per tenant per business day; a duplicate is
/// rejected without changing the existing submission or its Clearance item.
/// </summary>
public sealed class MorningRoundV2OnePerDayTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly MutableTimeProvider _clock = new(TestBusinessCalendar.MorningOf(20));
    private readonly CompetingSubmissionInterceptor _race = new();
    private TenantContext _tenantContext = null!;
    private StubCurrentUserContext _currentUser = null!;
    private MaintTrackDbContext _dbContext = null!;
    private MorningRoundV2Service _service = null!;

    private Guid _firstUserId;
    private Guid _secondUserId;
    private Guid _otherTenantUserId;
    private Guid _machineA;
    private Guid _machineB;
    private Guid _otherTenantMachine;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync();
        }

        _tenantContext = new TenantContext { TenantId = _tenantId };
        _dbContext = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>().UseSqlite(_connection).AddInterceptors(_race).Options,
            _tenantContext);
        await _dbContext.Database.EnsureCreatedAsync();

        _dbContext.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        _firstUserId = AddUser(_tenantId, "first");
        _secondUserId = AddUser(_tenantId, "second");
        _otherTenantUserId = AddUser(_otherTenantId, "foreign");
        _machineA = AddMachine(_tenantId, "A");
        _machineB = AddMachine(_tenantId, "B");
        _otherTenantMachine = AddMachine(_otherTenantId, "X");
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        _currentUser = new StubCurrentUserContext(_tenantId, _firstUserId);
        _service = new MorningRoundV2Service(_dbContext, _tenantContext, _currentUser, TestBusinessCalendar.Create(_clock));
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task FirstSubmission_UsesServerTimeAndBusinessDate_NotTheClientTimestamp()
    {
        var response = await SubmitAsync(clientTimestamp: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), ("ok", _machineA));

        var stored = await LoadSingleAsync(_tenantId);
        Assert.Equal(response.Id, stored.Id);
        Assert.Equal(new DateOnly(2026, 10, 20), stored.ReportDate);
        Assert.Equal(_clock.UtcNow.UtcDateTime, stored.SubmittedAt);
        Assert.Equal(_firstUserId, stored.SubmittedByUserId);
        Assert.Null(stored.UpdatedAt);
    }

    [Theory]
    [InlineData("ok", "ok")]
    [InlineData("ok", "fail")]
    [InlineData("fail", "ok")]
    [InlineData("fail", "fail")]
    public async Task SecondSubmissionSameDay_IsRejected_AndChangesNothing(string firstStatus, string secondStatus)
    {
        await SubmitAsync((firstStatus, _machineA));
        var original = await LoadSingleAsync(_tenantId);
        var originalItems = await AllItemsAsync();

        _clock.UtcNow = _clock.UtcNow.AddHours(5);
        _currentUser.UserId = _secondUserId;
        var conflict = await Assert.ThrowsAsync<MorningRoundV2AlreadySubmittedException>(
            () => SubmitAsync((secondStatus, _machineA), (secondStatus, _machineB)));

        Assert.Equal(original.ReportDate, conflict.ReportDate);
        AssertSameSubmission(original, await LoadSingleAsync(_tenantId));
        Assert.Equivalent(originalItems, await AllItemsAsync(), strict: true);
        Assert.Equal(firstStatus == "fail" ? 1 : 0, originalItems.Count);
    }

    [Theory]
    [InlineData(ClearanceStatus.Pending)]
    [InlineData(ClearanceStatus.Approved)]
    [InlineData(ClearanceStatus.Rejected)]
    public async Task Duplicate_DoesNotAlterTheClearanceDecisionOrFindings(string status)
    {
        var first = await SubmitAsync(("fail", _machineA, "oil leak"));
        if (status != ClearanceStatus.Pending)
        {
            await _dbContext.ClearanceItems
                .Where(c => c.MorningRoundV2SubmissionId == first.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.DecidedByUserId, (Guid?)_secondUserId)
                    .SetProperty(c => c.DecidedAt, (DateTime?)_clock.UtcNow.UtcDateTime.AddHours(1))
                    .SetProperty(c => c.DecisionNote, "original decision"));
        }
        var original = await LoadSingleAsync(_tenantId);
        var originalItem = Assert.Single(await AllItemsAsync());

        _clock.UtcNow = _clock.UtcNow.AddHours(2);
        _currentUser.UserId = _secondUserId;
        await Assert.ThrowsAsync<MorningRoundV2AlreadySubmittedException>(
            () => SubmitAsync(("ok", _machineA, "all fine now"), ("fail", _machineB, "new finding")));

        AssertSameSubmission(original, await LoadSingleAsync(_tenantId));
        Assert.Contains("oil leak", original.ItemsJson);
        Assert.Equivalent(originalItem, Assert.Single(await AllItemsAsync()), strict: true);
    }

    [Fact]
    public async Task ClientTimestampForAnEarlierDay_CannotOverwriteThatDaysReport()
    {
        _clock.UtcNow = TestBusinessCalendar.MorningOf(19);
        await SubmitAsync(("fail", _machineA));
        var yesterday = await LoadSingleAsync(_tenantId);

        _clock.UtcNow = TestBusinessCalendar.MorningOf(20);
        _currentUser.UserId = _secondUserId;
        var today = await SubmitAsync(clientTimestamp: yesterday.SubmittedAt, ("ok", _machineA));
        await Assert.ThrowsAsync<MorningRoundV2AlreadySubmittedException>(
            () => SubmitAsync(clientTimestamp: yesterday.SubmittedAt, ("ok", _machineB)));

        Assert.Equal(new DateOnly(2026, 10, 20), today.ReportDate);
        _dbContext.ChangeTracker.Clear();
        var storedYesterday = await _dbContext.MorningRoundV2Submissions.AsNoTracking().SingleAsync(s => s.Id == yesterday.Id);
        AssertSameSubmission(yesterday, storedYesterday);
        Assert.Equal(2, await _dbContext.MorningRoundV2Submissions.CountAsync());
    }

    [Fact]
    public async Task LateEveningUtc_CountsAsTheNextBusinessDay()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 10, 19, 21, 30, 0, TimeSpan.Zero);
        var response = await SubmitAsync(("ok", _machineA));

        _clock.UtcNow = TestBusinessCalendar.MorningOf(20);
        await Assert.ThrowsAsync<MorningRoundV2AlreadySubmittedException>(() => SubmitAsync(("ok", _machineA)));

        Assert.Equal(new DateOnly(2026, 10, 20), response.ReportDate);
    }

    [Fact]
    public async Task DifferentDays_EachAllowExactlyOneSubmission()
    {
        var day20 = await SubmitAsync(("fail", _machineA));
        _clock.UtcNow = TestBusinessCalendar.MorningOf(21);
        var day21 = await SubmitAsync(("fail", _machineA));
        await Assert.ThrowsAsync<MorningRoundV2AlreadySubmittedException>(() => SubmitAsync(("ok", _machineA)));

        Assert.Equal(new DateOnly(2026, 10, 20), day20.ReportDate);
        Assert.Equal(new DateOnly(2026, 10, 21), day21.ReportDate);
        Assert.Equal(2, await _dbContext.MorningRoundV2Submissions.CountAsync());
        Assert.Equal(2, (await AllItemsAsync()).Select(i => i.MorningRoundV2SubmissionId).Distinct().Count());
    }

    [Fact]
    public async Task Tenants_AreIsolated_EachGetsOneSubmissionPerDay()
    {
        await SubmitAsync(("ok", _machineA));
        var tenantA = await LoadSingleAsync(_tenantId);

        UseOtherTenant();
        var tenantB = await SubmitAsync(("fail", _otherTenantMachine));
        await Assert.ThrowsAsync<MorningRoundV2AlreadySubmittedException>(() => SubmitAsync(("ok", _otherTenantMachine)));

        Assert.Equal(tenantA.ReportDate, tenantB.ReportDate);
        Assert.NotEqual(tenantA.Id, tenantB.Id);
        AssertSameSubmission(tenantA, await LoadSingleAsync(_tenantId));
        var item = Assert.Single(await _dbContext.ClearanceItems.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(_otherTenantId, item.TenantId);
        Assert.Equal(tenantB.Id, item.MorningRoundV2SubmissionId);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("fail")]
    public async Task ConcurrentFirstSubmission_LosingWriteIsAConflict_NotAnError(string loserStatus)
    {
        var winnerId = Guid.NewGuid();
        var winnerAt = _clock.UtcNow.UtcDateTime.AddSeconds(-1);
        _race.BeforeNextSave = async () =>
        {
            await using var competitor = new MaintTrackDbContext(
                new DbContextOptionsBuilder<MaintTrackDbContext>().UseSqlite(_connection).Options,
                new TenantContext { TenantId = _tenantId });
            competitor.MorningRoundV2Submissions.Add(new MorningRoundV2Submission
            {
                Id = winnerId,
                TenantId = _tenantId,
                ReportDate = new DateOnly(2026, 10, 20),
                SubmittedAt = winnerAt,
                SubmittedByUserId = _secondUserId,
                ItemsJson = "[]",
                CreatedAt = winnerAt
            });
            await competitor.SaveChangesAsync();
        };

        var conflict = await Assert.ThrowsAsync<MorningRoundV2AlreadySubmittedException>(
            () => SubmitAsync((loserStatus, _machineA)));

        Assert.Equal(new DateOnly(2026, 10, 20), conflict.ReportDate);
        var stored = await LoadSingleAsync(_tenantId);
        Assert.Equal(winnerId, stored.Id);
        Assert.Equal(_secondUserId, stored.SubmittedByUserId);
        Assert.Equal(winnerAt, stored.SubmittedAt);
        Assert.Equal("[]", stored.ItemsJson);
        Assert.Empty(await AllItemsAsync());
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnrelatedSaveFailure_IsNotReportedAsAConflict()
    {
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TRIGGER fail_items BEFORE INSERT ON \"clearance_items\" BEGIN SELECT RAISE(ABORT, 'injected failure'); END;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => SubmitAsync(("fail", _machineA)));

        _dbContext.ChangeTracker.Clear();
        Assert.False(await _dbContext.MorningRoundV2Submissions.IgnoreQueryFilters().AnyAsync());
    }

    // ---------- Helpers ----------

    private Task<SubmitMorningRoundV2Response> SubmitAsync(params (string Status, Guid MachineId)[] findings) =>
        SubmitAsync(new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc), findings);

    private Task<SubmitMorningRoundV2Response> SubmitAsync(params (string Status, Guid MachineId, string Notes)[] findings) =>
        SubmitCoreAsync(
            new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc),
            findings.Select(f => new SubmitMorningRoundV2ItemRequest(f.MachineId, f.Status, f.Notes)).ToList());

    private Task<SubmitMorningRoundV2Response> SubmitAsync(DateTime clientTimestamp, params (string Status, Guid MachineId)[] findings) =>
        SubmitCoreAsync(
            clientTimestamp,
            findings.Select(f => new SubmitMorningRoundV2ItemRequest(f.MachineId, f.Status, null)).ToList());

    private async Task<SubmitMorningRoundV2Response> SubmitCoreAsync(
        DateTime clientTimestamp,
        IReadOnlyList<SubmitMorningRoundV2ItemRequest> items)
    {
        try
        {
            return await _service.SubmitAsync(new SubmitMorningRoundV2Request(clientTimestamp, items), CancellationToken.None);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<MorningRoundV2Submission> LoadSingleAsync(Guid tenantId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.MorningRoundV2Submissions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.TenantId == tenantId);
    }

    private async Task<List<ClearanceItem>> AllItemsAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.ClearanceItems.IgnoreQueryFilters().AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    }

    private static void AssertSameSubmission(MorningRoundV2Submission expected, MorningRoundV2Submission actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.ReportDate, actual.ReportDate);
        Assert.Equal(expected.ItemsJson, actual.ItemsJson);
        Assert.Equal(expected.SubmittedByUserId, actual.SubmittedByUserId);
        Assert.Equal(expected.SubmittedAt, actual.SubmittedAt);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
    }

    private void UseOtherTenant()
    {
        _tenantContext.TenantId = _otherTenantId;
        _currentUser.TenantId = _otherTenantId;
        _currentUser.UserId = _otherTenantUserId;
    }

    private Guid AddUser(Guid tenantId, string username)
    {
        var id = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = id,
            TenantId = tenantId,
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            DisplayName = username,
            RoleId = (int)UserRole.Worker,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private Guid AddMachine(Guid tenantId, string code)
    {
        var id = Guid.NewGuid();
        _dbContext.Machines.Add(new Machine
        {
            Id = id, TenantId = tenantId, Name = $"machine.{code}", Code = code, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    /// <summary>Commits a competing submission just before the service's own save, simulating a lost race.</summary>
    private sealed class CompetingSubmissionInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeNextSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (BeforeNextSave is { } competitor)
            {
                BeforeNextSave = null;
                await competitor();
            }

            return result;
        }
    }
}
