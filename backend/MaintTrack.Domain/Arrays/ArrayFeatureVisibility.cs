using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Arrays;

/// <summary>
/// Per-feature Array visibility override. Absence of a row means visible.
/// Maps to the <c>array_feature_visibility</c> table.
/// </summary>
public sealed class ArrayFeatureVisibility : TenantEntity
{
    public const string MachineParameterPhotosFeatureKey = "machine_parameter_photos";

    public Guid ArrayId { get; set; }

    public WorkGroup? Array { get; set; }

    public string FeatureKey { get; set; } = string.Empty;

    public bool IsVisible { get; set; }
}
