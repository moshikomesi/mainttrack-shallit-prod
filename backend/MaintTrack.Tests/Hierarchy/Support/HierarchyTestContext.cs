using MaintTrack.Application.Hierarchy;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.Machines;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.Hierarchy.Support;

public sealed class HierarchyTestContext : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public HierarchyTestContext(Guid? tenantId = null)
    {
        TenantId = tenantId ?? Guid.NewGuid();
        TenantContext = new TenantContext { TenantId = TenantId };
        QueryCounter = new SqlQueryCountInterceptor();

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MaintTrackDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(QueryCounter)
            .Options;

        DbContext = new MaintTrackDbContext(options, TenantContext);
        DbContext.Database.EnsureCreated();

        Service = new HierarchyService(DbContext, TenantContext);
    }

    public Guid TenantId { get; }

    public TenantContext TenantContext { get; }

    public SqlQueryCountInterceptor QueryCounter { get; }

    public MaintTrackDbContext DbContext { get; }

    public HierarchyService Service { get; }

    public async Task SaveAsync(params object[] entities)
    {
        DbContext.AddRange(entities);
        await DbContext.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

public static class HierarchyTestData
{
    public static WorkGroup CreateArray(
        Guid tenantId,
        string nameKey,
        int sortOrder = 0,
        bool isActive = true,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId,
            NameKey = nameKey,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

    public static Machine CreateMachine(
        Guid tenantId,
        string nameKey,
        string code,
        Guid? arrayId = null,
        bool isActive = true,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = tenantId,
            Name = nameKey,
            Code = code,
            IsActive = isActive,
            ArrayId = arrayId,
            CreatedAt = DateTime.UtcNow
        };

    public static HierarchyService.MachineHierarchyRow MachineRow(
        Guid id,
        string nameKey,
        Guid? arrayId = null) =>
        new(id, nameKey, arrayId);

    public static IReadOnlyList<HierarchyMachineDto> FlattenMachines(
        IReadOnlyList<HierarchyArrayDto> hierarchy) =>
        hierarchy.SelectMany(g => g.Machines).ToList();
}
