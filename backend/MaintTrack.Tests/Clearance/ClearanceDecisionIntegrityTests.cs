using System.Data;
using System.Data.Common;
using MaintTrack.Application.Clearance;
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
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// Decision atomicity, transaction isolation, independence from IsSafeToOperate, and the
/// access / by-source read paths used by the Clearance screens.
/// </summary>
public sealed class ClearanceDecisionIntegrityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly TransactionRecorder _transactions = new();
    private TenantContext _tenantContext = null!;
    private StubCurrentUserContext _currentUser = null!;
    private MaintTrackDbContext _dbContext = null!;
    private ClearanceService _service = null!;

    private User _approver = null!;
    private User _viewer = null!;
    private User _worker = null!;
    private User _superAdmin = null!;
    private User _manager = null!;
    private Guid _machineId;
    private Guid _otherTenantMachineId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await ExecuteSqlAsync("PRAGMA foreign_keys = ON;");

        _tenantContext = new TenantContext { TenantId = _tenantId };
        _currentUser = new StubCurrentUserContext(_tenantId, Guid.NewGuid());
        _dbContext = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(_transactions)
                .Options,
            _tenantContext);
        await _dbContext.Database.EnsureCreatedAsync();

        _dbContext.Roles.AddRange(
            new Role { Id = (int)UserRole.Worker, Name = "Worker" },
            new Role { Id = (int)UserRole.Manager, Name = "Manager" },
            new Role { Id = (int)UserRole.SuperAdmin, Name = "SuperAdmin" });
        _approver = AddUser(_tenantId, "approver", UserRole.Worker);
        _viewer = AddUser(_tenantId, "viewer", UserRole.Worker);
        _worker = AddUser(_tenantId, "worker", UserRole.Worker);
        _superAdmin = AddUser(_tenantId, "admin", UserRole.SuperAdmin);
        _manager = AddUser(_tenantId, "manager", UserRole.Manager);
        _dbContext.ClearanceApprovers.Add(new ClearanceApprover
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _approver.Id, CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClearanceViewers.Add(new ClearanceViewer
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _viewer.Id, CreatedAt = DateTime.UtcNow
        });
        _machineId = AddMachine(_tenantId, "M1");
        _otherTenantMachineId = AddMachine(_otherTenantId, "X1");
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        _service = new ClearanceService(_dbContext, _tenantContext, _currentUser);
        ActAs(_approver);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---------- Atomicity ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedAuditInsert_RollsBackTheDecision(bool approve)
    {
        var (_, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        await ExecuteSqlAsync(
            "CREATE TRIGGER fail_clearance_audit BEFORE INSERT ON audit_logs " +
            "WHEN NEW.action LIKE 'clearance_%' BEGIN SELECT RAISE(ABORT, 'injected audit failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() => approve
            ? _service.ApproveAsync(itemId, CancellationToken.None)
            : _service.RejectAsync(itemId, "note", CancellationToken.None));

        _dbContext.ChangeTracker.Clear();
        var item = await LoadItemAsync(itemId);
        Assert.Equal(ClearanceStatus.Pending, item.Status);
        Assert.Null(item.DecidedByUserId);
        Assert.Null(item.DecidedAt);
        Assert.Null(item.DecisionNote);
        Assert.Null(item.UpdatedAt);
        Assert.False(await _dbContext.AuditLogs.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task AfterFailedAudit_ALaterDecisionStillSucceedsOnce()
    {
        var (_, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        await ExecuteSqlAsync(
            "CREATE TRIGGER fail_clearance_audit BEFORE INSERT ON audit_logs " +
            "WHEN NEW.action LIKE 'clearance_%' BEGIN SELECT RAISE(ABORT, 'injected audit failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => _service.ApproveAsync(itemId, CancellationToken.None));
        await ExecuteSqlAsync("DROP TRIGGER fail_clearance_audit;");
        _dbContext.ChangeTracker.Clear();

        await _service.ApproveAsync(itemId, CancellationToken.None);

        Assert.Equal(ClearanceStatus.Approved, (await LoadItemAsync(itemId)).Status);
        var audit = Assert.Single(await _dbContext.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(("clearance_approved", nameof(ClearanceItem), itemId), (audit.Action, audit.EntityName, audit.EntityId));
    }

    // ---------- Isolation level ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decision_RunsInAnExplicitReadCommittedTransaction(bool approve)
    {
        var (_, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        _transactions.Started.Clear();

        if (approve)
            await _service.ApproveAsync(itemId, CancellationToken.None);
        else
            await _service.RejectAsync(itemId, null, CancellationToken.None);

        Assert.Equal(IsolationLevel.ReadCommitted, Assert.Single(_transactions.Started));
    }

    // ---------- IsSafeToOperate independence ----------

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Decisions_NeverChangeIsSafeToOperate(bool isSafeToOperate, bool approve)
    {
        var (entryId, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId, isSafeToOperate);

        if (approve)
            await _service.ApproveAsync(itemId, CancellationToken.None);
        else
            await _service.RejectAsync(itemId, "unsafe", CancellationToken.None);

        _dbContext.ChangeTracker.Clear();
        var entry = await _dbContext.MaintenanceEntries.AsNoTracking().SingleAsync(e => e.Id == entryId);
        Assert.Equal(isSafeToOperate, entry.IsSafeToOperate);
        Assert.Null(entry.UpdatedAt);
    }

    // ---------- Unassigned privileged roles ----------

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.SuperAdmin)]
    public async Task UnassignedManagerOrSuperAdmin_CannotDecide(UserRole role)
    {
        var (_, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        ActAs(role == UserRole.Manager ? _manager : _superAdmin);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ApproveAsync(itemId, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RejectAsync(itemId, null, CancellationToken.None));

        Assert.Equal(ClearanceStatus.Pending, (await LoadItemAsync(itemId)).Status);
    }

    // ---------- Access ----------

    [Fact]
    public async Task Access_ReflectsAssignmentRoleAndActiveState()
    {
        ActAs(_approver);
        Assert.Equal(new ClearanceAccessDto(true, true), await _service.GetAccessAsync(CancellationToken.None));

        ActAs(_viewer);
        Assert.Equal(new ClearanceAccessDto(true, false), await _service.GetAccessAsync(CancellationToken.None));

        ActAs(_superAdmin);
        Assert.Equal(new ClearanceAccessDto(false, false), await _service.GetAccessAsync(CancellationToken.None));

        ActAs(_manager);
        Assert.Equal(new ClearanceAccessDto(false, false), await _service.GetAccessAsync(CancellationToken.None));

        ActAs(_worker);
        Assert.Equal(new ClearanceAccessDto(false, false), await _service.GetAccessAsync(CancellationToken.None));

        await _dbContext.Users.Where(u => u.Id == _approver.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        ActAs(_approver);
        Assert.Equal(new ClearanceAccessDto(false, false), await _service.GetAccessAsync(CancellationToken.None));

        await _dbContext.Users.Where(u => u.Id == _viewer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        ActAs(_viewer);
        Assert.Equal(new ClearanceAccessDto(false, false), await _service.GetAccessAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Access_ForgedSuperAdminRoleClaim_DoesNotGrantView()
    {
        ActAs(_worker);
        _currentUser.RoleId = (int)UserRole.SuperAdmin;

        Assert.Equal(new ClearanceAccessDto(false, false), await _service.GetAccessAsync(CancellationToken.None));
    }

    // ---------- By source ----------

    [Fact]
    public async Task BySource_ReturnsDecisionMetadataAndCallerDecisionRight()
    {
        var (entryId, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        await _service.RejectAsync(itemId, "Replace the seal", CancellationToken.None);

        ActAs(_viewer);
        var asViewer = await _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, entryId, CancellationToken.None);
        ActAs(_approver);
        var asApprover = await _service.GetBySourceAsync("MAINTENANCE_ENTRY", entryId, CancellationToken.None);

        Assert.NotNull(asViewer);
        Assert.False(asViewer!.CanApprove);
        Assert.Equal(ClearanceStatus.Rejected, asViewer.Item.Status);
        Assert.Equal("approver", asViewer.Item.DecidedByName);
        Assert.Equal("Replace the seal", asViewer.Item.DecisionNote);
        Assert.NotNull(asViewer.Item.DecidedAt);
        Assert.True(asApprover!.CanApprove);
    }

    [Theory]
    [InlineData(UserRole.Worker)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.SuperAdmin)]
    public async Task BySource_ActiveTenantUserWithoutGrant_IsForbiddenEvenWithKnownReportId(UserRole role)
    {
        var (entryId, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        await _service.RejectAsync(itemId, "Replace the seal", CancellationToken.None);
        ActAs(role switch { UserRole.Manager => _manager, UserRole.SuperAdmin => _superAdmin, _ => _worker });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, entryId, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetBySourceAsync("treatment", entryId, CancellationToken.None));
    }

    [Fact]
    public async Task BySource_GrantsFromAnotherTenant_DoNotAuthorize()
    {
        var (entryId, _) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        var foreignViewer = AddUser(_otherTenantId, "foreign-viewer", UserRole.Worker);
        var foreignApprover = AddUser(_otherTenantId, "foreign-approver", UserRole.Worker);
        _dbContext.ClearanceViewers.Add(
            new ClearanceViewer { Id = Guid.NewGuid(), TenantId = _otherTenantId, UserId = foreignViewer.Id, CreatedAt = DateTime.UtcNow });
        _dbContext.ClearanceApprovers.Add(
            new ClearanceApprover { Id = Guid.NewGuid(), TenantId = _otherTenantId, UserId = foreignApprover.Id, CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        _dbContext.ClearanceViewers.Add(
            new ClearanceViewer { Id = Guid.NewGuid(), TenantId = _otherTenantId, UserId = _worker.Id, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
        _dbContext.ChangeTracker.Clear();

        foreach (var user in new[] { foreignViewer, foreignApprover, _worker })
        {
            ActAs(user);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, entryId, CancellationToken.None));
            Assert.Equal(new ClearanceAccessDto(false, false), await _service.GetAccessAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Viewer_CannotDecide()
    {
        var (_, itemId) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        ActAs(_viewer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ApproveAsync(itemId, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RejectAsync(itemId, "x", CancellationToken.None));

        var item = await LoadItemAsync(itemId);
        Assert.Equal(ClearanceStatus.Pending, item.Status);
        Assert.Null(item.DecisionNote);
    }

    [Fact]
    public async Task BySource_MorningRoundItem_ListsAbnormalFindingsOnly()
    {
        var submissionId = Guid.NewGuid();
        var normalMachine = AddMachine(_tenantId, "M2");
        _dbContext.MorningRoundV2Submissions.Add(new MorningRoundV2Submission
        {
            Id = submissionId,
            TenantId = _tenantId,
            ReportDate = new DateOnly(2026, 10, 20),
            SubmittedAt = DateTime.UtcNow,
            SubmittedByUserId = _worker.Id,
            ItemsJson = $"[{{\"machineId\":\"{_machineId}\",\"status\":\"fail\",\"notes\":\"oil leak\"}}," +
                        $"{{\"machineId\":\"{normalMachine}\",\"status\":\"ok\",\"notes\":\"fine\"}}]",
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClearanceItems.Add(new ClearanceItem
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            SourceType = ClearanceSourceType.MorningRoundV2,
            MorningRoundV2SubmissionId = submissionId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = _worker.Id,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var response = await _service.GetBySourceAsync(ClearanceSourceType.MorningRoundV2, submissionId, CancellationToken.None);

        var finding = Assert.Single(response!.Item.Findings);
        Assert.Equal(_machineId, finding.MachineId);
        Assert.Equal("machine.M1", finding.MachineName);
        Assert.Equal("oil leak", finding.Notes);
        Assert.Equal("worker", response.Item.SubmittedByName);
    }

    [Fact]
    public async Task BySource_ReportWithoutItem_ReturnsNull()
    {
        var entryId = await SeedEntryAsync(_tenantId, _machineId);

        Assert.Null(await _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, entryId, CancellationToken.None));
    }

    [Fact]
    public async Task BySource_ReportOfAnotherTenant_ReturnsNull()
    {
        var (foreignEntryId, _) = await SeedEntryWithItemAsync(_otherTenantId, _otherTenantMachineId);

        Assert.Null(await _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, foreignEntryId, CancellationToken.None));
    }

    [Fact]
    public async Task BySource_UnknownSourceType_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetBySourceAsync("treatment", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task BySource_InactiveOrForeignUser_IsUnauthorized()
    {
        var (entryId, _) = await SeedEntryWithItemAsync(_tenantId, _machineId);
        await _dbContext.Users.Where(u => u.Id == _worker.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        ActAs(_worker);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, entryId, CancellationToken.None));

        _currentUser.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetBySourceAsync(ClearanceSourceType.MaintenanceEntry, entryId, CancellationToken.None));
    }

    // ---------- Helpers ----------

    private void ActAs(User user)
    {
        _currentUser.UserId = user.Id;
        _currentUser.RoleId = user.RoleId;
    }

    private User AddUser(Guid tenantId, string username, UserRole role)
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
        return user;
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

    private async Task<Guid> SeedEntryAsync(Guid tenantId, Guid machineId, bool isSafeToOperate = true)
    {
        var entry = new MaintenanceEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 10, 20),
            Description = "work",
            EmployeeName = "Technician",
            WorkHours = 1,
            IsSafeToOperate = isSafeToOperate,
            CreatedByUserId = _worker.Id,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MaintenanceEntries.Add(entry);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return entry.Id;
    }

    private async Task<(Guid EntryId, Guid ItemId)> SeedEntryWithItemAsync(
        Guid tenantId, Guid machineId, bool isSafeToOperate = true)
    {
        var entryId = await SeedEntryAsync(tenantId, machineId, isSafeToOperate);
        var item = new ClearanceItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = entryId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = _worker.Id,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClearanceItems.Add(item);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return (entryId, item.Id);
    }

    private async Task<ClearanceItem> LoadItemAsync(Guid id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.ClearanceItems.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TransactionRecorder : DbTransactionInterceptor
    {
        public List<IsolationLevel> Started { get; } = new();

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Started.Add(eventData.IsolationLevel);
            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
