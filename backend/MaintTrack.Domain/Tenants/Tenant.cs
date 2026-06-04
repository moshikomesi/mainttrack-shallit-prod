using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Tenants;

/// <summary>
/// Represents a factory (tenant) in the system.
/// Note: This is a global entity and does not itself have a tenant_id column.
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public DateTime CreatedAt { get; set; }
}

