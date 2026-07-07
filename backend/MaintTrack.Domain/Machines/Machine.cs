using System;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Machines;

/// <summary>
/// Represents a machine in a tenant's factory.
/// </summary>
public class Machine : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Display order within its array, per the factory's reference hierarchy map.
    /// Machines with equal SortOrder fall back to alphabetical Name ordering.
    /// </summary>
    public int SortOrder { get; set; }

    public Guid? ArrayId { get; set; }

    public WorkGroup? Array { get; set; }
}

