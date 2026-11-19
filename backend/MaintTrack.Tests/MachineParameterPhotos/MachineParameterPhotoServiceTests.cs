using MaintTrack.Application.Hierarchy;
using MaintTrack.Application.MachineParameterPhotos;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.MachineParameterPhotos;

public sealed class MachineParameterPhotoServiceTests : IAsyncDisposable
{
    private readonly MachineParameterPhotoServiceFixture _fx = new();

    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task GetCatalog_ReturnsIndependentGroupsInOrder_AndOmitsInactiveAndSharedMachines()
    {
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var washing = await _fx.DbContext.ParameterPhotoGroups
            .SingleAsync(group => group.TenantId == _fx.TenantId && group.NameKey == ParameterPhotoCatalog.WashingSystemGroupKey);
        await _fx.SeedParameterAsync(_fx.TenantId, washing.Id, "parameterPhoto.parameter.inactive", sortOrder: 9, isActive: false);
        var inactiveGroup = await _fx.SeedGroupAsync(_fx.TenantId, "array.onion_system", sortOrder: 8, isActive: false);
        await _fx.SeedParameterAsync(_fx.TenantId, inactiveGroup.Id, "machine.onion.hopper", sortOrder: 1);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.washing_system");
        await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        await _fx.SeedMachineAsync(_fx.TenantId, "machine.elevatorToDestoner", "E1", array.Id);

        var result = await _fx.Service.GetCatalogAsync(CancellationToken.None);

        Assert.False(result.CanManage);
        Assert.Equal(
            new[]
            {
                ParameterPhotoCatalog.WashingSystemGroupKey,
                ParameterPhotoCatalog.CoolingSystemGroupKey,
                ParameterPhotoCatalog.PackingHouseGroupKey
            },
            result.Groups.Select(group => group.NameKey).ToArray());

        var washingGroup = result.Groups[0];
        Assert.Equal(
            new[] { ParameterPhotoCatalog.HopperKey, ParameterPhotoCatalog.CoolingSystemComputerKey },
            washingGroup.Parameters.Select(parameter => parameter.NameKey).ToArray());

        var coolingGroup = result.Groups[1];
        Assert.Equal(
            new[]
            {
                ParameterPhotoCatalog.CompressorRoomsKey,
                ParameterPhotoCatalog.ControlRoomAboveCoolingRoomsKey,
                ParameterPhotoCatalog.HumidityControllersKey,
                ParameterPhotoCatalog.GanShomronCoolingKey
            },
            coolingGroup.Parameters.Select(parameter => parameter.NameKey).ToArray());

        var packingGroup = result.Groups[2];
        Assert.Equal(ParameterPhotoCatalog.PackingHouseMachineKeys[0], packingGroup.Parameters[0].NameKey);
        Assert.Equal(
            ParameterPhotoCatalog.BagPullersUnderViscoseKey,
            packingGroup.Parameters[^1].NameKey);
        Assert.Equal(
            ParameterPhotoCatalog.PackingHouseMachineKeys.Length + 1,
            packingGroup.Parameters.Count);

        var returnedKeys = result.Groups
            .SelectMany(group => group.Parameters.Select(parameter => parameter.NameKey).Prepend(group.NameKey))
            .ToHashSet();
        Assert.DoesNotContain("array.onion_system", result.Groups.Select(group => group.NameKey));
        Assert.DoesNotContain("machine.largeHopper", returnedKeys);
        Assert.DoesNotContain("machine.smallHopper", returnedKeys);
        Assert.DoesNotContain("machine.elevatorToDestoner", returnedKeys);
        Assert.DoesNotContain("machine.onion.hopper", returnedKeys);
        Assert.DoesNotContain("machine.waterCooling.compressor1", returnedKeys);
        Assert.DoesNotContain("machine.packing.stackers", returnedKeys);
        Assert.DoesNotContain("machine.packing.visionSystem", returnedKeys);
        Assert.DoesNotContain("parameterPhoto.parameter.inactive", returnedKeys);
    }

