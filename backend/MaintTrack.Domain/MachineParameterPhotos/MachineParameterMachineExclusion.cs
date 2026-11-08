using MaintTrack.Domain.Common;
using MaintTrack.Domain.Machines;

namespace MaintTrack.Domain.MachineParameterPhotos;

/// <summary>
/// Hides a machine from Machine Parameter Photos only. A row means hidden;
/// absence of a row means visible. Maps to <c>machine_parameter_machine_visibility</c>.
/// </summary>
public sealed class MachineParameterMachineExclusion : TenantEntity
{
    public Guid MachineId { get; set; }

    public Machine? Machine { get; set; }
}
