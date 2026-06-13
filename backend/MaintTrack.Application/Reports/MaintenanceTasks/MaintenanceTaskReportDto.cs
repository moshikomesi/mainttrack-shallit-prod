namespace MaintTrack.Application.Reports.MaintenanceTasks;

public sealed record MaintenanceTaskReportDto(
    Guid Id,
    DateTime TaskDate,
    string ImageUrl,
    string? Description,
    bool IsConfirmed,
    Guid CreatedByUserId,
    string CreatedByUserName,
    Guid TenantId,
    DateTime CreatedAt);