    [Fact]
    public async Task GetCatalog_CanManage_UsesCurrentTenantAndUserAssignment()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.OtherTenantId, _fx.UserId);

        var withoutGrant = await _fx.Service.GetCatalogAsync(CancellationToken.None);
        Assert.False(withoutGrant.CanManage);

        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var withGrant = await _fx.Service.GetCatalogAsync(CancellationToken.None);
        Assert.True(withGrant.CanManage);
    }

    [Fact]
    public async Task GetByParameterId_ReturnsOnlySelectedParameterPhotosInOrder()
    {
        var parameterA = await SeedActiveParameterAsync("parameter.a");
        var parameterB = await SeedActiveParameterAsync("parameter.b");
        var later = await _fx.SeedPhotoAsync(_fx.TenantId, parameterA.Id, 2);
        var earlier = await _fx.SeedPhotoAsync(_fx.TenantId, parameterA.Id, 1);
        await _fx.SeedPhotoAsync(_fx.TenantId, parameterB.Id, 1);

        var photos = await _fx.Service.GetByParameterIdAsync(parameterA.Id, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.Equal(new[] { earlier.Id, later.Id }, photos.Select(photo => photo.Id).ToArray());
        Assert.All(photos, photo => Assert.Equal(parameterA.Id, photo.ParameterId));
    }

    [Fact]
    public async Task GetByParameterId_ReturnsLegacyPhotoWithoutChangingStoredFile()
    {
        var parameter = await SeedActiveParameterAsync("machine.sorter1");
        var machineArray = await _fx.SeedArrayAsync(_fx.TenantId, "array.packing_house");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.sorter1", "S1", machineArray.Id);
        var photo = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            parameter.Id,
            1,
            machine.Id,
            "/uploads/legacy-sorter.jpg");

        var photos = await _fx.Service.GetByParameterIdAsync(parameter.Id, CancellationToken.None);

        var returned = Assert.Single(photos);
        Assert.Equal(photo.Id, returned.Id);
        Assert.Equal("/uploads/legacy-sorter.jpg", returned.ImageUrl);
        var stored = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().SingleAsync();
        Assert.Equal(machine.Id, stored.MachineId);
        Assert.Equal("/uploads/legacy-sorter.jpg", stored.ImageUrl);
    }

    [Fact]
    public async Task GetByParameterId_DoesNotExposeOtherTenantPhotos()
    {
        var parameter = await SeedActiveParameterAsync("parameter.mine");
        var otherGroup = await _fx.SeedGroupAsync(_fx.OtherTenantId, "parameter.other-group");
        var otherParameter = await _fx.SeedParameterAsync(_fx.OtherTenantId, otherGroup.Id, "parameter.other");
        await _fx.SeedPhotoAsync(_fx.TenantId, parameter.Id, 1);
        await _fx.SeedPhotoAsync(_fx.OtherTenantId, otherParameter.Id, 1);

        var photos = await _fx.Service.GetByParameterIdAsync(parameter.Id, CancellationToken.None);
        Assert.Single(photos);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fx.Service.GetByParameterIdAsync(otherParameter.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task GetByParameterId_RejectsInactiveParameterOrGroup(bool parameterActive, bool groupActive)
    {
        var group = await _fx.SeedGroupAsync(_fx.TenantId, "parameter.group", isActive: groupActive);
        var parameter = await _fx.SeedParameterAsync(
            _fx.TenantId,
            group.Id,
            "parameter.item",
            isActive: parameterActive);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fx.Service.GetByParameterIdAsync(parameter.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Upload_RequiresManagerAssignment()
    {
        var parameter = await SeedActiveParameterAsync("parameter.a");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _fx.Service.UploadAsync(
                parameter.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload() },
                CancellationToken.None));
    }

    [Fact]
    public async Task Upload_OtherTenantManagerRowDoesNotGrantAccess()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "local-user");
        await _fx.GrantManagerAsync(_fx.OtherTenantId, _fx.UserId);
        var parameter = await SeedActiveParameterAsync("parameter.a");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _fx.Service.UploadAsync(
                parameter.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload() },
                CancellationToken.None));
    }

    [Theory]
    [InlineData("image/jpeg", "photo.jpg")]
    [InlineData("image/png", "photo.png")]
    [InlineData("image/webp", "photo.webp")]
    public async Task Upload_AcceptsAllowedImageTypesWithoutAMachine(string contentType, string fileName)
    {
        await GrantCurrentUserAsync();
        var parameter = await SeedActiveParameterAsync("parameter.a");

        var photos = await _fx.Service.UploadAsync(
            parameter.Id,
            new[] { MachineParameterPhotoServiceFixture.CreateUpload(contentType, fileName) },
            CancellationToken.None);

        Assert.Single(photos);
        Assert.Equal(1, photos[0].SortOrder);
        Assert.Equal(parameter.Id, photos[0].ParameterId);
        Assert.Equal(_fx.UserId, photos[0].CreatedByUserId);
        Assert.Single(_fx.Storage.UploadedUrls);
        var stored = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().SingleAsync();
        Assert.Null(stored.MachineId);
        Assert.Equal(parameter.Id, stored.ParameterId);
    }

    [Fact]
    public async Task Upload_AssignsDeterministicSequentialSortOrder()
    {
        await GrantCurrentUserAsync();
        var parameter = await SeedActiveParameterAsync("parameter.a");
        await _fx.SeedPhotoAsync(_fx.TenantId, parameter.Id, 3);

        var photos = await _fx.Service.UploadAsync(
            parameter.Id,
            new[]
            {
                MachineParameterPhotoServiceFixture.CreateUpload(fileName: "a.jpg"),
                MachineParameterPhotoServiceFixture.CreateUpload(fileName: "b.jpg"),
                MachineParameterPhotoServiceFixture.CreateUpload(fileName: "c.jpg")
            },
            CancellationToken.None);

        Assert.Equal(new[] { 4, 5, 6 }, photos.Select(photo => photo.SortOrder).ToArray());
    }

    [Fact]
    public async Task Upload_RejectsEmptyUnsupportedLargeAndTooManyFiles()
    {
        await GrantCurrentUserAsync();
        var parameter = await SeedActiveParameterAsync("parameter.a");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(parameter.Id, Array.Empty<MachineParameterPhotoUploadFile>(), CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                parameter.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload(bytes: 0) },
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                parameter.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload("image/gif", "x.gif") },
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                parameter.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload(bytes: (int)MachineParameterPhotoService.MaximumFileSizeBytes + 1) },
                CancellationToken.None));

        var tooMany = Enumerable.Range(0, 11)
            .Select(_ => MachineParameterPhotoServiceFixture.CreateUpload())
            .ToList();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(parameter.Id, tooMany, CancellationToken.None));
    }

    [Fact]
    public async Task Upload_RejectsInactiveUnknownAndCrossTenantParameters()
    {
        await GrantCurrentUserAsync();
        var inactiveGroup = await _fx.SeedGroupAsync(_fx.TenantId, "parameter.inactive-group", isActive: false);
        var inactiveGroupParameter = await _fx.SeedParameterAsync(
            _fx.TenantId,
            inactiveGroup.Id,
            "parameter.inactive-group-item");
        var visibleGroup = await _fx.SeedGroupAsync(_fx.TenantId, "parameter.visible");
        var inactiveParameter = await _fx.SeedParameterAsync(
            _fx.TenantId,
            visibleGroup.Id,
            "parameter.inactive",
            isActive: false);
        var otherGroup = await _fx.SeedGroupAsync(_fx.OtherTenantId, "parameter.other");
        var otherParameter = await _fx.SeedParameterAsync(_fx.OtherTenantId, otherGroup.Id, "parameter.other-item");

        var file = new[] { MachineParameterPhotoServiceFixture.CreateUpload() };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(inactiveGroupParameter.Id, file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(inactiveParameter.Id, file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(otherParameter.Id, file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(Guid.NewGuid(), file, CancellationToken.None));
        Assert.Empty(_fx.Storage.UploadedUrls);
    }

    [Fact]
    public async Task Upload_PartialStorageFailureCleansUpUploadedFiles()
    {
        await GrantCurrentUserAsync();
        var parameter = await SeedActiveParameterAsync("parameter.a");
        _fx.Storage.FailOnUploadNumber = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                parameter.Id,
                new[]
                {
                    MachineParameterPhotoServiceFixture.CreateUpload(fileName: "one.jpg"),
                    MachineParameterPhotoServiceFixture.CreateUpload(fileName: "two.jpg")
                },
                CancellationToken.None));

        Assert.Single(_fx.Storage.UploadedUrls);
        Assert.Equal(_fx.Storage.UploadedUrls, _fx.Storage.DeletedUrls);
        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
    }

    [Fact]
    public async Task Upload_DbFailureAfterUploadCleansUpFiles()
    {
        await GrantCurrentUserAsync();
        var parameter = await SeedActiveParameterAsync("parameter.a");
        _fx.SaveInterceptor.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                parameter.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload() },
                CancellationToken.None));

        Assert.Single(_fx.Storage.UploadedUrls);
        Assert.Equal(_fx.Storage.UploadedUrls, _fx.Storage.DeletedUrls);
        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
    }

    [Fact]
    public async Task Delete_RequiresManagerAndCurrentTenant()
    {
        var parameter = await SeedActiveParameterAsync("parameter.a");
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, parameter.Id, 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _fx.Service.DeleteAsync(photo.Id, CancellationToken.None));

        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.OtherTenantId, _fx.UserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _fx.Service.DeleteAsync(photo.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ManagerDeletesPhotoAndAttemptsStorageCleanup()
    {
        await GrantCurrentUserAsync();
        var parameter = await SeedActiveParameterAsync("parameter.a");
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, parameter.Id, 1, imageUrl: "/uploads/to-delete.jpg");

        await _fx.Service.DeleteAsync(photo.Id, CancellationToken.None);

        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
        Assert.Contains("/uploads/to-delete.jpg", _fx.Storage.DeletedUrls);
    }

    [Fact]
    public async Task Delete_MissingPhotoReturnsNotFound()
    {
        await GrantCurrentUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _fx.Service.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_OtherTenantPhotoIsNotFound()
    {
        await GrantCurrentUserAsync();
        var otherGroup = await _fx.SeedGroupAsync(_fx.OtherTenantId, "parameter.other");
        var otherParameter = await _fx.SeedParameterAsync(_fx.OtherTenantId, otherGroup.Id, "parameter.other-item");
        var otherPhoto = await _fx.SeedPhotoAsync(_fx.OtherTenantId, otherParameter.Id, 1);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _fx.Service.DeleteAsync(otherPhoto.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_StorageFailureAfterDbDeleteStillSucceeds()
    {
        await GrantCurrentUserAsync();
        var parameter = await SeedActiveParameterAsync("parameter.a");
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, parameter.Id, 1);
        _fx.Storage.FailOnDelete = true;

        await _fx.Service.DeleteAsync(photo.Id, CancellationToken.None);

        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
    }

    [Fact]
    public async Task Delete_InactiveParameterPhotoIsNotExposed_AndFileIsPreserved()
    {
        await GrantCurrentUserAsync();
        var group = await _fx.SeedGroupAsync(_fx.TenantId, "parameter.hidden");
        var parameter = await _fx.SeedParameterAsync(_fx.TenantId, group.Id, "parameter.hidden-item", isActive: false);
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, parameter.Id, 1, imageUrl: "/uploads/kept.jpg");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _fx.Service.DeleteAsync(photo.Id, CancellationToken.None));
        var remaining = Assert.Single(await _fx.DbContext.MachineParameterPhotos.AsNoTracking().ToListAsync());
        Assert.Equal(photo.Id, remaining.Id);
        Assert.Equal("/uploads/kept.jpg", remaining.ImageUrl);
        Assert.Empty(_fx.Storage.DeletedUrls);
    }

    [Fact]
    public async Task Delete_UnlinkedLegacyPhotoIsNotExposed()
    {
        await GrantCurrentUserAsync();
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.washing_system");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        var photo = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            parameterId: null,
            sortOrder: 1,
            machineId: machine.Id,
            imageUrl: "/uploads/unlinked.jpg");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _fx.Service.DeleteAsync(photo.Id, CancellationToken.None));
        var remaining = Assert.Single(await _fx.DbContext.MachineParameterPhotos.AsNoTracking().ToListAsync());
        Assert.Equal(machine.Id, remaining.MachineId);
        Assert.Null(remaining.ParameterId);
        Assert.Equal("/uploads/unlinked.jpg", remaining.ImageUrl);
    }

    [Fact]
    public async Task SharedHierarchy_StillIncludesMachinesOutsideTheParameterCatalog()
    {
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.washing_system");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        await _fx.ExcludeMachineAsync(_fx.TenantId, machine.Id);
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);

        var shared = await new HierarchyService(_fx.DbContext, _fx.TenantContext)
            .GetHierarchyAsync(CancellationToken.None);
        var catalog = await _fx.Service.GetCatalogAsync(CancellationToken.None);

        Assert.Contains(shared.SelectMany(group => group.Machines), item => item.Id == machine.Id);
        Assert.Contains(shared, group => group.NameKey == "array.washing_system");
        Assert.DoesNotContain(
            catalog.Groups.SelectMany(group => group.Parameters),
            parameter => parameter.NameKey == "machine.largeHopper");
        Assert.True(await _fx.DbContext.Machines.AnyAsync(item => item.Id == machine.Id && item.IsActive));
        Assert.DoesNotContain(catalog.Groups, group => group.NameKey == HierarchyDefaults.UnassignedNameKey);
    }

    private async Task GrantCurrentUserAsync()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
    }

    private async Task<ParameterPhotoParameter> SeedActiveParameterAsync(string nameKey)
    {
        var group = await _fx.SeedGroupAsync(_fx.TenantId, $"group-{nameKey}");
        return await _fx.SeedParameterAsync(_fx.TenantId, group.Id, nameKey);
    }
}
