using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Maintenance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Maintenance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaintTrack.Tests.Maintenance;

public sealed class MaintenanceEntryQueryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenantContext;
    private readonly StubCurrentUserContext _currentUser;
    private readonly MaintTrackDbContext _dbContext;
    private readonly MaintenanceEntryService _service;
    private readonly Guid _tenantId;
    private readonly Guid _machineId;
    private readonly Guid _typeId;

    public MaintenanceEntryQueryTests()
    {
        _tenantId = Guid.NewGuid();
        _machineId = Guid.NewGuid();
        _typeId = Guid.NewGuid();
        _tenantContext = new TenantContext { TenantId = _tenantId };
        _currentUser = new StubCurrentUserContext(_tenantId, Guid.NewGuid());

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new MaintTrackDbContext(options, _tenantContext);
        _dbContext.Database.EnsureCreated();

        _dbContext.Machines.Add(new Machine
        {
            Id = _machineId,
            TenantId = _tenantId,
            Name = "machine.test",
            Code = "M1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.MaintenanceTypes.Add(new MaintenanceType
        {
            Id = _typeId,
            TenantId = _tenantId,
            Code = "other",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        _service = new MaintenanceEntryService(
            _dbContext,
            _tenantContext,
            _currentUser,
            new NoOpFileStorageService(),
            NullLogger<MaintenanceEntryService>.Instance);
    }

    [Fact]
    public async Task GetAsync_NoDateFilter_ReturnsNewestOccurrenceFirst()
    {
        var older = await SeedEntryAsync(new DateOnly(2026, 9, 1), new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        var newer = await SeedEntryAsync(new DateOnly(2026, 9, 10), new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));
        var middle = await SeedEntryAsync(new DateOnly(2026, 9, 5), new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc));

        var list = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            PageNumber = 1,
            PageSize = 20
        }, CancellationToken.None);

        Assert.Equal(new[] { newer, middle, older }, list.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task GetAsync_SameDate_OrdersByCreatedAtThenIdDescending()
    {
        var day = new DateOnly(2026, 9, 10);
        var firstCreated = await SeedEntryAsync(day, new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));
        var secondCreated = await SeedEntryAsync(day, new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc));

        var sameInstantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sameInstantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var stamp = new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc);
        await SeedEntryWithIdAsync(sameInstantA, day, stamp);
        await SeedEntryWithIdAsync(sameInstantB, day, stamp);

        var list = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            FromDate = day,
            ToDate = day,
            PageNumber = 1,
            PageSize = 20
        }, CancellationToken.None);

        Assert.Equal(
            new[] { sameInstantB, sameInstantA, secondCreated, firstCreated },
            list.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task GetAsync_SingleDayFilter_IncludesOnlySelectedCalendarDay()
    {
        var selected = new DateOnly(2026, 9, 10);
        var onDay = await SeedEntryAsync(selected, new DateTime(2026, 9, 10, 23, 59, 0, DateTimeKind.Utc));
        await SeedEntryAsync(new DateOnly(2026, 9, 9), new DateTime(2026, 9, 9, 23, 59, 0, DateTimeKind.Utc));
        await SeedEntryAsync(new DateOnly(2026, 9, 11), new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc));

        var list = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            FromDate = selected,
            ToDate = selected,
            PageNumber = 1,
            PageSize = 20
        }, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal(onDay, list[0].Id);
        Assert.Equal(selected, list[0].Date);
    }

    [Fact]
    public async Task GetAsync_DateRangeFilter_IsInclusiveOfStartAndEndDays()
    {
        var start = new DateOnly(2026, 9, 1);
        var end = new DateOnly(2026, 9, 10);
        var startDay = await SeedEntryAsync(start, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var endDay = await SeedEntryAsync(end, new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc));
        var mid = await SeedEntryAsync(new DateOnly(2026, 9, 5), new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));
        await SeedEntryAsync(new DateOnly(2026, 8, 31), new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc));
        await SeedEntryAsync(new DateOnly(2026, 9, 11), new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc));

        var list = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            FromDate = start,
            ToDate = end,
            PageNumber = 1,
            PageSize = 20
        }, CancellationToken.None);

        Assert.Equal(new[] { endDay, mid, startDay }, list.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task GetAsync_DateRange_StillSortsNewestFirst()
    {
        var listIds = new List<Guid>();
        for (var day = 1; day <= 5; day++)
        {
            listIds.Add(await SeedEntryAsync(
                new DateOnly(2026, 9, day),
                new DateTime(2026, 9, day, 10, 0, 0, DateTimeKind.Utc)));
        }

        var list = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 5),
            PageNumber = 1,
            PageSize = 20
        }, CancellationToken.None);

        Assert.Equal(listIds.AsEnumerable().Reverse().ToArray(), list.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task GetAsync_ClearingFilter_ReturnsAllNewestFirst()
    {
        var a = await SeedEntryAsync(new DateOnly(2026, 9, 1), DateTime.UtcNow.AddDays(-2));
        var b = await SeedEntryAsync(new DateOnly(2026, 9, 10), DateTime.UtcNow);

        var filtered = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            FromDate = new DateOnly(2026, 9, 10),
            ToDate = new DateOnly(2026, 9, 10),
            PageNumber = 1,
            PageSize = 20
        }, CancellationToken.None);
        Assert.Single(filtered);
        Assert.Equal(b, filtered[0].Id);

        var all = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            PageNumber = 1,
            PageSize = 20
        }, CancellationToken.None);
        Assert.Equal(new[] { b, a }, all.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task GetAsync_FiltersBeforePagination()
    {
        var selected = new DateOnly(2026, 9, 10);
        var matchingNewest = await SeedEntryAsync(selected, new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc));
        var matchingOlder = await SeedEntryAsync(selected, new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));
        for (var i = 0; i < 5; i++)
        {
            await SeedEntryAsync(new DateOnly(2026, 9, 1), new DateTime(2026, 9, 1, 8, i, 0, DateTimeKind.Utc));
        }

        var page1 = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            FromDate = selected,
            ToDate = selected,
            PageNumber = 1,
            PageSize = 1
        }, CancellationToken.None);

        Assert.Single(page1);
        Assert.Equal(matchingNewest, page1[0].Id);

        var page2 = await _service.GetAsync(new GetMaintenanceEntriesRequest
        {
            FromDate = selected,
            ToDate = selected,
            PageNumber = 2,
            PageSize = 1
        }, CancellationToken.None);

        Assert.Single(page2);
        Assert.Equal(matchingOlder, page2[0].Id);
    }

    private async Task<Guid> SeedEntryAsync(DateOnly date, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await SeedEntryWithIdAsync(id, date, createdAt);
        return id;
    }

    private async Task SeedEntryWithIdAsync(Guid id, DateOnly date, DateTime createdAt)
    {
        _dbContext.MaintenanceEntries.Add(new MaintenanceEntry
        {
            Id = id,
            TenantId = _tenantId,
            MachineId = _machineId,
            Date = date,
            MaintenanceTypeId = _typeId,
            Description = "component.motor\nfault",
            EmployeeName = "tester",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = createdAt
        });
        await _dbContext.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class StubCurrentUserContext : ICurrentUserContext
    {
        public StubCurrentUserContext(Guid tenantId, Guid userId)
        {
            TenantId = tenantId;
            UserId = userId;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public int RoleId => (int)UserRole.Worker;
        public UserRole Role => UserRole.Worker;
        public string DisplayName => "Test Technician";
    }

    private sealed class NoOpFileStorageService : IFileStorageService
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
