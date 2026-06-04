namespace MaintTrack.Application.Reports.ForkliftReports;

/// <summary>
/// Response for the Forklift Reports Overview query.
/// </summary>
public sealed record ForkliftReportsResponse(
    IReadOnlyList<ForkliftReportListItemDto> Reports,
    IReadOnlyList<ExpiringInspectionDto> ExpiringInspections
);
