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

public sealed class MaintenanceEntryImageTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenantContext;
    private readonly StubCurrentUserContext _currentUser;
    private readonly MaintTrackDbContext _dbContext;
    private readonly MaintenanceEntryService _service;
    private readonly Guid _tenantId;
    private readonly Guid _machineId;
    private readonly Guid _typeId;

    public MaintenanceEntryImageTests()
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
            Code = "lubrication",
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
    public async Task CreateAsync_PersistsPrimaryAndAdditionalImages()
    {
        var id = await _service.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = _machineId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            MaintenanceTypeId = _typeId,
            ImageUrl = "/uploads/primary.jpg",
            AdditionalImageUrls = new[]
            {
                "/uploads/extra-1.jpg",
                "/uploads/extra-2.jpg"
            },
            WorkHours = 1,
            IsSafeToOperate = true
        }, CancellationToken.None);

        var dto = await _service.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("/uploads/primary.jpg", dto!.ImageUrl);
        Assert.Equal(2, dto.AdditionalImages.Count);
        Assert.Equal(1, dto.AdditionalImages[0].SortOrder);
        Assert.Equal("/uploads/extra-1.jpg", dto.AdditionalImages[0].ImageUrl);
        Assert.Equal(2, dto.AdditionalImages[1].SortOrder);
        Assert.Equal("/uploads/extra-2.jpg", dto.AdditionalImages[1].ImageUrl);
    }

    [Fact]
    public async Task CreateAsync_RejectsMoreThanTwoAdditionalImages()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(
            new CreateMaintenanceEntryRequest
            {
                MachineId = _machineId,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                MaintenanceTypeId = _typeId,
                ImageUrl = "/uploads/primary.jpg",
                AdditionalImageUrls = new[]
                {
                    "/uploads/a.jpg",
                    "/uploads/b.jpg",
                    "/uploads/c.jpg"
                },
                WorkHours = 1,
                IsSafeToOperate = true
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RejectsAdditionalImagesWithoutPrimary()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(
            new CreateMaintenanceEntryRequest
            {
                MachineId = _machineId,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                MaintenanceTypeId = _typeId,
                AdditionalImageUrls = new[] { "/uploads/extra.jpg" },
                WorkHours = 1,
                IsSafeToOperate = true
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsPrimaryImageChange()
    {
        var id = await _service.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = _machineId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            MaintenanceTypeId = _typeId,
            ImageUrl = "/uploads/primary.jpg",
            WorkHours = 1,
            IsSafeToOperate = true
        }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(
            id,
            new UpdateMaintenanceEntryRequest
            {
                MaintenanceTypeId = _typeId,
                ImageUrl = "/uploads/changed.jpg",
                EmployeeName = _currentUser.DisplayName,
                WorkHours = 1,
                IsSafeToOperate = true
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdAsync_LegacyEntryWithoutAdditionalImages_ReturnsEmptyList()
    {
        var id = await _service.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = _machineId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            MaintenanceTypeId = _typeId,
            ImageUrl = "/uploads/primary-only.jpg",
            WorkHours = 2,
            IsSafeToOperate = true
        }, CancellationToken.None);

        var dto = await _service.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("/uploads/primary-only.jpg", dto!.ImageUrl);
        Assert.Empty(dto.AdditionalImages);
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
