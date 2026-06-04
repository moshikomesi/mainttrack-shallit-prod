using System;

namespace MaintTrack.Domain.Common;

/// <summary>
/// Base type for all entities.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
}

/// <summary>
/// Base type for entities that belong to a tenant.
/// </summary>
public abstract class TenantEntity : BaseEntity
{
    public Guid TenantId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

