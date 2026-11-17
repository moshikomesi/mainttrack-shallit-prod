using System.Data;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Translations;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace MaintTrack.Infrastructure.Translations;

public sealed class TranslationRepository : ITranslationRepository
{
    private const string PostgresProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SourceIntegrityError = "Source text does not match its source hash.";
    private const string PostgresDeadlockSqlState = "40P01";
    private const int EnqueueAllMaxAttempts = 3;

    private readonly MaintTrackDbContext _db;
    private readonly ITenantContext _tenantContext;

    public TranslationRepository(MaintTrackDbContext db, ITenantContext tenantContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<EnqueueTranslationResult> EnqueueAsync(
        EnqueueTranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.TenantId is not Guid tenantId)
            throw new InvalidOperationException("Tenant not resolved.");

        var job = CreatePendingJob(request, tenantId);
        var existing = await FindJobAsync(job, cancellationToken);

        if (existing is not null)
            return new EnqueueTranslationResult(existing, false);

        _db.TranslationJobs.Add(job);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new EnqueueTranslationResult(job, true);
        }
        catch (DbUpdateException)
        {
            _db.Entry(job).State = EntityState.Detached;
            var winner = await FindJobAsync(job, cancellationToken);

            if (winner is null)
                throw;

            return new EnqueueTranslationResult(winner, false);
        }
    }

    public async Task<IReadOnlyList<EnqueueTranslationResult>> EnqueueAllAsync(
        IReadOnlyList<EnqueueTranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (_tenantContext.TenantId is not Guid tenantId)
            throw new InvalidOperationException("Tenant not resolved.");

        if (requests.Count == 0)
            return Array.Empty<EnqueueTranslationResult>();

        // Validate shape and uniqueness once; each attempt builds fresh job rows.
        var validationJobs = CreatePendingJobs(requests, tenantId);
        var distinctKeys = validationJobs
            .Select(job => (job.EntityType, job.EntityId, job.FieldName, job.SourceHash, job.TargetLanguage))
            .Distinct()
            .Count();
        if (distinctKeys != validationJobs.Count)
            throw new InvalidOperationException("Translation requests must not repeat the same source version and language.");

        for (var attempt = 1; attempt <= EnqueueAllMaxAttempts; attempt++)
        {
            var candidates = attempt == 1 ? validationJobs : CreatePendingJobs(requests, tenantId);
            var added = new List<TranslationJob>(candidates.Count);
            try
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                var results = new List<EnqueueTranslationResult>(candidates.Count);
                foreach (var candidate in candidates)
                {
                    var existing = await FindJobAsync(candidate, cancellationToken);
                    if (existing is not null)
                    {
                        results.Add(new EnqueueTranslationResult(existing, false));
                        continue;
                    }

                    _db.TranslationJobs.Add(candidate);
                    added.Add(candidate);
                    results.Add(new EnqueueTranslationResult(candidate, true));
                }

                if (added.Count > 0)
                    await _db.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return results;
            }
            catch (Exception exception) when (IsPostgresDeadlock(exception))
            {
                // await using has rolled back and disposed the failed transaction before this catch runs.
                Detach(added);
                if (attempt < EnqueueAllMaxAttempts)
                    continue;

                throw;
            }
            catch (DbUpdateException)
            {
                // The transaction has rolled back every insert from this call.
                // A unique-key conflict means another caller committed the same jobs.
                Detach(added);
                var winners = new List<EnqueueTranslationResult>(candidates.Count);
                foreach (var candidate in candidates)
                {
                    var winner = await FindJobAsync(candidate, cancellationToken);
                    if (winner is null)
                        throw;

                    winners.Add(new EnqueueTranslationResult(winner, false));
                }

                return winners;
            }
            catch
            {
                Detach(added);
                throw;
            }
        }

        throw new InvalidOperationException("Translation enqueue exhausted its deadlock retries without returning.");
    }
    public async Task<IReadOnlyList<TranslationJob>> ClaimPendingAsync(
        int batchSize,
        TimeSpan processingLease,
        CancellationToken cancellationToken)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        if (processingLease <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(processingLease));

        var now = DateTime.UtcNow;
        var staleBefore = now - processingLease;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var ids = IsPostgres
            ? await ClaimPostgresAsync(batchSize, now, staleBefore, cancellationToken)
            : await ClaimConditionalAsync(batchSize, now, staleBefore, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (ids.Count == 0)
            return Array.Empty<TranslationJob>();

        _db.ChangeTracker.Clear();
        return await _db.TranslationJobs
            .AsNoTracking()
            .Where(job => ids.Contains(job.Id))
            .OrderBy(job => job.CreatedAt)
            .ThenBy(job => job.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task RenewClaimsAsync(
        IReadOnlyCollection<TranslationClaim> claims,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var processing = TranslationJobStatus.Processing;
        foreach (var claim in claims)
        {
            await _db.TranslationJobs
                .Where(job => job.Id == claim.JobId
                    && job.ClaimToken == claim.ClaimToken
                    && job.Status == processing)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(job => job.UpdatedAt, now),
                    cancellationToken);
        }
    }

    public async Task<TranslationCompletionResult> CompleteAsync(
        Guid jobId,
        Guid claimToken,
        string sourceHash,
        string translatedText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceHash);
        ArgumentNullException.ThrowIfNull(translatedText);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var job = await _db.TranslationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.NotFound;
        }

        if (job.Status != TranslationJobStatus.Processing || job.ClaimToken != claimToken)
        {
            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.Stale;
        }

        DetachJobs();

        if (!string.Equals(job.SourceHash, sourceHash, StringComparison.Ordinal))
        {
            var reset = await ResetOwnedClaimAsync(jobId, claimToken, cancellationToken);
            if (reset != 1)
            {
                _db.ChangeTracker.Clear();
                await transaction.RollbackAsync(cancellationToken);
                return TranslationCompletionResult.Stale;
            }

            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.Stale;
        }

        if (!string.Equals(SourceTextHasher.Compute(job.SourceText), job.SourceHash, StringComparison.Ordinal))
        {
            // Retrying cannot repair a corrupted source, so only the current claim may fail it terminally.
            var failedAt = DateTime.UtcNow;
            await _db.TranslationJobs
                .Where(item => item.Id == jobId
                    && item.ClaimToken == claimToken
                    && item.Status == TranslationJobStatus.Processing
                    && item.SourceHash == job.SourceHash)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.Status, TranslationJobStatus.Failed)
                        .SetProperty(item => item.LastError, SourceIntegrityError)
                        .SetProperty(item => item.ProcessedAt, failedAt)
                        .SetProperty(item => item.UpdatedAt, failedAt),
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.Stale;
        }

        DetachJobs();
        var now = DateTime.UtcNow;
        var existing = await _db.TranslatedTexts.FirstOrDefaultAsync(
            item => item.TenantId == job.TenantId
                && item.EntityType == job.EntityType
                && item.EntityId == job.EntityId
                && item.FieldName == job.FieldName
                && item.SourceHash == job.SourceHash
                && item.TargetLanguage == job.TargetLanguage,
            cancellationToken);

        if (existing is null)
        {
            _db.TranslatedTexts.Add(new TranslatedText
            {
                Id = Guid.NewGuid(),
                TenantId = job.TenantId,
                EntityType = job.EntityType,
                EntityId = job.EntityId,
                FieldName = job.FieldName,
                SourceHash = job.SourceHash,
                TargetLanguage = job.TargetLanguage,
                Text = translatedText,
                CreatedAt = now
            });
        }
        else
        {
            existing.Text = translatedText;
            existing.UpdatedAt = now;
        }

        var completed = await _db.TranslationJobs
            .Where(item => item.Id == jobId
                && item.ClaimToken == claimToken
                && item.Status == TranslationJobStatus.Processing
                && item.SourceHash == sourceHash)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, TranslationJobStatus.Completed)
                    .SetProperty(item => item.ProcessedAt, now)
                    .SetProperty(item => item.UpdatedAt, now)
                    .SetProperty(item => item.LastError, (string?)null),
                cancellationToken);

        if (completed != 1)
        {
            _db.ChangeTracker.Clear();
            await transaction.RollbackAsync(cancellationToken);
            return TranslationCompletionResult.Stale;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TranslationCompletionResult.Completed;
    }

    public async Task<bool> FailAsync(
        Guid jobId,
        Guid claimToken,
        string error,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        var now = DateTime.UtcNow;
        var errorText = NormalizeError(error);
        var processing = TranslationJobStatus.Processing;
        var failedRows = await _db.TranslationJobs
            .Where(job => job.Id == jobId
                && job.ClaimToken == claimToken
                && job.Status == processing
                && job.AttemptCount >= maxAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, TranslationJobStatus.Failed)
                    .SetProperty(job => job.LastError, errorText)
                    .SetProperty(job => job.UpdatedAt, now)
                    .SetProperty(job => job.ProcessedAt, now),
                cancellationToken);

        if (failedRows == 1)
        {
            DetachJobs();
            return true;
        }

        var retryRows = await _db.TranslationJobs
            .Where(job => job.Id == jobId
                && job.ClaimToken == claimToken
                && job.Status == processing
                && job.AttemptCount < maxAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, TranslationJobStatus.Pending)
                    .SetProperty(job => job.LastError, errorText)
                    .SetProperty(job => job.UpdatedAt, now)
                    .SetProperty(job => job.ProcessedAt, (DateTime?)null)
                    .SetProperty(job => job.ClaimToken, (Guid?)null),
                cancellationToken);

        if (retryRows == 1)
        {
            DetachJobs();
            return true;
        }

        if (!await _db.TranslationJobs.AnyAsync(job => job.Id == jobId, cancellationToken))
            throw new InvalidOperationException("Translation job not found.");

        DetachJobs();
        return false;
    }

    public async Task<TranslatedText?> FindTranslationAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
            throw new InvalidOperationException("Tenant not resolved.");

        return await _db.TranslatedTexts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId
                    && item.EntityType == entityType
                    && item.EntityId == entityId
                    && item.FieldName == fieldName
                    && item.SourceHash == sourceHash
                    && item.TargetLanguage == targetLanguage,
                cancellationToken);
    }

    public async Task<IReadOnlyList<NarrativeTranslationRow>> GetNarrativeTranslationsAsync(
        Guid entityId,
        string entityType,
        string fieldName,
        string sourceHash,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
            throw new InvalidOperationException("Tenant not resolved.");

        var jobs = await _db.TranslationJobs
            .AsNoTracking()
            .Where(job => job.TenantId == tenantId
                && job.EntityId == entityId
                && job.EntityType == entityType
                && job.FieldName == fieldName
                && job.SourceHash == sourceHash)
            .Select(job => new { job.TargetLanguage, job.Status })
            .ToListAsync(cancellationToken);

        var texts = await _db.TranslatedTexts
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId
                && item.EntityId == entityId
                && item.EntityType == entityType
                && item.FieldName == fieldName
                && item.SourceHash == sourceHash)
            .Select(item => new { item.TargetLanguage, item.Text })
            .ToListAsync(cancellationToken);

        var rows = new Dictionary<string, NarrativeTranslationRow>(StringComparer.Ordinal);
        foreach (var job in jobs)
        {
            rows[job.TargetLanguage] = new NarrativeTranslationRow(job.TargetLanguage, job.Status, null);
        }

        foreach (var text in texts)
        {
            rows.TryGetValue(text.TargetLanguage, out var existing);
            rows[text.TargetLanguage] = new NarrativeTranslationRow(
                text.TargetLanguage,
                existing?.JobStatus,
                text.Text);
        }

        return rows.Values.ToArray();
    }

    private bool IsPostgres =>
        string.Equals(_db.Database.ProviderName, PostgresProvider, StringComparison.Ordinal);

    private List<TranslationJob> CreatePendingJobs(
        IReadOnlyList<EnqueueTranslationRequest> requests,
        Guid tenantId) =>
        requests
            .Select(request => CreatePendingJob(
                request ?? throw new ArgumentNullException(nameof(requests)),
                tenantId))
            .ToList();

    private TranslationJob CreatePendingJob(EnqueueTranslationRequest request, Guid tenantId)
    {
        var entityType = NormalizeRequired(request.EntityType, TranslationFieldLimits.EntityTypeMaxLength, "Entity type");
        var fieldName = NormalizeRequired(request.FieldName, TranslationFieldLimits.FieldNameMaxLength, "Field name");
        if (!TranslatableFields.IsAllowed(entityType, fieldName))
            throw new InvalidOperationException($"{entityType}.{fieldName} cannot be translated.");

        var targetLanguage = NormalizeLanguage(request.TargetLanguage);

        if (request.EntityId == Guid.Empty)
            throw new InvalidOperationException("Entity id is required.");

        if (request.SourceText is null)
            throw new InvalidOperationException("Source text is required.");

        if (request.SourceText.Length == 0)
            throw new InvalidOperationException("Source text is required.");

        return new TranslationJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = request.EntityId,
            FieldName = fieldName,
            SourceText = request.SourceText,
            SourceHash = SourceTextHasher.Compute(request.SourceText),
            TargetLanguage = targetLanguage,
            Status = TranslationJobStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static bool IsPostgresDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && string.Equals(postgres.SqlState, PostgresDeadlockSqlState, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private Task<TranslationJob?> FindJobAsync(TranslationJob candidate, CancellationToken cancellationToken) =>
        FindJobAsync(
            candidate.TenantId,
            candidate.EntityType,
            candidate.EntityId,
            candidate.FieldName,
            candidate.SourceHash,
            candidate.TargetLanguage,
            cancellationToken);

    private void Detach(IEnumerable<TranslationJob> jobs)
    {
        foreach (var job in jobs)
            _db.Entry(job).State = EntityState.Detached;
    }

    private async Task<TranslationJob?> FindJobAsync(
        Guid tenantId,
        string entityType,
        Guid entityId,
        string fieldName,
        string sourceHash,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        return await _db.TranslationJobs.FirstOrDefaultAsync(
            job => job.TenantId == tenantId
                && job.EntityType == entityType
                && job.EntityId == entityId
                && job.FieldName == fieldName
                && job.SourceHash == sourceHash
                && job.TargetLanguage == targetLanguage,
            cancellationToken);
    }

    private async Task<List<Guid>> ClaimConditionalAsync(
        int batchSize,
        DateTime now,
        DateTime staleBefore,
        CancellationToken cancellationToken)
    {
        var pending = TranslationJobStatus.Pending;
        var processing = TranslationJobStatus.Processing;
        var candidateIds = await _db.TranslationJobs
            .Where(job => job.Status == pending
                || (job.Status == processing && (job.UpdatedAt == null || job.UpdatedAt < staleBefore)))
            .OrderBy(job => job.CreatedAt)
            .ThenBy(job => job.Id)
            .Select(job => job.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var claimed = new List<Guid>(candidateIds.Count);
        foreach (var id in candidateIds)
        {
            var claimToken = Guid.NewGuid();
            var rows = await _db.TranslationJobs
                .Where(job => job.Id == id
                    && (job.Status == pending
                        || (job.Status == processing && (job.UpdatedAt == null || job.UpdatedAt < staleBefore))))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(job => job.Status, processing)
                        .SetProperty(job => job.AttemptCount, job => job.AttemptCount + 1)
                        .SetProperty(job => job.UpdatedAt, now)
                        .SetProperty(job => job.ClaimToken, claimToken),
                    cancellationToken);

            if (rows == 1)
                claimed.Add(id);
        }

        return claimed;
    }

    private async Task<List<Guid>> ClaimPostgresAsync(
        int batchSize,
        DateTime now,
        DateTime staleBefore,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var transaction = (NpgsqlTransaction)(_db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("A transaction is required to claim translation jobs."));

        var tenantId = _tenantContext.TenantId;
        var sql = tenantId is null
            ? """
              SELECT id
              FROM translation_jobs
              WHERE status = @pending
                 OR (status = @processing AND (updated_at IS NULL OR updated_at < @staleBefore))
              ORDER BY created_at, id
              LIMIT @batch
              FOR UPDATE SKIP LOCKED
              """
            : """
              SELECT id
              FROM translation_jobs
              WHERE tenant_id = @tenantId
                AND (
                    status = @pending
                    OR (status = @processing AND (updated_at IS NULL OR updated_at < @staleBefore))
                )
              ORDER BY created_at, id
              LIMIT @batch
              FOR UPDATE SKIP LOCKED
              """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("pending", TranslationJobStatus.Pending));
        command.Parameters.Add(new NpgsqlParameter("processing", TranslationJobStatus.Processing));
        command.Parameters.Add(new NpgsqlParameter("staleBefore", NpgsqlDbType.TimestampTz) { Value = staleBefore });
        command.Parameters.Add(new NpgsqlParameter("batch", batchSize));
        if (tenantId is Guid scopedTenantId)
            command.Parameters.Add(new NpgsqlParameter("tenantId", scopedTenantId));

        var ids = new List<Guid>(batchSize);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(reader.GetGuid(0));
        }

        const string updateSql = """
            UPDATE translation_jobs
            SET status = @processing,
                attempt_count = attempt_count + 1,
                updated_at = @now,
                claim_token = @claimToken
            WHERE id = @id
            """;

        foreach (var id in ids)
        {
            await using var update = new NpgsqlCommand(updateSql, connection, transaction);
            update.Parameters.Add(new NpgsqlParameter("processing", TranslationJobStatus.Processing));
            update.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
            update.Parameters.Add(new NpgsqlParameter("claimToken", Guid.NewGuid()));
            update.Parameters.Add(new NpgsqlParameter("id", id));
            var updated = await update.ExecuteNonQueryAsync(cancellationToken);
            if (updated != 1)
                throw new InvalidOperationException("Claimed translation job could not be fenced.");
        }

        return ids;
    }

    private void DetachJobs()
    {
        foreach (var entry in _db.ChangeTracker.Entries<TranslationJob>().ToList())
            entry.State = EntityState.Detached;
    }

    private async Task<int> ResetOwnedClaimAsync(
        Guid jobId,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        return await _db.TranslationJobs
            .Where(job => job.Id == jobId
                && job.ClaimToken == claimToken
                && job.Status == TranslationJobStatus.Processing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, TranslationJobStatus.Pending)
                    .SetProperty(job => job.LastError, "Source version changed before the translation was stored.")
                    .SetProperty(job => job.UpdatedAt, DateTime.UtcNow)
                    .SetProperty(job => job.ClaimToken, (Guid?)null),
                cancellationToken);
    }

    private static string NormalizeRequired(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"{label} is too long.");

        return trimmed;
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new InvalidOperationException("Target language is required.");

        var trimmed = language.Trim();
        if (!TranslationLanguages.IsSupported(trimmed))
            throw new InvalidOperationException("Target language is not supported.");

        return trimmed;
    }

    private static string NormalizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Translation failed.";

        var trimmed = error.Trim();
        return trimmed.Length <= TranslationFieldLimits.LastErrorMaxLength
            ? trimmed
            : trimmed[..TranslationFieldLimits.LastErrorMaxLength];
    }
}
