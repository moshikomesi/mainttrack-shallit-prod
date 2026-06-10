using MaintTrack.Application.MaintenanceTasks;
using MaintTrack.Domain.MaintenanceTasks;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MaintenanceTasks;

public sealed class MaintenanceTasksRepository : IMaintenanceTasksRepository
{
    private readonly MaintTrackDbContext _dbContext;

    public MaintenanceTasksRepository(MaintTrackDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(MaintenanceTaskLog task, CancellationToken ct)
    {
        await _dbContext.MaintenanceTaskLogs.AddAsync(task, ct);
    }

    public async Task<IReadOnlyList<MaintenanceTaskLog>> GetAsync(CancellationToken ct)
    {
        return await _dbContext.MaintenanceTaskLogs
            .AsNoTracking()
            .OrderByDescending(task => task.TaskDate)
            .ThenByDescending(task => task.CreatedAt)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _dbContext.SaveChangesAsync(ct);
    }
}
