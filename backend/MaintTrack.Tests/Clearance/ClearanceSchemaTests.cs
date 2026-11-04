using MaintTrack.Application.Maintenance;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.MorningRoundV2;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Maintenance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaintTrack.Tests.Clearance;

public sealed class ClearanceSchemaTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenantContext;
    private readonly MaintTrackDbContext _dbContext;
    private readonly Guid _tenantId;
    private readonly Guid _otherTenantId;

    public ClearanceSchemaTests()
    {
        _tenantId = Guid.NewGuid();
        _otherTenantId = Guid.NewGuid();
        _tenantContext = new TenantContext { TenantId = _tenantId };

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new MaintTrackDbContext(options, _tenantContext);
        _dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void Model_MapsTablesColumnsAndTenantFilters()
    {
        var item = _dbContext.Model.FindEntityType(typeof(ClearanceItem))!;
        var approver = _dbContext.Model.FindEntityType(typeof(ClearanceApprover))!;

        Assert.Equal("clearance_items", item.GetTableName());
        Assert.Equal("clearance_approvers", approver.GetTableName());
        Assert.NotNull(item.GetQueryFilter());
        Assert.NotNull(approver.GetQueryFilter());

        Assert.Equal("tenant_id", item.FindProperty(nameof(ClearanceItem.TenantId))!.GetColumnName());
        Assert.Equal("source_type", item.FindProperty(nameof(ClearanceItem.SourceType))!.GetColumnName());
        Assert.Equal(
            "morning_round_v2_submission_id",
            item.FindProperty(nameof(ClearanceItem.MorningRoundV2SubmissionId))!.GetColumnName());
        Assert.Equal("decided_by_user_id", item.FindProperty(nameof(ClearanceItem.DecidedByUserId))!.GetColumnName());
        Assert.Equal("decided_at", item.FindProperty(nameof(ClearanceItem.DecidedAt))!.GetColumnName());

        Assert.False(item.FindProperty(nameof(ClearanceItem.SourceType))!.IsNullable);
        Assert.False(item.FindProperty(nameof(ClearanceItem.Status))!.IsNullable);
        Assert.False(item.FindProperty(nameof(ClearanceItem.SubmittedByUserId))!.IsNullable);
        Assert.False(item.FindProperty(nameof(ClearanceItem.SubmittedAt))!.IsNullable);

        var checkConstraints = _dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ClearanceItem))!
            .GetCheckConstraints()
            .Select(c => c.Name)
            .ToHashSet();
        Assert.Contains("ck_clearance_items_source_type", checkConstraints);
        Assert.Contains("ck_clearance_items_status", checkConstraints);
        Assert.Contains("ck_clearance_items_source_reference", checkConstraints);
        Assert.Contains("ck_clearance_items_decision", checkConstraints);
    }

    [Fact]
    public void Model_AllClearanceForeignKeysRestrictDelete()
    {
        var foreignKeys = _dbContext.Model.FindEntityType(typeof(ClearanceItem))!.GetForeignKeys()
            .Concat(_dbContext.Model.FindEntityType(typeof(ClearanceApprover))!.GetForeignKeys())
            .ToList();

        Assert.Equal(4, foreignKeys.Count);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public void Model_HasQueueAndDuplicatePreventionIndexes()
    {
        var indexes = _dbContext.Model.FindEntityType(typeof(ClearanceItem))!.GetIndexes().ToList();

        Assert.Contains(indexes, index =>
            !index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(ClearanceItem.TenantId),
                nameof(ClearanceItem.Status),
                nameof(ClearanceItem.SubmittedAt)
            }));
        Assert.Contains(indexes, index =>
            index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ClearanceItem.MaintenanceEntryId) }));
        Assert.Contains(indexes, index =>
            index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ClearanceItem.MorningRoundV2SubmissionId) }));
    }

    [Fact]
    public async Task MaintenanceEntryItem_WithValidReference_Persists()
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);

        _dbContext.ClearanceItems.Add(CreateMaintenanceEntryItem(_tenantId, entry.Id));
        await _dbContext.SaveChangesAsync();

        var stored = await _dbContext.ClearanceItems.AsNoTracking().SingleAsync();
        Assert.Equal(ClearanceSourceType.MaintenanceEntry, stored.SourceType);
        Assert.Equal(ClearanceStatus.Pending, stored.Status);
        Assert.Equal(entry.Id, stored.MaintenanceEntryId);
        Assert.Null(stored.DecidedByUserId);
        Assert.Null(stored.DecidedAt);
    }

    [Fact]
    public async Task MorningRoundV2Item_WithValidReference_Persists()
    {
        var submission = await SeedSubmissionAsync(_tenantId);

        _dbContext.ClearanceItems.Add(CreateMorningRoundItem(_tenantId, submission.Id));
        await _dbContext.SaveChangesAsync();

        var stored = await _dbContext.ClearanceItems.AsNoTracking().SingleAsync();
        Assert.Equal(ClearanceSourceType.MorningRoundV2, stored.SourceType);
        Assert.Equal(submission.Id, stored.MorningRoundV2SubmissionId);
        Assert.Null(stored.MaintenanceEntryId);
    }

    [Theory]
    [InlineData("maintenance_entry_without_entry")]
    [InlineData("maintenance_entry_with_submission")]
    [InlineData("morning_round_without_submission")]
    [InlineData("morning_round_with_entry")]
    public async Task InvalidSourceReference_IsRejected(string scenario)
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);
        var submission = await SeedSubmissionAsync(_tenantId);

        var item = scenario switch
        {
            "maintenance_entry_without_entry" => With(CreateMaintenanceEntryItem(_tenantId, entry.Id), i => i.MaintenanceEntryId = null),
            "maintenance_entry_with_submission" => With(CreateMaintenanceEntryItem(_tenantId, entry.Id), i => i.MorningRoundV2SubmissionId = submission.Id),
            "morning_round_without_submission" => With(CreateMorningRoundItem(_tenantId, submission.Id), i => i.MorningRoundV2SubmissionId = null),
            "morning_round_with_entry" => With(CreateMorningRoundItem(_tenantId, submission.Id), i => i.MaintenanceEntryId = entry.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        _dbContext.ClearanceItems.Add(item);

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("treatment")]
    [InlineData("Maintenance_Entry")]
    [InlineData("MAINTENANCE_ENTRY")]
    public async Task UnknownSourceType_IsRejected(string sourceType)
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);

        _dbContext.ClearanceItems.Add(With(CreateMaintenanceEntryItem(_tenantId, entry.Id), i => i.SourceType = sourceType));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Pending")]
    [InlineData("cancelled")]
    public async Task UnknownStatus_IsRejected(string status)
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);

        _dbContext.ClearanceItems.Add(With(CreateMaintenanceEntryItem(_tenantId, entry.Id), i => i.Status = status));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Theory]
    [InlineData(ClearanceStatus.Approved)]
    [InlineData(ClearanceStatus.Rejected)]
    public async Task DecidedItem_WithoutApproverIdentityOrTimestamp_IsRejected(string status)
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);
        var approver = await SeedUserAsync(_tenantId, "approver");

        _dbContext.ClearanceItems.Add(With(CreateMaintenanceEntryItem(_tenantId, entry.Id), i =>
        {
            i.Status = status;
            i.DecidedByUserId = approver.Id;
        }));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task PendingItem_WithDecisionMetadata_IsRejected()
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);
        var approver = await SeedUserAsync(_tenantId, "approver");

        _dbContext.ClearanceItems.Add(With(CreateMaintenanceEntryItem(_tenantId, entry.Id), i =>
        {
            i.DecidedByUserId = approver.Id;
            i.DecidedAt = DateTime.UtcNow;
        }));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task RejectedItem_RecordsDecisionAndKeepsSourceReport()
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);
        var approver = await SeedUserAsync(_tenantId, "approver");
        var item = CreateMaintenanceEntryItem(_tenantId, entry.Id);
        _dbContext.ClearanceItems.Add(item);
        await _dbContext.SaveChangesAsync();

        var decidedAt = DateTime.UtcNow;
        item.Status = ClearanceStatus.Rejected;
        item.DecidedByUserId = approver.Id;
        item.DecidedAt = decidedAt;
        item.DecisionNote = "Missing photo of the replaced part.";
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var stored = await _dbContext.ClearanceItems.AsNoTracking().SingleAsync();
        Assert.Equal(ClearanceStatus.Rejected, stored.Status);
        Assert.Equal(approver.Id, stored.DecidedByUserId);
        Assert.Equal(decidedAt, stored.DecidedAt);
        Assert.True(await _dbContext.MaintenanceEntries.AnyAsync(e => e.Id == entry.Id));
    }

    [Fact]
    public async Task DecidedByUser_MustExist()
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);

        _dbContext.ClearanceItems.Add(With(CreateMaintenanceEntryItem(_tenantId, entry.Id), i =>
        {
            i.Status = ClearanceStatus.Approved;
            i.DecidedByUserId = Guid.NewGuid();
            i.DecidedAt = DateTime.UtcNow;
        }));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateItemForSameMaintenanceEntry_IsRejected()
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);
        _dbContext.ClearanceItems.Add(CreateMaintenanceEntryItem(_tenantId, entry.Id));
        await _dbContext.SaveChangesAsync();

        _dbContext.ClearanceItems.Add(CreateMaintenanceEntryItem(_tenantId, entry.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateItemForSameSubmission_IsRejected()
    {
        var submission = await SeedSubmissionAsync(_tenantId);
        _dbContext.ClearanceItems.Add(CreateMorningRoundItem(_tenantId, submission.Id));
        await _dbContext.SaveChangesAsync();

        _dbContext.ClearanceItems.Add(CreateMorningRoundItem(_tenantId, submission.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MaintenanceEntryItem_RequiresExistingEntry()
    {
        _dbContext.ClearanceItems.Add(CreateMaintenanceEntryItem(_tenantId, Guid.NewGuid()));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MorningRoundItem_RequiresExistingSubmission()
    {
        _dbContext.ClearanceItems.Add(CreateMorningRoundItem(_tenantId, Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingMaintenanceEntryWithClearanceItem_IsRestricted()
    {
        var entry = await SeedMaintenanceEntryAsync(_tenantId);
        _dbContext.ClearanceItems.Add(CreateMaintenanceEntryItem(_tenantId, entry.Id));
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var tracked = await _dbContext.MaintenanceEntries.SingleAsync(e => e.Id == entry.Id);
        _dbContext.MaintenanceEntries.Remove(tracked);

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ClearanceItem_IsTenantIsolated()
    {
        var mine = await SeedMaintenanceEntryAsync(_tenantId);
        var other = await SeedMaintenanceEntryAsync(_otherTenantId);

        _dbContext.ClearanceItems.AddRange(
            CreateMaintenanceEntryItem(_tenantId, mine.Id),
            CreateMaintenanceEntryItem(_otherTenantId, other.Id));
        await _dbContext.SaveChangesAsync();

        var visible = await _dbContext.ClearanceItems.AsNoTracking().ToListAsync();

        Assert.Single(visible);
        Assert.Equal(_tenantId, visible[0].TenantId);
        Assert.Equal(mine.Id, visible[0].MaintenanceEntryId);
    }

    [Fact]
    public async Task ClearanceApprover_IsTenantIsolated()
    {
        var mine = await SeedUserAsync(_tenantId, "tenant-approver");
        var other = await SeedUserAsync(_otherTenantId, "other-approver");

        _dbContext.ClearanceApprovers.AddRange(
            CreateApprover(_tenantId, mine.Id),
            CreateApprover(_otherTenantId, other.Id));
        await _dbContext.SaveChangesAsync();

        var visible = await _dbContext.ClearanceApprovers.AsNoTracking().ToListAsync();

        Assert.Single(visible);
        Assert.Equal(_tenantId, visible[0].TenantId);
        Assert.Equal(mine.Id, visible[0].UserId);
    }

    // Documents why Clearance services must also filter by tenant explicitly:
    // the shared query filter is disabled when the tenant is unresolved.
    [Fact]
    public async Task ClearanceQueryFilter_IsBypassedWhenTenantUnresolved()
    {
        var mine = await SeedMaintenanceEntryAsync(_tenantId);
        var other = await SeedMaintenanceEntryAsync(_otherTenantId);
        _dbContext.ClearanceItems.AddRange(
            CreateMaintenanceEntryItem(_tenantId, mine.Id),
            CreateMaintenanceEntryItem(_otherTenantId, other.Id));
        await _dbContext.SaveChangesAsync();

        _tenantContext.TenantId = null;

        Assert.Equal(2, await _dbContext.ClearanceItems.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task ClearanceApprover_IsUniquePerTenantAndUser()
    {
        var user = await SeedUserAsync(_tenantId, "approver");
        _dbContext.ClearanceApprovers.Add(CreateApprover(_tenantId, user.Id));
        await _dbContext.SaveChangesAsync();

        _dbContext.ClearanceApprovers.Add(CreateApprover(_tenantId, user.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ClearanceApprover_RequiresExistingUser()
    {
        _dbContext.ClearanceApprovers.Add(CreateApprover(_tenantId, Guid.NewGuid()));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    private static T With<T>(T value, Action<T> configure)
    {
        configure(value);
        return value;
    }

    private async Task<User> SeedUserAsync(Guid tenantId, string username)
    {
        if (!await _dbContext.Roles.AnyAsync(role => role.Id == 1))
        {
            _dbContext.Roles.Add(new Role { Id = 1, Name = "Worker" });
            await _dbContext.SaveChangesAsync();
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            DisplayName = username,
            RoleId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    private async Task<Machine> SeedMachineAsync(Guid tenantId, string code)
    {
        var machine = new Machine
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = $"machine.{code}",
            Code = code,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Machines.Add(machine);
        await _dbContext.SaveChangesAsync();
        return machine;
    }

    private async Task<MaintenanceEntry> SeedMaintenanceEntryAsync(Guid tenantId)
    {
        var entry = new MaintenanceEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MachineId = Guid.NewGuid(),
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Description = string.Empty,
            EmployeeName = "Technician",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MaintenanceEntries.Add(entry);
        await _dbContext.SaveChangesAsync();
        return entry;
    }

    private async Task<MorningRoundV2Submission> SeedSubmissionAsync(Guid tenantId)
    {
        var submission = new MorningRoundV2Submission
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReportDate = DateOnly.FromDateTime(DateTime.UtcNow),
            SubmittedAt = DateTime.UtcNow,
            SubmittedByUserId = Guid.NewGuid(),
            ItemsJson = "[]",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MorningRoundV2Submissions.Add(submission);
        await _dbContext.SaveChangesAsync();
        return submission;
    }

    private static ClearanceItem CreateMaintenanceEntryItem(Guid tenantId, Guid maintenanceEntryId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = maintenanceEntryId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

    private static ClearanceItem CreateMorningRoundItem(Guid tenantId, Guid submissionId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceType = ClearanceSourceType.MorningRoundV2,
            MorningRoundV2SubmissionId = submissionId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

    private static ClearanceApprover CreateApprover(Guid tenantId, Guid userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
}
