using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Translations;

/// <summary>
/// One queued translation of a copied source value. Completing a job never writes back to the source entity.
/// </summary>
public sealed class TranslationJob : TenantEntity
{
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public string SourceHash { get; set; } = string.Empty;

    public string TargetLanguage { get; set; } = string.Empty;

    public string Status { get; set; } = TranslationJobStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? LastError { get; set; }
}
