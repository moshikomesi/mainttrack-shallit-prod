using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.MorningRound;

public interface IMorningRoundService
{
    Task<MorningRoundDto?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<MorningRoundDto>> GetAsync(GetMorningRoundsRequest request, CancellationToken ct);

    Task<Guid> CreateAsync(CreateMorningRoundRequest request, CancellationToken ct);
}
