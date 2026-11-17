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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaintTrack.Tests.Translations;

public sealed class MaintenanceTranslationEnqueueTests : IAsyncLifetime
{
    private const string Narrative = "Seal leaking. Replaced the gasket.";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _machineId = Guid.NewGuid();
    private readonly Guid _typeId = Guid.NewGuid();
    private readonly TenantContext _tenantContext;
    private readonly MaintTrackDbContext _db;
    private readonly TranslationRepository _repository;

    public MaintenanceTranslationEnqueueTests()
    {
        _tenantContext = new TenantContext { TenantId = _tenantId };
        _connection.Open();
        var options = new DbContextOptionsBuilder<MaintTrackDbContext>().UseSqlite(_connection).Options;
        _db = new MaintTrackDbContext(options, _tenantContext);
        _repository = new TranslationRepository(_db, _tenantContext);
    }

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        _db.Machines.Add(new Machine
        {
            Id = _machineId,
            TenantId = _tenantId,
            Name = "machine.test",
            Code = "M1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.MaintenanceTypes.Add(new MaintenanceType
        {
            Id = _typeId,
            TenantId = _tenantId,
            Code = "other",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("maintenanceComponent.bearing")]
    [InlineData("maintenanceComponent.other")]
    [InlineData("Hydraulic pump")]
    [InlineData("Other: Hydraulic pump")]
    public async Task CreateAsync_EnqueuesNarrativeOnly_ForEveryComponentLine(string component)
    {
        var description = $"maintenanceLog.v2\n{component}\n{Narrative}";
        var entryId = await CreateAsync(description, EnabledService(_repository));

        var jobs = await JobsForAsync(entryId);
        Assert.Equal(3, jobs.Count);
        Assert.Equal(
            new[] { TranslationLanguages.English, TranslationLanguages.Hebrew, TranslationLanguages.Thai },
            jobs.Select(job => job.TargetLanguage).OrderBy(language => language).ToArray());
        Assert.All(jobs, job => Assert.Equal(Narrative, job.SourceText));
        Assert.All(jobs, job => Assert.DoesNotContain(component, job.SourceText, StringComparison.Ordinal));
        Assert.Equal(description, (await EntryAsync(entryId)).Description);
    }

    [Fact]
    public async Task CreateAsync_EnqueuesMarkedV1Narrative_IncludingComponentLikeFirstLine()
    {
        const string description = "maintenanceLog.v1\nHydraulic pump\nSeal leaking.";
        var entryId = await CreateAsync(description, EnabledService(_repository));

        var jobs = await JobsForAsync(entryId);
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, job => Assert.Equal("Hydraulic pump\nSeal leaking.", job.SourceText));
    }

    [Fact]
    public async Task RepeatedEnqueue_DoesNotCreateDuplicateJobs()
    {
        const string description = "maintenanceLog.v2\nmaintenanceComponent.bearing\nSeal leaking.";
        var service = EnabledService(_repository);
        var entryId = await CreateAsync(description, service);

        await service.EnqueueDescriptionTranslationsAsync(entryId, description);

        var jobs = await JobsForAsync(entryId);
        Assert.Equal(3, jobs.Count);
        Assert.Equal(3, jobs.Select(job => job.TargetLanguage).Distinct().Count());
    }

    [Fact]
    public async Task EnqueuedJobs_StayInsideTheOwningTenant()
    {
        const string description = "maintenanceLog.v2\nHydraulic pump\nSeal leaking.";
        var entryId = await CreateAsync(description, EnabledService(_repository));
        var otherTenant = Guid.NewGuid();

        try
        {
            _tenantContext.TenantId = otherTenant;
            Assert.Empty(await JobsForAsync(entryId));
            Assert.Empty(await _repository.GetNarrativeTranslationsAsync(
                entryId,
                TranslatableFields.MaintenanceEntry,
                TranslatableFields.Description,
                SourceTextHasher.Compute("Seal leaking."),
                CancellationToken.None));
        }
        finally
        {
            _tenantContext.TenantId = _tenantId;
        }

        Assert.Equal(3, await _db.TranslationJobs.IgnoreQueryFilters().CountAsync(job => job.EntityId == entryId));
    }

    [Theory]
    [InlineData("maintenanceLog.v2\nOther: Hydraulic pump")]
    [InlineData("Seal leaking. Replaced the gasket.")]
    [InlineData("Hydraulic pump\nSeal leaking. Replaced the gasket.")]
    public async Task CreateAsync_DoesNotQueue_WhenNarrativeIsAbsentOrUnmarked(string description)
    {
        var probe = new ProbeTranslationRepository();
        var entryId = await CreateAsync(description, EnabledService(probe));

        Assert.Equal(0, probe.Calls);
        Assert.Empty(await JobsForAsync(entryId));
        Assert.Equal(description, (await EntryAsync(entryId)).Description);
    }

    [Fact]
    public async Task DisabledFlag_DoesNotTouchTranslationStorage()
    {
        var description = $"maintenanceLog.v2\nHydraulic pump\n{Narrative}";
        var probe = new ProbeTranslationRepository();
        var service = new MaintenanceEntryService(
            _db,
            _tenantContext,
            new StubUser(_tenantId),
            new TrackingFileStorage(),
            NullLogger<MaintenanceEntryService>.Instance,
            Options.Create(new TranslationWorkerOptions { EnqueueEnabled = false }),
            probe);

        var entryId = await CreateAsync(description, service);
        var translations = await service.GetTranslationsAsync(entryId, CancellationToken.None);

        Assert.Equal(0, probe.Calls);
        Assert.Empty(await JobsForAsync(entryId));
        Assert.NotNull(translations);
        Assert.Equal(Narrative, translations!.OriginalText);
        Assert.All(translations.Translations, item =>
        {
            Assert.Equal(MaintenanceTranslationPublicStatus.Unavailable, item.Status);
            Assert.Null(item.Text);
        });
    }

    [Fact]
    public async Task EnqueueFailure_KeepsTheSavedReportAuditAndPhotos()
    {
        const string description = "maintenanceLog.v2\nOther: Hydraulic pump\nSeal leaking. Replaced the gasket.";
        var files = new TrackingFileStorage();
        var logger = new CountingLogger();
        var service = new MaintenanceEntryService(
            _db,
            _tenantContext,
            new StubUser(_tenantId),
            files,
            logger,
            Options.Create(new TranslationWorkerOptions { EnqueueEnabled = true }),
            new ProbeTranslationRepository { ThrowOnEnqueue = true });

        var entryId = await service.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = _machineId,
            Date = new DateOnly(2026, 11, 13),
            MaintenanceTypeId = _typeId,
            Description = description,
            ImageUrl = "/uploads/primary.png",
            AdditionalImageUrls = new[] { "/uploads/extra-1.png", "/uploads/extra-2.png" },
            WorkHours = 1.5m,
            IsSafeToOperate = true
        }, CancellationToken.None);

