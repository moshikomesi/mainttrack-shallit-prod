using MaintTrack.Domain.Translations;

namespace MaintTrack.Application.Translations;

public static class MaintenanceTranslationPublicStatus
{
    public const string Pending = "pending";

    public const string Completed = "completed";

    public const string Unavailable = "unavailable";
}

public sealed record MaintenanceTranslationItemDto(string Language, string Status, string? Text);

public sealed record MaintenanceTranslationsResponse(
    string OriginalText,
    IReadOnlyList<MaintenanceTranslationItemDto> Translations)
{
    public static MaintenanceTranslationsResponse WithoutRecords(string originalText) =>
        new(
            originalText,
            TranslationLanguages.Supported
                .Select(language => new MaintenanceTranslationItemDto(
                    language,
                    MaintenanceTranslationPublicStatus.Unavailable,
                    null))
                .ToArray());

    public static MaintenanceTranslationsResponse FromRows(
        string originalText,
        IReadOnlyList<NarrativeTranslationRow> rows)
    {
        var byLanguage = new Dictionary<string, NarrativeTranslationRow>(StringComparer.Ordinal);
        foreach (var row in rows)
            byLanguage[row.Language] = row;

        var items = new List<MaintenanceTranslationItemDto>(TranslationLanguages.Supported.Count);
        foreach (var language in TranslationLanguages.Supported)
        {
            if (!byLanguage.TryGetValue(language, out var row))
            {
                items.Add(Unavailable(language));
                continue;
            }

            if (!string.IsNullOrEmpty(row.Text))
            {
                items.Add(new MaintenanceTranslationItemDto(
                    language,
                    MaintenanceTranslationPublicStatus.Completed,
                    row.Text));
                continue;
            }

            if (row.JobStatus is TranslationJobStatus.Pending or TranslationJobStatus.Processing)
            {
                items.Add(new MaintenanceTranslationItemDto(
                    language,
                    MaintenanceTranslationPublicStatus.Pending,
                    null));
                continue;
            }

            items.Add(Unavailable(language));
        }

        return new MaintenanceTranslationsResponse(originalText, items);
    }

    private static MaintenanceTranslationItemDto Unavailable(string language) =>
        new(language, MaintenanceTranslationPublicStatus.Unavailable, null);
}
