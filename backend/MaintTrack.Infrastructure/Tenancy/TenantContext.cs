using System;
using MaintTrack.Application.Abstractions;

namespace MaintTrack.Infrastructure.Tenancy;

/// <summary>
/// Scoped implementation of <see cref="ITenantContext" /> used to flow the current tenant id
/// into the EF Core DbContext and other infrastructure components.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
}

