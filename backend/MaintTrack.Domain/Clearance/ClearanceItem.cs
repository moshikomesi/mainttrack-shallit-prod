using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Clearance;

/// <summary>
/// A report awaiting (or having received) Clearance approval. Maps to <c>clearance_items</c>.
/// Exactly one source reference is set, matching <see cref="SourceType" />:
/// <list type="bullet">
/// <item><c>maintenance_entry</c>: <see cref="MaintenanceEntryId" /> only.</item>
/// <item><c>morning_round_v2</c>: <see cref="MorningRoundV2SubmissionId" /> only — one item per
/// submission, however many abnormal machine findings it contains.</item>
/// </list>
/// Independent of <c>MaintenanceEntry.IsSafeToOperate</c>.
/// </summary>
public sealed class ClearanceItem : TenantEntity
{
    public string SourceType { get; set; } = string.Empty;

    public Guid? MaintenanceEntryId { get; set; }

    public Guid? MorningRoundV2SubmissionId { get; set; }

    public string Status { get; set; } = ClearanceStatus.Pending;

    public Guid SubmittedByUserId { get; set; }

    public DateTime SubmittedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }
}
