using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Hierarchy;

public interface IHierarchyService
{
    Task<IReadOnlyList<HierarchyArrayDto>> GetHierarchyAsync(CancellationToken ct);
}
