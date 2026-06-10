using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MaintenanceTasks;
using MaintTrack.Domain.MaintenanceTasks;

namespace MaintTrack.Infrastructure.MaintenanceTasks;

public sealed class MaintenanceTasksService : IMaintenanceTasksService
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IMaintenanceTasksRepository _repository;

    public MaintenanceTasksService(
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IMaintenanceTasksRepository repository)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<Guid> CreateAsync(CreateMaintenanceTaskRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            throw new InvalidOperationException("Image is required.");

        if (!request.IsConfirmed)
            throw new InvalidOperationException("Confirmation is required.");

        var now = DateTime.UtcNow;
        var task = new MaintenanceTaskLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId.Value,
            TaskDate = now,
            ImageUrl = request.ImageUrl.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim(),
            IsConfirmed = request.IsConfirmed,
            CreatedByUserId = _currentUserContext.UserId,
            CreatedAt = now
        };

        await _repository.AddAsync(task, ct);
        await _repository.SaveChangesAsync(ct);

        return task.Id;
    }

    public async Task<IReadOnlyList<MaintenanceTaskDto>> GetAsync(CancellationToken ct)
    {
        var tasks = await _repository.GetAsync(ct);
        return tasks
            .Select(task => new MaintenanceTaskDto(
                task.Id,
                task.TaskDate,
                task.ImageUrl,
                task.Description,
                task.IsConfirmed,
                task.CreatedByUserId,
                task.TenantId,
                task.CreatedAt))
            .ToList();
    }
}
