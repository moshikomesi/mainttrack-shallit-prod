using System;
using System.Collections.Generic;

namespace MaintTrack.Application.MorningRound;

public sealed record MorningRoundDto(
    Guid Id,
    DateOnly ReportDate,
    Guid PerformedByUserId,
    string PerformedByName,
    DateTime PerformedAt,
    Dictionary<Guid, string>? Notes
);
