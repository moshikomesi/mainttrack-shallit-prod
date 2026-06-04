using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Forklifts;

public interface IForkliftService
{
    Task<Guid> CreateAsync(CreateForkliftRequest request, CancellationToken ct);

    Task<IReadOnlyList<ForkliftDto>> GetAsync(CancellationToken ct);

    Task<ForkliftDto?> GetByIdAsync(Guid id, CancellationToken ct);

    Task UpdateAsync(Guid id, UpdateForkliftRequest request, CancellationToken ct);
}
