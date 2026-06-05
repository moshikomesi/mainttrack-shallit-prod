using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using MaintTrack.Api.Auth;
using MaintTrack.Domain.Tenants;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintTrack.Tests.Authentication;

public class LoginTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly MaintTrackDbContext _dbContext;
    private readonly Guid _testTenantId;
    private readonly Guid _testUserId;

    public LoginTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ConnectionStrings:Default", "Host=localhost;Port=5432;Database=mainttrack_test;Username=mainttrack;Password=change_me" },
                    { "Jwt:Issuer", "MaintTrack.Test" },
                    { "Jwt:Audience", "MaintTrack.Api.Test" },
                    { "Jwt:Key", "TestSecretKeyForJwtTokenGeneration12345678901234567890" }
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var scope = _factory.Services.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();

        _testTenantId = Guid.NewGuid();
        _testUserId = Guid.NewGuid();
    }

    public async Task InitializeAsync()
    {
        await _dbContext.Database.EnsureCreatedAsync();

        var tenant = new Tenant
        {
            Id = _testTenantId,
            Name = "Test Factory",
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Tenants.Add(tenant);

        _dbContext.Roles.AddRange(
            new Role { Id = 1, Name = "Worker" },
            new Role { Id = 2, Name = "Manager" },
            new Role { Id = 3, Name = "SuperAdmin" });

        var passwordHash = BCrypt.Net.BCrypt.HashPassword("testpassword123");
        var user = new User
        {
            Id = _testUserId,
            TenantId = _testTenantId,
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = passwordHash,
            DisplayName = "Test User",
            RoleId = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);

        await _dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.Database.EnsureDeletedAsync();
        await _dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns401()
    {
        var request = new LoginRequest
        {
            UsernameOrEmail = "testuser",
            Password = "wrongpassword"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("Invalid credentials", problemDetails.Title);
    }

    [Fact]
    public async Task Login_WithValidCredentials_SetsHttpOnlyCookieAndReturnsUser()
    {
        var request = new LoginRequest
        {
            UsernameOrEmail = "testuser",
            Password = "testpassword123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.Headers,
            h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
                 && h.Value.Any(v => v.Contains(AuthCookie.Name, StringComparison.OrdinalIgnoreCase)));

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResponse);
        Assert.True(loginResponse.ExpiresAtUtc > DateTime.UtcNow);
        Assert.NotNull(loginResponse.User);
        Assert.Equal(_testUserId, loginResponse.User.UserId);
        Assert.Equal(_testTenantId, loginResponse.User.TenantId);
        Assert.Equal("testuser", loginResponse.User.Username);
        Assert.Equal("test@example.com", loginResponse.User.Email);
        Assert.Equal("Manager", loginResponse.User.Role);
        Assert.Equal(2, loginResponse.User.RoleId);

        var meResponse = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var me = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(me);
        Assert.Equal(_testUserId, me.UserId);
        Assert.Equal(2, me.RoleId);
    }

    [Fact]
    public async Task Login_WithEmail_SetsAuthCookie()
    {
        var request = new LoginRequest
        {
            UsernameOrEmail = "test@example.com",
            Password = "testpassword123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var meResponse = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithBackwardCompatibleUsername_SetsAuthCookie()
    {
        var request = new
        {
            username = "testuser",
            password = "testpassword123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var meResponse = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsSessionCookie()
    {
        await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = "testuser",
            Password = "testpassword123"
        });

        var logoutResponse = await _client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var meResponse = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }
}

public class ProblemDetails
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
}
