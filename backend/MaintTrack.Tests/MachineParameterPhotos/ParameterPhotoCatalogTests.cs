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
        var migrationPath = FindMigrationFile();
        var migration = File.ReadAllText(migrationPath);

        foreach (var nameKey in ParameterPhotoCatalog.AllNameKeys())
        {
            Assert.Contains($"'{nameKey}'", migration, StringComparison.Ordinal);
        }

        Assert.Contains("machine.name = parameter.name_key", migration, StringComparison.Ordinal);
        Assert.Contains("SET parameter_id =", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM machine_parameter_photos\n                WHERE parameter_id", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("maintenance_tasks", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("annual_plan_tasks", migration, StringComparison.Ordinal);
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

        await _fx.DbContext.Database.ExecuteSqlRawAsync(ParameterPhotoLegacyLinker.LinkSql);

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
        Assert.Equal("/uploads/onion-hopper.jpg", onionPhoto.ImageUrl);
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
}
