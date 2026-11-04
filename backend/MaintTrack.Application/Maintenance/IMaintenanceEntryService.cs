using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Maintenance;

/// <summary>
/// Submitted maintenance reports are immutable: there is deliberately no update operation.
/// </summary>
public interface IMaintenanceEntryService
{
    Task<Guid> CreateAsync(CreateMaintenanceEntryRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<MaintenanceEntryDto>> GetAsync(GetMaintenanceEntriesRequest request, CancellationToken ct);

    Task<MaintenanceEntryDto?> GetByIdAsync(Guid id, CancellationToken ct);
}
