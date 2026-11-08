using MaintTrack.Domain.Common;
using MaintTrack.Domain.Users;

namespace MaintTrack.Domain.Clearance;

/// <summary>
/// Explicit assignment: a tenant user who may approve or reject Clearance items.
/// Not derived from role. Maps to <c>clearance_approvers</c>.
/// </summary>
public sealed class ClearanceApprover : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }
}
