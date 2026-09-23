using MaintTrack.Application.Hierarchy;
using MaintTrack.Application.MachineParameterPhotos;
using MaintTrack.Domain.Arrays;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.MachineParameterPhotos;

public sealed class MachineParameterPhotoServiceTests : IAsyncDisposable
{
    private readonly MachineParameterPhotoServiceFixture _fx = new();

    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task GetHierarchy_HidesHiddenArraysAndUnassigned()
    {
        var visible = await _fx.SeedArrayAsync(_fx.TenantId, "array.visible");
        var hidden = await _fx.SeedArrayAsync(_fx.TenantId, "array.hidden");
        await _fx.SeedMachineAsync(_fx.TenantId, "machine.visible", "V1", visible.Id);
        await _fx.SeedMachineAsync(_fx.TenantId, "machine.hidden", "H1", hidden.Id);
        await _fx.SeedMachineAsync(_fx.TenantId, "machine.unassigned", "U1", null);
        await _fx.HideArrayAsync(_fx.TenantId, hidden.Id);

        var result = await _fx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.False(result.CanManage);
        Assert.Single(result.Arrays);
        Assert.Equal(visible.Id, result.Arrays[0].ArrayId);
        Assert.DoesNotContain(result.Arrays, group => group.ArrayId == hidden.Id);
        Assert.DoesNotContain(result.Arrays, group => group.ArrayId is null);
        Assert.DoesNotContain(result.Arrays, group => group.NameKey == HierarchyDefaults.UnassignedNameKey);
        Assert.DoesNotContain(result.Arrays.SelectMany(group => group.Machines), machine => machine.Name == "machine.hidden");
    }

    [Fact]
    public async Task GetHierarchy_OtherFeatureHideDoesNotHideArray()
    {
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.other-feature");
        await _fx.SeedMachineAsync(_fx.TenantId, "machine.keep", "K1", array.Id);
        await _fx.HideArrayAsync(_fx.TenantId, array.Id, featureKey: "some_other_feature");

        var result = await _fx.Service.GetHierarchyAsync(CancellationToken.None);

        Assert.Contains(result.Arrays, group => group.ArrayId == array.Id);
    }

