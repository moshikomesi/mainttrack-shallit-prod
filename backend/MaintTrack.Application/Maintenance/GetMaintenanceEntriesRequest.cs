using System;

namespace MaintTrack.Application.Maintenance;

public sealed class GetMaintenanceEntriesRequest
{
    public Guid? MachineId { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? Search { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
