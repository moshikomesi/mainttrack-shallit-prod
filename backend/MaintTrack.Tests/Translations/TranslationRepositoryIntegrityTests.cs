using System.Data.Common;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Translations;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Infrastructure.Translations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
namespace MaintTrack.Tests.Translations;

public sealed class TranslationRepositoryIntegrityTests : IAsyncDisposable
{
    private const string Narrative = "Seal leaking. Replaced the gasket.";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _entityId = Guid.NewGuid();
    private readonly TenantContext _tenantContext;
    private readonly MaintTrackDbContext _db;
    private readonly TranslationRepository _repository;

    public TranslationRepositoryIntegrityTests()
    {
        _tenantContext = new TenantContext { TenantId = _tenantId };
        _connection.Open();
        _db = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>().UseSqlite(_connection).Options,
            _tenantContext);
        _db.Database.EnsureCreated();
        _repository = new TranslationRepository(_db, _tenantContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Complete_WithMatchingSourceTextAndHash_StoresTheTranslation()
    {
        await _repository.EnqueueAsync(Request(TranslationLanguages.English), CancellationToken.None);
        var claimed = await ClaimSingleAsync();

        var result = await _repository.CompleteAsync(
            claimed.Id,
            claimed.ClaimToken!.Value,
            claimed.SourceHash,
            "translated",
            CancellationToken.None);

        Assert.Equal(TranslationCompletionResult.Completed, result);
        Assert.Equal("translated", (await _db.TranslatedTexts.AsNoTracking().SingleAsync()).Text);
        Assert.Equal(TranslationJobStatus.Completed, (await _db.TranslationJobs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Complete_WithMismatchedSourceTextAndHash_FailsTheCurrentClaimTerminally()
    {
        await SeedTamperedJobAsync(_db);
        var claimed = await ClaimSingleAsync();

        var result = await _repository.CompleteAsync(
            claimed.Id,
            claimed.ClaimToken!.Value,
            claimed.SourceHash,
            "translation of the wrong text",
            CancellationToken.None);

        Assert.Equal(TranslationCompletionResult.Stale, result);
        Assert.Empty(await _db.TranslatedTexts.AsNoTracking().ToListAsync());
        var job = await _db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Failed, job.Status);
        Assert.Equal(claimed.ClaimToken, job.ClaimToken);
        Assert.False(string.IsNullOrWhiteSpace(job.LastError));
        Assert.NotNull(job.ProcessedAt);

        await _db.TranslationJobs.ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.UpdatedAt, DateTime.UtcNow.AddHours(-1)));
        Assert.Empty(await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    [Fact]
    public async Task Complete_WithMismatchedSourceTextAndAnotherClaimToken_LeavesTheJobUntouched()
    {
        await SeedTamperedJobAsync(_db);
        var claimed = await ClaimSingleAsync();
        var before = await _db.TranslationJobs.AsNoTracking().SingleAsync();

        var result = await _repository.CompleteAsync(
            claimed.Id,
            Guid.NewGuid(),
            claimed.SourceHash,
            "translation of the wrong text",
            CancellationToken.None);

        Assert.Equal(TranslationCompletionResult.Stale, result);
        Assert.Empty(await _db.TranslatedTexts.AsNoTracking().ToListAsync());
        var job = await _db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Processing, job.Status);
        Assert.Equal(claimed.ClaimToken, job.ClaimToken);
        Assert.Null(job.LastError);
        Assert.Null(job.ProcessedAt);
        Assert.Equal(before.UpdatedAt, job.UpdatedAt);
    }

    [Fact]
    public async Task Complete_WithMismatchedSourceText_DoesNotFailAClaimReleasedBeforeTheUpdate()
    {
        var releaseClaim = new ReleaseClaimBeforeUpdate();
        await using var db = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(releaseClaim)
                .Options,
            _tenantContext);
        var repository = new TranslationRepository(db, _tenantContext);
        await SeedTamperedJobAsync(db);
        var claimed = Assert.Single(await repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));
        releaseClaim.Armed = true;

        var result = await repository.CompleteAsync(
            claimed.Id,
            claimed.ClaimToken!.Value,
            claimed.SourceHash,
            "translation of the wrong text",
            CancellationToken.None);

        Assert.True(releaseClaim.Released);
        Assert.Equal(TranslationCompletionResult.Stale, result);
        Assert.Empty(await db.TranslatedTexts.AsNoTracking().ToListAsync());
        var job = await db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Null(job.ClaimToken);
        Assert.Null(job.LastError);
        Assert.Null(job.ProcessedAt);
    }

