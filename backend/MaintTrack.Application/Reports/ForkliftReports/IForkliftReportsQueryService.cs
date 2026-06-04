namespace MaintTrack.Application.Reports.ForkliftReports;

/// <summary>
/// Read-model query service for the Forklift Reports Overview screen.
/// </summary>
public interface IForkliftReportsQueryService
{
    Task<ForkliftReportsResponse> GetAsync(ForkliftReportsQuery query, CancellationToken ct);
}
