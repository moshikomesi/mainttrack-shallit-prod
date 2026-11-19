using System.Collections.Generic;
using System.Linq;

namespace MaintTrack.Domain.MachineParameterPhotos;

public sealed record ParameterPhotoParameterDefinition(string NameKey, int SortOrder);

public sealed record ParameterPhotoGroupDefinition(
    string NameKey,
    int SortOrder,
    IReadOnlyList<ParameterPhotoParameterDefinition> Parameters);

/// <summary>
/// Canonical Parameter Photos catalog. Packing House entries reuse the shared
/// machine translation keys so existing labels and photo associations stay
/// aligned. A photo is linked only when <c>machines.name</c> equals the
/// parameter <c>name_key</c> in the same tenant. Hopper, the cooling-system
/// computer, the Cooling System parameters, and the new bag-puller parameter
/// have no unambiguous machine match, so they are catalog-only.
/// </summary>
public static class ParameterPhotoCatalog
{
    public const string WashingSystemGroupKey = "array.washing_system";
    public const string CoolingSystemGroupKey = "parameterPhoto.group.coolingSystem";
    public const string PackingHouseGroupKey = "array.packing_house";

    public const string HopperKey = "parameterPhoto.parameter.hopper";
    public const string CoolingSystemComputerKey = "parameterPhoto.parameter.coolingSystemComputer";
    public const string CompressorRoomsKey = "parameterPhoto.parameter.compressorRooms";
    public const string ControlRoomAboveCoolingRoomsKey = "parameterPhoto.parameter.controlRoomAboveCoolingRooms";
    public const string HumidityControllersKey = "parameterPhoto.parameter.humidityControllers";
    public const string GanShomronCoolingKey = "parameterPhoto.parameter.ganShomronCooling";
    public const string BagPullersUnderViscoseKey = "parameterPhoto.parameter.bagPullersUnderViscose";

    /// <summary>
    /// Active Packing House machines from the hierarchy map, in display order.
    /// These name keys are the explicit legacy photo mapping.
    /// </summary>
    public static readonly string[] PackingHouseMachineKeys =
    [
        "machine.sorter1",
        "machine.sorter2",
        "machine.sorter3",
        "machine.packing.poolElevators",
        "machine.packing.conveyorSystem",
        "machine.packing.fastVerbrocken",
        "machine.packing.verbrocken",
        "machine.packing.bulkFiller",
        "machine.packing.metalDetector",
        "machine.scale1",
        "machine.packing1",
        "machine.scale2",
        "machine.packing2",
        "machine.scale3",
        "machine.packing3",
        "machine.scale4",
        "machine.packing4",
        "machine.scale5",
        "machine.packing5a",
        "machine.packing5b",
        "machine.sorterLine5",
        "machine.scale6",
        "machine.packing6a",
        "machine.packing6b",
        "machine.sorterLine6",
        "machine.masters",
        "machine.accumulatorOutside",
        "machine.viscose"
    ];

    public static IReadOnlyList<ParameterPhotoGroupDefinition> Groups { get; } = BuildGroups();

    public static IEnumerable<string> AllNameKeys()
    {
        foreach (var group in Groups)
        {
            yield return group.NameKey;
            foreach (var parameter in group.Parameters)
            {
                yield return parameter.NameKey;
            }
        }
    }

    private static IReadOnlyList<ParameterPhotoGroupDefinition> BuildGroups()
    {
        var packingParameters = PackingHouseMachineKeys
            .Select((nameKey, index) => new ParameterPhotoParameterDefinition(nameKey, index + 1))
            .Append(new ParameterPhotoParameterDefinition(
                BagPullersUnderViscoseKey,
                PackingHouseMachineKeys.Length + 1))
            .ToList();

        return
        [
            new ParameterPhotoGroupDefinition(
                WashingSystemGroupKey,
                1,
                [
                    new ParameterPhotoParameterDefinition(HopperKey, 1),
                    new ParameterPhotoParameterDefinition(CoolingSystemComputerKey, 2)
                ]),
            new ParameterPhotoGroupDefinition(
                CoolingSystemGroupKey,
                2,
                [
                    new ParameterPhotoParameterDefinition(CompressorRoomsKey, 1),
                    new ParameterPhotoParameterDefinition(ControlRoomAboveCoolingRoomsKey, 2),
                    new ParameterPhotoParameterDefinition(HumidityControllersKey, 3),
                    new ParameterPhotoParameterDefinition(GanShomronCoolingKey, 4)
                ]),
            new ParameterPhotoGroupDefinition(
                PackingHouseGroupKey,
                3,
                packingParameters)
        ];
    }
}
