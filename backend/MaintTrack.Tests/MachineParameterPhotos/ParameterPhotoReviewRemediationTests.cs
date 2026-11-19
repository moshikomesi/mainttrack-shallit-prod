using System.Reflection;
using MaintTrack.Application.MachineParameterPhotos;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using MaintTrack.Infrastructure.Migrations;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;

namespace MaintTrack.Tests.MachineParameterPhotos;

public sealed class ParameterPhotoReviewRemediationTests : IAsyncDisposable
{
    private readonly MachineParameterPhotoServiceFixture _fx = new();

    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Recovery_ListsUnlinkedPhotosForTheCurrentManagerOnly()
    {
        await GrantAsync();
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.washing_system");
        var first = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        var second = await _fx.SeedMachineAsync(_fx.TenantId, "machine.smallHopper", "H2", array.Id);
        var onion = await _fx.SeedMachineAsync(_fx.TenantId, "machine.onion.hopper", "OH", array.Id);
        var firstPhoto = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, first.Id, "/uploads/first.jpg");
        var secondPhoto = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, second.Id, "/uploads/second.jpg");
        var onionPhoto = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, onion.Id, "/uploads/onion.jpg");
        var otherMachine = await _fx.SeedMachineAsync(_fx.OtherTenantId, "machine.largeHopper", "HX", null);
        await _fx.SeedPhotoAsync(_fx.OtherTenantId, null, 1, otherMachine.Id, "/uploads/other.jpg");
        var linkedGroup = await _fx.SeedGroupAsync(_fx.TenantId, "group.linked");
        var linked = await _fx.SeedParameterAsync(_fx.TenantId, linkedGroup.Id, "parameter.linked");
        await _fx.SeedPhotoAsync(_fx.TenantId, linked.Id, 2, first.Id, "/uploads/linked.jpg");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateUngrantedService().GetLegacyPhotosAsync(CancellationToken.None));

        var recovered = await _fx.Service.GetLegacyPhotosAsync(CancellationToken.None);

        Assert.Equal(3, recovered.Count);
        Assert.Contains(recovered, photo => photo.Id == firstPhoto.Id && photo.MachineName == "machine.largeHopper" && photo.ImageUrl == "/uploads/first.jpg");
        Assert.Contains(recovered, photo => photo.Id == secondPhoto.Id && photo.MachineId == second.Id && photo.ImageUrl == "/uploads/second.jpg");
        Assert.Contains(recovered, photo => photo.Id == onionPhoto.Id && photo.MachineName == "machine.onion.hopper");
        Assert.DoesNotContain(recovered, photo => photo.ImageUrl == "/uploads/other.jpg");
        Assert.DoesNotContain(recovered, photo => photo.ImageUrl == "/uploads/linked.jpg");

        var stored = await _fx.DbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(photo => photo.Id == firstPhoto.Id);
        Assert.Null(stored.ParameterId);
        Assert.Equal(first.Id, stored.MachineId);
        Assert.Equal("/uploads/first.jpg", stored.ImageUrl);
    }

    [Fact]
    public async Task AssignLegacyPhoto_IsExplicitAndLeavesHopperEmpty()
    {
        await GrantAsync();
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.washing_system");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, null, 2, machine.Id, "/uploads/keep.jpg");
        var target = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId &&
            parameter.NameKey == ParameterPhotoCatalog.CoolingSystemComputerKey);

        var assigned = await _fx.Service.AssignLegacyPhotoAsync(photo.Id, target.Id, CancellationToken.None);

        Assert.Equal(target.Id, assigned.ParameterId);
        var stored = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().SingleAsync(item => item.Id == photo.Id);
        Assert.Equal(machine.Id, stored.MachineId);
        Assert.Equal("/uploads/keep.jpg", stored.ImageUrl);
        Assert.Equal(2, stored.SortOrder);
        var hopper = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId &&
            parameter.NameKey == ParameterPhotoCatalog.HopperKey);
        Assert.Empty(await _fx.DbContext.MachineParameterPhotos.Where(item => item.ParameterId == hopper.Id).ToListAsync());
        Assert.Empty(await _fx.Service.GetLegacyPhotosAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RollbackGuard_RefusesParameterPhotos_AndPreservesTheRow()
    {
        var group = await _fx.SeedGroupAsync(_fx.TenantId, "parameter.group");
        var parameter = await _fx.SeedParameterAsync(_fx.TenantId, group.Id, "parameter.item");
        var photo = await _fx.SeedPhotoAsync(_fx.TenantId, parameter.Id, 1, machineId: null, "/uploads/new.jpg");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ParameterPhotoRollbackGuard.EnsureSafeAsync(_fx.DbContext, CancellationToken.None));

        Assert.Equal(ParameterPhotoRollbackGuard.Message, exception.Message);
        var stored = await _fx.DbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(item => item.Id == photo.Id);
        Assert.Equal(parameter.Id, stored.ParameterId);
        Assert.Equal("/uploads/new.jpg", stored.ImageUrl);
        Assert.Empty(_fx.Storage.DeletedUrls);

        var legacyOnly = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            null,
            1,
            (await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", null)).Id,
            "/uploads/old.jpg");
        _fx.DbContext.MachineParameterPhotos.Remove(stored);
        await _fx.DbContext.SaveChangesAsync();
        await ParameterPhotoRollbackGuard.EnsureSafeAsync(_fx.DbContext, CancellationToken.None);
        Assert.NotNull(await _fx.DbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(item => item.Id == legacyOnly.Id));
    }

    [Fact]
    public async Task Linker_IsIdempotent_SkipsAmbiguousNamesAndPreservesAssociations()
    {
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.OtherTenantId, CancellationToken.None);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.packing_house");
        var viscoseA = await _fx.SeedMachineAsync(_fx.TenantId, "machine.viscose", "V1", array.Id);
        var viscoseB = await _fx.SeedMachineAsync(_fx.TenantId, "machine.viscose", "V2", array.Id);
        var sorter = await _fx.SeedMachineAsync(_fx.TenantId, "machine.sorter1", "S1", array.Id);
        var otherSorter = await _fx.SeedMachineAsync(_fx.OtherTenantId, "machine.sorter1", "S9", null);
        var sorterParameter = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId && parameter.NameKey == "machine.sorter1");
        var preservedTarget = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId && parameter.NameKey == "machine.sorter2");
        var ambiguousA = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, viscoseA.Id, "/uploads/a.jpg");
        var ambiguousB = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, viscoseB.Id, "/uploads/b.jpg");
        var unique = await _fx.SeedPhotoAsync(_fx.TenantId, null, 3, sorter.Id, "/uploads/sorter.jpg");
        var preserved = await _fx.SeedPhotoAsync(_fx.TenantId, preservedTarget.Id, 4, sorter.Id, "/uploads/preserved.jpg");
        var otherTenant = await _fx.SeedPhotoAsync(_fx.OtherTenantId, null, 1, otherSorter.Id, "/uploads/other-sorter.jpg");
        var occupied = await _fx.SeedPhotoAsync(_fx.TenantId, sorterParameter.Id, 5, machineId: null, "/uploads/occupied.jpg");
        var colliding = await _fx.SeedPhotoAsync(_fx.TenantId, null, 5, sorter.Id, "/uploads/collision.jpg");

        var first = await ParameterPhotoLegacyLinker.LinkAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var second = await ParameterPhotoLegacyLinker.LinkAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);

        Assert.Contains("machine.viscose", first.AmbiguousMachineNames);
        Assert.Equal(0, second.LinkedCount);
        var photos = await _fx.DbContext.MachineParameterPhotos.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(7, photos.Count);
        Assert.Null(photos.Single(photo => photo.Id == ambiguousA.Id).ParameterId);
        Assert.Equal(viscoseA.Id, photos.Single(photo => photo.Id == ambiguousA.Id).MachineId);
        Assert.Equal("/uploads/a.jpg", photos.Single(photo => photo.Id == ambiguousA.Id).ImageUrl);
        Assert.Null(photos.Single(photo => photo.Id == ambiguousB.Id).ParameterId);
        Assert.Equal(viscoseB.Id, photos.Single(photo => photo.Id == ambiguousB.Id).MachineId);
        Assert.Equal(sorterParameter.Id, photos.Single(photo => photo.Id == unique.Id).ParameterId);
        Assert.Equal(sorter.Id, photos.Single(photo => photo.Id == unique.Id).MachineId);
        Assert.Equal("/uploads/sorter.jpg", photos.Single(photo => photo.Id == unique.Id).ImageUrl);
        Assert.Equal(3, photos.Single(photo => photo.Id == unique.Id).SortOrder);
        Assert.Equal(preservedTarget.Id, photos.Single(photo => photo.Id == preserved.Id).ParameterId);
        Assert.Equal("/uploads/preserved.jpg", photos.Single(photo => photo.Id == preserved.Id).ImageUrl);
        Assert.Null(photos.Single(photo => photo.Id == otherTenant.Id).ParameterId);
        Assert.Null(photos.Single(photo => photo.Id == colliding.Id).ParameterId);
        Assert.Equal(sorter.Id, photos.Single(photo => photo.Id == colliding.Id).MachineId);
        Assert.Equal("/uploads/collision.jpg", photos.Single(photo => photo.Id == colliding.Id).ImageUrl);
        Assert.Equal(occupied.Id, photos.Single(photo => photo.Id == occupied.Id).Id);
        Assert.Equal(sorterParameter.Id, photos.Single(photo => photo.Id == occupied.Id).ParameterId);
    }

    [Fact]
    public async Task Exclusions_HideMatchingCatalogParametersWithoutRemovingLegacyRows()
    {
        await GrantAsync();
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var packing = await _fx.SeedArrayAsync(_fx.TenantId, "array.packing_house");
        var hiddenArray = await _fx.SeedArrayAsync(_fx.TenantId, "array.hidden-packing");
        var viscose = await _fx.SeedMachineAsync(_fx.TenantId, "machine.viscose", "V1", packing.Id);
        var sorter = await _fx.SeedMachineAsync(_fx.TenantId, "machine.sorter1", "S1", hiddenArray.Id);
        var hopper = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", packing.Id);
        await _fx.ExcludeMachineAsync(_fx.TenantId, viscose.Id);
        await _fx.ExcludeMachineAsync(_fx.TenantId, hopper.Id);
        await _fx.HideArrayAsync(_fx.TenantId, hiddenArray.Id);
        var otherArray = await _fx.SeedArrayAsync(_fx.TenantId, "array.other");
        var otherMachine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.sorter2", "S2", otherArray.Id);
        await _fx.HideArrayAsync(_fx.TenantId, otherArray.Id, featureKey: "some_other_feature");
        var viscoseParameter = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId && parameter.NameKey == "machine.viscose");
        var linked = await _fx.SeedPhotoAsync(_fx.TenantId, viscoseParameter.Id, 1, viscose.Id, "/uploads/viscose.jpg");
        var hopperPhoto = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, hopper.Id, "/uploads/hopper.jpg");

        await ParameterPhotoExclusionProjector.ProjectAsync(_fx.DbContext, CancellationToken.None);
        var catalog = await _fx.Service.GetCatalogAsync(CancellationToken.None);
        var visible = catalog.Groups.SelectMany(group => group.Parameters).Select(parameter => parameter.NameKey).ToHashSet();

        Assert.DoesNotContain("machine.viscose", visible);
        Assert.DoesNotContain("machine.sorter1", visible);
        Assert.Contains(ParameterPhotoCatalog.HopperKey, visible);
        Assert.Contains("machine.sorter2", visible);
        Assert.True(await _fx.DbContext.MachineParameterMachineExclusions.AnyAsync(item => item.MachineId == viscose.Id));
        Assert.True(await _fx.DbContext.Machines.AnyAsync(item => item.Id == otherMachine.Id));
        var storedViscose = await _fx.DbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(photo => photo.Id == linked.Id);
        Assert.Null(storedViscose.ParameterId);
        Assert.Equal(viscose.Id, storedViscose.MachineId);
        Assert.Equal("/uploads/viscose.jpg", storedViscose.ImageUrl);
        var recovered = await _fx.Service.GetLegacyPhotosAsync(CancellationToken.None);
        Assert.Contains(recovered, photo => photo.Id == linked.Id);
        Assert.Contains(recovered, photo => photo.Id == hopperPhoto.Id && photo.MachineName == "machine.largeHopper");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fx.Service.GetByParameterIdAsync(viscoseParameter.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Seeder_RestoresCanonicalOrder_AndLeavesInactiveAndCustomRows()
    {
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var washing = await _fx.DbContext.ParameterPhotoGroups.SingleAsync(group =>
            group.TenantId == _fx.TenantId && group.NameKey == ParameterPhotoCatalog.WashingSystemGroupKey);
        var hopper = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId && parameter.NameKey == ParameterPhotoCatalog.HopperKey);
        washing.SortOrder = 40;
        hopper.SortOrder = 12;
        hopper.IsActive = false;
        var custom = await _fx.SeedGroupAsync(_fx.TenantId, "parameter.custom", sortOrder: 7, isActive: true);
        var customParameter = await _fx.SeedParameterAsync(_fx.TenantId, custom.Id, "parameter.custom.item", sortOrder: 6);
        await _fx.DbContext.SaveChangesAsync();

        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);

        _fx.DbContext.ChangeTracker.Clear();
        var storedWashing = await _fx.DbContext.ParameterPhotoGroups.SingleAsync(group => group.Id == washing.Id);
        var storedHopper = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter => parameter.Id == hopper.Id);
        var storedCustom = await _fx.DbContext.ParameterPhotoGroups.SingleAsync(group => group.Id == custom.Id);
        var storedCustomParameter = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter => parameter.Id == customParameter.Id);
        Assert.Equal(1, storedWashing.SortOrder);
        Assert.Equal(1, storedHopper.SortOrder);
        Assert.False(storedHopper.IsActive);
        Assert.Equal(7, storedCustom.SortOrder);
        Assert.Equal(6, storedCustomParameter.SortOrder);
        Assert.True(storedCustom.IsActive);
    }

    [Fact]
    public async Task EnsureAsync_RecoversFromConcurrentUniqueConflict()
    {
        var connectionString = $"Data Source=file:catalog-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new ConcurrentSeedInterceptor(connectionString, tenantId))
            .Options;

        await using (var setup = new MaintTrackDbContext(options, new TenantContext { TenantId = tenantId }))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        await using var db = new MaintTrackDbContext(options, new TenantContext { TenantId = tenantId });
        await ParameterPhotoCatalogSeeder.EnsureAsync(db, tenantId, CancellationToken.None);

        var groups = await db.ParameterPhotoGroups.IgnoreQueryFilters().Where(group => group.TenantId == tenantId).ToListAsync();
        var parameters = await db.ParameterPhotoParameters.IgnoreQueryFilters().Where(parameter => parameter.TenantId == tenantId).ToListAsync();
        Assert.Equal(ParameterPhotoCatalog.Groups.Count, groups.Select(group => group.NameKey).Distinct().Count());
        Assert.Equal(groups.Count, groups.Select(group => group.NameKey).Distinct().Count());
        Assert.Equal(
            ParameterPhotoCatalog.Groups.Sum(group => group.Parameters.Count),
            parameters.Select(parameter => parameter.NameKey).Distinct().Count());
    }

    [Fact]
    public void ExclusionMigrationSql_PreservesLegacySourcesAndFailsClosedOnCrossTenantRows()
    {
        var source = File.ReadAllText(FindSource("ParameterPhotoExclusionProjector.cs"));
        Assert.Contains("machine_parameter_machine_visibility", source, StringComparison.Ordinal);
        Assert.Contains("array_feature_visibility", source, StringComparison.Ordinal);
        Assert.Contains("machine.name = parameter.name_key", source, StringComparison.Ordinal);
        Assert.Contains("feature_key = 'machine_parameter_photos'", source, StringComparison.Ordinal);
        Assert.Contains("a parameter references a group from another tenant", source, StringComparison.Ordinal);
        Assert.Contains("a photo references a parameter from another tenant", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM machine_parameter_photos", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM machine_parameter_machine_visibility", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM array_feature_visibility", source, StringComparison.Ordinal);

        var migration = File.ReadAllText(FindMigration("AddParameterPhotoReviewRemediation.cs"));
        Assert.Contains("ParameterPhotoExclusionProjector.TenantIntegrityGuardSql", migration, StringComparison.Ordinal);
        Assert.Contains("ParameterPhotoExclusionProjector.ProjectSql", migration, StringComparison.Ordinal);
        Assert.Contains("ParameterPhotoExclusionProjector.UnlinkExcludedLegacyPhotosSql", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM machine_parameter_photos", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("maintenance_tasks", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("annual_plan_tasks", migration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssignLegacyPhoto_OverlappingAttempts_LetExactlyOneSucceed()
    {
        var connectionString = $"Data Source=file:assign-{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var overlap = new OverlappingPhotoUpdateInterceptor();
        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(overlap)
            .Options;

        Guid firstParameterId;
        Guid secondParameterId;
        Guid hopperParameterId;
        Guid photoId;
        Guid machineId;
        await using (var setup = new MaintTrackDbContext(options, new TenantContext { TenantId = tenantId }))
        {
            setup.Database.SetCommandTimeout(5);
            await setup.Database.EnsureCreatedAsync();
            setup.Roles.Add(new MaintTrack.Domain.Users.Role { Id = 1, Name = "Worker" });
            setup.Users.Add(new MaintTrack.Domain.Users.User
            {
                Id = userId,
                TenantId = tenantId,
                Username = "assign-manager",
                Email = "assign@example.com",
                PasswordHash = "hash",
                DisplayName = "Assign Manager",
                RoleId = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
            await ParameterPhotoCatalogSeeder.EnsureAsync(setup, tenantId, CancellationToken.None);
            var array = new WorkGroup
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                NameKey = "array.washing_system",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            setup.Arrays.Add(array);
            machineId = Guid.NewGuid();
            setup.Machines.Add(new MaintTrack.Domain.Machines.Machine
            {
                Id = machineId,
                TenantId = tenantId,
                Name = "machine.largeHopper",
                Code = "H1",
                ArrayId = array.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            setup.MachineParameterPhotoManagers.Add(new MachineParameterPhotoManager
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
            firstParameterId = await setup.ParameterPhotoParameters
                .Where(parameter => parameter.TenantId == tenantId && parameter.NameKey == ParameterPhotoCatalog.CoolingSystemComputerKey)
                .Select(parameter => parameter.Id)
                .SingleAsync();
            secondParameterId = await setup.ParameterPhotoParameters
                .Where(parameter => parameter.TenantId == tenantId && parameter.NameKey == ParameterPhotoCatalog.CompressorRoomsKey)
                .Select(parameter => parameter.Id)
                .SingleAsync();
            hopperParameterId = await setup.ParameterPhotoParameters
                .Where(parameter => parameter.TenantId == tenantId && parameter.NameKey == ParameterPhotoCatalog.HopperKey)
                .Select(parameter => parameter.Id)
                .SingleAsync();
            photoId = Guid.NewGuid();
            setup.MachineParameterPhotos.Add(new MachineParameterPhoto
            {
                Id = photoId,
                TenantId = tenantId,
                MachineId = machineId,
                ImageUrl = "/uploads/overlap.jpg",
                SortOrder = 1,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        overlap.Arm();
        var overlapGate = new Barrier(2);
        var overlapScope = new AsyncLocal<Barrier?>();
        overlapScope.Value = overlapGate;
        MachineParameterPhotoService.BeforeAssignmentPersistAsync = async cancellationToken =>
        {
            await Task.Yield();
            var gate = overlapScope.Value;
            if (gate is null)
            {
                return;
            }

            var overlapped = await Task.Run(
                () => gate.SignalAndWait(TimeSpan.FromSeconds(8)),
                cancellationToken);
            if (!overlapped)
            {
                throw new TimeoutException("The two assignment attempts did not overlap.");
            }
        };
        try
        {
        await using var firstContext = new MaintTrackDbContext(options, new TenantContext { TenantId = tenantId });
        await using var secondContext = new MaintTrackDbContext(options, new TenantContext { TenantId = tenantId });
        firstContext.Database.SetCommandTimeout(5);
        secondContext.Database.SetCommandTimeout(5);
        var firstService = CreateService(firstContext, tenantId, userId);
        var secondService = CreateService(secondContext, tenantId, userId);

        var firstAttempt = firstService.AssignLegacyPhotoAsync(photoId, firstParameterId, CancellationToken.None);
        var secondAttempt = secondService.AssignLegacyPhotoAsync(photoId, secondParameterId, CancellationToken.None);
        var outcomes = await Task.WhenAll(CaptureAsync(firstAttempt), CaptureAsync(secondAttempt));

        Assert.Equal(2, overlap.AssignmentUpdates);
        Assert.All(overlap.AssignmentSql, sql =>
        {
            Assert.Contains("parameter_id", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NULL", sql, StringComparison.OrdinalIgnoreCase);
        });
        var succeeded = outcomes.Where(outcome => outcome.Photo is not null).ToList();
        var failed = outcomes.Where(outcome => outcome.Error is not null).ToList();
        Assert.Single(succeeded);
        var failure = Assert.Single(failed);
        Assert.IsType<KeyNotFoundException>(failure.Error);
        Assert.Contains(succeeded[0].Photo!.ParameterId, new[] { firstParameterId, secondParameterId });

        await using var verify = new MaintTrackDbContext(options, new TenantContext { TenantId = tenantId });
        var stored = await verify.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(photo => photo.Id == photoId);
        Assert.Equal(succeeded[0].Photo!.ParameterId, stored.ParameterId);
        Assert.NotEqual(hopperParameterId, stored.ParameterId);
        Assert.Equal(machineId, stored.MachineId);
        Assert.Equal("/uploads/overlap.jpg", stored.ImageUrl);
        Assert.Equal(1, await verify.MachineParameterPhotos.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            MachineParameterPhotoService.BeforeAssignmentPersistAsync = null;
        }
    }

    [Fact]
    public async Task RepeatedCatalogLoads_DoNotRunLegacyLinkUpdates()
    {
        var connectionString = $"Data Source=file:catalog-read-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var tenantId = Guid.NewGuid();
        var updates = new UpdateCommandCounter();
        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(updates)
            .Options;
        var tenant = new TenantContext { TenantId = tenantId };
        Guid photoId;
        Guid machineId;
        await using (var setup = new MaintTrackDbContext(options, tenant))
        {
            await setup.Database.EnsureCreatedAsync();
            await ParameterPhotoCatalogSeeder.EnsureAsync(setup, tenantId, CancellationToken.None);
            var array = new WorkGroup
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                NameKey = "array.packing_house",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            setup.Arrays.Add(array);
            machineId = Guid.NewGuid();
            setup.Machines.Add(new MaintTrack.Domain.Machines.Machine
            {
                Id = machineId,
                TenantId = tenantId,
                Name = "machine.viscose",
                Code = "V1",
                ArrayId = array.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            var hopperMachineId = Guid.NewGuid();
            setup.Machines.Add(new MaintTrack.Domain.Machines.Machine
            {
                Id = hopperMachineId,
                TenantId = tenantId,
                Name = "machine.largeHopper",
                Code = "H1",
                ArrayId = array.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
            photoId = Guid.NewGuid();
            setup.MachineParameterPhotos.AddRange(
                new MachineParameterPhoto
                {
                    Id = photoId,
                    TenantId = tenantId,
                    MachineId = machineId,
                    ImageUrl = "/uploads/viscose-unlinked.jpg",
                    SortOrder = 4,
                    CreatedByUserId = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow
                },
                new MachineParameterPhoto
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    MachineId = hopperMachineId,
                    ImageUrl = "/uploads/large-hopper.jpg",
                    SortOrder = 1,
                    CreatedByUserId = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow
                });
            await setup.SaveChangesAsync();
        }

        updates.Reset();
        var logger = new CollectingLogger();
        await using var db = new MaintTrackDbContext(options, tenant);
        var service = new MachineParameterPhotoService(
            db,
            tenant,
            new StubCurrentUserContext(tenantId, Guid.NewGuid()),
            new RecordingFileStorageService(),
            logger);
        var before = await db.MachineParameterPhotos.AsNoTracking().IgnoreQueryFilters().OrderBy(photo => photo.ImageUrl).ToListAsync();

        await service.GetCatalogAsync(CancellationToken.None);
        await service.GetCatalogAsync(CancellationToken.None);

        var after = await db.MachineParameterPhotos.AsNoTracking().IgnoreQueryFilters().OrderBy(photo => photo.ImageUrl).ToListAsync();
        Assert.Equal(0, updates.UpdateCount);
        Assert.Empty(logger.Messages);
        Assert.Equal(before.Select(Snapshot), after.Select(Snapshot));
        Assert.All(after, photo => Assert.Null(photo.ParameterId));
        Assert.Contains(after, photo => photo.Id == photoId && photo.MachineId == machineId && photo.ImageUrl == "/uploads/viscose-unlinked.jpg");
    }

    [Fact]
    public async Task CatalogInitialization_LinksUniquePhotosOnce_AndWarnsWithoutClaimingAllLinkingWasSkipped()
    {
        var logger = new CollectingLogger();
        var service = new MachineParameterPhotoService(
            _fx.DbContext,
            _fx.TenantContext,
            _fx.CurrentUser,
            _fx.Storage,
            logger);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.packing_house");
        var viscoseA = await _fx.SeedMachineAsync(_fx.TenantId, "machine.viscose", "V1", array.Id);
        var viscoseB = await _fx.SeedMachineAsync(_fx.TenantId, "machine.viscose", "V2", array.Id);
        var sorter = await _fx.SeedMachineAsync(_fx.TenantId, "machine.sorter1", "S1", array.Id);
        var large = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        var small = await _fx.SeedMachineAsync(_fx.TenantId, "machine.smallHopper", "H2", array.Id);
        var onion = await _fx.SeedMachineAsync(_fx.TenantId, "machine.onion.hopper", "OH", array.Id);
        var ambiguousA = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, viscoseA.Id, "/uploads/ambiguous-a.jpg");
        var ambiguousB = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, viscoseB.Id, "/uploads/ambiguous-b.jpg");
        var unique = await _fx.SeedPhotoAsync(_fx.TenantId, null, 2, sorter.Id, "/uploads/sorter-unique.jpg");
        var largePhoto = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, large.Id, "/uploads/large.jpg");
        var smallPhoto = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, small.Id, "/uploads/small.jpg");
        var onionPhoto = await _fx.SeedPhotoAsync(_fx.TenantId, null, 1, onion.Id, "/uploads/onion.jpg");

        await service.GetCatalogAsync(CancellationToken.None);
        await service.GetCatalogAsync(CancellationToken.None);

        Assert.Contains("were still linked", ParameterPhotoLegacyLinker.AmbiguousNamesMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Skipped legacy", ParameterPhotoLegacyLinker.AmbiguousNamesMessage, StringComparison.Ordinal);
        var warning = Assert.Single(logger.Messages);
        Assert.Contains("machine.viscose", warning, StringComparison.Ordinal);
        Assert.Contains("were still linked", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Skipped legacy", warning, StringComparison.Ordinal);

        _fx.DbContext.ChangeTracker.Clear();
        var photos = await _fx.DbContext.MachineParameterPhotos.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var sorterParameterId = await _fx.DbContext.ParameterPhotoParameters
            .Where(parameter => parameter.TenantId == _fx.TenantId && parameter.NameKey == "machine.sorter1")
            .Select(parameter => parameter.Id)
            .SingleAsync();
        var hopperParameterId = await _fx.DbContext.ParameterPhotoParameters
            .Where(parameter => parameter.TenantId == _fx.TenantId && parameter.NameKey == ParameterPhotoCatalog.HopperKey)
            .Select(parameter => parameter.Id)
            .SingleAsync();
        Assert.Equal(sorterParameterId, photos.Single(photo => photo.Id == unique.Id).ParameterId);
        Assert.Equal(sorter.Id, photos.Single(photo => photo.Id == unique.Id).MachineId);
        Assert.Equal("/uploads/sorter-unique.jpg", photos.Single(photo => photo.Id == unique.Id).ImageUrl);
        Assert.Equal(2, photos.Single(photo => photo.Id == unique.Id).SortOrder);
        Assert.Null(photos.Single(photo => photo.Id == ambiguousA.Id).ParameterId);
        Assert.Null(photos.Single(photo => photo.Id == ambiguousB.Id).ParameterId);
        Assert.Null(photos.Single(photo => photo.Id == largePhoto.Id).ParameterId);
        Assert.Null(photos.Single(photo => photo.Id == smallPhoto.Id).ParameterId);
        Assert.Null(photos.Single(photo => photo.Id == onionPhoto.Id).ParameterId);
        Assert.Equal(large.Id, photos.Single(photo => photo.Id == largePhoto.Id).MachineId);
        Assert.Equal("/uploads/large.jpg", photos.Single(photo => photo.Id == largePhoto.Id).ImageUrl);
        Assert.DoesNotContain(photos, photo => photo.ParameterId == hopperParameterId);
    }

    [Fact]
    public async Task AssignLegacyPhoto_UsesNextSortWhenTargetSortIsTaken()
    {
        await GrantAsync();
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var target = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId &&
            parameter.NameKey == ParameterPhotoCatalog.CoolingSystemComputerKey);
        var existing = await _fx.SeedPhotoAsync(_fx.TenantId, target.Id, 4, machineId: null, "/uploads/existing-sort.jpg");
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.washing_system");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        var legacy = await _fx.SeedPhotoAsync(_fx.TenantId, null, 4, machine.Id, "/uploads/legacy-sort.jpg");

        var assigned = await _fx.Service.AssignLegacyPhotoAsync(legacy.Id, target.Id, CancellationToken.None);

        Assert.Equal(target.Id, assigned.ParameterId);
        Assert.Equal(5, assigned.SortOrder);
        _fx.DbContext.ChangeTracker.Clear();
        var storedLegacy = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().SingleAsync(photo => photo.Id == legacy.Id);
        var storedExisting = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().SingleAsync(photo => photo.Id == existing.Id);
        Assert.Equal(machine.Id, storedLegacy.MachineId);
        Assert.Equal("/uploads/legacy-sort.jpg", storedLegacy.ImageUrl);
        Assert.Equal(5, storedLegacy.SortOrder);
        Assert.Equal(target.Id, storedExisting.ParameterId);
        Assert.Equal(4, storedExisting.SortOrder);
        Assert.Equal("/uploads/existing-sort.jpg", storedExisting.ImageUrl);
        Assert.Null(storedExisting.MachineId);
    }

    [Fact]
    public void CatalogMigrationDown_StopsBeforeSchemaChangesWhenParameterPhotosWouldBeLost()
    {
        var operations = DownOperations<AddIndependentParameterPhotoCatalog>();
        var guard = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Equal(ParameterPhotoRollbackGuard.Sql, guard.Sql);
        Assert.Contains("RAISE EXCEPTION", guard.Sql, StringComparison.Ordinal);
        Assert.All(operations.Skip(1), operation => Assert.IsNotType<SqlOperation>(operation));

        using var connection = OpenMigrationConnection();
        CreateMigrationSchema(connection, includeExclusions: false);
        var photoId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var parameterId = Guid.NewGuid();
        InsertMigrationPhoto(connection, photoId, machineId, parameterId, "/uploads/kept.jpg");

        var exception = Assert.Throws<InvalidOperationException>(() => MigrationDownRunner.Apply(operations, connection));

        Assert.Equal(ParameterPhotoRollbackGuard.Message, exception.Message);
        var stored = ReadMigrationPhoto(connection, photoId);
        Assert.Equal(machineId, stored.MachineId);
        Assert.Equal(parameterId, stored.ParameterId);
        Assert.Equal("/uploads/kept.jpg", stored.ImageUrl);
        Assert.Equal(1, CountMigrationPhotos(connection));
        Assert.True(TableExists(connection, "parameter_photo_parameters"));
        Assert.True(TableExists(connection, "parameter_photo_groups"));
        Assert.True(ColumnExists(connection, "machine_parameter_photos", "parameter_id"));
    }

    [Fact]
    public void CatalogMigrationDown_KeepsLegacyRowsWhenRollbackIsAllowed()
    {
        var operations = DownOperations<AddIndependentParameterPhotoCatalog>();
        using var connection = OpenMigrationConnection();
        CreateMigrationSchema(connection, includeExclusions: false);
        var photoId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        InsertMigrationPhoto(connection, photoId, machineId, parameterId: null, "/uploads/legacy-kept.jpg");

        MigrationDownRunner.Apply(operations, connection);

        var stored = ReadMigrationPhoto(connection, photoId);
        Assert.Equal(machineId, stored.MachineId);
        Assert.Null(stored.ParameterId);
        Assert.Equal("/uploads/legacy-kept.jpg", stored.ImageUrl);
        Assert.Equal(1, CountMigrationPhotos(connection));
        Assert.False(ColumnExists(connection, "machine_parameter_photos", "parameter_id"));
        Assert.False(TableExists(connection, "parameter_photo_parameters"));
        Assert.False(TableExists(connection, "parameter_photo_groups"));
    }

    [Fact]
    public void RemediationMigrationDown_RemovesExclusionsWithoutDeletingPhotos()
    {
        var operations = DownOperations<AddParameterPhotoReviewRemediation>();
        Assert.DoesNotContain(operations, operation => operation is SqlOperation sql && sql.Sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase));

        using var connection = OpenMigrationConnection();
        CreateMigrationSchema(connection, includeExclusions: true);
        var photoId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var parameterId = Guid.NewGuid();
        InsertMigrationPhoto(connection, photoId, machineId, parameterId, "/uploads/still-here.jpg");

        MigrationDownRunner.Apply(operations, connection);

        var stored = ReadMigrationPhoto(connection, photoId);
        Assert.Equal(machineId, stored.MachineId);
        Assert.Equal(parameterId, stored.ParameterId);
        Assert.Equal("/uploads/still-here.jpg", stored.ImageUrl);
        Assert.Equal(1, CountMigrationPhotos(connection));
        Assert.False(TableExists(connection, "parameter_photo_parameter_exclusions"));
        Assert.True(TableExists(connection, "parameter_photo_parameters"));
    }

    private async Task GrantAsync()
    {
        await _fx.SeedUserAsync(_fx.TenantId, _fx.UserId, "manager");
        await _fx.GrantManagerAsync(_fx.TenantId, _fx.UserId);
    }

    private MachineParameterPhotoService CreateUngrantedService()
    {
        var otherUser = Guid.NewGuid();
        return new MachineParameterPhotoService(
            _fx.DbContext,
            _fx.TenantContext,
            new StubCurrentUserContext(_fx.TenantId, otherUser),
            _fx.Storage,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MachineParameterPhotoService>.Instance);
    }

    private static string FindMigration(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "MaintTrack.Infrastructure", "Migrations", fileName);
            if (File.Exists(candidate) && !candidate.EndsWith(".Designer.cs", StringComparison.Ordinal))
            {
                return candidate;
            }

            var migrations = Path.Combine(directory.FullName, "backend", "MaintTrack.Infrastructure", "Migrations");
            if (Directory.Exists(migrations))
            {
                var match = Directory.GetFiles(migrations, "*" + fileName)
                    .SingleOrDefault(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
                if (match is not null)
                {
                    return match;
                }
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{fileName} was not found.");
    }

    private static MachineParameterPhotoService CreateService(MaintTrackDbContext dbContext, Guid tenantId, Guid userId) =>
        new(
            dbContext,
            new TenantContext { TenantId = tenantId },
            new StubCurrentUserContext(tenantId, userId),
            new RecordingFileStorageService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MachineParameterPhotoService>.Instance);

    private static async Task<(MachineParameterPhotoDto? Photo, Exception? Error)> CaptureAsync(
        Task<MachineParameterPhotoDto> assignment)
    {
        try
        {
            return (await assignment, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static string Snapshot(MachineParameterPhoto photo) =>
        $"{photo.Id:N}|{photo.ParameterId:N}|{photo.MachineId:N}|{photo.ImageUrl}|{photo.SortOrder}";

    private static IReadOnlyList<MigrationOperation> DownOperations<TMigration>()
        where TMigration : Migration, new()
    {
        var migration = new TMigration();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var method = typeof(TMigration).GetMethod(
            "Down",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(MigrationBuilder) },
            modifiers: null);
        method!.Invoke(migration, new object[] { builder });
        return builder.Operations;
    }

    private static SqliteConnection OpenMigrationConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static void CreateMigrationSchema(SqliteConnection connection, bool includeExclusions)
    {
        Execute(
            connection,
            """
            CREATE TABLE parameter_photo_groups (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                name_key TEXT NOT NULL
            );
            CREATE TABLE parameter_photo_parameters (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                name_key TEXT NOT NULL
            );
            CREATE TABLE machine_parameter_photos (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                machine_id TEXT NULL,
                parameter_id TEXT NULL,
                image_url TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
        if (includeExclusions)
        {
            Execute(
                connection,
                """
                CREATE TABLE parameter_photo_parameter_exclusions (
                    id TEXT NOT NULL PRIMARY KEY,
                    parameter_id TEXT NOT NULL,
                    tenant_id TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                """);
        }
    }

    private static void InsertMigrationPhoto(
        SqliteConnection connection,
        Guid photoId,
        Guid machineId,
        Guid? parameterId,
        string imageUrl)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO machine_parameter_photos
                (id, tenant_id, machine_id, parameter_id, image_url, sort_order, created_by_user_id, created_at)
            VALUES
                ($id, $tenant, $machine, $parameter, $url, 1, $user, $created);
            """;
        command.Parameters.AddWithValue("$id", photoId.ToString());
        command.Parameters.AddWithValue("$tenant", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$machine", machineId.ToString());
        command.Parameters.AddWithValue("$parameter", (object?)parameterId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$url", imageUrl);
        command.Parameters.AddWithValue("$user", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static (Guid? MachineId, Guid? ParameterId, string ImageUrl) ReadMigrationPhoto(SqliteConnection connection, Guid photoId)
    {
        using var command = connection.CreateCommand();
        var parameterColumn = ColumnExists(connection, "machine_parameter_photos", "parameter_id")
            ? "parameter_id"
            : "NULL";
        command.CommandText = $"SELECT machine_id, {parameterColumn}, image_url FROM machine_parameter_photos WHERE id = $id";
        command.Parameters.AddWithValue("$id", photoId.ToString());
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return (
            reader.IsDBNull(0) ? null : Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetString(2));
    }

    private static int CountMigrationPhotos(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM machine_parameter_photos";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $name";
        command.Parameters.AddWithValue("$name", column);
        return command.ExecuteScalar() is not null;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string FindSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "backend",
                "MaintTrack.Infrastructure",
                "MachineParameterPhotos",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{fileName} was not found.");
    }

    private sealed class MigrationDownRunner
    {
        public static void Apply(IReadOnlyList<MigrationOperation> operations, SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            foreach (var operation in operations)
            {
                switch (operation)
                {
                    case SqlOperation sql when sql.Sql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase):
                        throw new InvalidOperationException("Down deletes rows.");
                    case SqlOperation sql when sql.Sql.Contains("RAISE EXCEPTION", StringComparison.Ordinal):
                        if (ParameterPhotosExist(connection, transaction))
                        {
                            transaction.Rollback();
                            throw new InvalidOperationException(ParameterPhotoRollbackGuard.Message);
                        }

                        break;
                    case DropTableOperation dropTable:
                        Execute(connection, transaction, $"DROP TABLE IF EXISTS {Quote(dropTable.Name)}");
                        break;
                    case DropColumnOperation dropColumn:
                        Execute(connection, transaction, $"ALTER TABLE {Quote(dropColumn.Table)} DROP COLUMN {Quote(dropColumn.Name)}");
                        break;
                    case DropIndexOperation dropIndex:
                        Execute(connection, transaction, $"DROP INDEX IF EXISTS {Quote(dropIndex.Name)}");
                        break;
                    case DropUniqueConstraintOperation dropConstraint:
                        Execute(connection, transaction, $"DROP INDEX IF EXISTS {Quote(dropConstraint.Name)}");
                        break;
                    case CreateIndexOperation createIndex:
                        var columns = string.Join(", ", createIndex.Columns.Select(Quote));
                        var unique = createIndex.IsUnique ? "UNIQUE " : string.Empty;
                        Execute(
                            connection,
                            transaction,
                            $"CREATE {unique}INDEX IF NOT EXISTS {Quote(createIndex.Name)} ON {Quote(createIndex.Table)} ({columns})");
                        break;
                    case AlterColumnOperation alter when alter.Name == "machine_id" && !alter.IsNullable:
                        using (var check = connection.CreateCommand())
                        {
                            check.Transaction = transaction;
                            check.CommandText = $"SELECT COUNT(*) FROM {Quote(alter.Table)} WHERE {Quote(alter.Name)} IS NULL";
                            if (Convert.ToInt64(check.ExecuteScalar()) > 0)
                            {
                                throw new InvalidOperationException("Rollback found a null machine_id. Photo rows were not deleted.");
                            }
                        }

                        break;
                    case DropForeignKeyOperation:
                    case DropCheckConstraintOperation:
                    case AddForeignKeyOperation:
                        break;
                    default:
                        throw new InvalidOperationException($"Unhandled Down operation {operation.GetType().Name}.");
                }
            }

            transaction.Commit();
        }

        private static bool ParameterPhotosExist(SqliteConnection connection, System.Data.Common.DbTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "SELECT COUNT(*) FROM machine_parameter_photos WHERE parameter_id IS NOT NULL";
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }

        private static void Execute(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed class OverlappingPhotoUpdateInterceptor : DbCommandInterceptor
    {
        private int _armed;

        public int AssignmentUpdates { get; private set; }

        public List<string> AssignmentSql { get; } = new();

        public void Arm() => _armed = 1;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed == 1 && IsAssignmentUpdate(command.CommandText))
            {
                lock (AssignmentSql)
                {
                    AssignmentUpdates++;
                    AssignmentSql.Add(command.CommandText);
                }
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsAssignmentUpdate(string sql) =>
            sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("machine_parameter_photos", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UpdateCommandCounter : DbCommandInterceptor
    {
        public int UpdateCount { get; private set; }

        public void Reset() => UpdateCount = 0;

        public override InterceptionResult<int> NonQueryExecuting(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Count(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count(command.CommandText);
            return new ValueTask<InterceptionResult<int>>(result);
        }

        private void Count(string sql)
        {
            if (sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                UpdateCount++;
            }
        }
    }

    private sealed class CollectingLogger : ILogger<MachineParameterPhotoService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class ConcurrentSeedInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly Guid _tenantId;
        private int _fired;

        public ConcurrentSeedInterceptor(string connectionString, Guid tenantId)
        {
            _connectionString = connectionString;
            _tenantId = tenantId;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0 &&
                eventData.Context!.ChangeTracker.Entries<ParameterPhotoGroup>().Any(entry => entry.State == EntityState.Added))
            {
                var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
                    .UseSqlite(_connectionString)
                    .Options;
                using var other = new MaintTrackDbContext(options, new TenantContext { TenantId = _tenantId });
                ParameterPhotoCatalogSeeder.EnsureAsync(other, _tenantId, CancellationToken.None).GetAwaiter().GetResult();
                throw new DbUpdateException(
                    "UNIQUE constraint failed",
                    new Exception("UNIQUE constraint failed: parameter_photo_groups.tenant_id, parameter_photo_groups.name_key"));
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
