namespace MaintTrack.Application.Reports.ForkliftReports;

/// <summary>
/// Forklift with inspection expiring within the configured window.
/// </summary>
public sealed record ExpiringInspectionDto(
    Guid ForkliftId,
    string LicenseNumber,
    DateOnly InspectionExpiryDate,
    int DaysRemaining
);
