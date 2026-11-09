using MaintTrack.Application.Translations;
using MaintTrack.Domain.Translations;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Infrastructure.Translations;
using MaintTrack.Tests.Clearance;
using Microsoft.EntityFrameworkCore;
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
        });
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

    private async Task<IReadOnlyList<TranslationJob>> ClaimAsync(int batchSize)
    {
        await using var opened = Open(null);
        return await opened.Repository.ClaimPendingAsync(batchSize, TimeSpan.FromMinutes(5), CancellationToken.None);
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
}
