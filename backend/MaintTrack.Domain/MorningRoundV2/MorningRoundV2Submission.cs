using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MorningRoundV2;

/// <summary>
/// Machine-based morning round checklist submission (v2).
/// Isolated from v1 <see cref="MorningRound.MorningRoundReport" />.
/// </summary>
public class MorningRoundV2Submission : TenantEntity
{
    public DateOnly ReportDate { get; set; }

    public DateTime SubmittedAt { get; set; }

    public Guid SubmittedByUserId { get; set; }

    public string ItemsJson { get; set; } = "[]";
}
