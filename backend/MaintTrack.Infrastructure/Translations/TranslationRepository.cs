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

        var sourceHash = SourceTextHasher.Compute(request.SourceText);
        var existing = await FindJobAsync(
            tenantId,
            entityType,
            request.EntityId,
            fieldName,
            sourceHash,
            targetLanguage,
            cancellationToken);

        if (existing is not null)
            return new EnqueueTranslationResult(existing, false);

        var now = DateTime.UtcNow;
        var job = new TranslationJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = request.EntityId,
            FieldName = fieldName,
            SourceText = request.SourceText,
            SourceHash = sourceHash,
            TargetLanguage = targetLanguage,
            Status = TranslationJobStatus.Pending,
            AttemptCount = 0,
            CreatedAt = now
        };

        _db.TranslationJobs.Add(job);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new EnqueueTranslationResult(job, true);
        }
        catch (DbUpdateException)
        {
            _db.Entry(job).State = EntityState.Detached;
            var winner = await FindJobAsync(
                tenantId,
                entityType,
                request.EntityId,
                fieldName,
                sourceHash,
                targetLanguage,
                cancellationToken);

            if (winner is null)
                throw;

            return new EnqueueTranslationResult(winner, false);
        }
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

    private bool IsPostgres =>
        string.Equals(_db.Database.ProviderName, PostgresProvider, StringComparison.Ordinal);

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
