namespace MaintTrack.Domain.Translations;

public enum MaintenanceDescriptionVersion
{
    Unmarked = 0,
    V1 = 1,
    V2 = 2
}

/// <summary>
/// Version, original-language component line, and free-text narrative of a maintenance description.
/// Unmarked reports have an empty narrative so they are never queued or rewritten.
/// </summary>
public sealed record MaintenanceDescriptionParts(
    MaintenanceDescriptionVersion Version,
    string? Component,
    string Narrative)
{
    public bool HasTranslatableNarrative =>
        Version != MaintenanceDescriptionVersion.Unmarked && Narrative.Length > 0;
}

/// <summary>
/// Reads a maintenance description without guessing a version from its contents.
/// A version applies only when its sentinel is the exact first line.
/// </summary>
public static class MaintenanceDescriptionText
{
    public const string V1Sentinel = "maintenanceLog.v1";

    public const string V2Sentinel = "maintenanceLog.v2";

    public static MaintenanceDescriptionParts Extract(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return Unmarked();

        var lines = description
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        if (lines[0] == V1Sentinel)
        {
            return new MaintenanceDescriptionParts(
                MaintenanceDescriptionVersion.V1,
                null,
                JoinFrom(lines, 1));
        }

        if (lines[0] == V2Sentinel)
        {
            var component = lines.Length > 1 ? lines[1].Trim() : string.Empty;
            return new MaintenanceDescriptionParts(
                MaintenanceDescriptionVersion.V2,
                component,
                JoinFrom(lines, 2));
        }

        return Unmarked();
    }

    private static MaintenanceDescriptionParts Unmarked() =>
        new(MaintenanceDescriptionVersion.Unmarked, null, string.Empty);

    private static string JoinFrom(string[] lines, int startIndex)
    {
        if (startIndex >= lines.Length)
            return string.Empty;

        return string.Join('\n', lines.Skip(startIndex)).Trim();
    }
}
