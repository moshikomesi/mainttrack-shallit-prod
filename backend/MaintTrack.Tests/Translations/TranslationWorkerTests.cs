using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Translations;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Infrastructure.Translations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaintTrack.Tests.Translations;

public sealed class TranslationWorkerTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ServiceProvider _provider;

    public TranslationWorkerTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ITranslationProvider, ScriptedTranslationProvider>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public void PollingInterval_NeverBecomesABusyLoop()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), new TranslationWorkerOptions { PollingIntervalSeconds = 0 }.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), new TranslationWorkerOptions().PollingInterval);
        Assert.Equal(1, new TranslationWorkerOptions { BatchSize = 0 }.NormalizedBatchSize);
        Assert.Equal(1, new TranslationWorkerOptions { MaxAttempts = 0 }.NormalizedMaxAttempts);
    }

    [Fact]
    public async Task WithoutProvider_LeavesJobsPending()
    {
        await EnqueueAsync("Seal leaking");
        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        await using var provider = services.BuildServiceProvider();
        var worker = Worker(provider, maxAttempts: 1);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        await using var scope = _provider.CreateAsyncScope();
        var job = await scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>().TranslationJobs.SingleAsync();
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public async Task ProviderException_DoesNotStopLaterJobs()
    {
        await EnqueueAsync("explode now");
        await EnqueueAsync("bearing noise");
        var worker = Worker(_provider, maxAttempts: 1);

        var firstPass = await worker.ProcessOnceAsync(CancellationToken.None);
        var secondPass = await worker.ProcessOnceAsync(CancellationToken.None);
        await EnqueueAsync("cooling fan");
        var thirdPass = await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(2, firstPass);
        Assert.Equal(0, secondPass);
        Assert.Equal(1, thirdPass);

        await using var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = _tenantId;
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        var jobs = await db.TranslationJobs.AsNoTracking().ToListAsync();
        Assert.Equal(TranslationJobStatus.Failed, jobs.Single(job => job.SourceText == "explode now").Status);
        Assert.Equal("provider exploded", jobs.Single(job => job.SourceText == "explode now").LastError);
        Assert.Equal(TranslationJobStatus.Completed, jobs.Single(job => job.SourceText == "bearing noise").Status);
        Assert.Equal(TranslationJobStatus.Completed, jobs.Single(job => job.SourceText == "cooling fan").Status);
        var translations = await db.TranslatedTexts.AsNoTracking().ToListAsync();
        Assert.Equal("he:bearing noise", Assert.Single(translations, item => item.Text == "he:bearing noise").Text);
        Assert.Equal("bearing noise", jobs.Single(job => job.SourceText == "bearing noise").SourceText);
    }

    [Fact]
    public async Task IterationFailure_DoesNotTerminateTheWorker()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITranslationProvider, ScriptedTranslationProvider>();
        services.AddScoped<ITranslationRepository, ThrowingTranslationRepository>();
        await using var provider = services.BuildServiceProvider();
        var worker = Worker(provider, maxAttempts: 1);
        using var cancellation = new CancellationTokenSource();

        var running = worker.RunUntilCancelledAsync(cancellation.Token);
        await Task.Delay(100);
        cancellation.Cancel();

        await running.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task EnqueueAsync(string sourceText)
    {
        await using var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = _tenantId;
        var repository = scope.ServiceProvider.GetRequiredService<ITranslationRepository>();
        await repository.EnqueueAsync(
            new EnqueueTranslationRequest(
                "MaintenanceEntry",
                Guid.NewGuid(),
                "Description",
                sourceText,
                TranslationLanguages.Hebrew),
            CancellationToken.None);
    }

    private static TranslationWorker Worker(IServiceProvider provider, int maxAttempts) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TranslationWorkerOptions
            {
                PollingIntervalSeconds = 1,
                BatchSize = 10,
                MaxAttempts = maxAttempts,
                ProcessingLeaseSeconds = 300
            },
            NullLogger<TranslationWorker>.Instance);

    private sealed class ScriptedTranslationProvider : ITranslationProvider
    {
        public Task<string> TranslateAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            if (text.Contains("explode", StringComparison.Ordinal))
                throw new InvalidOperationException("provider exploded");

            return Task.FromResult($"{targetLanguage}:{text}");
        }
    }

    private sealed class ThrowingTranslationRepository : ITranslationRepository
    {
        public Task<IReadOnlyList<TranslationJob>> ClaimPendingAsync(
            int batchSize,
            TimeSpan processingLease,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("claim failed");

        public Task<TranslationCompletionResult> CompleteAsync(
            Guid jobId,
            string sourceHash,
            string translatedText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EnqueueTranslationResult> EnqueueAsync(
            EnqueueTranslationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FailAsync(
            Guid jobId,
            string error,
            int maxAttempts,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TranslatedText?> FindTranslationAsync(
            Guid entityId,
            string entityType,
            string fieldName,
            string sourceHash,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
