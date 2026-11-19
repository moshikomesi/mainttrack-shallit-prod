using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Infrastructure.MachineParameterPhotos;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.MachineParameterPhotos;

public sealed class ParameterPhotoCatalogTests : IAsyncDisposable
{
    private readonly MachineParameterPhotoServiceFixture _fx = new();

    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public void Catalog_DefinesTheRequiredGroupsAndParameters()
    {
        Assert.Equal(
            new[]
            {
                ParameterPhotoCatalog.WashingSystemGroupKey,
                ParameterPhotoCatalog.CoolingSystemGroupKey,
                ParameterPhotoCatalog.PackingHouseGroupKey
            },
            ParameterPhotoCatalog.Groups.Select(group => group.NameKey).ToArray());

        Assert.Equal(
            new[] { 1, 2, 3 },
            ParameterPhotoCatalog.Groups.Select(group => group.SortOrder).ToArray());

        var washing = ParameterPhotoCatalog.Groups[0];
        Assert.Equal(
            new[] { ParameterPhotoCatalog.HopperKey, ParameterPhotoCatalog.CoolingSystemComputerKey },
            washing.Parameters.Select(parameter => parameter.NameKey).ToArray());

        var cooling = ParameterPhotoCatalog.Groups[1];
        Assert.Equal(
            new[]
            {
                ParameterPhotoCatalog.CompressorRoomsKey,
                ParameterPhotoCatalog.ControlRoomAboveCoolingRoomsKey,
                ParameterPhotoCatalog.HumidityControllersKey,
                ParameterPhotoCatalog.GanShomronCoolingKey
            },
            cooling.Parameters.Select(parameter => parameter.NameKey).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, cooling.Parameters.Select(parameter => parameter.SortOrder).ToArray());

        var packing = ParameterPhotoCatalog.Groups[2];
        Assert.Equal(
            ParameterPhotoCatalog.PackingHouseMachineKeys,
            packing.Parameters.Take(ParameterPhotoCatalog.PackingHouseMachineKeys.Length).Select(parameter => parameter.NameKey).ToArray());
        Assert.Equal(ParameterPhotoCatalog.BagPullersUnderViscoseKey, packing.Parameters[^1].NameKey);
        Assert.Equal(29, packing.Parameters[^1].SortOrder);
    }

    [Fact]
    public void Migration_SeedsEveryCatalogKeyAndUsesTheExplicitPhotoLink()
    {
        var migration = File.ReadAllText(FindMigrationFile());
        var linker = File.ReadAllText(FindSourceFile("ParameterPhotoLegacyLinker.cs"));
        var guard = File.ReadAllText(FindSourceFile("ParameterPhotoRollbackGuard.cs"));

        foreach (var nameKey in ParameterPhotoCatalog.AllNameKeys())
        {
            Assert.Contains($"'{nameKey}'", migration, StringComparison.Ordinal);
        }

        Assert.Contains("ParameterPhotoLegacyLinker.MigrationLinkSql", migration, StringComparison.Ordinal);
        Assert.Contains("ParameterPhotoRollbackGuard.Sql", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM machine_parameter_photos", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("maintenance_tasks", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("annual_plan_tasks", migration, StringComparison.Ordinal);
        Assert.Contains("machine.name = parameter.name_key", linker, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)", linker, StringComparison.Ordinal);
        Assert.Contains("'machine.largeHopper'", linker, StringComparison.Ordinal);
        Assert.Contains("'machine.smallHopper'", linker, StringComparison.Ordinal);
        Assert.Contains("'machine.onion.hopper'", linker, StringComparison.Ordinal);
        Assert.Contains("'parameterPhoto.parameter.hopper'", linker, StringComparison.Ordinal);
        Assert.Contains(ParameterPhotoRollbackGuard.Message, guard, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", guard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyLink_PreservesPackingHousePhotos_AndDoesNotInventHopperMappings()
    {
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var packing = await _fx.DbContext.ParameterPhotoParameters
            .SingleAsync(parameter =>
                parameter.TenantId == _fx.TenantId &&
                parameter.NameKey == "machine.viscose");
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.packing_house");
        var viscose = await _fx.SeedMachineAsync(_fx.TenantId, "machine.viscose", "V1", array.Id);
        var largeHopper = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);
        var smallHopper = await _fx.SeedMachineAsync(_fx.TenantId, "machine.smallHopper", "H2", array.Id);
        var onionArray = await _fx.SeedArrayAsync(_fx.OtherTenantId, "array.onion_system");
        var onionHopper = await _fx.SeedMachineAsync(_fx.TenantId, "machine.onion.hopper", "O1", onionArray.Id);

        var linked = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            parameterId: null,
            sortOrder: 4,
            machineId: viscose.Id,
            imageUrl: "/uploads/viscose.jpg");
        var unlinkedHopper = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            parameterId: null,
            sortOrder: 1,
            machineId: largeHopper.Id,
            imageUrl: "/uploads/large-hopper.jpg");
        var unlinkedOnion = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            parameterId: null,
            sortOrder: 2,
            machineId: onionHopper.Id,
            imageUrl: "/uploads/onion-hopper.jpg");
        var unlinkedSmall = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            parameterId: null,
            sortOrder: 3,
            machineId: smallHopper.Id,
            imageUrl: "/uploads/small-hopper.jpg");

        await ParameterPhotoLegacyLinker.LinkAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        await ParameterPhotoLegacyLinker.LinkAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);

        var photos = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().ToListAsync();
        var viscosePhoto = photos.Single(photo => photo.Id == linked.Id);
        Assert.Equal(packing.Id, viscosePhoto.ParameterId);
        Assert.Equal(viscose.Id, viscosePhoto.MachineId);
        Assert.Equal("/uploads/viscose.jpg", viscosePhoto.ImageUrl);
        Assert.Equal(4, viscosePhoto.SortOrder);

        var hopperPhoto = photos.Single(photo => photo.Id == unlinkedHopper.Id);
        Assert.Null(hopperPhoto.ParameterId);
        Assert.Equal(largeHopper.Id, hopperPhoto.MachineId);
        Assert.Equal("/uploads/large-hopper.jpg", hopperPhoto.ImageUrl);

        var onionPhoto = photos.Single(photo => photo.Id == unlinkedOnion.Id);
        Assert.Null(onionPhoto.ParameterId);
        Assert.Equal(onionHopper.Id, onionPhoto.MachineId);
        Assert.Equal("/uploads/onion-hopper.jpg", onionPhoto.ImageUrl);

        var smallPhoto = photos.Single(photo => photo.Id == unlinkedSmall.Id);
        Assert.Null(smallPhoto.ParameterId);
        Assert.Equal(smallHopper.Id, smallPhoto.MachineId);
        Assert.Equal("/uploads/small-hopper.jpg", smallPhoto.ImageUrl);
        Assert.Equal(4, photos.Count);

        var hopperParameter = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId &&
            parameter.NameKey == ParameterPhotoCatalog.HopperKey);
        Assert.DoesNotContain(photos, photo => photo.ParameterId == hopperParameter.Id);
    }

    private static string FindMigrationFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "backend",
                "MaintTrack.Infrastructure",
                "Migrations");
            if (Directory.Exists(candidate))
            {
                var match = Directory.GetFiles(candidate, "*AddIndependentParameterPhotoCatalog.cs")
                    .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
                return match;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Parameter photo catalog migration file was not found.");
    }

    private static string FindSourceFile(string fileName)
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
}
