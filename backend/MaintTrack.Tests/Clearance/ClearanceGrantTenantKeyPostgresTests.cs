using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// Applies the real migration chain to a throwaway PostgreSQL database (same opt-in and naming
/// as <see cref="ClearancePostgresConcurrencyTests"/>) to prove the tenant-scoped grant keys
/// keep existing valid grants and reject cross-tenant ones.
/// </summary>
public sealed class ClearanceGrantTenantKeyPostgresTests : IAsyncLifetime
{
    private const string FoundationMigration = "20261019172521_AddClearanceFoundation";
    private const string ForeignKeyViolation = "23503";
    private const string RestrictViolation = "23001";

    private readonly string? _server = Environment.GetEnvironmentVariable(ClearancePostgresConcurrencyTests.ServerVariable);
    private readonly string _database = $"mainttrack_clearance_test_{Guid.NewGuid():N}";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
        {
            return;
        }

        await using (var admin = new NpgsqlConnection(_server))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, $"CREATE DATABASE \"{_database}\"");
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_server) { Database = _database, Pooling = false }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_server))
        {
            return;
        }

        await using var admin = new NpgsqlConnection(_server);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
    }

    [PostgresFact]
    public async Task GrantTenantKeys_KeepExistingValidGrantsAndRejectCrossTenantGrants()
    {
        var approverId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var otherTenantUserId = Guid.NewGuid();

        await using (var db = CreateContext())
        {
            await CreateCurrentSchemaWithAllMigrationsAppliedAsync(db);
            await db.GetService<IMigrator>().MigrateAsync(FoundationMigration);

            db.Roles.Add(new Role { Id = (int)UserRole.Worker, Name = "Worker" });
            db.Users.AddRange(
                NewUser(approverId, _tenantId, "approver"),
                NewUser(viewerId, _tenantId, "viewer"),
                NewUser(otherTenantUserId, _otherTenantId, "other"));
            db.ClearanceApprovers.Add(new ClearanceApprover
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, UserId = approverId, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            await db.Database.MigrateAsync();
        }

        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            Assert.Equal(1L, await ScalarAsync(connection,
                $"SELECT COUNT(*) FROM clearance_approvers WHERE user_id = '{approverId}' AND tenant_id = '{_tenantId}'"));
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'AK_users_id_tenant_id' AND contype = 'u'"));
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname IN ('FK_clearance_approvers_users_user_id', 'FK_clearance_viewers_users_user_id')"));

            await ExecuteAsync(connection,
                $"INSERT INTO clearance_viewers (id, user_id, tenant_id, created_at) VALUES ('{Guid.NewGuid()}', '{viewerId}', '{_tenantId}', now())");

            foreach (var table in new[] { "clearance_approvers", "clearance_viewers" })
            {
                var mismatch = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                    $"INSERT INTO {table} (id, user_id, tenant_id, created_at) VALUES ('{Guid.NewGuid()}', '{viewerId}', '{_otherTenantId}', now())"));
                Assert.Equal(ForeignKeyViolation, mismatch.SqlState);
            }

            foreach (var userId in new[] { approverId, viewerId })
            {
                var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                    $"DELETE FROM users WHERE id = '{userId}'"));
                Assert.Equal(RestrictViolation, delete.SqlState);
            }

            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM clearance_approvers"));
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM clearance_viewers"));
        }

        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(FoundationMigration);
        }

        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM clearance_approvers"));
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'AK_users_id_tenant_id'"));
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'FK_clearance_approvers_users_user_id'"));
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'clearance_viewers'"));
        }
    }

    // Not every historical migration is discoverable, so the chain cannot build an empty
    // database; start from the current model and let Down/Up of the grant-key migration run.
    private static async Task CreateCurrentSchemaWithAllMigrationsAppliedAsync(MaintTrackDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        var history = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateScript());
        foreach (var migration in db.Database.GetMigrations())
        {
            await db.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migration, ProductInfo.GetVersion())));
        }
    }

    private static User NewUser(Guid id, Guid tenantId, string username) => new()
    {
        Id = id,
        TenantId = tenantId,
        Username = username,
        Email = $"{username}@example.com",
        PasswordHash = "hash",
        DisplayName = username,
        RoleId = (int)UserRole.Worker,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    private MaintTrackDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MaintTrackDbContext>().UseNpgsql(_connectionString).Options,
            new TenantContext { TenantId = _tenantId });

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
