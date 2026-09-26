using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MachineParameterPhotos;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaintTrack.Tests.MachineParameterPhotos;

internal sealed class RecordingFileStorageService : IFileStorageService
{
    private int _uploadCount;

    public List<string> UploadedUrls { get; } = new();
    public List<string> DeletedUrls { get; } = new();
    public int FailOnUploadNumber { get; set; } = int.MaxValue;
    public bool FailOnDelete { get; set; }

    public Task<string> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string tenantId,
        CancellationToken ct)
    {
        _uploadCount++;
        if (_uploadCount >= FailOnUploadNumber)
        {
            throw new InvalidOperationException("storage fail");
        }

        var url = $"/uploads/{tenantId}/{Guid.NewGuid():N}-{fileName}";
        UploadedUrls.Add(url);
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string fileUrl, CancellationToken ct)
    {
        if (FailOnDelete)
        {
            throw new InvalidOperationException("delete fail");
        }

        DeletedUrls.Add(fileUrl);
        return Task.CompletedTask;
    }
}

internal sealed class StubCurrentUserContext : ICurrentUserContext
{
    public StubCurrentUserContext(Guid tenantId, Guid userId)
    {
        TenantId = tenantId;
        UserId = userId;
    }

    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public int RoleId { get; set; } = 1;
    public UserRole Role => UserRole.Worker;
    public string DisplayName => "Test User";
}

internal sealed class FailNextPhotoSaveInterceptor : SaveChangesInterceptor
{
    public bool FailNext { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (FailNext &&
            eventData.Context is not null &&
            eventData.Context.ChangeTracker.Entries<MachineParameterPhoto>()
                .Any(entry => entry.State == EntityState.Added))
        {
            FailNext = false;
            throw new InvalidOperationException("simulated db failure");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal sealed class MachineParameterPhotoServiceFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public MachineParameterPhotoServiceFixture()
    {
        TenantId = Guid.NewGuid();
        OtherTenantId = Guid.NewGuid();
        UserId = Guid.NewGuid();
        TenantContext = new TenantContext { TenantId = TenantId };
        CurrentUser = new StubCurrentUserContext(TenantId, UserId);
        Storage = new RecordingFileStorageService();
        SaveInterceptor = new FailNextPhotoSaveInterceptor();

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(SaveInterceptor)
            .Options;

        DbContext = new MaintTrackDbContext(options, TenantContext);
        DbContext.Database.EnsureCreated();

        Service = new MachineParameterPhotoService(
            DbContext,
            TenantContext,
            CurrentUser,
            new HierarchyService(DbContext, TenantContext),
            Storage,
            NullLogger<MachineParameterPhotoService>.Instance);
    }

    public Guid TenantId { get; }
    public Guid OtherTenantId { get; }
    public Guid UserId { get; }
    public TenantContext TenantContext { get; }
    public StubCurrentUserContext CurrentUser { get; }
    public RecordingFileStorageService Storage { get; }
    public FailNextPhotoSaveInterceptor SaveInterceptor { get; }
    public MaintTrackDbContext DbContext { get; }
    public MachineParameterPhotoService Service { get; }

    public async Task SeedRoleAsync()
    {
        if (!await DbContext.Roles.AnyAsync())
        {
            DbContext.Roles.Add(new Role { Id = 1, Name = "Worker" });
            await DbContext.SaveChangesAsync();
        }
    }

    public async Task<User> SeedUserAsync(Guid tenantId, Guid? userId = null, string? username = null)
    {
        await SeedRoleAsync();
        var user = new User
        {
            Id = userId ?? Guid.NewGuid(),
            TenantId = tenantId,
            Username = username ?? $"user-{Guid.NewGuid():N}"[..12],
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            DisplayName = "User",
            RoleId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync();
        return user;
    }

    public async Task GrantManagerAsync(Guid tenantId, Guid userId)
    {
        DbContext.MachineParameterPhotoManagers.Add(new MachineParameterPhotoManager
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();
    }

    public async Task<WorkGroup> SeedArrayAsync(
        Guid tenantId,
        string nameKey,
        bool isActive = true)
    {
        var array = new WorkGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NameKey = nameKey,
            SortOrder = 1,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        DbContext.Arrays.Add(array);
        await DbContext.SaveChangesAsync();
        return array;
    }

    public async Task<Machine> SeedMachineAsync(
        Guid tenantId,
        string name,
        string code,
        Guid? arrayId,
        bool isActive = true)
    {
        var machine = new Machine
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Code = code,
            ArrayId = arrayId,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        DbContext.Machines.Add(machine);
        await DbContext.SaveChangesAsync();
        return machine;
    }

    public async Task HideArrayAsync(Guid tenantId, Guid arrayId, string? featureKey = null, bool isVisible = false)
    {
        DbContext.ArrayFeatureVisibilities.Add(new ArrayFeatureVisibility
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ArrayId = arrayId,
            FeatureKey = featureKey ?? ArrayFeatureVisibility.MachineParameterPhotosFeatureKey,
            IsVisible = isVisible,
            CreatedAt = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();
    }

    public async Task<MachineParameterPhoto> SeedPhotoAsync(
        Guid tenantId,
        Guid machineId,
        int sortOrder,
        string? imageUrl = null)
    {
        var photo = new MachineParameterPhoto
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MachineId = machineId,
            ImageUrl = imageUrl ?? $"/uploads/{tenantId}/{Guid.NewGuid():N}.jpg",
            SortOrder = sortOrder,
            CreatedByUserId = UserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(sortOrder)
        };
        DbContext.MachineParameterPhotos.Add(photo);
        await DbContext.SaveChangesAsync();
        return photo;
    }

    public static MachineParameterPhotoUploadFile CreateUpload(
        string contentType = "image/jpeg",
        string fileName = "photo.jpg",
        int bytes = 16) =>
        new(new MemoryStream(new byte[bytes]), fileName, contentType, bytes);

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
