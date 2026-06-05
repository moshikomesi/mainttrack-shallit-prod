using MaintTrack.Application.Abstractions;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MaintTrack.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <see cref="MaintTrackDbContext" /> used by EF Core tools to
/// create the DbContext instance when running migrations.
/// </summary>
public sealed class MaintTrackDbContextFactory
    : IDesignTimeDbContextFactory<MaintTrackDbContext>
{
    public MaintTrackDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MaintTrackDbContext>();

        // NOTE: This connection string is for design-time tooling only.
        // Runtime configuration still comes from Program.cs and appsettings / environment.
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=mainttrack_dev;Username=postgres;Password=postgres");

        ITenantContext tenantContext = new TenantContext
        {
            TenantId = null
        };

        return new MaintTrackDbContext(optionsBuilder.Options, tenantContext);
    }
}

