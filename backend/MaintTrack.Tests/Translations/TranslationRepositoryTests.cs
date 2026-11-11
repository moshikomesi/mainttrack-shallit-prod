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
        Assert.Equal("claim_token", job.FindProperty(nameof(TranslationJob.ClaimToken))!.GetColumnName(jobStore));
        Assert.True(job.FindProperty(nameof(TranslationJob.ClaimToken))!.IsNullable);
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
        Assert.NotNull(claimed.ClaimToken);
        Assert.NotNull(other.ClaimToken);
        Assert.NotEqual(claimed.ClaimToken, other.ClaimToken);
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
        var first = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.True(await _repository.FailAsync(first.Id, first.ClaimToken!.Value, "temporary outage", 2, CancellationToken.None));
        var retried = await _db.TranslationJobs.SingleAsync();
        Assert.Equal(TranslationJobStatus.Pending, retried.Status);
        Assert.Equal(1, retried.AttemptCount);
        Assert.Equal("temporary outage", retried.LastError);
        Assert.Null(retried.ProcessedAt);
        Assert.Null(retried.ClaimToken);

        var again = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.Equal(enqueued.Job.Id, again.Id);
        Assert.Equal(2, again.AttemptCount);
        Assert.NotEqual(first.ClaimToken, again.ClaimToken);

        Assert.True(await _repository.FailAsync(again.Id, again.ClaimToken!.Value, "still down", 2, CancellationToken.None));
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
        var first = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None));

        var job = await _db.TranslationJobs.SingleAsync();
        job.UpdatedAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var reclaimed = Assert.Single(
            await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.Equal(enqueued.Job.Id, reclaimed.Id);
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.Equal(TranslationJobStatus.Processing, reclaimed.Status);
        Assert.NotEqual(first.ClaimToken, reclaimed.ClaimToken);
        Assert.Equal(1, await _db.TranslationJobs.CountAsync());
    }

    [Fact]
    public async Task ExpiredClaim_CannotCompleteOrFailAfterANewerClaim()
    {
        var enqueued = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        var first = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromSeconds(1), CancellationToken.None));
        var firstToken = first.ClaimToken!.Value;

        var stored = await _db.TranslationJobs.SingleAsync();
        stored.UpdatedAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var second = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromSeconds(1), CancellationToken.None));
        var secondToken = second.ClaimToken!.Value;
        Assert.NotEqual(firstToken, secondToken);

        Assert.Equal(
            TranslationCompletionResult.Stale,
            await _repository.CompleteAsync(first.Id, firstToken, enqueued.Job.SourceHash, "stale text", CancellationToken.None));
        Assert.False(await _repository.FailAsync(first.Id, firstToken, "stale failure", 5, CancellationToken.None));

        var stillOwned = await _db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Processing, stillOwned.Status);
        Assert.Equal(secondToken, stillOwned.ClaimToken);
        Assert.Equal(2, stillOwned.AttemptCount);
        Assert.Null(stillOwned.LastError);
        Assert.Empty(await _db.TranslatedTexts.AsNoTracking().ToListAsync());

        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(second.Id, secondToken, second.SourceHash, "current text", CancellationToken.None));
        Assert.Equal(
            TranslationCompletionResult.Stale,
            await _repository.CompleteAsync(first.Id, firstToken, enqueued.Job.SourceHash, "late overwrite", CancellationToken.None));
        Assert.False(await _repository.FailAsync(first.Id, firstToken, "late failure", 5, CancellationToken.None));

        var completed = await _db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(TranslationJobStatus.Completed, completed.Status);
        Assert.Equal(secondToken, completed.ClaimToken);
        Assert.Equal("current text", (await _db.TranslatedTexts.AsNoTracking().SingleAsync()).Text);
    }

    [Fact]
    public async Task Renew_ExtendsOnlyTheCurrentClaim()
    {
        await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        var first = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromSeconds(1), CancellationToken.None));
        var stored = await _db.TranslationJobs.SingleAsync();
        stored.UpdatedAt = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var second = Assert.Single(await _repository.ClaimPendingAsync(1, TimeSpan.FromSeconds(1), CancellationToken.None));
        var ownedAt = second.UpdatedAt;
        await _repository.RenewClaimsAsync(
            new[] { new TranslationClaim(first.Id, first.ClaimToken!.Value) },
            CancellationToken.None);

        var afterStaleRenew = await _db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(second.ClaimToken, afterStaleRenew.ClaimToken);
        Assert.Equal(ownedAt, afterStaleRenew.UpdatedAt);

        await Task.Delay(50);
        await _repository.RenewClaimsAsync(
            new[] { new TranslationClaim(second.Id, second.ClaimToken!.Value) },
            CancellationToken.None);
        var renewed = await _db.TranslationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(second.ClaimToken, renewed.ClaimToken);
        Assert.True(renewed.UpdatedAt > ownedAt);
    }

    [Fact]
    public async Task Enqueue_AcceptsFaultDescription_AndRejectsOtherFields()
    {
        Assert.True(TranslatableFields.IsAllowed(TranslatableFields.MaintenanceEntry, TranslatableFields.Description));
        Assert.False(TranslatableFields.IsAllowed(TranslatableFields.MaintenanceEntry, "RepairDescription"));
        Assert.False(TranslatableFields.IsAllowed("MachineComponent", "NameKey"));

        var allowed = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        Assert.True(allowed.Created);
        Assert.Equal(TranslatableFields.MaintenanceEntry, allowed.Job.EntityType);
        Assert.Equal(TranslatableFields.Description, allowed.Job.FieldName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.EnqueueAsync(
            new EnqueueTranslationRequest(
                TranslatableFields.MaintenanceEntry,
                _entityId,
                "RepairDescription",
                "gasket replaced",
                TranslationLanguages.English),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.EnqueueAsync(
            new EnqueueTranslationRequest(
                "MachineComponent",
                _entityId,
                "NameKey",
                "component.bearing",
                TranslationLanguages.Thai),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.EnqueueAsync(
            new EnqueueTranslationRequest(
                TranslatableFields.MaintenanceEntry,
                _entityId,
                "description",
                "Seal leaking",
                TranslationLanguages.Hebrew),
            CancellationToken.None));

        Assert.Equal(1, await _db.TranslationJobs.CountAsync());
    }

    [Fact]
    public async Task SourceChange_KeepsThePreviousTranslationAddressable()
    {
        var original = await EnqueueAsync("Seal leaking", TranslationLanguages.Hebrew);
        var claimed = Assert.Single(await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(
                claimed.Id,
                claimed.ClaimToken!.Value,
                original.Job.SourceHash,
                "דליפת אטם",
                CancellationToken.None));

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
        var claimed = await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None);
        var olderClaim = claimed.Single(job => job.Id == older.Job.Id);
        var newerClaim = claimed.Single(job => job.Id == newer.Job.Id);
        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(
                newerClaim.Id,
                newerClaim.ClaimToken!.Value,
                newer.Job.SourceHash,
                "newer text",
                CancellationToken.None));

        Assert.Equal(
            TranslationCompletionResult.Completed,
            await _repository.CompleteAsync(
                olderClaim.Id,
                olderClaim.ClaimToken!.Value,
                older.Job.SourceHash,
                "older text",
                CancellationToken.None));

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

        var stale = await _repository.CompleteAsync(
            older.Job.Id,
            olderClaim.ClaimToken!.Value,
            capturedHash,
            "stale overwrite",
            CancellationToken.None);

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

        var result = await _repository.CompleteAsync(
            job.Job.Id,
            Guid.NewGuid(),
            job.Job.SourceHash,
            "too early",
            CancellationToken.None);

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
        var claimed = Assert.Single(await _repository.ClaimPendingAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None));
        await _repository.CompleteAsync(
            claimed.Id,
            claimed.ClaimToken!.Value,
            queued.Job.SourceHash,
            "translated fault",
            CancellationToken.None);

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
