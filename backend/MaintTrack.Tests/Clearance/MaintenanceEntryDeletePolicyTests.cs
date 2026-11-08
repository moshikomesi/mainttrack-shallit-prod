using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Maintenance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Tests.MachineParameterPhotos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaintTrack.Tests.Clearance;

public sealed class MaintenanceEntryDeletePolicyTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenantContext;
    private readonly StubCurrentUserContext _currentUser;
    private readonly RecordingFileStorageService _storage;
    private readonly MaintTrackDbContext _dbContext;
    private readonly MaintenanceEntryService _service;
    private readonly Guid _tenantId;

    public MaintenanceEntryDeletePolicyTests()
    {
        _tenantId = Guid.NewGuid();
        _tenantContext = new TenantContext { TenantId = _tenantId };
        _currentUser = new StubCurrentUserContext(_tenantId, Guid.NewGuid())
        {
            RoleId = (int)UserRole.SuperAdmin
        };
        _storage = new RecordingFileStorageService();

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

        _service = new MaintenanceEntryService(
            _dbContext,
            _tenantContext,
            _currentUser,
            _storage,
            NullLogger<MaintenanceEntryService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task DeleteAsync_LegacyEntryWithoutClearance_IsRejected()
    {
        var entry = await SeedEntryAsync(_tenantId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(entry.Id, CancellationToken.None));

        Assert.Equal("Maintenance reports cannot be deleted.", ex.Message);
        await AssertNothingDeletedAsync(entry.Id);
    }

    [Fact]
    public async Task DeleteAsync_EntryLinkedToClearance_IsRejected()
    {
        var entry = await SeedEntryAsync(_tenantId);
        await SeedClearanceItemAsync(_tenantId, entry.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(entry.Id, CancellationToken.None));

        Assert.Equal("Maintenance reports cannot be deleted.", ex.Message);
        await AssertNothingDeletedAsync(entry.Id);
        Assert.True(await _dbContext.ClearanceItems.AnyAsync(i => i.MaintenanceEntryId == entry.Id));
    }

    [Theory]
    [InlineData(ClearanceStatus.Approved)]
    [InlineData(ClearanceStatus.Rejected)]
    public async Task DeleteAsync_EntryWithDecidedClearance_IsRejected(string status)
    {
        var entry = await SeedEntryAsync(_tenantId);
        var deciderId = await SeedUserAsync();
        _dbContext.ClearanceItems.Add(new ClearanceItem
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = entry.Id,
            Status = status,
            SubmittedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
            DecidedByUserId = deciderId,
            DecidedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(entry.Id, CancellationToken.None));

        Assert.Equal("Maintenance reports cannot be deleted.", ex.Message);
        await AssertNothingDeletedAsync(entry.Id);
    }

    [Fact]
    public async Task DeleteAsync_EntryLinkedFromAnotherTenantRow_IsRejected()
    {
        var entry = await SeedEntryAsync(_tenantId);
        await SeedClearanceItemAsync(Guid.NewGuid(), entry.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(entry.Id, CancellationToken.None));

        await AssertNothingDeletedAsync(entry.Id);
    }

    [Theory]
    [InlineData((int)UserRole.Worker, false)]
    [InlineData((int)UserRole.Worker, true)]
    [InlineData((int)UserRole.Manager, false)]
    [InlineData((int)UserRole.Manager, true)]
    public async Task DeleteAsync_NonSuperAdmin_IsStillUnauthorized(int roleId, bool linkedToClearance)
    {
        var entry = await SeedEntryAsync(_tenantId);
        if (linkedToClearance)
        {
            await SeedClearanceItemAsync(_tenantId, entry.Id);
        }

        _currentUser.RoleId = roleId;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.DeleteAsync(entry.Id, CancellationToken.None));

        await AssertNothingDeletedAsync(entry.Id);
    }

    [Fact]
    public async Task DeleteAsync_EntryInAnotherTenant_IsUnauthorizedWithoutDetails()
    {
        var otherEntry = await SeedEntryAsync(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.DeleteAsync(otherEntry.Id, CancellationToken.None));

        Assert.Equal("You are not allowed to delete this maintenance entry.", ex.Message);
        await AssertNothingDeletedAsync(otherEntry.Id);
    }

    [Fact]
    public async Task DeleteAsync_MissingEntry_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_UnresolvedTenant_IsRejected()
    {
        var entry = await SeedEntryAsync(_tenantId);
        _tenantContext.TenantId = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DeleteAsync(entry.Id, CancellationToken.None));

        Assert.Equal("Tenant not resolved.", ex.Message);
        _tenantContext.TenantId = _tenantId;
        await AssertNothingDeletedAsync(entry.Id);
    }

    private async Task AssertNothingDeletedAsync(Guid entryId)
    {
        _dbContext.ChangeTracker.Clear();
        Assert.True(await _dbContext.MaintenanceEntries.IgnoreQueryFilters().AnyAsync(e => e.Id == entryId));
        Assert.False(await _dbContext.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "maintenance_deleted"));
        Assert.Empty(_storage.DeletedUrls);
    }

    private async Task<MaintenanceEntry> SeedEntryAsync(Guid tenantId)
    {
        var entry = new MaintenanceEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MachineId = Guid.NewGuid(),
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Description = string.Empty,
            ImageUrl = $"/uploads/{tenantId}/{Guid.NewGuid():N}.jpg",
            EmployeeName = "Technician",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MaintenanceEntries.Add(entry);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return entry;
    }

    private async Task<Guid> SeedUserAsync()
    {
        _dbContext.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Username = "decider",
            Email = "decider@example.com",
            PasswordHash = "hash",
            DisplayName = "decider",
            RoleId = (int)UserRole.Worker,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return user.Id;
    }

    private async Task SeedClearanceItemAsync(Guid tenantId, Guid maintenanceEntryId)
    {
        _dbContext.ClearanceItems.Add(new ClearanceItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = maintenanceEntryId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = Guid.NewGuid(),
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }
}
