using MaintTrack.Domain.MaintenanceTasks;

namespace MaintTrack.Application.MaintenanceTasks;

public interface IMaintenanceTasksRepository
{
    Task AddAsync(MaintenanceTaskLog task, CancellationToken ct);

    Task<IReadOnlyList<MaintenanceTaskLog>> GetAsync(CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
