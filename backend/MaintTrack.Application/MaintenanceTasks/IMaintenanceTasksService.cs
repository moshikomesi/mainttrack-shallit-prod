namespace MaintTrack.Application.MaintenanceTasks;

public interface IMaintenanceTasksService
{
    Task<Guid> CreateAsync(CreateMaintenanceTaskRequest request, CancellationToken ct);

    Task<IReadOnlyList<MaintenanceTaskDto>> GetAsync(CancellationToken ct);
}
