namespace MaintTrack.Application.MaintenanceTasks;

public sealed record MaintenanceTaskDto(
    Guid Id,
    DateTime TaskDate,
    string ImageUrl,
    string? Description,
    bool IsConfirmed,
    Guid CreatedByUserId,
    Guid TenantId,
    DateTime CreatedAt);
