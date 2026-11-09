using MaintTrack.Domain.Translations;

namespace MaintTrack.Application.Translations;

public sealed record EnqueueTranslationRequest(
    string EntityType,
    Guid EntityId,
    string FieldName,
    string SourceText,
    string TargetLanguage);

public sealed record EnqueueTranslationResult(TranslationJob Job, bool Created);

public enum TranslationCompletionResult
{
    Completed,
    Stale,
    NotFound
}

public interface ITranslationRepository
{
    Task<EnqueueTranslationResult> EnqueueAsync(
        EnqueueTranslationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a batch of pending jobs to <c>processing</c>. A job is claimed by at most one caller.
    /// When the current tenant is unresolved, jobs from every tenant can be claimed.
    /// A resolved tenant only claims its own jobs.
    /// </summary>
    Task<IReadOnlyList<TranslationJob>> ClaimPendingAsync(
        int batchSize,
        TimeSpan processingLease,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores the translation for the job's source hash and marks the job completed.
    /// A hash that no longer matches the job writes nothing.
    /// </summary>
    Task<TranslationCompletionResult> CompleteAsync(
        Guid jobId,
        string sourceHash,
        string translatedText,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records an error on the same job row. The job returns to pending until <paramref name="maxAttempts"/>.
    /// </summary>
    Task FailAsync(
        Guid jobId,
        string error,
        int maxAttempts,
        CancellationToken cancellationToken);

    Task<TranslatedText?> FindTranslationAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken);
}
