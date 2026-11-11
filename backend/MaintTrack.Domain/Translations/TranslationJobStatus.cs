namespace MaintTrack.Domain.Translations;

/// <summary>
/// Stored values of <c>translation_jobs.status</c>.
/// </summary>
public static class TranslationJobStatus
{
    public const string Pending = "pending";

    public const string Processing = "processing";

    public const string Completed = "completed";

    public const string Failed = "failed";
}
