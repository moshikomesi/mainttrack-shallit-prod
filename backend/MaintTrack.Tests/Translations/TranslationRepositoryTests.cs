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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaintTrack.Tests.Translations;

public sealed class TranslationRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenantContext;
    private readonly MaintTrackDbContext _db;
    private readonly TranslationRepository _repository;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _entityId = Guid.NewGuid();

    public TranslationRepositoryTests()
    {
        _tenantContext = new TenantContext { TenantId = _tenantId };
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new MaintTrackDbContext(Options(), _tenantContext);
        _db.Database.EnsureCreated();
        _repository = new TranslationRepository(_db, _tenantContext);
    }

    [Fact]
    public void SourceHash_IsDeterministicAndChangesWithTheText()
    {
        const string hebrew = "אטם דולף";
        const string thai = "ซีลรั่ว";

        Assert.Equal(SourceTextHasher.Compute(hebrew), SourceTextHasher.Compute(hebrew));
        Assert.NotEqual(SourceTextHasher.Compute(hebrew), SourceTextHasher.Compute(hebrew + " "));
        Assert.NotEqual(SourceTextHasher.Compute(hebrew), SourceTextHasher.Compute(thai));
        Assert.Equal(TranslationFieldLimits.SourceHashLength, SourceTextHasher.Compute(hebrew).Length);
        Assert.Throws<ArgumentNullException>(() => SourceTextHasher.Compute(null!));
    }

    [Fact]
    public void Model_IsolatesVersionsByTenantEntityFieldAndLanguage()
    {
        var designModel = _db.GetService<IDesignTimeModel>().Model;
        var job = designModel.FindEntityType(typeof(TranslationJob))!;
        var translation = designModel.FindEntityType(typeof(TranslatedText))!;
        var jobStore = StoreObjectIdentifier.Table("translation_jobs", null);
        var translationStore = StoreObjectIdentifier.Table("translated_texts", null);

        Assert.Equal("translation_jobs", job.GetTableName());
        Assert.Equal("translated_texts", translation.GetTableName());
        Assert.NotNull(job.GetQueryFilter());
        Assert.NotNull(translation.GetQueryFilter());
        Assert.Empty(job.GetForeignKeys());
        Assert.Empty(translation.GetForeignKeys());

        AssertUnique(
            job,
            "ux_translation_jobs_source_version",
            "TenantId",
            "EntityType",
            "EntityId",
            "FieldName",
            "SourceHash",
            "TargetLanguage");
        AssertUnique(
            translation,
            "ux_translated_texts_source_version",
            "TenantId",
            "EntityType",
            "EntityId",
            "FieldName",
            "SourceHash",
            "TargetLanguage");

        Assert.NotNull(job.GetIndexes().Single(index => index.GetDatabaseName() == "ix_translation_jobs_status_created_at"));
        Assert.NotNull(job.GetIndexes().Single(index => index.GetDatabaseName() == "ix_translation_jobs_tenant_entity"));
        Assert.Equal("source_hash", job.FindProperty(nameof(TranslationJob.SourceHash))!.GetColumnName(jobStore));
        Assert.Equal("translated_text", translation.FindProperty(nameof(TranslatedText.Text))!.GetColumnName(translationStore));

        var languageCheck = job.GetCheckConstraints().Single(check => check.Name == "ck_translation_jobs_target_language");
        Assert.Contains("'he'", languageCheck.Sql, StringComparison.Ordinal);
        Assert.Contains("'en'", languageCheck.Sql, StringComparison.Ordinal);
        Assert.Contains("'th'", languageCheck.Sql, StringComparison.Ordinal);
        Assert.Contains(
            "'pending'",
            job.GetCheckConstraints().Single(check => check.Name == "ck_translation_jobs_status").Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enqueue_SameSourceVersion_DoesNotCreateASecondJob()
    {
        var first = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        var duplicate = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        var english = await EnqueueAsync("Seal leaking", TranslationLanguages.English);

        Assert.True(first.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(first.Job.Id, duplicate.Job.Id);
        Assert.True(english.Created);
        Assert.Equal(TranslationJobStatus.Pending, first.Job.Status);
        Assert.Equal(0, first.Job.AttemptCount);
        Assert.Equal(2, await _db.TranslationJobs.CountAsync());
    }

    [Fact]
    public async Task Enqueue_PreservesSourceTextAndRejectsUnsupportedLanguage()
    {
        const string source = "  אטם דולף\nreplaced ";
        var job = await EnqueueAsync(source, TranslationLanguages.Thai);

        Assert.Equal(source, job.Job.SourceText);
        Assert.Equal(SourceTextHasher.Compute(source), job.Job.SourceHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => EnqueueAsync(source, "fr"));
        Assert.Equal(1, await _db.TranslationJobs.CountAsync());
    }

    [Fact]
    public async Task TenantScope_CannotReadOrClaimAnotherTenantsJobs()
    {
        await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        var otherTenant = new TenantContext { TenantId = Guid.NewGuid() };
        await using var otherDb = new MaintTrackDbContext(Options(), otherTenant);
        var otherRepository = new TranslationRepository(otherDb, otherTenant);

        Assert.Empty(await otherDb.TranslationJobs.ToListAsync());
        Assert.Empty(await otherRepository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.Null(await otherRepository.FindTranslationAsync(
            _entityId,
            "MaintenanceEntry",
            "Description",
            SourceTextHasher.Compute("Seal leaking"),
            TranslationLanguages.Hebrew,
            CancellationToken.None));

        var unresolved = new TenantContext();
        await using var unresolvedDb = new MaintTrackDbContext(Options(), unresolved);
        var unresolvedRepository = new TranslationRepository(unresolvedDb, unresolved);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unresolvedRepository.FindTranslationAsync(
            _entityId,
            "MaintenanceEntry",
            "Description",
            SourceTextHasher.Compute("Seal leaking"),
            TranslationLanguages.Hebrew,
            CancellationToken.None));

        var stored = Assert.Single(await _db.TranslationJobs.ToListAsync());
        Assert.Equal(_tenantId, stored.TenantId);
        Assert.Equal(TranslationJobStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task Claim_MovesPendingToProcessing_AndDoesNotClaimTwice()
    {
        await EnqueueAsync("first", TranslationLanguages.Hebrew);
        await EnqueueAsync("second", TranslationLanguages.Hebrew);

        var firstClaim = await _repository.ClaimPendingAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None);
        var secondClaim = await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None);
        var thirdClaim = await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None);

        var claimed = Assert.Single(firstClaim);
        var other = Assert.Single(secondClaim);
        Assert.Empty(thirdClaim);
        Assert.NotEqual(claimed.Id, other.Id);
        Assert.Equal(TranslationJobStatus.Processing, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.All(await _db.TranslationJobs.AsNoTracking().ToListAsync(), job =>
        {
            Assert.Equal(TranslationJobStatus.Processing, job.Status);
            Assert.Equal(1, job.AttemptCount);
        });
    }

    [Fact]
    public async Task Fail_RetriesTheSameJob_ThenMarksItFailed()
    {
        var enqueued = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        await _repository.ClaimPendingAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None);

        await _repository.FailAsync(enqueued.Job.Id, "temporary outage", 2, CancellationToken.None);
        var retried = await _db.TranslationJobs.SingleAsync();
        Assert.Equal(TranslationJobStatus.Pending, retried.Status);
        Assert.Equal(1, retried.AttemptCount);
        Assert.Equal("temporary outage", retried.LastError);
        Assert.Null(retried.ProcessedAt);

        var again = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.Equal(enqueued.Job.Id, again.Id);
        Assert.Equal(2, again.AttemptCount);

        await _repository.FailAsync(enqueued.Job.Id, "still down", 2, CancellationToken.None);
        var failed = await _db.TranslationJobs.SingleAsync();
        Assert.Equal(TranslationJobStatus.Failed, failed.Status);
        Assert.Equal(2, failed.AttemptCount);
        Assert.NotNull(failed.ProcessedAt);
        Assert.Equal(1, await _db.TranslationJobs.CountAsync());
        Assert.Empty(await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredProcessingLease_CanBeClaimedAgain()
    {
        var enqueued = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        await _repository.ClaimPendingAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None);

        var job = await _db.TranslationJobs.SingleAsync();
        job.UpdatedAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var reclaimed = Assert.Single(
            await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.Equal(enqueued.Job.Id, reclaimed.Id);
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.Equal(TranslationJobStatus.Processing, reclaimed.Status);
        Assert.Equal(1, await _db.TranslationJobs.CountAsync());
    }

    [Fact]
    public async Task SourceChange_KeepsThePreviousTranslationAddressable()
    {
        var original = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(original.Job.Id, original.Job.SourceHash, "דליפת אטם", CancellationToken.None));

        var revised = await EnqueueAsync("Seal replaced", TranslationLanguages.Hebrew);
        var originalTranslation = await _repository.FindTranslationAsync(
            _entityId,
            "MaintenanceEntry",
            "Description",
            original.Job.SourceHash,
            TranslationLanguages.Hebrew,
            CancellationToken.None);
        var revisedTranslation = await _repository.FindTranslationAsync(
            _entityId,
            "MaintenanceEntry",
            "Description",
            revised.Job.SourceHash,
            TranslationLanguages.Hebrew,
            CancellationToken.None);

        Assert.NotEqual(original.Job.SourceHash, revised.Job.SourceHash);
        Assert.Equal("דליפת אטם", originalTranslation!.Text);
        Assert.Null(revisedTranslation);
        Assert.Equal("Seal leaking", (await _db.TranslationJobs.SingleAsync(job => job.Id == original.Job.Id)).SourceText);
    }

    [Fact]
    public async Task StaleCompletion_DoesNotOverwriteANewerSourceVersion()
    {
        var older = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        var newer = await EnqueueAsync("Seal replaced", TranslationLanguages.English);
        await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(newer.Job.Id, newer.Job.SourceHash, "newer text", CancellationToken.None));

        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(older.Job.Id, older.Job.SourceHash, "older text", CancellationToken.None));

        var newerRow = await _db.TranslatedTexts.SingleAsync(item => item.SourceHash == newer.Job.SourceHash);
        var olderRow = await _db.TranslatedTexts.SingleAsync(item => item.SourceHash == older.Job.SourceHash);
        Assert.Equal("newer text", newerRow.Text);
        Assert.Equal("older text", olderRow.Text);
        Assert.NotEqual(newerRow.Id, olderRow.Id);

        var capturedHash = older.Job.SourceHash;
        var processing = await _db.TranslationJobs.SingleAsync(job => job.Id == older.Job.Id);
        processing.Status = TranslationJobStatus.Processing;
        processing.SourceHash = SourceTextHasher.Compute("mutated-after-claim");
        await _db.SaveChangesAsync();

        var stale = await _repository.CompleteAsync(older.Job.Id, capturedHash, "stale overwrite", CancellationToken.None);

        Assert.Equal(TranslationCompletionResult.Stale, stale);
        Assert.Equal("older text", (await _db.TranslatedTexts.SingleAsync(item => item.SourceHash == capturedHash)).Text);
        Assert.DoesNotContain(await _db.TranslatedTexts.ToListAsync(), item => item.Text == "stale overwrite");
        Assert.Equal("newer text", (await _db.TranslatedTexts.SingleAsync(item => item.Id == newerRow.Id)).Text);
        var returned = await _db.TranslationJobs.SingleAsync(job => job.Id == older.Job.Id);
        Assert.Equal(TranslationJobStatus.Pending, returned.Status);
        Assert.Contains("Source version", returned.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_PendingJob_WritesNothing()
    {
        var job = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);

        var result = await _repository.CompleteAsync(job.Job.Id, job.Job.SourceHash, "too early", CancellationToken.None);

        Assert.Equal(TranslationCompletionResult.Stale, result);
        Assert.Empty(await _db.TranslatedTexts.ToListAsync());
        Assert.Equal(TranslationJobStatus.Pending, (await _db.TranslationJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task MaintenanceReportCreation_IgnoresTranslationFailuresAndKeepsDescription()
    {
        const string description = "component.bearing\nSeal leaking. Replaced the gasket.";
        var userId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        _db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
        _db.Users.Add(new User
        {
            Id = userId,
            TenantId = _tenantId,
            Username = "tech",
            Email = "tech@example.com",
            PasswordHash = "hash",
            DisplayName = "Test Technician",
            RoleId = (int)UserRole.Worker,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.Machines.Add(new Machine
        {
            Id = machineId,
            TenantId = _tenantId,
            Name = "machine.test",
            Code = "M1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.MaintenanceTypes.Add(new MaintenanceType
        {
            Id = typeId,
            TenantId = _tenantId,
            Code = "other",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.TranslationJobs.Add(new TranslationJob
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EntityType = "MaintenanceEntry",
            EntityId = Guid.NewGuid(),
            FieldName = "Description",
            SourceText = "previous failure",
            SourceHash = SourceTextHasher.Compute("previous failure"),
            TargetLanguage = TranslationLanguages.Hebrew,
            Status = TranslationJobStatus.Failed,
            AttemptCount = 5,
            LastError = "provider down",
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = new MaintenanceEntryService(
            _db,
            _tenantContext,
            new StubUser(_tenantId, userId),
            new NoOpFileStorageService(),
            NullLogger<MaintenanceEntryService>.Instance);

        var entryId = await service.CreateAsync(new CreateMaintenanceEntryRequest
        {
            MachineId = machineId,
            Date = new DateOnly(2026, 11, 9),
            MaintenanceTypeId = typeId,
            Description = description,
            WorkHours = 1.5m,
            IsSafeToOperate = true
        }, CancellationToken.None);

        var created = await service.GetByIdAsync(entryId, CancellationToken.None);
        Assert.NotNull(created);
        Assert.Equal(description, created!.Description);

        var queued = await EnqueueAsync(description, TranslationLanguages.Hebrew, entryId);
        await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None);
        await _repository.CompleteAsync(queued.Job.Id, queued.Job.SourceHash, "translated fault", CancellationToken.None);

        var reread = await _db.MaintenanceEntries.IgnoreQueryFilters().SingleAsync(entry => entry.Id == entryId);
        Assert.Equal(description, reread.Description);
        Assert.Equal(
            "translated fault",
            (await _db.TranslatedTexts.IgnoreQueryFilters().SingleAsync(item => item.EntityId == entryId)).Text);
        Assert.Equal(
            TranslationJobStatus.Failed,
            (await _db.TranslationJobs.IgnoreQueryFilters().SingleAsync(job => job.SourceText == "previous failure")).Status);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private DbContextOptions<MaintTrackDbContext> Options() =>
        new DbContextOptionsBuilder<MaintTrackDbContext>().UseSqlite(_connection).Options;

    private Task<EnqueueTranslationResult> EnqueueAsync(string sourceText, string language) =>
        EnqueueAsync(sourceText, language, _entityId);

    private Task<EnqueueTranslationResult> EnqueueAsync(string sourceText, string language, Guid entityId) =>
        _repository.EnqueueAsync(
            new EnqueueTranslationRequest("MaintenanceEntry", entityId, "Description", sourceText, language),
            CancellationToken.None);

    private static void AssertUnique(IEntityType entity, string indexName, params string[] properties)
    {
        var index = entity.GetIndexes().Single(candidate => candidate.GetDatabaseName() == indexName);
        Assert.True(index.IsUnique);
        Assert.Equal(properties, index.Properties.Select(property => property.Name).ToArray());
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
        public string DisplayName => "Test Technician";
    }

    private sealed class NoOpFileStorageService : IFileStorageService
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
