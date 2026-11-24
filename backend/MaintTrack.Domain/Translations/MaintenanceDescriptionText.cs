namespace MaintTrack.Domain.Translations;

public enum MaintenanceDescriptionVersion
{
    Unmarked = 0,
    V1 = 1,
    V2 = 2,
    Legacy = 3
}

/// <summary>
/// Version, original-language component line, and free-text narrative of a maintenance description.
/// A blank description stays unmarked and is never queued.
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
/// Reads a maintenance description.
/// V1 and V2 apply only when their sentinel is the exact first line.
/// A legacy description uses a component key only when its trimmed first line is a
/// <c>maintenanceComponent.*</c> localization key. Otherwise the whole description is narrative.
/// </summary>
public static class MaintenanceDescriptionText
{
    public const string V1Sentinel = "maintenanceLog.v1";

    public const string V2Sentinel = "maintenanceLog.v2";

    public const string ComponentKeyPrefix = "maintenanceComponent.";

    public static MaintenanceDescriptionParts Extract(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return Unmarked();

        var normalized = description
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');

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

        var componentKey = lines[0].Trim();
        if (IsComponentKey(componentKey))
        {
            return new MaintenanceDescriptionParts(
                MaintenanceDescriptionVersion.Legacy,
                componentKey,
                JoinFrom(lines, 1));
        }

        var narrative = normalized.Trim();
        if (narrative.Length == 0)
            return Unmarked();

        return new MaintenanceDescriptionParts(
            MaintenanceDescriptionVersion.Legacy,
            null,
            narrative);
    }

    private static MaintenanceDescriptionParts Unmarked() =>
        new(MaintenanceDescriptionVersion.Unmarked, null, string.Empty);

    private static bool IsComponentKey(string line)
    {
        if (!line.StartsWith(ComponentKeyPrefix, StringComparison.Ordinal))
            return false;

        var suffix = line.AsSpan(ComponentKeyPrefix.Length);
        if (suffix.IsEmpty || !char.IsAsciiLetter(suffix[0]))
            return false;

        for (var i = 1; i < suffix.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(suffix[i]))
                return false;
        }

        return true;
    }

    private static string JoinFrom(string[] lines, int startIndex)
    {
        if (startIndex >= lines.Length)
            return string.Empty;

        return string.Join('\n', lines.Skip(startIndex)).Trim();
    }
}
