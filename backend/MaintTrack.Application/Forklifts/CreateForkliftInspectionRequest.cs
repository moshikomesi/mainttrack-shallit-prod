using System;

namespace MaintTrack.Application.Forklifts;

public sealed class CreateForkliftInspectionRequest
{
    public DateOnly TestDate { get; init; }

    public DateOnly ExpiryDate { get; init; }
}