        var saved = await _db.MaintenanceEntries
            .Include(entry => entry.AdditionalImages)
            .SingleAsync(entry => entry.Id == entryId);

        Assert.Equal(description, saved.Description);
        Assert.Equal("/uploads/primary.png", saved.ImageUrl);
        Assert.Equal(new[] { "/uploads/extra-1.png", "/uploads/extra-2.png" }, saved.AdditionalImages
            .OrderBy(image => image.SortOrder)
            .Select(image => image.ImageUrl)
            .ToArray());
        Assert.Empty(files.Deleted);
        Assert.Equal(1, logger.ErrorCount);
        Assert.Equal(
            1,
            await _db.AuditLogs.CountAsync(log =>
                log.EntityId == entryId && log.Action == "maintenance_created" && log.EntityName == "MaintenanceEntry"));
        Assert.Empty(await JobsForAsync(entryId));
    }

    [Fact]
    public async Task PostSaveEnqueue_DoesNotUseTheRequestCancellationToken()
    {
        using var request = new CancellationTokenSource();
        var probe = new ProbeTranslationRepository();
        var service = EnabledService(probe);

        await service.CreateAsync(Request("maintenanceLog.v2\nHydraulic pump\nSeal leaking."), request.Token);

        Assert.Equal(1, probe.Calls);
        Assert.NotNull(probe.ReceivedEnqueueToken);
        Assert.False(probe.ReceivedEnqueueToken!.Value.CanBeCanceled);
    }

    [Fact]
    public async Task RequestCancelledAfterReportSave_StillPersistsAllThreeJobs()
    {
        using var request = new CancellationTokenSource();
        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new CancelAfterMaintenanceSave(request))
            .Options;
        await using var db = new MaintTrackDbContext(options, _tenantContext);
        var service = new MaintenanceEntryService(
            db,
            _tenantContext,
            new StubUser(_tenantId),
            new TrackingFileStorage(),
            NullLogger<MaintenanceEntryService>.Instance,
            Options.Create(new TranslationWorkerOptions { EnqueueEnabled = true }),
            new TranslationRepository(db, _tenantContext));

        var entryId = await service.CreateAsync(
            Request($"maintenanceLog.v2\nmaintenanceComponent.bearing\n{Narrative}"),
            request.Token);

        Assert.True(request.IsCancellationRequested);
        var jobs = await JobsForAsync(entryId);
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, job => Assert.Equal(Narrative, job.SourceText));
    }

    [Theory]
    [InlineData(TranslationLanguages.English)]
    [InlineData(TranslationLanguages.Thai)]
    public async Task LaterLanguageInsertFailure_KeepsNoPartialJobsAndKeepsTheReport(string failingLanguage)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_translation_language BEFORE INSERT ON translation_jobs " +
            $"WHEN NEW.target_language = '{failingLanguage}' " +
            "BEGIN SELECT RAISE(ABORT, 'simulated translation insert failure'); END;");
        var files = new TrackingFileStorage();
        var logger = new CountingLogger();
        var service = new MaintenanceEntryService(
            _db,
            _tenantContext,
            new StubUser(_tenantId),
            files,
            logger,
            Options.Create(new TranslationWorkerOptions { EnqueueEnabled = true }),
            _repository);
        var description = $"maintenanceLog.v2\nOther: Hydraulic pump\n{Narrative}";

        var entryId = await service.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = _machineId,
            Date = new DateOnly(2026, 11, 13),
            MaintenanceTypeId = _typeId,
            Description = description,
            ImageUrl = "/uploads/primary.png",
            AdditionalImageUrls = new[] { "/uploads/extra-1.png" },
            WorkHours = 1,
            IsSafeToOperate = true
        }, CancellationToken.None);

        Assert.Empty(await _db.TranslationJobs.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(1, logger.ErrorCount);
        Assert.Empty(files.Deleted);
        var saved = await _db.MaintenanceEntries
            .Include(entry => entry.AdditionalImages)
            .SingleAsync(entry => entry.Id == entryId);
        Assert.Equal(description, saved.Description);
        Assert.Equal("/uploads/primary.png", saved.ImageUrl);
        Assert.Equal("/uploads/extra-1.png", Assert.Single(saved.AdditionalImages).ImageUrl);
        Assert.Equal(
            1,
            await _db.AuditLogs.CountAsync(log => log.EntityId == entryId && log.Action == "maintenance_created"));
    }

    private CreateMaintenanceEntryRequest Request(string description) =>
        new()
        {
            MachineId = _machineId,
            Date = new DateOnly(2026, 11, 13),
            MaintenanceTypeId = _typeId,
            Description = description,
            WorkHours = 1,
            IsSafeToOperate = true
        };

    private sealed class CancelAfterMaintenanceSave : SaveChangesInterceptor
    {
        private readonly CancellationTokenSource _request;

        public CancelAfterMaintenanceSave(CancellationTokenSource request) => _request = request;

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            _request.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private MaintenanceEntryService EnabledService(ITranslationRepository repository) =>
        new(
            _db,
            _tenantContext,
            new StubUser(_tenantId),
            new TrackingFileStorage(),
            NullLogger<MaintenanceEntryService>.Instance,
            Options.Create(new TranslationWorkerOptions { EnqueueEnabled = true }),
            repository);

    private Task<Guid> CreateAsync(string description, MaintenanceEntryService service) =>
        service.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = _machineId,
            Date = new DateOnly(2026, 11, 13),
            MaintenanceTypeId = _typeId,
            Description = description,
            WorkHours = 1,
            IsSafeToOperate = true
        }, CancellationToken.None);

    private Task<List<TranslationJob>> JobsForAsync(Guid entryId) =>
        _db.TranslationJobs.Where(job => job.EntityId == entryId).ToListAsync();

    private Task<MaintenanceEntry> EntryAsync(Guid entryId) =>
        _db.MaintenanceEntries.SingleAsync(entry => entry.Id == entryId);

    private sealed class StubUser : ICurrentUserContext
    {
        public StubUser(Guid tenantId) => TenantId = tenantId;

        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; }
        public int RoleId => (int)UserRole.Worker;
        public UserRole Role => UserRole.Worker;
        public string DisplayName => "Test Technician";
    }

    private sealed class TrackingFileStorage : IFileStorageService
    {
        public List<string> Deleted { get; } = new();

        public Task<string> UploadAsync(
            Stream stream,
            string fileName,
            string contentType,
            string tenantId,
            CancellationToken ct) =>
            Task.FromResult($"/uploads/{fileName}");

        public Task DeleteAsync(string fileUrl, CancellationToken ct)
        {
            Deleted.Add(fileUrl);
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeTranslationRepository : ITranslationRepository
    {
        public int Calls { get; private set; }

        public bool ThrowOnEnqueue { get; init; }

        public CancellationToken? ReceivedEnqueueToken { get; private set; }

        public Task<EnqueueTranslationResult> EnqueueAsync(
            EnqueueTranslationRequest request,
            CancellationToken cancellationToken) =>
            Reject<EnqueueTranslationResult>();

        public Task<IReadOnlyList<EnqueueTranslationResult>> EnqueueAllAsync(
            IReadOnlyList<EnqueueTranslationRequest> requests,
            CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedEnqueueToken = cancellationToken;
            if (ThrowOnEnqueue)
                throw new InvalidOperationException("translation database unavailable");

            throw new InvalidOperationException("translation storage was accessed");
        }

        public Task<IReadOnlyList<NarrativeTranslationRow>> GetNarrativeTranslationsAsync(
            Guid entityId,
            string entityType,
            string fieldName,
            string sourceHash,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("translation storage was accessed");
        }

        public Task<IReadOnlyList<TranslationJob>> ClaimPendingAsync(
            int batchSize,
            TimeSpan processingLease,
            CancellationToken cancellationToken) =>
            Reject<IReadOnlyList<TranslationJob>>();

        public Task RenewClaimsAsync(
            IReadOnlyCollection<TranslationClaim> claims,
            CancellationToken cancellationToken) =>
            Reject();

        public Task<TranslationCompletionResult> CompleteAsync(
            Guid jobId,
            Guid claimToken,
            string sourceHash,
            string translatedText,
            CancellationToken cancellationToken) =>
            Reject<TranslationCompletionResult>();

        public Task<bool> FailAsync(
            Guid jobId,
            Guid claimToken,
            string error,
            int maxAttempts,
            CancellationToken cancellationToken) =>
            Reject<bool>();

        public Task<TranslatedText?> FindTranslationAsync(
            Guid entityId,
            string entityType,
            string fieldName,
            string sourceHash,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Reject<TranslatedText?>();

        private Task Reject()
        {
            Calls++;
            throw new InvalidOperationException("translation storage was accessed");
        }

        private Task<T> Reject<T>()
        {
            Calls++;
            throw new InvalidOperationException("translation storage was accessed");
        }
    }

    private sealed class CountingLogger : ILogger<MaintenanceEntryService>
    {
        public int ErrorCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                ErrorCount++;
        }
    }
}
