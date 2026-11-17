using MaintTrack.Domain.Translations;

namespace MaintTrack.Application.Translations;

public sealed record EnqueueTranslationRequest(
    string EntityType,
    Guid EntityId,
    string FieldName,
    string SourceText,
    string TargetLanguage);

public sealed record EnqueueTranslationResult(TranslationJob Job, bool Created);

/// <summary>
/// Public-facing translation row: language, job status, and translated text only.
/// </summary>
public sealed record NarrativeTranslationRow(string Language, string? JobStatus, string? Text);

/// <summary>
/// The job id and claim token returned to the worker that successfully claimed the job.
/// </summary>
public sealed record TranslationClaim(Guid JobId, Guid ClaimToken);

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
    /// Persists every missing job in one transaction. If any insert fails, none of them are kept.
    /// Jobs that already exist are returned with <c>Created = false</c>.
    /// </summary>
    Task<IReadOnlyList<EnqueueTranslationResult>> EnqueueAllAsync(
        IReadOnlyList<EnqueueTranslationRequest> requests,
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
    /// Extends the processing lease for claims this caller still owns.
    /// A token that is no longer current does not change that job.
    /// </summary>
    Task RenewClaimsAsync(
        IReadOnlyCollection<TranslationClaim> claims,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores the translation for the job's source hash and marks the job completed.
    /// Only the current claim token can write. A hash that no longer matches the job writes nothing.
    /// </summary>
    Task<TranslationCompletionResult> CompleteAsync(
        Guid jobId,
        Guid claimToken,
        string sourceHash,
        string translatedText,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records an error on the same job row when <paramref name="claimToken"/> is still current.
    /// The job returns to pending until <paramref name="maxAttempts"/>.
    /// Returns false when a newer claim owns the job.
    /// </summary>
    Task<bool> FailAsync(
        Guid jobId,
        Guid claimToken,
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

    /// <summary>
    /// Reads language, job status, and translated text for one source version.
    /// The result does not include job ids, claim tokens, errors, or attempt counts.
    /// </summary>
    Task<IReadOnlyList<NarrativeTranslationRow>> GetNarrativeTranslationsAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        CancellationToken cancellationToken);
}
