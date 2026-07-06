using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using MaintTrack.Api.Auth;
using MaintTrack.Application.Authentication;
using MaintTrack.Application.Hierarchy;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Tenants;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Tests.Hierarchy.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MaintTrack.Tests.Hierarchy;

public class HierarchyApiIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private MaintTrackDbContext _dbContext = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _connection.Open();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Jwt:Issuer", "MaintTrack.Test" },
                    { "Jwt:Audience", "MaintTrack.Api.Test" },
                    {
                        "Jwt:Key",
                        "TestSecretKeyForJwtTokenGeneration12345678901234567890"
                    }
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<MaintTrackDbContext>));
                services.RemoveAll(typeof(MaintTrackDbContext));

                services.AddDbContext<MaintTrackDbContext>((sp, options) =>
                    options.UseSqlite(_connection));
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var scope = _factory.Services.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        await _dbContext.Database.EnsureCreatedAsync();

        _dbContext.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Hierarchy Test Factory",
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.Roles.AddRange(
            new Role { Id = 1, Name = "Worker" },
            new Role { Id = 2, Name = "Manager" });

        _dbContext.Users.Add(new User
        {
            Id = _userId,
            TenantId = _tenantId,
            Username = "hierarchyuser",
            Email = "hierarchy@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("testpassword123"),
            DisplayName = "Hierarchy User",
            RoleId = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        var array = new WorkGroup
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            NameKey = "array.line1",
            SortOrder = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Arrays.Add(array);
        _dbContext.Machines.AddRange(
            new Machine
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Name = "machine.assigned",
                Code = "A1",
                ArrayId = array.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Machine
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Name = "machine.unassigned",
                Code = "U1",
                ArrayId = null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

        await _dbContext.SaveChangesAsync();

        AuthenticateClient(_factory.Services);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void AuthenticateClient(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var expiresAtUtc = DateTime.UtcNow.AddHours(8);
        var claims = new[]
        {
            new Claim("user_id", _userId.ToString()),
            new Claim("tenant_id", _tenantId.ToString()),
            new Claim("role_id", "2"),
            new Claim(ClaimTypes.Email, "hierarchy@example.com"),
            new Claim(ClaimTypes.Name, "hierarchyuser"),
            new Claim("display_name", "Hierarchy User")
        };

        var token = jwtService.GenerateToken(claims, expiresAtUtc);
        _client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookie.Name}={token}");
    }

    [Fact]
    public async Task GetHierarchy_ReturnsStableContract()
    {
        var response = await _client.GetAsync("/api/v1/hierarchy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var hierarchy = await response.Content.ReadFromJsonAsync<List<HierarchyArrayDto>>();
        Assert.NotNull(hierarchy);
        Assert.Equal(2, hierarchy.Count);

        HierarchyInvariantAssertions.AssertStableSchema(hierarchy);
        HierarchyInvariantAssertions.AssertUnassignedGroupRules(hierarchy);
        HierarchyInvariantAssertions.AssertNoDuplicateMachines(hierarchy);

        Assert.Equal(2, hierarchy.SelectMany(g => g.Machines).Count());
    }

    [Fact]
    public async Task GetHierarchy_WithoutAuth_Returns401()
    {
        var unauthenticated = _factory.CreateClient();
        var response = await unauthenticated.GetAsync("/api/v1/hierarchy");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
