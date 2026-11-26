using System.Net;
using System.Text;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Maintenance;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Translations;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Maintenance;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using MaintTrack.Infrastructure.Translations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        var shortLease = new TranslationWorkerOptions { ProcessingLeaseSeconds = 1 };
        Assert.True(shortLease.ClaimRenewalInterval < shortLease.ProcessingLease);
        Assert.True(shortLease.ClaimRenewalInterval >= TimeSpan.FromMilliseconds(200));
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

    [Fact]
    public async Task ProviderRegistered_EnqueueDisabled_WithPendingJobs_DoesNotClaimOrCallProvider()
    {
        await EnqueueAsync("Seal leaking");
        await EnqueueAsync("Bearing noise");
        var counting = new CountingTranslationProvider();
        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ITranslationProvider>(counting);
        await using var provider = services.BuildServiceProvider();
        var worker = Worker(provider, maxAttempts: 3, enqueueEnabled: false);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Equal(0, counting.TranslateCalls);
        Assert.Equal(0, counting.DetectCalls);
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        var jobs = await db.TranslationJobs.AsNoTracking().ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, job =>
        {
            Assert.Equal(TranslationJobStatus.Pending, job.Status);
            Assert.Equal(0, job.AttemptCount);
            Assert.Null(job.ClaimToken);
        });
        Assert.Empty(await db.TranslatedTexts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ProviderRegistered_EnqueueDisabled_NeverTouchesTheRepository()
    {
        var counting = new CountingTranslationProvider();
        var services = new ServiceCollection();
        services.AddSingleton<ITranslationProvider>(counting);
        services.AddScoped<ITranslationRepository, ThrowingTranslationRepository>();
        await using var provider = services.BuildServiceProvider();
        var worker = Worker(provider, maxAttempts: 1, enqueueEnabled: false);

        // ThrowingTranslationRepository fails on any claim, so a clean 0 proves no claim was attempted.
        Assert.Equal(0, await worker.ProcessOnceAsync(CancellationToken.None));
        Assert.Equal(0, counting.TranslateCalls);

        using var cancellation = new CancellationTokenSource();
        var running = worker.RunUntilCancelledAsync(cancellation.Token);
        await Task.Delay(100);
        cancellation.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, counting.TranslateCalls);
    }

    [Fact]
    public async Task ProviderRegistered_EnqueueEnabled_StillProcessesPendingJobs()
    {
        await EnqueueAsync("Seal leaking");
        var counting = new CountingTranslationProvider();
        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ITranslationProvider>(counting);
        await using var provider = services.BuildServiceProvider();
        var worker = Worker(provider, maxAttempts: 3, enqueueEnabled: true);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(1, counting.TranslateCalls);
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        Assert.Equal(TranslationJobStatus.Completed, (await db.TranslationJobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("he:Seal leaking", (await db.TranslatedTexts.AsNoTracking().SingleAsync()).Text);
    }

    [Fact]
    public async Task WithoutProvider_EnqueueEnabledOrDisabled_LeavesJobsPending()
    {
        await EnqueueAsync("Seal leaking");
        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        await using var provider = services.BuildServiceProvider();

        foreach (var enabled in new[] { true, false })
        {
            var worker = Worker(provider, maxAttempts: 1, enqueueEnabled: enabled);
            Assert.Equal(0, await worker.ProcessOnceAsync(CancellationToken.None));
        }

        await using var scope = _provider.CreateAsyncScope();
        var job = await scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>().TranslationJobs.SingleAsync();
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public async Task WhitespaceProviderOutput_FailsJobAndDoesNotStoreBlankText()
    {
        await EnqueueAsync("Seal leaking");
        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ITranslationProvider, WhitespaceTranslationProvider>();
        await using var provider = services.BuildServiceProvider();

        var worker = Worker(provider, maxAttempts: 1);
        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        var job = await db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Failed, job.Status);
        Assert.Empty(await db.TranslatedTexts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SameLanguageDetectedByProvider_CompletesJobWithoutStoringSelfTranslation()
    {
        var entityId = Guid.NewGuid();
        await using (var enqueueScope = _provider.CreateAsyncScope())
        {
            enqueueScope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = _tenantId;
            var repository = enqueueScope.ServiceProvider.GetRequiredService<ITranslationRepository>();
            await repository.EnqueueAllAsync(
                new[]
                {
                    new EnqueueTranslationRequest("MaintenanceEntry", entityId, "Description", "אטם דולף", TranslationLanguages.Hebrew),
                    new EnqueueTranslationRequest("MaintenanceEntry", entityId, "Description", "אטם דולף", TranslationLanguages.English),
                    new EnqueueTranslationRequest("MaintenanceEntry", entityId, "Description", "אטם דולף", TranslationLanguages.Thai),
                },
                CancellationToken.None);
        }

        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ITranslationProvider>(new MetadataTranslationProvider("he"));
        await using var provider = services.BuildServiceProvider();

        var worker = Worker(provider, maxAttempts: 1);
        Assert.Equal(3, await worker.ProcessOnceAsync(CancellationToken.None));

        await using var verifyScope = provider.CreateAsyncScope();
        verifyScope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = _tenantId;
        var db = verifyScope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        var jobs = await db.TranslationJobs.AsNoTracking().ToListAsync();
        Assert.All(jobs, job => Assert.Equal(TranslationJobStatus.Completed, job.Status));

        var texts = await db.TranslatedTexts.AsNoTracking().ToListAsync();
        Assert.Equal(2, texts.Count);
        Assert.DoesNotContain(texts, item => item.TargetLanguage == TranslationLanguages.Hebrew);

        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<ITranslationRepository>();
        var rows = await verifyRepo.GetNarrativeTranslationsAsync(
            entityId,
            "MaintenanceEntry",
            "Description",
            SourceTextHasher.Compute("אטם דולף"),
            CancellationToken.None);
        var response = MaintenanceTranslationsResponse.FromRows("אטם דולף", rows);
        var hebrew = Assert.Single(response.Translations, item => item.Language == TranslationLanguages.Hebrew);
        Assert.Equal(MaintenanceTranslationPublicStatus.Unavailable, hebrew.Status);
        Assert.Null(hebrew.Text);
        Assert.Equal(
            MaintenanceTranslationPublicStatus.Completed,
            Assert.Single(response.Translations, item => item.Language == TranslationLanguages.English).Status);
        Assert.Equal(
            MaintenanceTranslationPublicStatus.Completed,
            Assert.Single(response.Translations, item => item.Language == TranslationLanguages.Thai).Status);
    }

    [Fact]
    public async Task AzureTranslatorProvider_EndToEndFlow_DetectsOnceEnqueuesTwoAndTranslatesWithoutWorkerDetectCalls()
    {
        var detectRequests = 0;
        var translateTargets = new List<string>();
        var handler = new StubAzureHandler(request =>
        {
            var uri = request.RequestUri!.ToString();
            if (uri.Contains("/detect", StringComparison.Ordinal))
            {
                detectRequests++;
                return """[{"language":"th","score":1.0,"isTranslationSupported":true}]""";
            }

            if (uri.Contains("&to=he", StringComparison.Ordinal))
            {
                translateTargets.Add("he");
                return """[{"detectedLanguage":{"language":"th","score":1.0},"translations":[{"text":"אטם דולף","to":"he"}]}]""";
            }

            if (uri.Contains("&to=en", StringComparison.Ordinal))
            {
                translateTargets.Add("en");
                return """[{"detectedLanguage":{"language":"th","score":1.0},"translations":[{"text":"Seal leaking","to":"en"}]}]""";
            }

            throw new InvalidOperationException($"Unexpected request URI: {uri}");
        });

        var azureProvider = new AzureTranslatorProvider(
            new HttpClient(handler),
            Options.Create(new AzureTranslatorOptions { ApiKey = "test-key" }));

        var machineId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var seedScope = _provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
            db.Machines.Add(new Machine
            {
                Id = machineId,
                TenantId = _tenantId,
                Name = "machine.test",
                Code = "M1",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            db.MaintenanceTypes.Add(new MaintenanceType
            {
                Id = typeId,
                TenantId = _tenantId,
                Code = "other",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddSingleton<ITranslationProvider>(azureProvider);
        await using var provider = services.BuildServiceProvider();

        Guid entryId;
        await using (var createScope = provider.CreateAsyncScope())
        {
            createScope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = _tenantId;
            var service = new MaintenanceEntryService(
                createScope.ServiceProvider.GetRequiredService<MaintTrackDbContext>(),
                createScope.ServiceProvider.GetRequiredService<ITenantContext>(),
                new StubUser(_tenantId, userId),
                new NoOpStorage(),
                NullLogger<MaintenanceEntryService>.Instance,
                Options.Create(new TranslationWorkerOptions { EnqueueEnabled = true }),
                createScope.ServiceProvider.GetRequiredService<ITranslationRepository>(),
                createScope.ServiceProvider.GetRequiredService<ITranslationProvider>());

            entryId = await service.CreateAsync(new CreateMaintenanceEntryRequest
            {
                MachineId = machineId,
                Date = new DateOnly(2026, 11, 25),
                MaintenanceTypeId = typeId,
                Description = "maintenanceLog.v2\nmaintenanceComponent.driveShaft\nซีลรั่ว",
                WorkHours = 1,
                IsSafeToOperate = true
            }, CancellationToken.None);
        }

        Assert.Equal(1, detectRequests);
        var worker = Worker(provider, maxAttempts: 2);
        Assert.Equal(2, await worker.ProcessOnceAsync(CancellationToken.None));
        Assert.Equal(1, detectRequests);
        Assert.Equal(new[] { "en", "he" }, translateTargets.OrderBy(x => x).ToArray());

        await using var readScope = provider.CreateAsyncScope();
        readScope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = _tenantId;
        var readService = new MaintenanceEntryService(
            readScope.ServiceProvider.GetRequiredService<MaintTrackDbContext>(),
            readScope.ServiceProvider.GetRequiredService<ITenantContext>(),
            new StubUser(_tenantId, userId),
            new NoOpStorage(),
            NullLogger<MaintenanceEntryService>.Instance,
            Options.Create(new TranslationWorkerOptions { EnqueueEnabled = true }),
            readScope.ServiceProvider.GetRequiredService<ITranslationRepository>(),
            readScope.ServiceProvider.GetRequiredService<ITranslationProvider>());

        var translations = await readService.GetTranslationsAsync(entryId, CancellationToken.None);
        Assert.NotNull(translations);
        Assert.Equal("ซีลรั่ว", translations!.OriginalText);
        Assert.Equal("אטם דולף", Assert.Single(translations.Translations, item => item.Language == "he").Text);
        Assert.Equal("Seal leaking", Assert.Single(translations.Translations, item => item.Language == "en").Text);
        var thai = Assert.Single(translations.Translations, item => item.Language == "th");
        Assert.Equal(MaintenanceTranslationPublicStatus.Unavailable, thai.Status);
        Assert.Null(thai.Text);
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

    private static TranslationWorker Worker(
        IServiceProvider provider,
        int maxAttempts,
        bool enqueueEnabled = true) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TranslationWorkerOptions
            {
                PollingIntervalSeconds = 1,
                BatchSize = 10,
                MaxAttempts = maxAttempts,
                ProcessingLeaseSeconds = 300,
                EnqueueEnabled = enqueueEnabled
            },
            NullLogger<TranslationWorker>.Instance);

    private sealed class CountingTranslationProvider : ITranslationProvider
    {
        public int TranslateCalls { get; private set; }

        public int DetectCalls { get; private set; }

        public Task<string> TranslateAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            TranslateCalls++;
            return Task.FromResult($"{targetLanguage}:{text}");
        }

        public Task<string?> DetectLanguageAsync(string text, CancellationToken cancellationToken)
        {
            DetectCalls++;
            return Task.FromResult<string?>(null);
        }

        public Task<TranslationResult> TranslateWithMetadataAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            TranslateCalls++;
            return Task.FromResult(new TranslationResult($"{targetLanguage}:{text}"));
        }
    }

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
            Guid claimToken,
            string sourceHash,
            string translatedText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TranslationCompletionResult> CompleteWithoutTranslationAsync(
            Guid jobId,
            Guid claimToken,
            string sourceHash,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EnqueueTranslationResult> EnqueueAsync(
            EnqueueTranslationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EnqueueTranslationResult>> EnqueueAllAsync(
            IReadOnlyList<EnqueueTranslationRequest> requests,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> FailAsync(
            Guid jobId,
            Guid claimToken,
            string error,
            int maxAttempts,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RenewClaimsAsync(
            IReadOnlyCollection<TranslationClaim> claims,
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

        public Task<IReadOnlyList<NarrativeTranslationRow>> GetNarrativeTranslationsAsync(
            Guid entityId,
            string entityType,
            string fieldName,
            string sourceHash,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class WhitespaceTranslationProvider : ITranslationProvider
    {
        public Task<string> TranslateAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult("   \t\n  ");
    }

    private sealed class MetadataTranslationProvider : ITranslationProvider
    {
        private readonly string _detectedSource;

        public MetadataTranslationProvider(string detectedSource) => _detectedSource = detectedSource;

        public Task<string> TranslateAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult($"{targetLanguage}:{text}");

        public Task<TranslationResult> TranslateWithMetadataAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TranslationResult($"{targetLanguage}:{text}", _detectedSource));
    }

    private sealed class StubAzureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _respond;

        public StubAzureHandler(Func<HttpRequestMessage, string> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = _respond(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubUser : ICurrentUserContext
    {
        public StubUser(Guid tenantId, Guid userId)
        {
            TenantId = tenantId;
            UserId = userId;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public int RoleId => (int)UserRole.Worker;
        public UserRole Role => UserRole.Worker;
        public string DisplayName => "Worker";
    }

    private sealed class NoOpStorage : IFileStorageService
    {
        public Task<string> UploadAsync(
            Stream stream,
            string fileName,
            string contentType,
            string tenantId,
            CancellationToken ct) =>
            Task.FromResult($"/uploads/{fileName}");

        public Task DeleteAsync(string fileUrl, CancellationToken ct) => Task.CompletedTask;
    }
}
