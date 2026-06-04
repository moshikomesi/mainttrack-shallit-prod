using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Audit;

/// <summary>
/// Records an audit event for a tenant (e.g. who did what to which entity).
/// </summary>
public class AuditLog : TenantEntity
{
    public Guid UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public Guid EntityId { get; set; }
}
