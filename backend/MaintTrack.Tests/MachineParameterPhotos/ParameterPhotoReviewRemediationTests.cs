using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
