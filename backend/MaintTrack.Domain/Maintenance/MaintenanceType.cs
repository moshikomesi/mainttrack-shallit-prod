using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Maintenance;

public class MaintenanceType : TenantEntity
{
    public string Code { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
