using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.MachineParameterPhotos;

public sealed class MachineParameterPhotoSchemaTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenantContext;
    private readonly MaintTrackDbContext _dbContext;
    private readonly Guid _tenantId;
    private readonly Guid _otherTenantId;

    public MachineParameterPhotoSchemaTests()
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
    public async Task MachineParameterPhoto_IsTenantIsolated()
    {
        var tenantMachine = CreateMachine(_tenantId, "machine.mine", "M1");
        var otherMachine = CreateMachine(_otherTenantId, "machine.other", "O1");
        _dbContext.Machines.AddRange(tenantMachine, otherMachine);
        await _dbContext.SaveChangesAsync();

        _dbContext.MachineParameterPhotos.AddRange(
            CreatePhoto(_tenantId, tenantMachine.Id, 1),
            CreatePhoto(_otherTenantId, otherMachine.Id, 1));
        await _dbContext.SaveChangesAsync();

        var visible = await _dbContext.MachineParameterPhotos.AsNoTracking().ToListAsync();

        Assert.Single(visible);
        Assert.Equal(_tenantId, visible[0].TenantId);
        Assert.Equal(tenantMachine.Id, visible[0].MachineId);
    }

    [Fact]
    public async Task MachineParameterPhotoManager_IsTenantIsolated()
    {
        var tenantUser = await SeedUserAsync(_tenantId, "tenant-user");
        var otherUser = await SeedUserAsync(_otherTenantId, "other-user");

        _dbContext.MachineParameterPhotoManagers.AddRange(
            CreateManager(_tenantId, tenantUser.Id),
            CreateManager(_otherTenantId, otherUser.Id));
        await _dbContext.SaveChangesAsync();

        var visible = await _dbContext.MachineParameterPhotoManagers.AsNoTracking().ToListAsync();

        Assert.Single(visible);
        Assert.Equal(_tenantId, visible[0].TenantId);
        Assert.Equal(tenantUser.Id, visible[0].UserId);
    }

    [Fact]
    public async Task ArrayFeatureVisibility_IsTenantIsolated()
    {
        var tenantArray = CreateArray(_tenantId, "array.mine");
        var otherArray = CreateArray(_otherTenantId, "array.other");
        _dbContext.Arrays.AddRange(tenantArray, otherArray);
        await _dbContext.SaveChangesAsync();

        _dbContext.ArrayFeatureVisibilities.AddRange(
            CreateVisibility(_tenantId, tenantArray.Id, isVisible: false),
            CreateVisibility(_otherTenantId, otherArray.Id, isVisible: false));
        await _dbContext.SaveChangesAsync();

        var visible = await _dbContext.ArrayFeatureVisibilities.AsNoTracking().ToListAsync();

        Assert.Single(visible);
        Assert.Equal(_tenantId, visible[0].TenantId);
        Assert.Equal(tenantArray.Id, visible[0].ArrayId);
    }

    [Fact]
    public async Task MachineParameterPhotoManager_IsUniquePerTenantAndUser()
    {
        var user = await SeedUserAsync(_tenantId, "manager-user");
        _dbContext.MachineParameterPhotoManagers.Add(CreateManager(_tenantId, user.Id));
        await _dbContext.SaveChangesAsync();

        _dbContext.MachineParameterPhotoManagers.Add(CreateManager(_tenantId, user.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MachineParameterPhoto_RequiresExistingMachine()
    {
        _dbContext.MachineParameterPhotos.Add(CreatePhoto(_tenantId, Guid.NewGuid(), 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MachineParameterPhoto_RestrictsMachineDelete()
    {
        var machine = CreateMachine(_tenantId, "machine.protected", "P1");
        _dbContext.Machines.Add(machine);
        await _dbContext.SaveChangesAsync();

        _dbContext.MachineParameterPhotos.Add(CreatePhoto(_tenantId, machine.Id, 1));
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var deleteBehavior = _dbContext.Model
            .FindEntityType(typeof(MachineParameterPhoto))!
            .GetForeignKeys()
            .Single(fk => fk.Properties.Any(property => property.Name == nameof(MachineParameterPhoto.MachineId)))
            .DeleteBehavior;
        Assert.Equal(DeleteBehavior.Restrict, deleteBehavior);

        var trackedMachine = await _dbContext.Machines.SingleAsync(item => item.Id == machine.Id);
        _dbContext.Machines.Remove(trackedMachine);

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MachineParameterPhoto_AllowsUniqueSortOrderPerMachine()
    {
        var machine = CreateMachine(_tenantId, "machine.sort", "S1");
        _dbContext.Machines.Add(machine);
        await _dbContext.SaveChangesAsync();

        _dbContext.MachineParameterPhotos.Add(CreatePhoto(_tenantId, machine.Id, 1));
        await _dbContext.SaveChangesAsync();

        _dbContext.MachineParameterPhotos.Add(CreatePhoto(_tenantId, machine.Id, 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ArrayFeatureVisibility_NoRowMeansVisible()
    {
        var array = CreateArray(_tenantId, "array.visible-by-default");
        _dbContext.Arrays.Add(array);
        await _dbContext.SaveChangesAsync();

        var rows = await _dbContext.ArrayFeatureVisibilities.AsNoTracking().ToListAsync();

        Assert.True(IsArrayVisibleForFeature(rows, array.Id, ArrayFeatureVisibility.MachineParameterPhotosFeatureKey));
    }

    [Fact]
    public async Task ArrayFeatureVisibility_HiddenRowHidesArrayForFeature()
    {
        var array = CreateArray(_tenantId, "array.hidden");
        _dbContext.Arrays.Add(array);
        await _dbContext.SaveChangesAsync();

        _dbContext.ArrayFeatureVisibilities.Add(CreateVisibility(_tenantId, array.Id, isVisible: false));
        await _dbContext.SaveChangesAsync();

        var rows = await _dbContext.ArrayFeatureVisibilities.AsNoTracking().ToListAsync();

        Assert.False(IsArrayVisibleForFeature(rows, array.Id, ArrayFeatureVisibility.MachineParameterPhotosFeatureKey));
    }

    [Fact]
    public async Task ArrayFeatureVisibility_VisibleRowKeepsArrayVisible()
    {
        var array = CreateArray(_tenantId, "array.explicit-visible");
        _dbContext.Arrays.Add(array);
        await _dbContext.SaveChangesAsync();

        _dbContext.ArrayFeatureVisibilities.Add(CreateVisibility(_tenantId, array.Id, isVisible: true));
        await _dbContext.SaveChangesAsync();

        var rows = await _dbContext.ArrayFeatureVisibilities.AsNoTracking().ToListAsync();

        Assert.True(IsArrayVisibleForFeature(rows, array.Id, ArrayFeatureVisibility.MachineParameterPhotosFeatureKey));
    }

    [Fact]
    public async Task ArrayFeatureVisibility_OtherFeatureHideDoesNotAffectThisFeature()
    {
        var array = CreateArray(_tenantId, "array.other-feature");
        _dbContext.Arrays.Add(array);
        await _dbContext.SaveChangesAsync();

        _dbContext.ArrayFeatureVisibilities.Add(new ArrayFeatureVisibility
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ArrayId = array.Id,
            FeatureKey = "some_other_feature",
            IsVisible = false,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var rows = await _dbContext.ArrayFeatureVisibilities.AsNoTracking().ToListAsync();

        Assert.True(IsArrayVisibleForFeature(rows, array.Id, ArrayFeatureVisibility.MachineParameterPhotosFeatureKey));
    }

    private static bool IsArrayVisibleForFeature(
        IReadOnlyList<ArrayFeatureVisibility> rows,
        Guid arrayId,
        string featureKey)
    {
        var row = rows.SingleOrDefault(item => item.ArrayId == arrayId && item.FeatureKey == featureKey);
        return row is null || row.IsVisible;
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

    private static WorkGroup CreateArray(Guid tenantId, string nameKey) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NameKey = nameKey,
            SortOrder = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static Machine CreateMachine(Guid tenantId, string name, string code) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Code = code,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static MachineParameterPhoto CreatePhoto(Guid tenantId, Guid machineId, int sortOrder) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MachineId = machineId,
            ImageUrl = $"/uploads/{tenantId}/{Guid.NewGuid():N}.jpg",
            SortOrder = sortOrder,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

    private static MachineParameterPhotoManager CreateManager(Guid tenantId, Guid userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

    private static ArrayFeatureVisibility CreateVisibility(Guid tenantId, Guid arrayId, bool isVisible) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ArrayId = arrayId,
            FeatureKey = ArrayFeatureVisibility.MachineParameterPhotosFeatureKey,
            IsVisible = isVisible,
            CreatedAt = DateTime.UtcNow
        };
}
