using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Maintenance;

public interface IMaintenanceTypeService
{
    Task<IReadOnlyList<MaintenanceTypeDto>> GetAllAsync(CancellationToken ct);
}
