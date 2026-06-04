using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Maintenance;

public interface IMaintenanceEntryService
{
    Task<Guid> CreateAsync(CreateMaintenanceEntryRequest request, CancellationToken ct);

    Task UpdateAsync(Guid id, UpdateMaintenanceEntryRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<MaintenanceEntryDto>> GetAsync(GetMaintenanceEntriesRequest request, CancellationToken ct);

    Task<MaintenanceEntryDto?> GetByIdAsync(Guid id, CancellationToken ct);

    Task MigrateBase64ImagesAsync(CancellationToken ct);
}
