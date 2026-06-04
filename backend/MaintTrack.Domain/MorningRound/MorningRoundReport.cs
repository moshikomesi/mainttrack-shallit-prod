using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MorningRound;

/// <summary>
/// A morning round report for a given date, performed by a user.
/// Checklist labels come from translations; only notes are stored in NotesJson by index (1-35).
/// </summary>
public class MorningRoundReport : TenantEntity
{
    public DateOnly ReportDate { get; set; }

    public Guid PerformedByUserId { get; set; }

    public DateTime PerformedAt { get; set; }

    /// <summary>
    /// JSON object mapping checklist index (1-35) to optional note.
    /// Example: {"4": "Low water pressure", "7": "Minor leak"}
    /// </summary>
    public string NotesJson { get; set; } = "{}";
}
