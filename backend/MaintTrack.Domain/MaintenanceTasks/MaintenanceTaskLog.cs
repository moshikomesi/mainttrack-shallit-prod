using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MaintenanceTasks;

public sealed class MaintenanceTaskLog : BaseEntity
{
    public Guid TenantId { get; set; }

    public DateTime TaskDate { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsConfirmed { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