    [Fact]
    public async Task Complete_WithAnOutdatedClaimToken_RemainsStale()
    {
        await _repository.EnqueueAsync(Request(TranslationLanguages.Thai), CancellationToken.None);
        var claimed = await ClaimSingleAsync();

        var stale = await _repository.CompleteAsync(
            claimed.Id,
            Guid.NewGuid(),
            claimed.SourceHash,
            "late text",
            CancellationToken.None);

        Assert.Equal(TranslationCompletionResult.Stale, stale);
        Assert.Empty(await _db.TranslatedTexts.AsNoTracking().ToListAsync());
        var job = await _db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Processing, job.Status);
        Assert.Equal(claimed.ClaimToken, job.ClaimToken);

        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(
                claimed.Id,
                claimed.ClaimToken!.Value,
                claimed.SourceHash,
                "current text",
                CancellationToken.None));
    }

    [Fact]
    public async Task EnqueueAll_PersistsAllThreeLanguagesTogether()
    {
        var results = await _repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.True(result.Created));
        var jobs = await _db.TranslationJobs.AsNoTracking().ToListAsync();
        Assert.Equal(
            new[] { TranslationLanguages.English, TranslationLanguages.Hebrew, TranslationLanguages.Thai },
            jobs.Select(job => job.TargetLanguage).OrderBy(language => language).ToArray());
        Assert.All(jobs, job =>
        {
            Assert.Equal(_tenantId, job.TenantId);
            Assert.Equal(_entityId, job.EntityId);
            Assert.Equal(SourceTextHasher.Compute(Narrative), job.SourceHash);
        });
    }

    [Fact]
    public async Task EnqueueAll_Repeated_ReturnsTheExistingJobs()
    {
        var first = await _repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None);
        var second = await _repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None);

        Assert.All(second, result => Assert.False(result.Created));
        Assert.Equal(
            first.Select(result => result.Job.Id).OrderBy(id => id),
            second.Select(result => result.Job.Id).OrderBy(id => id));
        Assert.Equal(3, await _db.TranslationJobs.CountAsync());
    }

    [Theory]
    [InlineData(TranslationLanguages.English)]
    [InlineData(TranslationLanguages.Thai)]
    public async Task EnqueueAll_WhenALaterLanguageInsertFails_KeepsNoPartialSet(string failingLanguage)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_translation_language BEFORE INSERT ON translation_jobs " +
            $"WHEN NEW.target_language = '{failingLanguage}' " +
            "BEGIN SELECT RAISE(ABORT, 'simulated translation insert failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            _repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None));

        Assert.Empty(await _db.TranslationJobs.IgnoreQueryFilters().AsNoTracking().ToListAsync());

        await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_translation_language;");
        var retried = await _repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None);
        Assert.All(retried, result => Assert.True(result.Created));
        Assert.Equal(3, await _db.TranslationJobs.CountAsync());
    }

    [Fact]
    public async Task EnqueueAll_RetriesAfterPostgresDeadlock_AndPersistsOneJobPerLanguage()
    {
        var interceptor = new SaveChangesFaultInterceptor(deadlocksBeforeSuccess: 1);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection, interceptor);
        await db.Database.EnsureCreatedAsync();
        var repository = new TranslationRepository(db, _tenantContext);

        var results = await repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None);

        Assert.Equal(2, interceptor.SaveAttempts);
        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.True(result.Created));
        Assert.Equal(3, await db.TranslationJobs.CountAsync());
        Assert.Equal(3, (await db.TranslationJobs.AsNoTracking().ToListAsync())
            .Select(job => job.TargetLanguage)
            .Distinct()
            .Count());
    }

    [Fact]
    public async Task EnqueueAll_DoesNotRetryNonDeadlockFailures()
    {
        var interceptor = new SaveChangesFaultInterceptor(nonDeadlockOnce: true);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection, interceptor);
        await db.Database.EnsureCreatedAsync();
        var repository = new TranslationRepository(db, _tenantContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None));

        Assert.Equal("simulated non-deadlock failure", exception.Message);
        Assert.Equal(1, interceptor.SaveAttempts);
        Assert.Empty(await db.TranslationJobs.IgnoreQueryFilters().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task EnqueueAll_PropagatesDeadlockAfterThreeAttempts()
    {
        var interceptor = new SaveChangesFaultInterceptor(deadlocksBeforeSuccess: 3);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection, interceptor);
        await db.Database.EnsureCreatedAsync();
        var repository = new TranslationRepository(db, _tenantContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None));

        Assert.Equal(3, interceptor.SaveAttempts);
        Assert.True(ContainsPostgresDeadlock(exception));
        Assert.Empty(await db.TranslationJobs.IgnoreQueryFilters().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task EnqueueAll_FinalDirectDbUpdateDeadlock_PropagatesEvenWhenWinnersExist()
    {
        var hideWinners = new HideTranslationJobReadsInterceptor();
        var saveFaults = new SaveChangesFaultInterceptor(
            deadlocksBeforeSuccess: 3,
            wrapDeadlockInInvalidOperation: false,
            revealWinnersOnFinalDeadlock: hideWinners)
        {
            SuppressFaults = true
        };
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection, hideWinners, saveFaults);
        await db.Database.EnsureCreatedAsync();

        // Winners exist. Finds during the attempt are hidden so SaveChanges still runs.
        // Revealing them on the final deadlock means the unique-conflict path would otherwise succeed.
        foreach (var language in TranslationLanguages.Supported)
        {
            db.TranslationJobs.Add(new TranslationJob
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                EntityType = TranslatableFields.MaintenanceEntry,
                EntityId = _entityId,
                FieldName = TranslatableFields.Description,
                SourceText = Narrative,
                SourceHash = SourceTextHasher.Compute(Narrative),
                TargetLanguage = language,
                Status = TranslationJobStatus.Pending,
                AttemptCount = 0,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        saveFaults.SuppressFaults = false;
        hideWinners.Active = true;
        var repository = new TranslationRepository(db, _tenantContext);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.EnqueueAllAsync(AllLanguages(), CancellationToken.None));

        Assert.Equal(3, saveFaults.SaveAttempts);
        Assert.False(hideWinners.Active);
        Assert.True(ContainsPostgresDeadlock(exception));
        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("40P01", ((PostgresException)exception.InnerException!).SqlState);
        Assert.Equal(3, await db.TranslationJobs.IgnoreQueryFilters().CountAsync());
    }

    private static bool ContainsPostgresDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == "40P01")
                return true;
        }

        return false;
    }

    private MaintTrackDbContext CreateDb(SqliteConnection connection, params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<MaintTrackDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptors)
                .Options,
            _tenantContext);

    private EnqueueTranslationRequest Request(string language) =>
        new(TranslatableFields.MaintenanceEntry, _entityId, TranslatableFields.Description, Narrative, language);

    private IReadOnlyList<EnqueueTranslationRequest> AllLanguages() =>
        TranslationLanguages.Supported.Select(Request).ToArray();

    private async Task<TranslationJob> ClaimSingleAsync() =>
        Assert.Single(await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));

    private async Task SeedTamperedJobAsync(MaintTrackDbContext db)
    {
        db.TranslationJobs.Add(new TranslationJob
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EntityType = TranslatableFields.MaintenanceEntry,
            EntityId = _entityId,
            FieldName = TranslatableFields.Description,
            SourceText = "Tampered source text",
            SourceHash = SourceTextHasher.Compute(Narrative),
            TargetLanguage = TranslationLanguages.English,
            Status = TranslationJobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Simulates the lease expiring between the completion read and the integrity-failure update.
    /// </summary>
    private sealed class ReleaseClaimBeforeUpdate : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public bool Released { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed
                && !Released
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("translation_jobs", StringComparison.Ordinal))
            {
                await using var release = command.Connection!.CreateCommand();
                release.Transaction = command.Transaction;
                release.CommandText = "UPDATE translation_jobs SET status = 'pending', claim_token = NULL";
                await release.ExecuteNonQueryAsync(cancellationToken);
                Released = true;
            }

            return result;
        }
    }

    /// <summary>
    /// Hides existing translation_jobs rows from reads until revealed, so SaveChanges still runs.
    /// </summary>
    private sealed class HideTranslationJobReadsInterceptor : DbCommandInterceptor
    {
        public bool Active { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            HideIfNeeded(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            HideIfNeeded(command);
            return ValueTask.FromResult(result);
        }

        private void HideIfNeeded(DbCommand command)
        {
            if (!Active)
                return;

            if (command.CommandText.Contains("translation_jobs", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText += " AND 1 = 0";
            }
        }
    }

    /// <summary>
    /// Throws the observed EF/Npgsql deadlock chain during SaveChanges, or a non-deadlock failure.
    /// </summary>
    private sealed class SaveChangesFaultInterceptor : SaveChangesInterceptor
    {
        private readonly int _deadlocksBeforeSuccess;
        private readonly bool _nonDeadlockOnce;
        private readonly bool _wrapDeadlockInInvalidOperation;
        private readonly HideTranslationJobReadsInterceptor? _revealWinnersOnFinalDeadlock;
        private int _deadlocksThrown;

        public SaveChangesFaultInterceptor(
            int deadlocksBeforeSuccess = 0,
            bool nonDeadlockOnce = false,
            bool wrapDeadlockInInvalidOperation = true,
            HideTranslationJobReadsInterceptor? revealWinnersOnFinalDeadlock = null)
        {
            _deadlocksBeforeSuccess = deadlocksBeforeSuccess;
            _nonDeadlockOnce = nonDeadlockOnce;
            _wrapDeadlockInInvalidOperation = wrapDeadlockInInvalidOperation;
            _revealWinnersOnFinalDeadlock = revealWinnersOnFinalDeadlock;
        }

        public int SaveAttempts { get; private set; }

        public bool SuppressFaults { get; set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            FaultIfNeeded();
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            FaultIfNeeded();
            return ValueTask.FromResult(result);
        }

        private void FaultIfNeeded()
        {
            if (SuppressFaults)
                return;

            SaveAttempts++;
            if (_nonDeadlockOnce && SaveAttempts == 1)
                throw new InvalidOperationException("simulated non-deadlock failure");
            if (_deadlocksThrown < _deadlocksBeforeSuccess)
            {
                _deadlocksThrown++;
                if (_revealWinnersOnFinalDeadlock is not null
                    && _deadlocksThrown == _deadlocksBeforeSuccess)
                {
                    _revealWinnersOnFinalDeadlock.Active = false;
                }

                throw CreateDeadlockException();
            }
        }

        private Exception CreateDeadlockException()
        {
            var postgres = new PostgresException(
                "deadlock detected",
                "ERROR",
                "ERROR",
                "40P01");
            var update = new DbUpdateException(
                "An error occurred while saving the entity changes. See the inner exception for details.",
                postgres);
            if (_wrapDeadlockInInvalidOperation)
            {
                return new InvalidOperationException(
                    "An exception has been raised that is likely due to a transient failure.",
                    update);
            }

            return update;
        }
    }
}
