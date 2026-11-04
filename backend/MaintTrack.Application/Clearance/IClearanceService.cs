namespace MaintTrack.Application.Clearance;

public interface IClearanceService
{
    /// <summary>
    /// True only when the current user is active in the current tenant and has a
    /// clearance_approvers assignment in that tenant. Roles never grant this.
    /// </summary>
    Task<bool> IsCurrentUserApproverAsync(CancellationToken ct);

    /// <summary>
    /// Visibility (active users with a clearance_viewers or clearance_approvers assignment) and
    /// decision right (active approvers only). Roles never grant either.
    /// </summary>
    Task<ClearanceAccessDto> GetAccessAsync(CancellationToken ct);

    /// <summary>
    /// Tenant-scoped, paginated Clearance queue.
    /// Throws <see cref="UnauthorizedAccessException"/> (checked first) when the user may not view
    /// Clearance information and <see cref="ArgumentException"/> for an unknown status.
    /// </summary>
    Task<ClearanceQueueResponse> GetQueueAsync(ClearanceQueueQuery query, CancellationToken ct);

    /// <summary>
    /// The Clearance item of a source report in the current tenant, or null when the report has none.
    /// Same visibility as the queue. Throws <see cref="UnauthorizedAccessException"/> (checked first)
    /// when the user may not view Clearance information and <see cref="ArgumentException"/> for an
    /// unknown source type.
    /// </summary>
    Task<ClearanceSourceItemResponse?> GetBySourceAsync(string sourceType, Guid sourceId, CancellationToken ct);

    /// <summary>
    /// Moves a pending item to approved. The approval itself is the approver's return-to-operation
    /// safety declaration; the clearance_approved audit row records who made it and when.
    /// Throws <see cref="UnauthorizedAccessException"/> for non-approvers, <see cref="KeyNotFoundException"/>
    /// when the item is not in the current tenant, and <see cref="ClearanceDecisionConflictException"/> when
    /// it is no longer pending.
    /// </summary>
    Task<ClearanceDecisionDto> ApproveAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Moves a pending item to rejected with an optional note. Same errors as
    /// <see cref="ApproveAsync"/>, plus <see cref="ArgumentException"/> for an over-long note.
    /// </summary>
    Task<ClearanceDecisionDto> RejectAsync(Guid id, string? note, CancellationToken ct);
}
