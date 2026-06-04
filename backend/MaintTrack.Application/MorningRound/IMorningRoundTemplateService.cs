using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.MorningRound;

public interface IMorningRoundTemplateService
{
    Task<IReadOnlyList<MorningRoundTemplateItemDto>> GetTemplateAsync(CancellationToken ct);
}