    [Fact]
    public async Task GetHierarchy_CanManage_UsesCurrentTenantAndUserAssignment()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.OtherTenantId, _fx.UserId);

        var withoutGrant = await _fx.Service.GetHierarchyAsync(CancellationToken.None);
        Assert.False(withoutGrant.CanManage);

        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var withGrant = await _fx.Service.GetHierarchyAsync(CancellationToken.None);
        Assert.True(withGrant.CanManage);
    }

    [Fact]
    public async Task GetByMachineId_ReturnsOnlySelectedMachinePhotosInOrder()
    {
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machineA = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);
        var machineB = await _fx.SeedMachineAsync(_fx.TenantId, "machine.b", "B1", array.Id);
        var later = await _fx.SeedPhotoAsync(_fx.TenantId, machineA.Id, 2);
        var earlier = await _fx.SeedPhotoAsync(_fx.TenantId, machineA.Id, 1);
        await _fx.SeedPhotoAsync(_fx.TenantId, machineB.Id, 1);

        var photos = await _fx.Service.GetByMachineIdAsync(machineA.Id, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.Equal(new[] { earlier.Id, later.Id }, photos.Select(photo => photo.Id).ToArray());
        Assert.All(photos, photo => Assert.Equal(machineA.Id, photo.MachineId));
    }

    [Fact]
    public async Task GetByMachineId_DoesNotExposeOtherTenantPhotos()
    {
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.mine");
        var otherArray = await _fx.SeedArrayAsync(_fx.OtherTenantId, "array.other");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.mine", "M1", array.Id);
        var otherMachine = await _fx.SeedMachineAsync(_fx.OtherTenantId, "machine.other", "O1", otherArray.Id);
        await _fx.SeedPhotoAsync(_fx.TenantId, machine.Id, 1);
        await _fx.SeedPhotoAsync(_fx.OtherTenantId, otherMachine.Id, 1);

        var photos = await _fx.Service.GetByMachineIdAsync(machine.Id, CancellationToken.None);
        Assert.Single(photos);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fx.Service.GetByMachineIdAsync(otherMachine.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task GetByMachineId_RejectsInactiveOrUnassignedOrHiddenMachine(
        bool machineActive,
        bool hasArray,
        bool arrayActive)
    {
        Guid? arrayId = null;
        if (hasArray)
        {
            var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.gate", isActive: arrayActive);
            arrayId = array.Id;
        }

        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.gate", "G1", arrayId, machineActive);
        if (hasArray && arrayActive && machineActive)
        {
            await _fx.HideArrayAsync(_fx.TenantId, arrayId!.Value);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fx.Service.GetByMachineIdAsync(machine.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Upload_RequiresManagerAssignment()
    {
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _fx.Service.UploadAsync(
                machine.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload() },
                CancellationToken.None));
    }

    [Fact]
    public async Task Upload_OtherTenantManagerRowDoesNotGrantAccess()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "local-user");
        await _fx.GrantManagerAsync(_fx.OtherTenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _fx.Service.UploadAsync(
                machine.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload() },
                CancellationToken.None));
    }

    [Theory]
    [InlineData("image/jpeg", "photo.jpg")]
    [InlineData("image/png", "photo.png")]
    [InlineData("image/webp", "photo.webp")]
    public async Task Upload_AcceptsAllowedImageTypes(string contentType, string fileName)
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);

        var photos = await _fx.Service.UploadAsync(
            machine.Id,
            new[] { MachineParameterPhotoServiceFixture.CreateUpload(contentType, fileName) },
            CancellationToken.None);

        Assert.Single(photos);
        Assert.Equal(1, photos[0].SortOrder);
        Assert.Equal(machine.Id, photos[0].MachineId);
        Assert.Equal(_fx.UserId, photos[0].CreatedByUserId);
        Assert.Single(_fx.Storage.UploadedUrls);
    }

    [Fact]
    public async Task Upload_AssignsDeterministicSequentialSortOrder()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);
        await _fx.SeedPhotoAsync(_fx.TenantId, machine.Id, 3);

        var photos = await _fx.Service.UploadAsync(
            machine.Id,
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
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(machine.Id, Array.Empty<MachineParameterPhotoUploadFile>(), CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                machine.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload(bytes: 0) },
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                machine.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload("image/gif", "x.gif") },
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                machine.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload(bytes: (int)MachineParameterPhotoService.MaximumFileSizeBytes + 1) },
                CancellationToken.None));

        var tooMany = Enumerable.Range(0, 11)
            .Select(_ => MachineParameterPhotoServiceFixture.CreateUpload())
            .ToList();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(machine.Id, tooMany, CancellationToken.None));
    }

    [Fact]
    public async Task Upload_RejectsHiddenInactiveAndCrossTenantMachines()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);

        var hiddenArray = await _fx.SeedArrayAsync(_fx.TenantId, "array.hidden");
        var hiddenMachine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.hidden", "H1", hiddenArray.Id);
        await _fx.HideArrayAsync(_fx.TenantId, hiddenArray.Id);

        var inactiveArray = await _fx.SeedArrayAsync(_fx.TenantId, "array.inactive", isActive: false);
        var inactiveArrayMachine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.inactive-array", "IA1", inactiveArray.Id);

        var visibleArray = await _fx.SeedArrayAsync(_fx.TenantId, "array.visible");
        var inactiveMachine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.inactive", "I1", visibleArray.Id, isActive: false);
        var unassigned = await _fx.SeedMachineAsync(_fx.TenantId, "machine.none", "N1", null);

        var otherArray = await _fx.SeedArrayAsync(_fx.OtherTenantId, "array.other");
        var otherMachine = await _fx.SeedMachineAsync(_fx.OtherTenantId, "machine.other", "O1", otherArray.Id);

        var file = new[] { MachineParameterPhotoServiceFixture.CreateUpload() };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(hiddenMachine.Id, file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(inactiveArrayMachine.Id, file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(inactiveMachine.Id, file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(unassigned.Id, file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(otherMachine.Id, file, CancellationToken.None));
        Assert.Empty(_fx.Storage.UploadedUrls);
    }

    [Fact]
    public async Task Upload_PartialStorageFailureCleansUpUploadedFiles()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);
        _fx.Storage.FailOnUploadNumber = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                machine.Id,
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
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);
        _fx.SaveInterceptor.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fx.Service.UploadAsync(
                machine.Id,
                new[] { MachineParameterPhotoServiceFixture.CreateUpload() },
                CancellationToken.None));

        Assert.Single(_fx.Storage.UploadedUrls);
        Assert.Equal(_fx.Storage.UploadedUrls, _fx.Storage.DeletedUrls);
        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
    }

    [Fact]
    public async Task Delete_RequiresManagerAndCurrentTenant()
    {
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, machine.Id, 1);

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
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, machine.Id, 1, "/uploads/to-delete.jpg");

        await _fx.Service.DeleteAsync(photo.Id, CancellationToken.None);

        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
        Assert.Contains("/uploads/to-delete.jpg", _fx.Storage.DeletedUrls);
    }

    [Fact]
    public async Task Delete_MissingPhotoReturnsNotFound()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _fx.Service.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_OtherTenantPhotoIsNotFound()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var otherArray = await _fx.SeedArrayAsync(_fx.OtherTenantId, "array.other");
        var otherMachine = await _fx.SeedMachineAsync(_fx.OtherTenantId, "machine.other", "O1", otherArray.Id);
        var otherPhoto = await _fx.SeedPhotoAsync(_fx.OtherTenantId, otherMachine.Id, 1);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _fx.Service.DeleteAsync(otherPhoto.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_StorageFailureAfterDbDeleteStillSucceeds()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.line");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.a", "A1", array.Id);
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, machine.Id, 1);
        _fx.Storage.FailOnDelete = true;

        await _fx.Service.DeleteAsync(photo.Id, CancellationToken.None);

        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
    }

    [Fact]
    public async Task Delete_HiddenArrayPhotoIsNotExposed()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.hidden");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.hidden", "H1", array.Id);
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, machine.Id, 1);
        await _fx.HideArrayAsync(_fx.TenantId, array.Id);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _fx.Service.DeleteAsync(photo.Id, CancellationToken.None));
        Assert.Single(await _fx.DbContext.MachineParameterPhotos.ToListAsync());
    }
}
