namespace MaintTrack.Domain.Translations;

/// <summary>
/// Languages the translation infrastructure is allowed to store.
/// </summary>
public static class TranslationLanguages
{
    public const string Hebrew = "he";

    public const string English = "en";

    public const string Thai = "th";

    public static readonly IReadOnlyList<string> Supported = new[] { Hebrew, English, Thai };

    public static bool IsSupported(string? language) =>
        language is Hebrew or English or Thai;

    /// <summary>
    /// Normalizes a detected or regional language tag (such as <c>en-US</c> or legacy <c>iw</c>)
    /// into one of <see cref="Supported"/>, or returns <c>null</c> when unsupported/unknown.
    /// </summary>
    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        var trimmed = language.Trim();
        var separatorIndex = trimmed.IndexOfAny(new[] { '-', '_' });
        var primary = (separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed).ToLowerInvariant();
        if (primary == "iw")
            primary = Hebrew;

        return IsSupported(primary) ? primary : null;
    }

    /// <summary>
    /// Returns every supported target language except <paramref name="sourceLanguage"/>.
    /// When the source language is unknown or outside <see cref="Supported"/>, returns all supported targets.
    /// </summary>
    public static IReadOnlyList<string> TargetsExcluding(string? sourceLanguage)
    {
        var normalized = Normalize(sourceLanguage);
        if (normalized is null)
            return Supported;

        return Supported
            .Where(language => !string.Equals(language, normalized, StringComparison.Ordinal))
            .ToArray();
    }
}
