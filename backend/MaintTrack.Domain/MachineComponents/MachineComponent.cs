using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MachineComponents;

/// <summary>
/// Global per-tenant catalog of machine components for Maintenance Log v2.
/// Display text is resolved via <see cref="NameKey"/> on the frontend.
/// </summary>
public sealed class MachineComponent : TenantEntity
{
    public string Code { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
