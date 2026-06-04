using System;

namespace MaintTrack.Application.Maintenance;

/// <summary>
/// Active maintenance type for dropdowns; display labels come from frontend i18n via <c>code</c>.
/// </summary>
public sealed record MaintenanceTypeDto(Guid Id, string Code);
