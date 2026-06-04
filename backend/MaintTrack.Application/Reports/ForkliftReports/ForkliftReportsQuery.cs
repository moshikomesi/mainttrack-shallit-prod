namespace MaintTrack.Application.Reports.ForkliftReports;

/// <summary>
/// Filter for the Forklift Reports Overview query.
/// </summary>
public class ForkliftReportsQuery
{
    public string? ForkliftNumber { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public string? Type { get; set; }

    public bool? InspectionExpiringSoon { get; set; }

    public int? ExpiryDays { get; set; }

    public string? Search { get; set; }

    public int? PageNumber { get; set; }

    public int? PageSize { get; set; }
}
