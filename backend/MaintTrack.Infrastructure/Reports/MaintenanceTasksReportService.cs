using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Reports.MaintenanceTasks;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Reports;

public sealed class MaintenanceTasksReportService : IMaintenanceTasksReportService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public MaintenanceTasksReportService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IReadOnlyList<MaintenanceTaskReportDto>> GetAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return Array.Empty<MaintenanceTaskReportDto>();

        var query =
            from task in _dbContext.MaintenanceTaskLogs.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking()
                on task.CreatedByUserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            orderby task.TaskDate descending, task.CreatedAt descending
            select new MaintenanceTaskReportDto(
                task.Id,
                task.TaskDate,
                task.ImageUrl,
                task.Description,
                task.IsConfirmed,
                task.CreatedByUserId,
                user == null
                    ? task.CreatedByUserId.ToString()
                    : string.IsNullOrWhiteSpace(user.DisplayName)
                        ? user.Username
                        : user.DisplayName,
                task.TenantId,
                task.CreatedAt);

        return await query.ToListAsync(ct);
    }
}
