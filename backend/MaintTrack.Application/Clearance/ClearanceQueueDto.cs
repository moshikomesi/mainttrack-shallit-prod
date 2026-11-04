namespace MaintTrack.Application.Clearance;

/// <summary>
/// One Clearance queue row. Carries source identifiers and queue display data only;
/// full report details stay on the existing maintenance / morning round report endpoints.
/// Machine fields describe the machine of a maintenance entry; a Morning Round V2 item
/// spans several machines, so its abnormal machines are listed in <see cref="Findings"/>.
/// </summary>
public sealed record ClearanceItemDto(
    Guid Id,
    string SourceType,
    Guid? MaintenanceEntryId,
    Guid? MorningRoundV2SubmissionId,
    Guid? MachineId,
    string? MachineName,
    Guid? ArrayId,
    DateOnly ReportDate,
    string? Description,
    string? MaintenanceTypeCode,
    IReadOnlyList<ClearanceFindingDto> Findings,
    string Status,
    Guid SubmittedByUserId,
    string? SubmittedByName,
    DateTime SubmittedAt,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTime? DecidedAt,
    string? DecisionNote
);

/// <summary>An abnormal machine finding of a Morning Round V2 report.</summary>
public sealed record ClearanceFindingDto(
    Guid MachineId,
    string MachineName,
    Guid? ArrayId,
    string? Notes
);

/// <summary>
/// Paginated Clearance queue. <see cref="CanApprove"/> reflects the database approver
/// assignment of the current user, evaluated on this request.
/// </summary>
public sealed record ClearanceQueueResponse(
    IReadOnlyList<ClearanceItemDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    bool CanApprove
);

/// <summary>What the current user may do with Clearance, read from the database on every request.</summary>
public sealed record ClearanceAccessDto(
    bool CanView,
    bool CanApprove
);

/// <summary>The Clearance item of one source report, with the current user's decision right.</summary>
public sealed record ClearanceSourceItemResponse(
    ClearanceItemDto Item,
    bool CanApprove
);
