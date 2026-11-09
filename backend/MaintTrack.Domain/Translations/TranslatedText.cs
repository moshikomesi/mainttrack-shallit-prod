using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Translations;

/// <summary>
/// Translation for one tenant, entity, field, source version, and target language.
/// Rows for older source hashes remain stored and are not the current version.
/// </summary>
public sealed class TranslatedText : TenantEntity
{
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string SourceHash { get; set; } = string.Empty;

    /// <summary>
    /// Translated value for <see cref="SourceHash"/>. The column is <c>translated_text</c>.
    /// The property cannot share the class name.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    public string TargetLanguage { get; set; } = string.Empty;
}
