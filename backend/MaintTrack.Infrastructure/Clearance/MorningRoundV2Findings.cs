using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MaintTrack.Infrastructure.Clearance;

/// <summary>
/// Reads abnormal machine findings from <c>morning_round_v2_submissions.items_json</c>.
/// A finding is abnormal when its status is <c>fail</c>, the only non-normal value the
/// Morning Round V2 submit endpoint accepts (<c>ok</c> / <c>fail</c>).
/// </summary>
internal static class MorningRoundV2Findings
{
    public const string AbnormalStatus = "fail";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool IsAbnormal(string? status) =>
        string.Equals(status?.Trim(), AbnormalStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>Abnormal findings, one per machine (the last entry wins, as in the report view).</summary>
    public static IReadOnlyList<Finding> Abnormal(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson))
        {
            return Array.Empty<Finding>();
        }

        List<Finding>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<Finding>>(itemsJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Array.Empty<Finding>();
        }

        return (items ?? new List<Finding>())
            .GroupBy(i => i.MachineId)
            .Select(g => g.Last())
            .Where(i => IsAbnormal(i.Status))
            .ToList();
    }

    public sealed record Finding(Guid MachineId, string? Status, string? Notes);
}
