using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Machines;

public interface IMachineService
{
    Task<IReadOnlyList<MachineDto>> GetAllAsync(CancellationToken ct);

    Task<Guid> CreateAsync(CreateMachineRequest request, CancellationToken ct);
}

