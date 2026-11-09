namespace MaintTrack.Domain.Translations;

/// <summary>
/// Languages the translation infrastructure is allowed to store.
/// </summary>
public static class TranslationLanguages
{
    public const string Hebrew = "he";

    public const string English = "en";

    public const string Thai = "th";

    public static bool IsSupported(string? language) =>
        language is Hebrew or English or Thai;
}
