using MaintTrack.Application.Clearance;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.MorningRoundV2;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Clearance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.Clearance;

public sealed class ClearanceServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private TenantContext _tenantContext = null!;
    private StubCurrentUserContext _currentUser = null!;
    private MaintTrackDbContext _dbContext = null!;
    private ClearanceService _service = null!;

    private User _approver = null!;
    private User _worker = null!;
    private WorkGroup _array = null!;
    private WorkGroup _otherArray = null!;
    private Machine _machine = null!;
    private Machine _secondMachine = null!;
    private Machine _otherTenantMachine = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync();
        }

        _tenantContext = new TenantContext { TenantId = _tenantId };
        _currentUser = new StubCurrentUserContext(_tenantId, Guid.NewGuid());

        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new MaintTrackDbContext(options, _tenantContext);
        await _dbContext.Database.EnsureCreatedAsync();

        _dbContext.Roles.AddRange(
            new Role { Id = (int)UserRole.Worker, Name = "Worker" },
            new Role { Id = (int)UserRole.Manager, Name = "Manager" },
            new Role { Id = (int)UserRole.SuperAdmin, Name = "SuperAdmin" });
        await _dbContext.SaveChangesAsync();

        _approver = await SeedUserAsync(_tenantId, "approver", UserRole.Worker);
        _worker = await SeedUserAsync(_tenantId, "worker", UserRole.Worker);
        await SeedApproverAsync(_tenantId, _approver.Id);

        _array = await SeedArrayAsync(_tenantId, "array.one");
        _otherArray = await SeedArrayAsync(_tenantId, "array.two");
        _machine = await SeedMachineAsync(_tenantId, "M1", _array.Id);
        _secondMachine = await SeedMachineAsync(_tenantId, "M2", _otherArray.Id);
        _otherTenantMachine = await SeedMachineAsync(_otherTenantId, "X1", null);

        _service = new ClearanceService(_dbContext, _tenantContext, _currentUser);
        ActAs(_approver);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---------- Tenant resolution ----------

    [Fact]
    public async Task UnresolvedTenant_IsRejectedForApproverCheckAndQueue()
    {
        _tenantContext.TenantId = null;

        var check = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.IsCurrentUserApproverAsync(CancellationToken.None));
        var queue = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));

        Assert.Equal("Tenant not resolved.", check.Message);
        Assert.Equal("Tenant not resolved.", queue.Message);
    }

    // ---------- Approver eligibility ----------

    [Fact]
    public async Task ActiveAssignedApprover_CanApproveAndReadQueue()
    {
        Assert.True(await _service.IsCurrentUserApproverAsync(CancellationToken.None));

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);
        Assert.True(response.CanApprove);
    }

    [Fact]
    public async Task UnassignedWorker_IsNotApproverAndCannotReadQueue()
    {
        ActAs(_worker);

        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task InactiveAssignedApprover_IsNotApproverAndCannotReadQueue()
    {
        await SetActiveAsync(_approver.Id, false);

        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task ApproverCheck_IsEvaluatedAgainstDatabaseOnEveryCall()
    {
        Assert.True(await _service.IsCurrentUserApproverAsync(CancellationToken.None));

        await _dbContext.ClearanceApprovers.Where(a => a.UserId == _approver.Id).ExecuteDeleteAsync();

        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.SuperAdmin)]
    public async Task ElevatedRoles_AreNeverApproversWithoutAssignment(UserRole role)
    {
        var user = await SeedUserAsync(_tenantId, $"elevated-{role}", role);
        ActAs(user);

        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnassignedSuperAdmin_CannotReadQueueOrApprove()
    {
        var admin = await SeedUserAsync(_tenantId, "admin", UserRole.SuperAdmin);
        ActAs(admin);

        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Theory]
    [InlineData(UserRole.Worker)]
    [InlineData(UserRole.SuperAdmin)]
    public async Task AssignedViewer_CanReadQueueButIsNotApprover(UserRole role)
    {
        var viewer = await SeedUserAsync(_tenantId, $"viewer-{role}", role);
        await SeedViewerAsync(_tenantId, viewer.Id);
        ActAs(viewer);

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);

        Assert.False(response.CanApprove);
        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InactiveAssignedViewer_CannotReadQueue()
    {
        var viewer = await SeedUserAsync(_tenantId, "inactive-viewer", UserRole.Worker);
        await SeedViewerAsync(_tenantId, viewer.Id);
        await SetActiveAsync(viewer.Id, false);
        ActAs(viewer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task ViewerAssignmentInAnotherTenant_DoesNotGrantView()
    {
        await Assert.ThrowsAsync<DbUpdateException>(() => SeedViewerAsync(_otherTenantId, _worker.Id));
        _dbContext.ChangeTracker.Clear();
        var foreignViewer = await SeedUserAsync(_otherTenantId, "foreign-viewer", UserRole.Worker);
        await SeedViewerAsync(_otherTenantId, foreignViewer.Id);

        ActAs(_worker);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));

        ActAs(foreignViewer);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task UnassignedManager_CannotReadQueue()
    {
        var manager = await SeedUserAsync(_tenantId, "manager", UserRole.Manager);
        ActAs(manager);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task InactiveSuperAdmin_CannotReadQueue()
    {
        var admin = await SeedUserAsync(_tenantId, "inactive-admin", UserRole.SuperAdmin);
        await SetActiveAsync(admin.Id, false);
        ActAs(admin);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task JwtRoleClaim_IsNotTrusted_DatabaseRoleDecidesQueueAccess()
    {
        ActAs(_worker);
        _currentUser.RoleId = (int)UserRole.SuperAdmin;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task AssignmentRowInAnotherTenant_IsRejectedAndDoesNotGrantApproval()
    {
        await Assert.ThrowsAsync<DbUpdateException>(() => SeedApproverAsync(_otherTenantId, _worker.Id));
        _dbContext.ChangeTracker.Clear();
        ActAs(_worker);

        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task ApproverOfAnotherTenant_IsNotApproverInCurrentTenant()
    {
        var foreignApprover = await SeedUserAsync(_otherTenantId, "foreign", UserRole.SuperAdmin);
        await SeedApproverAsync(_otherTenantId, foreignApprover.Id);
        ActAs(foreignApprover);

        Assert.False(await _service.IsCurrentUserApproverAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task SelfApproval_ApproverSeesOwnSubmissionWithCanApprove()
    {
        var entry = await SeedEntryAsync(_tenantId, _machine.Id, "own fault");
        var item = await SeedEntryItemAsync(_tenantId, entry.Id, submittedBy: _approver.Id);

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal(item.Id, row.Id);
        Assert.Equal(_approver.Id, row.SubmittedByUserId);
        Assert.Equal("approver", row.SubmittedByName);
        Assert.True(response.CanApprove);
    }

    // ---------- Tenant isolation ----------

    [Fact]
    public async Task Queue_ReturnsOnlyCurrentTenantItems()
    {
        var entry = await SeedEntryAsync(_tenantId, _machine.Id, "local");
        var local = await SeedEntryItemAsync(_tenantId, entry.Id);

        var otherEntry = await SeedEntryAsync(_otherTenantId, _otherTenantMachine.Id, "foreign");
        await SeedEntryItemAsync(_otherTenantId, otherEntry.Id);
        var otherSubmission = await SeedSubmissionAsync(_otherTenantId);
        await SeedMorningRoundItemAsync(_otherTenantId, otherSubmission.Id, _otherTenantMachine.Id);

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(local.Id, Assert.Single(response.Items).Id);
    }

    [Fact]
    public async Task Queue_FilteringByAnotherTenantsMachineOrArray_ReturnsNothing()
    {
        var otherArray = await SeedArrayAsync(_otherTenantId, "array.foreign");
        var otherMachine = await SeedMachineAsync(_otherTenantId, "X2", otherArray.Id);
        var otherEntry = await SeedEntryAsync(_otherTenantId, otherMachine.Id, "foreign");
        await SeedEntryItemAsync(_otherTenantId, otherEntry.Id);

        var byMachine = await _service.GetQueueAsync(
            new ClearanceQueueQuery { MachineId = otherMachine.Id }, CancellationToken.None);
        var byArray = await _service.GetQueueAsync(
            new ClearanceQueueQuery { ArrayId = otherArray.Id }, CancellationToken.None);

        Assert.Empty(byMachine.Items);
        Assert.Equal(0, byMachine.TotalCount);
        Assert.Empty(byArray.Items);
        Assert.Equal(0, byArray.TotalCount);
    }

    [Fact]
    public async Task Queue_ExcludesItemsWhoseSourceOrMachineBelongsToAnotherTenant()
    {
        var foreignEntry = await SeedEntryAsync(_otherTenantId, _otherTenantMachine.Id, "foreign");
        await SeedEntryItemAsync(_tenantId, foreignEntry.Id);

        var foreignSubmission = await SeedSubmissionAsync(_otherTenantId);
        await SeedMorningRoundItemAsync(_tenantId, foreignSubmission.Id, _machine.Id);

        var localEntryOnForeignMachine = await SeedEntryAsync(_tenantId, _otherTenantMachine.Id, "mismatch");
        await SeedEntryItemAsync(_tenantId, localEntryOnForeignMachine.Id);

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Queue_MorningRoundFindingOnAnotherTenantsMachine_DoesNotExposeThatMachine()
    {
        var localSubmission = await SeedSubmissionAsync(_tenantId);
        var item = await SeedMorningRoundItemAsync(_tenantId, localSubmission.Id, _otherTenantMachine.Id);

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);
        var byForeignMachine = await _service.GetQueueAsync(
            new ClearanceQueueQuery { MachineId = _otherTenantMachine.Id }, CancellationToken.None);

        var finding = Assert.Single(Assert.Single(response.Items, i => i.Id == item.Id).Findings);
        Assert.Equal("machine.unknown", finding.MachineName);
        Assert.Null(finding.ArrayId);
        Assert.Empty(byForeignMachine.Items);
    }

    // ---------- Status validation and filters ----------

    [Theory]
    [InlineData("deleted")]
    [InlineData("in_review")]
    [InlineData("approve")]
    public async Task Queue_UnknownStatus_IsRejected(string status)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetQueueAsync(new ClearanceQueueQuery { Status = status }, CancellationToken.None));
    }

    [Theory]
    [InlineData("pending", ClearanceStatus.Pending)]
    [InlineData(" Approved ", ClearanceStatus.Approved)]
    [InlineData("REJECTED", ClearanceStatus.Rejected)]
    public async Task Queue_StatusFilter_ReturnsOnlyMatchingItems(string filter, string expected)
    {
        await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "p")).Id);
        await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "a")).Id,
            status: ClearanceStatus.Approved);
        await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "r")).Id,
            status: ClearanceStatus.Rejected);

        var response = await _service.GetQueueAsync(
            new ClearanceQueueQuery { Status = filter }, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(response.Items).Status);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task Queue_WithoutStatus_ReturnsAllStatuses()
    {
        await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "p")).Id);
        await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "a")).Id,
            status: ClearanceStatus.Approved);

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task Queue_ArrayAndMachineFilters_ApplyToBothSourceTypes()
    {
        var entryItem = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);
        var submission = await SeedSubmissionAsync(_tenantId);
        var roundItem = await SeedMorningRoundItemAsync(_tenantId, submission.Id, _secondMachine.Id);

        var byArray = await _service.GetQueueAsync(
            new ClearanceQueueQuery { ArrayId = _array.Id }, CancellationToken.None);
        var byOtherArray = await _service.GetQueueAsync(
            new ClearanceQueueQuery { ArrayId = _otherArray.Id }, CancellationToken.None);
        var byMachine = await _service.GetQueueAsync(
            new ClearanceQueueQuery { MachineId = _secondMachine.Id }, CancellationToken.None);
        var mismatched = await _service.GetQueueAsync(
            new ClearanceQueueQuery { ArrayId = _array.Id, MachineId = _secondMachine.Id }, CancellationToken.None);

        Assert.Equal(entryItem.Id, Assert.Single(byArray.Items).Id);
        Assert.Equal(roundItem.Id, Assert.Single(byOtherArray.Items).Id);
        Assert.Equal(roundItem.Id, Assert.Single(byMachine.Items).Id);
        Assert.Empty(mismatched.Items);
    }

    // ---------- Pagination ----------

    [Fact]
    public async Task Queue_PendingTotalCount_CountsOnlyPendingAcrossSourcesTenantAndPages()
    {
        for (var i = 0; i < 3; i++)
        {
            var pendingEntry = await SeedEntryAsync(_tenantId, _machine.Id, $"pending{i}");
            await SeedEntryItemAsync(_tenantId, pendingEntry.Id);
        }
        var submission = await SeedSubmissionAsync(_tenantId);
        await SeedMorningRoundItemAsync(_tenantId, submission.Id, _secondMachine.Id);

        var approvedEntry = await SeedEntryAsync(_tenantId, _machine.Id, "approved");
        await SeedEntryItemAsync(_tenantId, approvedEntry.Id, ClearanceStatus.Approved);
        var rejectedEntry = await SeedEntryAsync(_tenantId, _machine.Id, "rejected");
        await SeedEntryItemAsync(_tenantId, rejectedEntry.Id, ClearanceStatus.Rejected);

        var foreignEntry = await SeedEntryAsync(_otherTenantId, _otherTenantMachine.Id, "foreign");
        await SeedEntryItemAsync(_otherTenantId, foreignEntry.Id);

        var response = await _service.GetQueueAsync(
            new ClearanceQueueQuery { Status = ClearanceStatus.Pending, PageNumber = 1, PageSize = 1 },
            CancellationToken.None);

        Assert.Equal(4, response.TotalCount);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task Queue_Pagination_IsStableAndComplete()
    {
        var sameInstant = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var seeded = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            var entry = await SeedEntryAsync(_tenantId, _machine.Id, $"e{i}");
            var submittedAt = i < 10 ? sameInstant : sameInstant.AddMinutes(i);
            seeded.Add((await SeedEntryItemAsync(_tenantId, entry.Id, submittedAt: submittedAt)).Id);
        }

        var pages = new List<ClearanceQueueResponse>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            pages.Add(await _service.GetQueueAsync(
                new ClearanceQueueQuery { PageNumber = pageNumber, PageSize = 10 }, CancellationToken.None));
        }

        Assert.All(pages, p => Assert.Equal(25, p.TotalCount));
        Assert.Equal(new[] { 10, 10, 5 }, pages.Select(p => p.Items.Count));

        var ids = pages.SelectMany(p => p.Items).Select(i => i.Id).ToList();
        Assert.Equal(25, ids.Distinct().Count());
        Assert.Equal(seeded.OrderBy(id => id), ids.OrderBy(id => id));

        var ordered = pages.SelectMany(p => p.Items).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i - 1].SubmittedAt >= ordered[i].SubmittedAt);
        }

        var repeat = await _service.GetQueueAsync(
            new ClearanceQueueQuery { PageNumber = 3, PageSize = 10 }, CancellationToken.None);
        Assert.Equal(pages[2].Items.Select(i => i.Id), repeat.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Queue_PaginationDefaultsAndLimits_AreApplied()
    {
        await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);

        var defaults = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);
        var invalid = await _service.GetQueueAsync(
            new ClearanceQueueQuery { PageNumber = 0, PageSize = -5 }, CancellationToken.None);
        var capped = await _service.GetQueueAsync(
            new ClearanceQueueQuery { PageSize = 1000 }, CancellationToken.None);
        var beyond = await _service.GetQueueAsync(
            new ClearanceQueueQuery { PageNumber = 5 }, CancellationToken.None);

        Assert.Equal((1, 20), (defaults.PageNumber, defaults.PageSize));
        Assert.Equal((1, 20), (invalid.PageNumber, invalid.PageSize));
        Assert.Equal(100, capped.PageSize);
        Assert.Empty(beyond.Items);
        Assert.Equal(1, beyond.TotalCount);
    }

    // ---------- Mapping ----------

    [Fact]
    public async Task Queue_MapsSourceIdentifiersAndDisplayData()
    {
        var entry = await SeedEntryAsync(_tenantId, _machine.Id, "Bearing noise");
        var entryItem = await SeedEntryItemAsync(_tenantId, entry.Id, submittedBy: _worker.Id,
            submittedAt: DateTime.UtcNow.AddMinutes(-5));

        var submission = await SeedSubmissionAsync(_tenantId);
        var decidedAt = DateTime.UtcNow;
        var roundItem = await SeedMorningRoundItemAsync(_tenantId, submission.Id, _secondMachine.Id,
            status: ClearanceStatus.Rejected, decidedBy: _approver.Id, decidedAt: decidedAt, note: "Recheck");

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);

        var round = response.Items.Single(i => i.Id == roundItem.Id);
        Assert.Equal(ClearanceSourceType.MorningRoundV2, round.SourceType);
        Assert.Null(round.MaintenanceEntryId);
        Assert.Equal(submission.Id, round.MorningRoundV2SubmissionId);
        Assert.Null(round.MachineId);
        Assert.Null(round.MachineName);
        Assert.Null(round.ArrayId);
        var finding = Assert.Single(round.Findings);
        Assert.Equal(_secondMachine.Id, finding.MachineId);
        Assert.Equal(_secondMachine.Name, finding.MachineName);
        Assert.Equal(_otherArray.Id, finding.ArrayId);
        Assert.Equal("abnormal", finding.Notes);
        Assert.Equal(submission.ReportDate, round.ReportDate);
        Assert.Null(round.Description);
        Assert.Equal(ClearanceStatus.Rejected, round.Status);
        Assert.Equal(_approver.Id, round.DecidedByUserId);
        Assert.Equal("approver", round.DecidedByName);
        Assert.NotNull(round.DecidedAt);
        Assert.Equal("Recheck", round.DecisionNote);

        var maintenance = response.Items.Single(i => i.Id == entryItem.Id);
        Assert.Equal(ClearanceSourceType.MaintenanceEntry, maintenance.SourceType);
        Assert.Equal(entry.Id, maintenance.MaintenanceEntryId);
        Assert.Null(maintenance.MorningRoundV2SubmissionId);
        Assert.Equal(_machine.Id, maintenance.MachineId);
        Assert.Equal(_array.Id, maintenance.ArrayId);
        Assert.Equal(entry.Date, maintenance.ReportDate);
        Assert.Equal("Bearing noise", maintenance.Description);
        Assert.Equal("worker", maintenance.SubmittedByName);
        Assert.Null(maintenance.DecidedByUserId);
        Assert.Null(maintenance.DecidedByName);
        Assert.Null(maintenance.DecidedAt);

        Assert.Equal(new[] { roundItem.Id, entryItem.Id }, response.Items.Select(i => i.Id));
    }

    // ---------- IsSafeToOperate / technician declaration ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Queue_DoesNotInferOrChangeStatusFromIsSafeToOperate(bool isSafeToOperate)
    {
        var entry = await SeedEntryAsync(_tenantId, _machine.Id, "declared", isSafeToOperate);
        await SeedEntryItemAsync(_tenantId, entry.Id);

        var response = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal(ClearanceStatus.Pending, row.Status);
        Assert.Null(row.DecidedByUserId);
        Assert.Null(row.DecidedAt);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.MaintenanceEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.Equal(isSafeToOperate, stored.IsSafeToOperate);
        Assert.Equal(1, await _dbContext.ClearanceItems.CountAsync());
    }

    [Fact]
    public void QueueDto_DoesNotExposeIsSafeToOperate()
    {
        Assert.Null(typeof(ClearanceItemDto).GetProperty("IsSafeToOperate"));
    }

    // ---------- Decisions: success paths ----------

    [Fact]
    public async Task Approve_PendingItem_SetsServerSideDecisionMetadataAndAudit()
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id,
            submittedBy: _worker.Id);
        _currentUser.RoleId = (int)UserRole.SuperAdmin;
        var before = DateTime.UtcNow;

        var result = await _service.ApproveAsync(item.Id, CancellationToken.None);

        var after = DateTime.UtcNow;
        Assert.Equal(item.Id, result.Id);
        Assert.Equal(ClearanceStatus.Approved, result.Status);
        Assert.Equal(_approver.Id, result.DecidedByUserId);
        Assert.InRange(result.DecidedAt, before, after);
        Assert.Null(result.DecisionNote);

        var stored = await LoadItemAsync(item.Id);
        Assert.Equal(ClearanceStatus.Approved, stored.Status);
        Assert.Equal(_approver.Id, stored.DecidedByUserId);
        Assert.Equal(result.DecidedAt, stored.DecidedAt);
        Assert.Equal(result.DecidedAt, stored.UpdatedAt);
        Assert.Null(stored.DecisionNote);
        Assert.Equal(_worker.Id, stored.SubmittedByUserId);
        Assert.Equal(item.SubmittedAt, stored.SubmittedAt);

        var audits = await _dbContext.AuditLogs.AsNoTracking().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(("clearance_approved", nameof(ClearanceItem), item.Id, _approver.Id, _tenantId, result.DecidedAt),
            (audit.Action, audit.EntityName, audit.EntityId, audit.UserId, audit.TenantId, audit.CreatedAt));
    }

    [Fact]
    public async Task Reject_PendingItemWithoutNote_StoresRejectedWithNullNote()
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);

        var result = await _service.RejectAsync(item.Id, null, CancellationToken.None);

        Assert.Equal(ClearanceStatus.Rejected, result.Status);
        var stored = await LoadItemAsync(item.Id);
        Assert.Equal(ClearanceStatus.Rejected, stored.Status);
        Assert.Equal(_approver.Id, stored.DecidedByUserId);
        Assert.NotNull(stored.DecidedAt);
        Assert.Null(stored.DecisionNote);
        Assert.Equal("clearance_rejected", (await _dbContext.AuditLogs.AsNoTracking().SingleAsync()).Action);
    }

    [Theory]
    [InlineData("Recheck the bolts", "Recheck the bolts")]
    [InlineData("  Trimmed note \n", "Trimmed note")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    public async Task Reject_NoteIsNormalizedAndPersisted(string note, string? expected)
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);

        var result = await _service.RejectAsync(item.Id, note, CancellationToken.None);

        Assert.Equal(expected, result.DecisionNote);
        Assert.Equal(expected, (await LoadItemAsync(item.Id)).DecisionNote);
    }

    [Fact]
    public async Task Reject_NoteAtMaximumLength_IsAccepted()
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);
        var note = new string('n', 2000);

        await _service.RejectAsync(item.Id, note, CancellationToken.None);

        Assert.Equal(note, (await LoadItemAsync(item.Id)).DecisionNote);
    }

    [Fact]
    public async Task Reject_NoteTooLong_IsRejectedAndItemStaysPending()
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.RejectAsync(item.Id, new string('n', 2001), CancellationToken.None));

        await AssertStillPendingAsync(item.Id);
    }

    [Fact]
    public async Task Decide_MorningRoundItem_IsSupported()
    {
        var submission = await SeedSubmissionAsync(_tenantId);
        var item = await SeedMorningRoundItemAsync(_tenantId, submission.Id, _secondMachine.Id);

        await _service.ApproveAsync(item.Id, CancellationToken.None);

        Assert.Equal(ClearanceStatus.Approved, (await LoadItemAsync(item.Id)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelfApproval_AssignedApproverCanDecideOwnSubmission(bool approve)
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "own")).Id,
            submittedBy: _approver.Id);

        var result = approve
            ? await _service.ApproveAsync(item.Id, CancellationToken.None)
            : await _service.RejectAsync(item.Id, "own note", CancellationToken.None);

        Assert.Equal(_approver.Id, result.DecidedByUserId);
        var stored = await LoadItemAsync(item.Id);
        Assert.Equal(_approver.Id, stored.SubmittedByUserId);
        Assert.Equal(_approver.Id, stored.DecidedByUserId);
    }

    // ---------- Decisions: authorization ----------

    public static TheoryData<string> NonApproverScenarios => new()
    {
        "worker", "superadmin", "manager", "inactive-approver", "foreign-approver", "viewer"
    };

    [Theory]
    [MemberData(nameof(NonApproverScenarios))]
    public async Task Decide_NonApprover_IsForbiddenAndItemUnchanged(string scenario)
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);
        await ArrangeNonApproverAsync(scenario);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ApproveAsync(item.Id, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.RejectAsync(item.Id, "note", CancellationToken.None));

        await AssertStillPendingAsync(item.Id);
        Assert.False(await _dbContext.AuditLogs.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task Decide_NonApprover_GetsForbiddenEvenForMissingItem()
    {
        ActAs(_worker);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ApproveAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Decide_UnresolvedTenant_IsRejected()
    {
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);
        _tenantContext.TenantId = null;

        var approve = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApproveAsync(item.Id, CancellationToken.None));
        var reject = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RejectAsync(item.Id, null, CancellationToken.None));

        Assert.Equal("Tenant not resolved.", approve.Message);
        Assert.Equal("Tenant not resolved.", reject.Message);
        _tenantContext.TenantId = _tenantId;
        await AssertStillPendingAsync(item.Id);
    }

    // ---------- Decisions: tenant isolation ----------

    [Fact]
    public async Task Decide_ItemOfAnotherTenant_IsNotFoundAndUnchanged()
    {
        var otherEntry = await SeedEntryAsync(_otherTenantId, _otherTenantMachine.Id, "foreign");
        var otherItem = await SeedEntryItemAsync(_otherTenantId, otherEntry.Id);

        var approve = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.ApproveAsync(otherItem.Id, CancellationToken.None));
        var reject = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.RejectAsync(otherItem.Id, "x", CancellationToken.None));
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.ApproveAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(missing.Message, approve.Message);
        Assert.Equal(missing.Message, reject.Message);
        await AssertStillPendingAsync(otherItem.Id);
        var queue = await _service.GetQueueAsync(new ClearanceQueueQuery(), CancellationToken.None);
        Assert.DoesNotContain(queue.Items, i => i.Id == otherItem.Id);
    }

    [Fact]
    public async Task Decide_ItemWhoseSourceBelongsToAnotherTenant_IsNotFound()
    {
        var foreignEntry = await SeedEntryAsync(_otherTenantId, _otherTenantMachine.Id, "foreign");
        var mislinked = await SeedEntryItemAsync(_tenantId, foreignEntry.Id);
        var foreignSubmission = await SeedSubmissionAsync(_otherTenantId);
        var foreignMachineItem = await SeedMorningRoundItemAsync(_tenantId, foreignSubmission.Id, _machine.Id);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.ApproveAsync(mislinked.Id, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.RejectAsync(foreignMachineItem.Id, null, CancellationToken.None));

        await AssertStillPendingAsync(mislinked.Id);
        await AssertStillPendingAsync(foreignMachineItem.Id);
    }

    // ---------- Decisions: already decided / no overwrite ----------

    [Theory]
    [InlineData(ClearanceStatus.Approved)]
    [InlineData(ClearanceStatus.Rejected)]
    public async Task Decide_AlreadyDecidedItem_IsConflictAndMetadataPreserved(string existingStatus)
    {
        var originalDecider = await SeedUserAsync(_tenantId, "original-decider", UserRole.Worker);
        var originalAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var submission = await SeedSubmissionAsync(_tenantId);
        var item = await SeedMorningRoundItemAsync(_tenantId, submission.Id, _machine.Id,
            status: existingStatus, decidedBy: originalDecider.Id, decidedAt: originalAt, note: "original");

        await Assert.ThrowsAsync<ClearanceDecisionConflictException>(
            () => _service.ApproveAsync(item.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ClearanceDecisionConflictException>(
            () => _service.RejectAsync(item.Id, "overwrite attempt", CancellationToken.None));

        var stored = await LoadItemAsync(item.Id);
        Assert.Equal(existingStatus, stored.Status);
        Assert.Equal(originalDecider.Id, stored.DecidedByUserId);
        Assert.Equal(originalAt, stored.DecidedAt);
        Assert.Equal("original", stored.DecisionNote);
        Assert.False(await _dbContext.AuditLogs.AnyAsync());
    }

    [Fact]
    public async Task Decide_SecondDecisionAfterFirst_IsConflictAndFirstDecisionWins()
    {
        var secondApprover = await SeedUserAsync(_tenantId, "second-approver", UserRole.Worker);
        await SeedApproverAsync(_tenantId, secondApprover.Id);
        var item = await SeedEntryItemAsync(_tenantId, (await SeedEntryAsync(_tenantId, _machine.Id, "e")).Id);

        var first = await _service.RejectAsync(item.Id, "first", CancellationToken.None);
        ActAs(secondApprover);
        await Assert.ThrowsAsync<ClearanceDecisionConflictException>(
            () => _service.ApproveAsync(item.Id, CancellationToken.None));

        var stored = await LoadItemAsync(item.Id);
        Assert.Equal(ClearanceStatus.Rejected, stored.Status);
        Assert.Equal(_approver.Id, stored.DecidedByUserId);
        Assert.Equal(first.DecidedAt, stored.DecidedAt);
        Assert.Equal("first", stored.DecisionNote);
        Assert.Equal(1, await _dbContext.AuditLogs.CountAsync());
    }

    // ---------- Decisions: separation from IsSafeToOperate / no deletion ----------

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Decide_DoesNotChangeSourceMaintenanceEntry(bool isSafeToOperate, bool approve)
    {
        var entry = await SeedEntryAsync(_tenantId, _machine.Id, "declared", isSafeToOperate);
        var item = await SeedEntryItemAsync(_tenantId, entry.Id);

        if (approve)
        {
            await _service.ApproveAsync(item.Id, CancellationToken.None);
        }
        else
        {
            await _service.RejectAsync(item.Id, "note", CancellationToken.None);
        }

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.MaintenanceEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        Assert.Equal(isSafeToOperate, stored.IsSafeToOperate);
        Assert.Equal(entry.Description, stored.Description);
        Assert.Equal(entry.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(1, await _dbContext.MaintenanceEntries.CountAsync());
        Assert.Equal(1, await _dbContext.ClearanceItems.CountAsync());
    }

    // ---------- Helpers ----------

    private async Task ArrangeNonApproverAsync(string scenario)
    {
        switch (scenario)
        {
            case "worker":
                ActAs(_worker);
                break;
            case "superadmin":
                ActAs(await SeedUserAsync(_tenantId, "unassigned-admin", UserRole.SuperAdmin));
                break;
            case "manager":
                ActAs(await SeedUserAsync(_tenantId, "unassigned-manager", UserRole.Manager));
                break;
            case "inactive-approver":
                await SetActiveAsync(_approver.Id, false);
                break;
            case "foreign-approver":
                var foreign = await SeedUserAsync(_otherTenantId, "foreign-approver", UserRole.SuperAdmin);
                await SeedApproverAsync(_otherTenantId, foreign.Id);
                ActAs(foreign);
                break;
            case "viewer":
                await SeedViewerAsync(_tenantId, _worker.Id);
                ActAs(_worker);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private async Task<ClearanceItem> LoadItemAsync(Guid id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.ClearanceItems.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task AssertStillPendingAsync(Guid id)
    {
        var stored = await LoadItemAsync(id);
        Assert.Equal(ClearanceStatus.Pending, stored.Status);
        Assert.Null(stored.DecidedByUserId);
        Assert.Null(stored.DecidedAt);
        Assert.Null(stored.DecisionNote);
    }

    private void ActAs(User user)
    {
        _currentUser.UserId = user.Id;
        _currentUser.RoleId = user.RoleId;
    }

    private async Task SetActiveAsync(Guid userId, bool isActive)
    {
        await _dbContext.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, isActive));
    }

    private async Task<User> SeedUserAsync(Guid tenantId, string username, UserRole role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            DisplayName = username,
            RoleId = (int)role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return user;
    }

    private async Task SeedApproverAsync(Guid tenantId, Guid userId)
    {
        _dbContext.ClearanceApprovers.Add(new ClearanceApprover
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task SeedViewerAsync(Guid tenantId, Guid userId)
    {
        _dbContext.ClearanceViewers.Add(new ClearanceViewer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<WorkGroup> SeedArrayAsync(Guid tenantId, string nameKey)
    {
        var array = new WorkGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NameKey = nameKey,
            SortOrder = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Arrays.Add(array);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return array;
    }

    private async Task<Machine> SeedMachineAsync(Guid tenantId, string code, Guid? arrayId)
    {
        var machine = new Machine
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = $"machine.{code}",
            Code = code,
            ArrayId = arrayId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Machines.Add(machine);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return machine;
    }

    private async Task<MaintenanceEntry> SeedEntryAsync(
        Guid tenantId, Guid machineId, string description, bool isSafeToOperate = true)
    {
        var entry = new MaintenanceEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 10, 15),
            Description = description,
            EmployeeName = "Technician",
            WorkHours = 1,
            IsSafeToOperate = isSafeToOperate,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MaintenanceEntries.Add(entry);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return entry;
    }

    private async Task<MorningRoundV2Submission> SeedSubmissionAsync(Guid tenantId)
    {
        var submission = new MorningRoundV2Submission
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReportDate = new DateOnly(2026, 10, 16),
            SubmittedAt = DateTime.UtcNow,
            SubmittedByUserId = Guid.NewGuid(),
            ItemsJson = "[]",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MorningRoundV2Submissions.Add(submission);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return submission;
    }

    private async Task<ClearanceItem> SeedEntryItemAsync(
        Guid tenantId,
        Guid maintenanceEntryId,
        string status = ClearanceStatus.Pending,
        Guid? submittedBy = null,
        DateTime? submittedAt = null)
    {
        var item = new ClearanceItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = maintenanceEntryId,
            Status = status,
            SubmittedByUserId = submittedBy ?? Guid.NewGuid(),
            SubmittedAt = submittedAt ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        if (status != ClearanceStatus.Pending)
        {
            item.DecidedByUserId = _approver.Id;
            item.DecidedAt = DateTime.UtcNow;
        }

        _dbContext.ClearanceItems.Add(item);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return item;
    }

    private async Task<ClearanceItem> SeedMorningRoundItemAsync(
        Guid tenantId,
        Guid submissionId,
        Guid abnormalMachineId,
        string status = ClearanceStatus.Pending,
        Guid? decidedBy = null,
        DateTime? decidedAt = null,
        string? note = null)
    {
        var itemsJson = $"[{{\"machineId\":\"{abnormalMachineId}\",\"status\":\"fail\",\"notes\":\"abnormal\"}}]";
        await _dbContext.MorningRoundV2Submissions.IgnoreQueryFilters()
            .Where(s => s.Id == submissionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.ItemsJson, itemsJson));

        var item = new ClearanceItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceType = ClearanceSourceType.MorningRoundV2,
            MorningRoundV2SubmissionId = submissionId,
            Status = status,
            SubmittedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
            DecidedByUserId = decidedBy,
            DecidedAt = decidedAt,
            DecisionNote = note,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClearanceItems.Add(item);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return item;
    }
}
