using MaintTrack.Domain.Common;
using MaintTrack.Domain.Machines;

namespace MaintTrack.Domain.MachineComponents;

/// <summary>
/// Maps which components belong to each machine for Maintenance Log v2.
/// </summary>
public sealed class MachineComponentMapping : TenantEntity
{
    public Guid MachineId { get; set; }

    public Machine? Machine { get; set; }

    public Guid ComponentId { get; set; }

    public MachineComponent? Component { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
