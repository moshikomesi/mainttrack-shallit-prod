using MaintTrack.Application.Maintenance;
using MaintTrack.Application.MorningRoundV2;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Maintenance;
using MaintTrack.Infrastructure.MorningRoundV2;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using MaintTrack.Tests.MorningRoundV2;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// Automatic Clearance item creation from Maintenance Log and Morning Round V2 submissions.
/// </summary>
public sealed class ClearanceItemCreationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly MutableTimeProvider _clock = new(TestBusinessCalendar.MorningOf(20));
    private TenantContext _tenantContext = null!;
    private StubCurrentUserContext _currentUser = null!;
    private MaintTrackDbContext _dbContext = null!;
    private MaintenanceEntryService _maintenanceService = null!;
    private MorningRoundV2Service _morningRoundService = null!;

    private Guid _userId;
    private Guid _machineA;
    private Guid _machineB;
    private Guid _machineC;
    private Guid _typeId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await ExecuteSqlAsync("PRAGMA foreign_keys = ON;");

        _tenantContext = new TenantContext { TenantId = _tenantId };
        _dbContext = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>().UseSqlite(_connection).Options,
            _tenantContext);
        await _dbContext.Database.EnsureCreatedAsync();

        _dbContext.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        _userId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = _userId,
            TenantId = _tenantId,
            Username = "tech",
            Email = "tech@example.com",
            PasswordHash = "hash",
            DisplayName = "Tech",
            RoleId = (int)UserRole.Worker,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _machineA = AddMachine(_tenantId, "A");
        _machineB = AddMachine(_tenantId, "B");
        _machineC = AddMachine(_tenantId, "C");
        _typeId = Guid.NewGuid();
        _dbContext.MaintenanceTypes.Add(new MaintenanceType
        {
            Id = _typeId, TenantId = _tenantId, Code = "lubrication", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        _currentUser = new StubCurrentUserContext(_tenantId, _userId);
        _maintenanceService = new MaintenanceEntryService(
            _dbContext,
            _tenantContext,
            _currentUser,
            new RecordingFileStorageService(),
            NullLogger<MaintenanceEntryService>.Instance);
        _morningRoundService = new MorningRoundV2Service(
            _dbContext, _tenantContext, _currentUser, TestBusinessCalendar.Create(_clock));
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---------- Maintenance Log ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MaintenanceSubmission_CreatesOnePendingItemForThatReport(bool isSafeToOperate)
    {
        var entryId = await CreateMaintenanceAsync(isSafeToOperate);

        var item = Assert.Single(await AllItemsAsync());
        Assert.Equal(_tenantId, item.TenantId);
        Assert.Equal(ClearanceSourceType.MaintenanceEntry, item.SourceType);
        Assert.Equal(entryId, item.MaintenanceEntryId);
        Assert.Null(item.MorningRoundV2SubmissionId);
        Assert.Equal(ClearanceStatus.Pending, item.Status);
        Assert.Equal(_userId, item.SubmittedByUserId);
        Assert.Null(item.DecidedByUserId);
        Assert.Null(item.DecidedAt);
        Assert.Null(item.DecisionNote);

        var entry = await _dbContext.MaintenanceEntries.AsNoTracking().SingleAsync(e => e.Id == entryId);
        Assert.Equal(isSafeToOperate, entry.IsSafeToOperate);
        Assert.Equal(entry.CreatedAt, item.SubmittedAt);
    }

    [Fact]
    public async Task RepeatedMaintenanceSubmissions_CreateExactlyOneItemPerReport()
    {
        var first = await CreateMaintenanceAsync();
        var second = await CreateMaintenanceAsync();

        var items = await AllItemsAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(
            new[] { first, second }.OrderBy(id => id),
            items.Select(i => i.MaintenanceEntryId!.Value).OrderBy(id => id));
    }

    [Fact]
    public async Task MaintenanceSubmission_WhenItemInsertFails_SavesNothing()
    {
        await FailInsertsIntoAsync("clearance_items");

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateMaintenanceAsync());

        _dbContext.ChangeTracker.Clear();
        Assert.False(await _dbContext.MaintenanceEntries.IgnoreQueryFilters().AnyAsync());
        Assert.False(await _dbContext.AuditLogs.IgnoreQueryFilters().AnyAsync());
        Assert.False(await _dbContext.ClearanceItems.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task MaintenanceSubmission_WhenReportInsertFails_CreatesNoItem()
    {
        await FailInsertsIntoAsync("MaintenanceEntries");

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateMaintenanceAsync());

        _dbContext.ChangeTracker.Clear();
        Assert.False(await _dbContext.ClearanceItems.IgnoreQueryFilters().AnyAsync());
    }

    // ---------- Morning Round V2 ----------

    [Fact]
    public async Task NormalMorningRound_CreatesNoItem()
    {
        var response = await SubmitMorningRoundAsync(("ok", _machineA), ("ok", _machineB));

        Assert.True(await _dbContext.MorningRoundV2Submissions.AnyAsync(s => s.Id == response.Id));
        Assert.Empty(await AllItemsAsync());
    }

    [Fact]
    public async Task NormalMorningRound_WithNotesOnNormalMachines_CreatesNoItem()
    {
        await _morningRoundService.SubmitAsync(new SubmitMorningRoundV2Request(
            new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc),
            new[] { new SubmitMorningRoundV2ItemRequest(_machineA, "ok", "cleaned the filter") }),
            CancellationToken.None);

        Assert.Empty(await AllItemsAsync());
    }

    [Fact]
    public async Task AbnormalMorningRound_CreatesOnePendingItemForTheSubmission()
    {
        var response = await SubmitMorningRoundAsync(("ok", _machineA), ("fail", _machineB));

        var item = Assert.Single(await AllItemsAsync());
        Assert.Equal(_tenantId, item.TenantId);
        Assert.Equal(ClearanceSourceType.MorningRoundV2, item.SourceType);
        Assert.Equal(response.Id, item.MorningRoundV2SubmissionId);
        Assert.Null(item.MaintenanceEntryId);
        Assert.Equal(ClearanceStatus.Pending, item.Status);
        Assert.Equal(_userId, item.SubmittedByUserId);
        Assert.Equal(response.SubmittedAt, item.SubmittedAt);
    }

    [Theory]
    [InlineData("fail")]
    [InlineData(" FAIL ")]
    public async Task AbnormalStatus_IsRecognizedAfterNormalization(string status)
    {
        await SubmitMorningRoundAsync((status, _machineA));

        Assert.Single(await AllItemsAsync());
    }

    [Fact]
    public async Task MultipleAbnormalFindings_CreateOnlyOneItem()
    {
        await SubmitMorningRoundAsync(("fail", _machineA), ("fail", _machineB), ("fail", _machineC));

        Assert.Single(await AllItemsAsync());
    }

    [Fact]
    public async Task DifferentDays_CreateOneItemPerSubmission()
    {
        await SubmitMorningRoundAsync(("fail", _machineA), day: 20);
        await SubmitMorningRoundAsync(("fail", _machineA), day: 21);

        var items = await AllItemsAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(2, items.Select(i => i.MorningRoundV2SubmissionId).Distinct().Count());
    }

    [Fact]
    public async Task AbnormalMorningRound_WhenItemInsertFails_SavesNoSubmission()
    {
        await FailInsertsIntoAsync("clearance_items");

        await Assert.ThrowsAsync<DbUpdateException>(() => SubmitMorningRoundAsync(("fail", _machineA)));

        _dbContext.ChangeTracker.Clear();
        Assert.False(await _dbContext.MorningRoundV2Submissions.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task MorningRoundItem_IsCreatedInTheSubmittingTenantOnly()
    {
        _tenantContext.TenantId = _otherTenantId;
        _currentUser.TenantId = _otherTenantId;
        var foreignMachine = AddMachine(_otherTenantId, "X");
        await _dbContext.SaveChangesAsync();

        var response = await SubmitMorningRoundAsync(("fail", foreignMachine));

        var item = Assert.Single(await _dbContext.ClearanceItems.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(_otherTenantId, item.TenantId);
        Assert.Equal(response.Id, item.MorningRoundV2SubmissionId);

        _tenantContext.TenantId = _tenantId;
        Assert.Empty(await _dbContext.ClearanceItems.AsNoTracking().ToListAsync());
    }

    // ---------- Helpers ----------

    private Guid AddMachine(Guid tenantId, string code)
    {
        var id = Guid.NewGuid();
        _dbContext.Machines.Add(new Machine
        {
            Id = id, TenantId = tenantId, Name = $"machine.{code}", Code = code, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private Task<Guid> CreateMaintenanceAsync(bool isSafeToOperate = true) =>
        _maintenanceService.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = _machineA,
            Date = new DateOnly(2026, 10, 20),
            MaintenanceTypeId = _typeId,
            WorkHours = 1,
            IsSafeToOperate = isSafeToOperate
        }, CancellationToken.None);

    private Task<SubmitMorningRoundV2Response> SubmitMorningRoundAsync(
        (string Status, Guid MachineId) first,
        params (string Status, Guid MachineId)[] rest) =>
        SubmitMorningRoundAsync(new[] { first }.Concat(rest).ToArray(), 20);

    private Task<SubmitMorningRoundV2Response> SubmitMorningRoundAsync((string Status, Guid MachineId) only, int day) =>
        SubmitMorningRoundAsync(new[] { only }, day);

    private async Task<SubmitMorningRoundV2Response> SubmitMorningRoundAsync(
        (string Status, Guid MachineId)[] findings,
        int day)
    {
        _clock.UtcNow = TestBusinessCalendar.MorningOf(day);
        var response = await _morningRoundService.SubmitAsync(new SubmitMorningRoundV2Request(
                new DateTime(2026, 10, day, 6, 0, 0, DateTimeKind.Utc),
                findings.Select(f => new SubmitMorningRoundV2ItemRequest(f.MachineId, f.Status, null)).ToList()),
            CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        return response;
    }

    private async Task<List<ClearanceItem>> AllItemsAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.ClearanceItems.AsNoTracking().ToListAsync();
    }

    private Task FailInsertsIntoAsync(string table) =>
        ExecuteSqlAsync(
            $"CREATE TRIGGER fail_{table} BEFORE INSERT ON \"{table}\" BEGIN SELECT RAISE(ABORT, 'injected failure'); END;");

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
