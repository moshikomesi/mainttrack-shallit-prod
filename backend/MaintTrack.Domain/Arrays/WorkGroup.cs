using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Arrays;

/// <summary>
/// Work group (array) — root organizational level for machines.
/// Maps to the <c>arrays</c> table.
/// </summary>
public class WorkGroup : TenantEntity
{
    public string NameKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsMorningRoundEnabled { get; set; }
}
