using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Maintenance;

public sealed class MaintenanceEntryImage : TenantEntity
{
    public Guid MaintenanceEntryId { get; set; }

    public MaintenanceEntry MaintenanceEntry { get; set; } = null!;

    public string ImageUrl { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
