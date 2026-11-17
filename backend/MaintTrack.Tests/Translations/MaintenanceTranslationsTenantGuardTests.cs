using System.Data.Common;
using MaintTrack.Application.Abstractions;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaintTrack.Tests.Translations;

public sealed class MaintenanceTranslationsTenantGuardTests : IAsyncLifetime
{
    private const string Narrative = "Seal leaking. Replaced the gasket.";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _entryId = Guid.NewGuid();
    private readonly TenantContext _tenantContext = new();
    private readonly CommandCounter _commands = new();
    private readonly NarrativeReadCounter _translationReads = new();
    private readonly MaintTrackDbContext _db;
    private readonly MaintenanceEntryService _service;

    public MaintenanceTranslationsTenantGuardTests()
    {
        _connection.Open();
        _db = new MaintTrackDbContext(
            new DbContextOptionsBuilder<MaintTrackDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(_commands)
                .Options,
            _tenantContext);
        _service = new MaintenanceEntryService(
            _db,
            _tenantContext,
            new StubUser(_tenantId),
            new NoOpFileStorage(),
            NullLogger<MaintenanceEntryService>.Instance,
            Options.Create(new TranslationWorkerOptions { EnqueueEnabled = true }),
            new CountingTranslationRepository(new TranslationRepository(_db, _tenantContext), _translationReads));
    }

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        var machineId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _db.Machines.Add(new Machine
        {
            Id = machineId,
            TenantId = _tenantId,
            Name = "machine.guard",
            Code = "G",
            IsActive = true,
            CreatedAt = now
        });
        _db.MaintenanceEntries.Add(new MaintenanceEntry
        {
            Id = _entryId,
            TenantId = _tenantId,
            MachineId = machineId,
            Date = new DateOnly(2026, 11, 13),
            Description = $"maintenanceLog.v2\nHydraulic pump\n{Narrative}",
            EmployeeName = "Worker",
            WorkHours = 1,
            IsSafeToOperate = true,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = now
        });
        _db.TranslatedTexts.Add(new TranslatedText
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EntityType = TranslatableFields.MaintenanceEntry,
            EntityId = _entryId,
            FieldName = TranslatableFields.Description,
            SourceHash = SourceTextHasher.Compute(Narrative),
            TargetLanguage = TranslationLanguages.English,
            Text = "Translated seal note",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task MissingTenant_ReturnsNotFound_WithoutAnyDatabaseOrTranslationLookup()
    {
        _tenantContext.TenantId = null;
        _commands.Count = 0;

        var response = await _service.GetTranslationsAsync(_entryId, CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(0, _commands.Count);
        Assert.Equal(0, _translationReads.Count);
    }

    [Fact]
    public async Task ValidTenant_ReadsItsOwnTranslations()
    {
        _tenantContext.TenantId = _tenantId;

        var response = await _service.GetTranslationsAsync(_entryId, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(Narrative, response!.OriginalText);
        var english = Assert.Single(response.Translations, item => item.Language == TranslationLanguages.English);
        Assert.Equal(MaintenanceTranslationPublicStatus.Completed, english.Status);
        Assert.Equal("Translated seal note", english.Text);
        Assert.Equal(1, _translationReads.Count);
    }

    [Fact]
    public async Task OtherTenant_ReturnsNotFound_WithoutTranslationLookup()
    {
        _tenantContext.TenantId = Guid.NewGuid();

        var response = await _service.GetTranslationsAsync(_entryId, CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(0, _translationReads.Count);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubUser : ICurrentUserContext
    {
        public StubUser(Guid tenantId) => TenantId = tenantId;

        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; }
        public int RoleId => (int)UserRole.Worker;
        public UserRole Role => UserRole.Worker;
        public string DisplayName => "Worker";
    }

    private sealed class NoOpFileStorage : IFileStorageService
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
