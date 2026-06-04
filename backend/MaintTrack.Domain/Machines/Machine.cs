using System;
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
}

