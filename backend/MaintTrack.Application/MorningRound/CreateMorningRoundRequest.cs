using System;
using System.Collections.Generic;

namespace MaintTrack.Application.MorningRound;

public sealed class CreateMorningRoundRequest
{
    public DateOnly ReportDate { get; init; }

    /// <summary>
    /// Notes by MorningRoundTemplateItem id (GUID as string) -> note.
    /// Example: {"8f3b9c2a-91f7-4a8a-a8c4-2e72a4a9f8cd": "Low water pressure"}
    /// </summary>
    public Dictionary<string, string>? Notes { get; init; }
}
