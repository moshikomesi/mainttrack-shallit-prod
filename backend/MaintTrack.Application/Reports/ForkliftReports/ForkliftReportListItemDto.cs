namespace MaintTrack.Application.Reports.ForkliftReports;

/// <summary>
/// A single row in the forklift reports list (read model).
/// </summary>
public sealed record ForkliftReportListItemDto(
    Guid ReportId,
    Guid ForkliftId,
    string ForkliftNumber,
    DateOnly ReportDate,
    int TreatmentsCount,
    int FaultsCount
);
