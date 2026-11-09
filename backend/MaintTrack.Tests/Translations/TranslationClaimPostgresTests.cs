using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Translations;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Infrastructure.Translations;
using MaintTrack.Tests.Clearance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MaintTrack.Tests.Translations;

/// <summary>
/// Concurrent claims against PostgreSQL. Runs only when <c>MAINTTRACK_CLEARANCE_PG_SERVER</c>
/// points at a server that can create a throwaway database. The database is dropped afterwards.
/// </summary>
public sealed class TranslationClaimPostgresTests : IAsyncLifetime
{
    private readonly string? _server = Environment.GetEnvironmentVariable(ClearancePostgresConcurrencyTests.ServerVariable);
    private readonly string _database = $"mainttrack_translation_test_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
            return;

        await using (var admin = new NpgsqlConnection(_server))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_server)
        {
            Database = _database,
            Pooling = false
        }.ConnectionString;

        await using var db = Open(null).Db;
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
            return;

        await using var admin = new NpgsqlConnection(_server);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ConcurrentClaims_TakeDisjointJobs()
    {
        await ResetAsync();
        await SeedAsync(40);

        var tasks = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => ClaimAsync(10)))
            .ToArray();
        var claimed = await Task.WhenAll(tasks);
        var ids = claimed.SelectMany(batch => batch.Select(job => job.Id)).ToArray();

        Assert.Equal(40, ids.Length);
        Assert.Equal(40, ids.Distinct().Count());
        Assert.All(claimed.SelectMany(batch => batch), job =>
        {
            Assert.Equal(TranslationJobStatus.Processing, job.Status);
            Assert.Equal(1, job.AttemptCount);
            Assert.NotNull(job.ClaimToken);
        });
        Assert.Equal(40, claimed.SelectMany(batch => batch).Select(job => job.ClaimToken).Distinct().Count());
    }

    [PostgresFact]
    public async Task ExpiredClaim_RejectsThePreviousOwnerOnPostgres()
    {
        await ResetAsync();
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        EnqueueTranslationResult enqueued;
        await using (var opened = Open(tenantId))
        {
            enqueued = await opened.Repository.EnqueueAsync(
                new EnqueueTranslationRequest(
                    TranslatableFields.MaintenanceEntry,
                    entityId,
                    TranslatableFields.Description,
                    "Seal leaking",
                    TranslationLanguages.Hebrew),
                CancellationToken.None);
        }

        await using var firstOwner = Open(null);
        var first = Assert.Single(await firstOwner.Repository.ClaimPendingAsync(1, TimeSpan.FromSeconds(1), CancellationToken.None));
        var firstToken = first.ClaimToken!.Value;

        await using (var editor = Open(null))
        {
            await editor.Db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE translation_jobs SET updated_at = {DateTime.UtcNow.AddHours(-1)} WHERE id = {first.Id}");
        }

        await using var secondOwner = Open(null);
        var second = Assert.Single(await secondOwner.Repository.ClaimPendingAsync(1, TimeSpan.FromSeconds(1), CancellationToken.None));
        var secondToken = second.ClaimToken!.Value;
        Assert.NotEqual(firstToken, secondToken);

        await using var staleOwner = Open(tenantId);
        Assert.Equal(
            TranslationCompletionResult.Stale,
            await staleOwner.Repository.CompleteAsync(
                first.Id,
                firstToken,
                enqueued.Job.SourceHash,
                "stale text",
                CancellationToken.None));
        Assert.False(await staleOwner.Repository.FailAsync(first.Id, firstToken, "stale failure", 5, CancellationToken.None));

        var owned = await secondOwner.Db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Processing, owned.Status);
        Assert.Equal(secondToken, owned.ClaimToken);
        Assert.Null(owned.LastError);
        Assert.Empty(await secondOwner.Db.TranslatedTexts.AsNoTracking().ToListAsync());

        Assert.Equal(
            TranslationCompletionResult.Completed,
            await secondOwner.Repository.CompleteAsync(
                second.Id,
                secondToken,
                second.SourceHash,
                "current text",
                CancellationToken.None));
        Assert.Equal(
            TranslationCompletionResult.Stale,
            await staleOwner.Repository.CompleteAsync(
                first.Id,
                firstToken,
                enqueued.Job.SourceHash,
                "late overwrite",
                CancellationToken.None));
        Assert.False(await staleOwner.Repository.FailAsync(first.Id, firstToken, "late failure", 5, CancellationToken.None));

        Assert.Equal("current text", (await secondOwner.Db.TranslatedTexts.AsNoTracking().SingleAsync()).Text);
        Assert.Equal(TranslationJobStatus.Completed, (await secondOwner.Db.TranslationJobs.AsNoTracking().SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task SlowBatch_DoesNotLetWaitingJobsExpire()
    {
        await ResetAsync();
        var tenantId = Guid.NewGuid();
        await using (var opened = Open(tenantId))
        {
            foreach (var source in new[] { "slow fault", "second fault", "third fault" })
            {
                var enqueued = await opened.Repository.EnqueueAsync(
                    new EnqueueTranslationRequest(
                        TranslatableFields.MaintenanceEntry,
                        Guid.NewGuid(),
                        TranslatableFields.Description,
                        source,
                        TranslationLanguages.Hebrew),
                    CancellationToken.None);
                Assert.True(enqueued.Created);
            }
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseNpgsql(_connectionString));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ITranslationProvider>(new HoldingProvider(started, release));
        await using var provider = services.BuildServiceProvider();
        var worker = new TranslationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TranslationWorkerOptions
            {
                PollingIntervalSeconds = 30,
                BatchSize = 10,
                MaxAttempts = 3,
                ProcessingLeaseSeconds = 1
            },
            NullLogger<TranslationWorker>.Instance);

        var processing = worker.ProcessOnceAsync(CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            Assert.Empty(await ClaimAsync(10, TimeSpan.FromSeconds(1)));
            release.TrySetResult();
            Assert.Equal(3, await processing.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            release.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await using var check = Open(tenantId);
        var jobs = await check.Db.TranslationJobs.AsNoTracking().ToListAsync();
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, job =>
        {
            Assert.Equal(TranslationJobStatus.Completed, job.Status);
            Assert.Equal(1, job.AttemptCount);
        });
        Assert.Equal(3, await check.Db.TranslatedTexts.CountAsync());
    }

    [PostgresFact]
    public async Task LockedPendingRows_AreSkippedInsteadOfBlocking()
    {
        await ResetAsync();
        await SeedAsync(8);

        await using var blocker = Open(null);
        await using var transaction = await blocker.Db.Database.BeginTransactionAsync();
        await blocker.Db.Database.ExecuteSqlRawAsync("SELECT id FROM translation_jobs FOR UPDATE");

        var claim = Task.Run(() => ClaimAsync(20));
        var finished = await Task.WhenAny(claim, Task.Delay(TimeSpan.FromSeconds(3)));
        var finishedInTime = finished == claim;
        await transaction.RollbackAsync();

        Assert.True(finishedInTime, "Claim waited on rows another transaction already locked.");
        Assert.Empty(await claim);

        var afterUnlock = await ClaimAsync(20);
        Assert.Equal(8, afterUnlock.Count);
    }

    [PostgresFact]
    public async Task ConcurrentEnqueue_CreatesOneJob()
    {
        await ResetAsync();
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var request = new EnqueueTranslationRequest(
            "MaintenanceEntry",
            entityId,
            "Description",
            "Seal leaking",
            TranslationLanguages.Hebrew);

        var results = await Task.WhenAll(
            Task.Run(() => EnqueueAsync(tenantId, request)),
            Task.Run(() => EnqueueAsync(tenantId, request)));

        Assert.Single(results, result => result.Created);
        Assert.Single(results, result => !result.Created);
        Assert.Equal(results[0].Job.Id, results[1].Job.Id);

        await using var db = Open(tenantId);
        Assert.Equal(1, await db.Db.TranslationJobs.CountAsync());
    }

    private async Task ResetAsync()
    {
        await using var db = Open(null);
        await db.Db.Database.ExecuteSqlRawAsync("TRUNCATE translation_jobs, translated_texts");
    }

    private async Task SeedAsync(int count)
    {
        var tenantId = Guid.NewGuid();
        await using var opened = Open(tenantId);
        for (var index = 0; index < count; index++)
        {
            var result = await opened.Repository.EnqueueAsync(
                new EnqueueTranslationRequest(
                    "MaintenanceEntry",
                    Guid.NewGuid(),
                    "Description",
                    $"fault {index}",
                    TranslationLanguages.Hebrew),
                CancellationToken.None);
            Assert.True(result.Created);
        }
    }

    private Task<IReadOnlyList<TranslationJob>> ClaimAsync(int batchSize) =>
        ClaimAsync(batchSize, TimeSpan.FromMinutes(5));

    private async Task<IReadOnlyList<TranslationJob>> ClaimAsync(int batchSize, TimeSpan lease)
    {
        await using var opened = Open(null);
        return await opened.Repository.ClaimPendingAsync(batchSize, lease, CancellationToken.None);
    }

    private async Task<EnqueueTranslationResult> EnqueueAsync(Guid tenantId, EnqueueTranslationRequest request)
    {
        await using var opened = Open(tenantId);
        return await opened.Repository.EnqueueAsync(request, CancellationToken.None);
    }

    private Opened Open(Guid? tenantId)
    {
        var tenant = new TenantContext { TenantId = tenantId };
        var db = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
        return new Opened(db, new TranslationRepository(db, tenant));
    }

    private sealed class Opened : IAsyncDisposable
    {
        public Opened(MaintTrackDbContext db, TranslationRepository repository)
        {
            Db = db;
            Repository = repository;
        }

        public MaintTrackDbContext Db { get; }

        public TranslationRepository Repository { get; }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class HoldingProvider : ITranslationProvider
    {
        private readonly TaskCompletionSource _started;
        private readonly TaskCompletionSource _release;
        private int _calls;

        public HoldingProvider(TaskCompletionSource started, TaskCompletionSource release)
        {
            _started = started;
            _release = release;
        }

        public async Task<string> TranslateAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return $"{targetLanguage}:{text}";
        }
    }
}
