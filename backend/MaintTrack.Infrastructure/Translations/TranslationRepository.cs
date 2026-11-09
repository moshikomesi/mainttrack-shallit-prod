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

    public async Task<TranslationCompletionResult> CompleteAsync(
        Guid jobId,
        string sourceHash,
        string translatedText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceHash);
        ArgumentNullException.ThrowIfNull(translatedText);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var job = await _db.TranslationJobs.FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.NotFound;
        }

        if (job.Status != TranslationJobStatus.Processing)
        {
            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.Stale;
        }

        if (!string.Equals(job.SourceHash, sourceHash, StringComparison.Ordinal))
        {
            var nowMismatch = DateTime.UtcNow;
            job.Status = TranslationJobStatus.Pending;
            job.LastError = "Source version changed before the translation was stored.";
            job.UpdatedAt = nowMismatch;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.Stale;
        }

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
        else if (!string.Equals(existing.SourceHash, job.SourceHash, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return TranslationCompletionResult.Stale;
        }
        else
        {
            existing.Text = translatedText;
            existing.UpdatedAt = now;
        }

        job.Status = TranslationJobStatus.Completed;
        job.ProcessedAt = now;
        job.UpdatedAt = now;
        job.LastError = null;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TranslationCompletionResult.Completed;
    }

    public async Task FailAsync(
        Guid jobId,
        string error,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        var job = await _db.TranslationJobs.FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null)
            throw new InvalidOperationException("Translation job not found.");

        if (job.Status != TranslationJobStatus.Processing)
            throw new InvalidOperationException("Only a processing translation job can fail.");

        var now = DateTime.UtcNow;
        job.LastError = NormalizeError(error);
        job.UpdatedAt = now;

        if (job.AttemptCount >= maxAttempts)
        {
            job.Status = TranslationJobStatus.Failed;
            job.ProcessedAt = now;
        }
        else
        {
            job.Status = TranslationJobStatus.Pending;
            job.ProcessedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
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
            var rows = await _db.TranslationJobs
                .Where(job => job.Id == id
                    && (job.Status == pending
                        || (job.Status == processing && (job.UpdatedAt == null || job.UpdatedAt < staleBefore))))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(job => job.Status, processing)
                        .SetProperty(job => job.AttemptCount, job => job.AttemptCount + 1)
                        .SetProperty(job => job.UpdatedAt, now),
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
              WITH claimed AS (
                  SELECT id
                  FROM translation_jobs
                  WHERE status = @pending
                     OR (status = @processing AND (updated_at IS NULL OR updated_at < @staleBefore))
                  ORDER BY created_at, id
                  LIMIT @batch
                  FOR UPDATE SKIP LOCKED
              )
              UPDATE translation_jobs AS job
              SET status = @processing,
                  attempt_count = job.attempt_count + 1,
                  updated_at = @now
              FROM claimed
              WHERE job.id = claimed.id
              RETURNING job.id
              """
            : """
              WITH claimed AS (
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
              )
              UPDATE translation_jobs AS job
              SET status = @processing,
                  attempt_count = job.attempt_count + 1,
                  updated_at = @now
              FROM claimed
              WHERE job.id = claimed.id
              RETURNING job.id
              """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("pending", TranslationJobStatus.Pending));
        command.Parameters.Add(new NpgsqlParameter("processing", TranslationJobStatus.Processing));
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
        command.Parameters.Add(new NpgsqlParameter("staleBefore", NpgsqlDbType.TimestampTz) { Value = staleBefore });
        command.Parameters.Add(new NpgsqlParameter("batch", batchSize));
        if (tenantId is Guid scopedTenantId)
            command.Parameters.Add(new NpgsqlParameter("tenantId", scopedTenantId));

        var ids = new List<Guid>(batchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetGuid(0));

        return ids;
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
