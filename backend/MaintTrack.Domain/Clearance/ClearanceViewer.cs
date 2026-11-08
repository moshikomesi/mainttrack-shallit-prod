using MaintTrack.Domain.Common;
using MaintTrack.Domain.Users;

namespace MaintTrack.Domain.Clearance;

/// <summary>
/// Explicit assignment: a tenant user who may view Clearance information (queue, status,
/// decision details) without deciding. Not derived from role. Maps to <c>clearance_viewers</c>.
/// </summary>
public sealed class ClearanceViewer : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }
}
