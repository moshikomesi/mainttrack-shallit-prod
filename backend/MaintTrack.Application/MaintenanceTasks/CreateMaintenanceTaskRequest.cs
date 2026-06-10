namespace MaintTrack.Application.MaintenanceTasks;

public sealed class CreateMaintenanceTaskRequest
{
    public string ImageUrl { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsConfirmed { get; init; }
}
