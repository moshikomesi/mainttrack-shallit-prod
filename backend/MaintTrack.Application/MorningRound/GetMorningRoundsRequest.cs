using System;

namespace MaintTrack.Application.MorningRound;

public sealed class GetMorningRoundsRequest
{
    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public Guid? PerformedByUserId { get; init; }
}
