namespace MaintTrack.Domain.Translations;

/// <summary>
/// Entity and field pairs whose text may be queued for translation.
/// A field is listed only after it exists on the domain model.
/// </summary>
public static class TranslatableFields
{
    public const string MaintenanceEntry = "MaintenanceEntry";

    public const string Description = "Description";

    private static readonly HashSet<TranslatableField> Allowed = new()
    {
        new(MaintenanceEntry, Description)
    };

    public static bool IsAllowed(string entityType, string fieldName) =>
        Allowed.Contains(new TranslatableField(entityType, fieldName));

    private readonly record struct TranslatableField(string EntityType, string FieldName);
}
