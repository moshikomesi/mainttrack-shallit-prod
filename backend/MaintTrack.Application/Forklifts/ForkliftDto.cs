using System;

namespace MaintTrack.Application.Forklifts;

public sealed record ForkliftDto(
    Guid Id,
    Guid TenantId,
    string LicenseNumber,
    string? Description,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastInspectionDate,
    DateTime? InspectionExpiryDate
);
