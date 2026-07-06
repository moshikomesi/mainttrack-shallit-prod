using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.MachineComponents;

public interface IMachineComponentService
{
    Task<IReadOnlyList<MachineComponentDto>> GetByMachineIdAsync(Guid machineId, CancellationToken ct);
}
