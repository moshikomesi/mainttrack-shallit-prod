using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Clearance;

/// <summary>
/// The single definition of a Clearance approver: a user who is active in the tenant and has a
/// clearance_approvers assignment in that same tenant. Roles never grant approver rights.
/// </summary>
internal static class ClearanceApproverRules
{
    public static IQueryable<User> ActiveApprovers(MaintTrackDbContext dbContext, Guid tenantId) =>
        dbContext.Users.Where(u => u.TenantId == tenantId
            && u.IsActive
            && dbContext.ClearanceApprovers.Any(a => a.TenantId == tenantId && a.UserId == u.Id));

    public static Task<bool> IsActiveApproverAsync(
        MaintTrackDbContext dbContext,
        Guid tenantId,
        Guid userId,
        CancellationToken ct) =>
        ActiveApprovers(dbContext, tenantId).AsNoTracking().AnyAsync(u => u.Id == userId, ct);
}
