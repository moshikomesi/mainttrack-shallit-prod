namespace MaintTrack.Application.Clearance;

/// <summary>Optional body of the reject action.</summary>
public sealed record ClearanceRejectRequest(string? Note);

/// <summary>Current decision of a Clearance item after a successful approve or reject.</summary>
public sealed record ClearanceDecisionDto(
    Guid Id,
    string Status,
    Guid DecidedByUserId,
    DateTime DecidedAt,
    string? DecisionNote
);

/// <summary>
/// The item exists in the current tenant but is no longer pending, so the decision was not applied.
/// </summary>
public sealed class ClearanceDecisionConflictException : InvalidOperationException
{
    public ClearanceDecisionConflictException()
        : base("This Clearance item has already been decided.")
    {
    }
}
