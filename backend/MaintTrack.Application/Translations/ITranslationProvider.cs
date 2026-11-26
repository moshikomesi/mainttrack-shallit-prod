namespace MaintTrack.Application.Translations;

public sealed record TranslationResult(string Text, string? DetectedSourceLanguage = null);

/// <summary>
/// Translates text and optionally detects source languages for translation jobs.
/// </summary>
public interface ITranslationProvider
{
    Task<string> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken);

    Task<string?> DetectLanguageAsync(
        string text,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    async Task<TranslationResult> TranslateWithMetadataAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        new(await TranslateAsync(text, sourceLanguage, targetLanguage, cancellationToken));
}
