namespace MaintTrack.Application.Reports.MaintenanceTasks;

public interface IMaintenanceTasksReportService
{
    Task<IReadOnlyList<MaintenanceTaskReportDto>> GetAsync(CancellationToken ct);
}
