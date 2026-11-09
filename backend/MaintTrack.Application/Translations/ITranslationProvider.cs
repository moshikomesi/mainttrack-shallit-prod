namespace MaintTrack.Application.Translations;

/// <summary>
/// Translates a piece of text from one language to another.
/// No provider is registered in this phase.
/// </summary>
public interface ITranslationProvider
{
    Task<string> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken);
}
