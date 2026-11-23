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
                ParameterPhotoCatalog.GanShomronGroupKey,
                ParameterPhotoCatalog.PackingHouseGroupKey
            },
            ParameterPhotoCatalog.Groups.Select(group => group.NameKey).ToArray());

        Assert.Equal(
            new[] { 1, 2, 3, 4 },
            ParameterPhotoCatalog.Groups.Select(group => group.SortOrder).ToArray());

        var washing = ParameterPhotoCatalog.Groups[0];
        Assert.Equal(
            new[] { ParameterPhotoCatalog.HopperKey, ParameterPhotoCatalog.WashingSystemComputerKey },
            washing.Parameters.Select(parameter => parameter.NameKey).ToArray());

        var cooling = ParameterPhotoCatalog.Groups[1];
        Assert.Equal(
            new[]
            {
                ParameterPhotoCatalog.CompressorRoomsKey,
                ParameterPhotoCatalog.ControlRoomAboveCoolingRoomsKey,
                ParameterPhotoCatalog.HumidityControllersKey
            },
            cooling.Parameters.Select(parameter => parameter.NameKey).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, cooling.Parameters.Select(parameter => parameter.SortOrder).ToArray());
        Assert.DoesNotContain(
            ParameterPhotoCatalog.RetiredGanShomronCoolingKey,
            cooling.Parameters.Select(parameter => parameter.NameKey));

        var ganShomron = ParameterPhotoCatalog.Groups[2];
        Assert.Equal(
            ParameterPhotoCatalog.GanShomronMachineKeys.Append(ParameterPhotoCatalog.BagPullersUnderViscoseKey),
            ganShomron.Parameters.Select(parameter => parameter.NameKey).ToArray());

        var packing = ParameterPhotoCatalog.Groups[3];
        Assert.Equal(
            ParameterPhotoCatalog.PackingHouseMachineKeys,
            packing.Parameters.Take(ParameterPhotoCatalog.PackingHouseMachineKeys.Length).Select(parameter => parameter.NameKey).ToArray());
        Assert.Equal("machine.viscose", packing.Parameters[^1].NameKey);
        Assert.Equal(28, packing.Parameters.Count);
        Assert.DoesNotContain(
            ParameterPhotoCatalog.BagPullersUnderViscoseKey,
            packing.Parameters.Select(parameter => parameter.NameKey));
    }

    [Fact]
    public async Task GetCatalog_ShowsGanShomron_WhenTheStoredCatalogPredatesThatGroup()
    {
        var washing = await _fx.SeedGroupAsync(_fx.TenantId, ParameterPhotoCatalog.WashingSystemGroupKey, 1);
        await _fx.SeedParameterAsync(_fx.TenantId, washing.Id, ParameterPhotoCatalog.HopperKey, 1);
        await _fx.SeedParameterAsync(_fx.TenantId, washing.Id, ParameterPhotoCatalog.WashingSystemComputerKey, 2);
        var cooling = await _fx.SeedGroupAsync(_fx.TenantId, ParameterPhotoCatalog.CoolingSystemGroupKey, 2);
        await _fx.SeedParameterAsync(_fx.TenantId, cooling.Id, ParameterPhotoCatalog.CompressorRoomsKey, 1);
        await _fx.SeedParameterAsync(_fx.TenantId, cooling.Id, ParameterPhotoCatalog.ControlRoomAboveCoolingRoomsKey, 2);
        await _fx.SeedParameterAsync(_fx.TenantId, cooling.Id, ParameterPhotoCatalog.HumidityControllersKey, 3);
        await _fx.SeedParameterAsync(_fx.TenantId, cooling.Id, ParameterPhotoCatalog.RetiredGanShomronCoolingKey, 4);
        var packing = await _fx.SeedGroupAsync(_fx.TenantId, ParameterPhotoCatalog.PackingHouseGroupKey, 3);
        var sort = 1;
        foreach (var nameKey in ParameterPhotoCatalog.PackingHouseMachineKeys)
        {
            await _fx.SeedParameterAsync(_fx.TenantId, packing.Id, nameKey, sort++);
        }

        await _fx.SeedParameterAsync(_fx.TenantId, packing.Id, ParameterPhotoCatalog.BagPullersUnderViscoseKey, sort);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.washing_system");
        var machine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.largeHopper", "H1", array.Id);

        var catalog = await _fx.Service.GetCatalogAsync(CancellationToken.None);

        Assert.Equal(
            new[]
            {
                ParameterPhotoCatalog.WashingSystemGroupKey,
                ParameterPhotoCatalog.CoolingSystemGroupKey,
                ParameterPhotoCatalog.GanShomronGroupKey,
                ParameterPhotoCatalog.PackingHouseGroupKey
            },
            catalog.Groups.Select(group => group.NameKey).ToArray());
        Assert.Equal(
            new[] { ParameterPhotoCatalog.HopperKey, ParameterPhotoCatalog.WashingSystemComputerKey },
            catalog.Groups[0].Parameters.Select(parameter => parameter.NameKey).ToArray());
        Assert.DoesNotContain(
            catalog.Groups[1].Parameters,
            parameter => parameter.NameKey == ParameterPhotoCatalog.RetiredGanShomronCoolingKey);
        Assert.Equal(
            ParameterPhotoCatalog.GanShomronMachineKeys.Append(ParameterPhotoCatalog.BagPullersUnderViscoseKey),
            catalog.Groups[2].Parameters.Select(parameter => parameter.NameKey).ToArray());
        Assert.Equal("machine.viscose", catalog.Groups[3].Parameters[^1].NameKey);
        Assert.Equal(28, catalog.Groups[3].Parameters.Count);
        Assert.DoesNotContain(
            catalog.Groups[3].Parameters,
            parameter => parameter.NameKey == ParameterPhotoCatalog.BagPullersUnderViscoseKey);

        var storedMachine = await _fx.DbContext.Machines.SingleAsync(item => item.Id == machine.Id);
        Assert.Equal("machine.largeHopper", storedMachine.Name);
        Assert.Equal(array.Id, storedMachine.ArrayId);
    }

    [Fact]
    public async Task Ensure_RetiresGanShomronCooling_WithoutChangingMachines()
    {
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var cooling = await _fx.DbContext.ParameterPhotoGroups.SingleAsync(group =>
            group.TenantId == _fx.TenantId && group.NameKey == ParameterPhotoCatalog.CoolingSystemGroupKey);
        await _fx.SeedParameterAsync(
            _fx.TenantId,
            cooling.Id,
            ParameterPhotoCatalog.RetiredGanShomronCoolingKey,
            sortOrder: 4);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.ginoshar");
        var machine = await _fx.SeedMachineAsync(
            _fx.TenantId,
            "machine.ginoshar.netPackingMachine",
            "GS1",
            array.Id);
        var machineId = machine.Id;
        var arrayId = array.Id;

        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var catalog = await _fx.Service.GetCatalogAsync(CancellationToken.None);

        var retired = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId &&
            parameter.NameKey == ParameterPhotoCatalog.RetiredGanShomronCoolingKey);
        Assert.False(retired.IsActive);
        Assert.Equal(cooling.Id, retired.GroupId);

        var ganShomron = Assert.Single(catalog.Groups, group => group.NameKey == ParameterPhotoCatalog.GanShomronGroupKey);
        Assert.Equal(
            ParameterPhotoCatalog.GanShomronMachineKeys.Append(ParameterPhotoCatalog.BagPullersUnderViscoseKey),
            ganShomron.Parameters.Select(parameter => parameter.NameKey).ToArray());
        Assert.DoesNotContain(
            catalog.Groups.SelectMany(group => group.Parameters),
            parameter => parameter.NameKey == ParameterPhotoCatalog.RetiredGanShomronCoolingKey);

        var storedMachine = await _fx.DbContext.Machines.SingleAsync(item => item.Id == machineId);
        var storedArray = await _fx.DbContext.Arrays.SingleAsync(item => item.Id == arrayId);
        Assert.Equal("machine.ginoshar.netPackingMachine", storedMachine.Name);
        Assert.Equal(arrayId, storedMachine.ArrayId);
        Assert.Equal("array.ginoshar", storedArray.NameKey);
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
        Assert.Contains("ParameterPhotoLegacyLinker.CompressorRoomsLinkSql", migration, StringComparison.Ordinal);
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
        Assert.DoesNotContain(
            $"'{ParameterPhotoCatalog.RetiredGanShomronCoolingKey}'",
            migration,
            StringComparison.Ordinal);
        Assert.Contains("'array.ginoshar'", migration, StringComparison.Ordinal);
        Assert.Contains("'machine.ginoshar.netPackingMachine'", migration, StringComparison.Ordinal);
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

    [Fact]
    public async Task CompressorRoomsLink_ClearsHighSortsOnTheSourceMachines()
    {
        await AssertCompressorLinkClearsBothIndexes(
            otherParameterSort: 120_000,
            compressorRoomsSort: 80_000);
    }

    [Fact]
    public async Task CompressorRoomsLink_ClearsHighSortsAlreadyOnCompressorRooms()
    {
        await AssertCompressorLinkClearsBothIndexes(
            otherParameterSort: 70_000,
            compressorRoomsSort: 130_000);
    }

    private async Task AssertCompressorLinkClearsBothIndexes(int otherParameterSort, int compressorRoomsSort)
    {
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        var compressorRooms = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId &&
            parameter.NameKey == ParameterPhotoCatalog.CompressorRoomsKey);
        var otherParameter = await _fx.DbContext.ParameterPhotoParameters.SingleAsync(parameter =>
            parameter.TenantId == _fx.TenantId &&
            parameter.NameKey == ParameterPhotoCatalog.ControlRoomAboveCoolingRoomsKey);
        var array = await _fx.SeedArrayAsync(_fx.TenantId, "array.rooms");
        var counts = new Dictionary<string, int>
        {
            ["machine.rooms.compressor1"] = 9,
            ["machine.rooms.compressor2"] = 9,
            ["machine.rooms.compressor3"] = 9,
            ["machine.rooms.compressor4"] = 9,
            ["machine.waterCooling.compressor1"] = 1,
            ["machine.waterCooling.compressor2"] = 1,
            ["machine.waterCooling.compressor3"] = 1,
            ["machine.waterCooling.compressor4"] = 1
        };
        var originals = new List<(Guid Id, string MachineName, Guid MachineId, int SortOrder, string ImageUrl, Guid CreatedByUserId)>();
        foreach (var (machineName, photoCount) in counts)
        {
            var machine = await _fx.SeedMachineAsync(_fx.TenantId, machineName, machineName, array.Id);
            for (var index = 0; index < photoCount; index++)
            {
                var sortOrder = 25_000 + (index * 1_000);
                var photo = await _fx.SeedPhotoAsync(
                    _fx.TenantId,
                    parameterId: null,
                    sortOrder,
                    machine.Id,
                    imageUrl: $"/uploads/{machineName}-{sortOrder}.jpg");
                originals.Add((photo.Id, machineName, machine.Id, sortOrder, photo.ImageUrl, photo.CreatedByUserId));
            }
        }

        var sourceMachine = await _fx.DbContext.Machines.SingleAsync(machine =>
            machine.Name == "machine.rooms.compressor1");
        var linkedElsewhere = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            otherParameter.Id,
            otherParameterSort,
            sourceMachine.Id,
            imageUrl: "/uploads/linked-elsewhere.jpg");
        var compressorRoomsMachine = await _fx.DbContext.Machines.SingleAsync(machine =>
            machine.Name == "machine.rooms.compressor2");
        var alreadyOnCompressorRooms = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            compressorRooms.Id,
            compressorRoomsSort,
            compressorRoomsMachine.Id,
            imageUrl: "/uploads/already-on-compressor-rooms.jpg");
        var unrelatedMachine = await _fx.SeedMachineAsync(_fx.TenantId, "machine.rooms.controlRoom", "C1", array.Id);
        var unrelatedPhoto = await _fx.SeedPhotoAsync(
            _fx.TenantId,
            parameterId: null,
            sortOrder: 77_777,
            unrelatedMachine.Id,
            imageUrl: "/uploads/control-room.jpg");

        var floor = new[]
        {
            otherParameterSort,
            compressorRoomsSort,
            originals.Max(photo => photo.SortOrder)
        }.Max();

        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        _fx.DbContext.ChangeTracker.Clear();

        var photos = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().ToListAsync();
        Assert.Equal(43, photos.Count);
        var linked = originals
            .Select(original => photos.Single(photo => photo.Id == original.Id))
            .OrderBy(photo => photo.SortOrder)
            .ToList();
        Assert.Equal(40, linked.Count);
        Assert.All(linked, photo => Assert.Equal(compressorRooms.Id, photo.ParameterId));
        Assert.Equal(Enumerable.Range(floor + 1, 40), linked.Select(photo => photo.SortOrder));
        var expectedOrder = originals
            .OrderBy(photo => photo.MachineName, StringComparer.Ordinal)
            .ThenBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.Id)
            .Select(photo => photo.Id)
            .ToList();
        Assert.Equal(expectedOrder, linked.Select(photo => photo.Id));
        foreach (var original in originals)
        {
            var stored = photos.Single(photo => photo.Id == original.Id);
            Assert.Equal(original.MachineId, stored.MachineId);
            Assert.Equal(original.ImageUrl, stored.ImageUrl);
            Assert.Equal(original.CreatedByUserId, stored.CreatedByUserId);
            Assert.Equal(_fx.TenantId, stored.TenantId);
        }

        var preservedElsewhere = photos.Single(photo => photo.Id == linkedElsewhere.Id);
        Assert.Equal(otherParameter.Id, preservedElsewhere.ParameterId);
        Assert.Equal(otherParameterSort, preservedElsewhere.SortOrder);
        Assert.Equal("/uploads/linked-elsewhere.jpg", preservedElsewhere.ImageUrl);
        Assert.Equal(sourceMachine.Id, preservedElsewhere.MachineId);

        var preservedRooms = photos.Single(photo => photo.Id == alreadyOnCompressorRooms.Id);
        Assert.Equal(compressorRooms.Id, preservedRooms.ParameterId);
        Assert.Equal(compressorRoomsSort, preservedRooms.SortOrder);
        Assert.Equal("/uploads/already-on-compressor-rooms.jpg", preservedRooms.ImageUrl);
        Assert.Equal(compressorRoomsMachine.Id, preservedRooms.MachineId);

        var untouched = photos.Single(photo => photo.Id == unrelatedPhoto.Id);
        Assert.Null(untouched.ParameterId);
        Assert.Equal(unrelatedMachine.Id, untouched.MachineId);
        Assert.Equal("/uploads/control-room.jpg", untouched.ImageUrl);
        Assert.Equal(77_777, untouched.SortOrder);

        AssertUniquePhotoSortIndexes(photos);

        var firstPass = photos.ToDictionary(
            photo => photo.Id,
            photo => (photo.ParameterId, photo.SortOrder, photo.MachineId, photo.ImageUrl, photo.CreatedByUserId, photo.TenantId));
        await ParameterPhotoCatalogSeeder.EnsureAsync(_fx.DbContext, _fx.TenantId, CancellationToken.None);
        _fx.DbContext.ChangeTracker.Clear();
        var secondPass = await _fx.DbContext.MachineParameterPhotos.AsNoTracking().ToListAsync();
        Assert.Equal(firstPass.Count, secondPass.Count);
        foreach (var photo in secondPass)
        {
            var before = firstPass[photo.Id];
            Assert.Equal(before.ParameterId, photo.ParameterId);
            Assert.Equal(before.SortOrder, photo.SortOrder);
            Assert.Equal(before.MachineId, photo.MachineId);
            Assert.Equal(before.ImageUrl, photo.ImageUrl);
            Assert.Equal(before.CreatedByUserId, photo.CreatedByUserId);
            Assert.Equal(before.TenantId, photo.TenantId);
        }

        AssertUniquePhotoSortIndexes(secondPass);
    }

    private static void AssertUniquePhotoSortIndexes(IReadOnlyCollection<MachineParameterPhoto> photos)
    {
        var parameterSorts = photos
            .Where(photo => photo.ParameterId is not null)
            .Select(photo => (photo.ParameterId, photo.SortOrder))
            .ToList();
        Assert.Equal(parameterSorts.Count, parameterSorts.Distinct().Count());

        var machineSorts = photos
            .Where(photo => photo.MachineId is not null)
            .Select(photo => (photo.MachineId, photo.SortOrder))
            .ToList();
        Assert.Equal(machineSorts.Count, machineSorts.Distinct().Count());
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
