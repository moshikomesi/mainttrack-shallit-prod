namespace MaintTrack.Application.Clearance;

/// <summary>
/// Filter for the Clearance queue. Tenant is always taken from the authenticated context.
/// </summary>
public sealed class ClearanceQueueQuery
{
    /// <summary>pending, approved or rejected. Null returns all statuses.</summary>
    public string? Status { get; set; }

    /// <summary>Matches machines.array_id, the same grouping used by the hierarchy service.</summary>
    public Guid? ArrayId { get; set; }

    public Guid? MachineId { get; set; }

    public int? PageNumber { get; set; }

    public int? PageSize { get; set; }
}
