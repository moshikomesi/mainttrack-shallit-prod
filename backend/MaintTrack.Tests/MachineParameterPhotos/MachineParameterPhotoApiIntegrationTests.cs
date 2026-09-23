using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MaintTrack.Api.Auth;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Authentication;
using MaintTrack.Application.Hierarchy;
using MaintTrack.Application.MachineParameterPhotos;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Tenants;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MaintTrack.Tests.MachineParameterPhotos;

public sealed class MachineParameterPhotoApiIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RecordingFileStorageService _storage = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private MaintTrackDbContext _dbContext = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _visibleArrayId = Guid.NewGuid();
    private readonly Guid _hiddenArrayId = Guid.NewGuid();
    private readonly Guid _visibleMachineId = Guid.NewGuid();
    private readonly Guid _hiddenMachineId = Guid.NewGuid();
    private readonly Guid _inactiveMachineId = Guid.NewGuid();
    private readonly Guid _unassignedMachineId = Guid.NewGuid();
    private readonly Guid _otherMachineId = Guid.NewGuid();
    private readonly Guid _photoId = Guid.NewGuid();
    private readonly Guid _otherPhotoId = Guid.NewGuid();

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
                services.AddDbContext<MaintTrackDbContext>((_, options) => options.UseSqlite(_connection));

                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService>(_storage);
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var scope = _factory.Services.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        await _dbContext.Database.EnsureCreatedAsync();

        _dbContext.Tenants.AddRange(
            new Tenant
            {
                Id = _tenantId,
                Name = "Photos Factory",
                Status = TenantStatus.Active,
                CreatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                Id = _otherTenantId,
                Name = "Other Factory",
                Status = TenantStatus.Active,
                CreatedAt = DateTime.UtcNow
            });

        _dbContext.Roles.Add(new Role { Id = 1, Name = "Worker" });
        _dbContext.Users.Add(new User
        {
            Id = _userId,
            TenantId = _tenantId,
            Username = "photouser",
            Email = "photo@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("testpassword123"),
            DisplayName = "Photo User",
            RoleId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.Arrays.AddRange(
            new WorkGroup
            {
                Id = _visibleArrayId,
                TenantId = _tenantId,
                NameKey = "array.visible",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new WorkGroup
            {
                Id = _hiddenArrayId,
                TenantId = _tenantId,
                NameKey = "array.hidden",
                SortOrder = 2,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new WorkGroup
            {
                Id = Guid.NewGuid(),
                TenantId = _otherTenantId,
                NameKey = "array.other",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

        var otherArrayId = _dbContext.Arrays.Local.First(array => array.TenantId == _otherTenantId).Id;

        _dbContext.Machines.AddRange(
            new Machine
            {
                Id = _visibleMachineId,
                TenantId = _tenantId,
                Name = "machine.visible",
                Code = "V1",
                ArrayId = _visibleArrayId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Machine
            {
                Id = _hiddenMachineId,
                TenantId = _tenantId,
                Name = "machine.hidden",
                Code = "H1",
                ArrayId = _hiddenArrayId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Machine
            {
                Id = _inactiveMachineId,
                TenantId = _tenantId,
                Name = "machine.inactive",
                Code = "I1",
                ArrayId = _visibleArrayId,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            },
            new Machine
            {
                Id = _unassignedMachineId,
                TenantId = _tenantId,
                Name = "machine.unassigned",
                Code = "U1",
                ArrayId = null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Machine
            {
                Id = _otherMachineId,
                TenantId = _otherTenantId,
                Name = "machine.other",
                Code = "O1",
                ArrayId = otherArrayId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

        _dbContext.ArrayFeatureVisibilities.Add(new ArrayFeatureVisibility
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ArrayId = _hiddenArrayId,
            FeatureKey = ArrayFeatureVisibility.MachineParameterPhotosFeatureKey,
            IsVisible = false,
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.MachineParameterPhotos.AddRange(
            new MachineParameterPhoto
            {
                Id = _photoId,
                TenantId = _tenantId,
                MachineId = _visibleMachineId,
                ImageUrl = "/uploads/visible.jpg",
                SortOrder = 1,
                CreatedByUserId = _userId,
                CreatedAt = DateTime.UtcNow
            },
            new MachineParameterPhoto
            {
                Id = _otherPhotoId,
                TenantId = _otherTenantId,
                MachineId = _otherMachineId,
                ImageUrl = "/uploads/other.jpg",
                SortOrder = 1,
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            });

        await _dbContext.SaveChangesAsync();
        Authenticate(_userId, _tenantId);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Endpoints_WithoutAuth_Return401()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/machine-parameter-photos/hierarchy")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/machine-parameter-photos?machineId={_visibleMachineId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/machine-parameter-photos", new MultipartFormDataContent())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/v1/machine-parameter-photos/{_photoId}")).StatusCode);
    }

    [Fact]
    public async Task PublicHierarchy_StillIncludesHiddenAndUnassignedGroups()
    {
        var response = await _client.GetAsync("/api/v1/hierarchy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var hierarchy = await response.Content.ReadFromJsonAsync<List<HierarchyArrayDto>>();
        Assert.NotNull(hierarchy);
        Assert.Contains(hierarchy, group => group.ArrayId == _hiddenArrayId);
        Assert.Contains(hierarchy, group => group.ArrayId is null && group.NameKey == HierarchyDefaults.UnassignedNameKey);
    }

    [Fact]
    public async Task FeatureHierarchy_FiltersHiddenAndUnassigned_AndReportsCanManage()
    {
        var response = await _client.GetAsync("/api/v1/machine-parameter-photos/hierarchy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("canManage").GetBoolean());

        var arrays = document.RootElement.GetProperty("arrays").EnumerateArray().ToList();
        Assert.Single(arrays);
        Assert.Equal(_visibleArrayId, arrays[0].GetProperty("arrayId").GetGuid());
        Assert.Equal("array.visible", arrays[0].GetProperty("nameKey").GetString());
        Assert.Equal(_visibleMachineId, arrays[0].GetProperty("machines")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_RequiresMachineId_AndReturnsCurrentMachinePhotos()
    {
        var missing = await _client.GetAsync("/api/v1/machine-parameter-photos");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var response = await _client.GetAsync($"/api/v1/machine-parameter-photos?machineId={_visibleMachineId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var photos = await response.Content.ReadFromJsonAsync<List<MachineParameterPhotoDto>>();
        Assert.NotNull(photos);
        Assert.Single(photos);
        Assert.Equal(_photoId, photos[0].Id);
    }

    [Fact]
    public async Task List_DoesNotExposeHiddenOrOtherTenantMachines()
    {
        var hidden = await _client.GetAsync($"/api/v1/machine-parameter-photos?machineId={_hiddenMachineId}");
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);

        var other = await _client.GetAsync($"/api/v1/machine-parameter-photos?machineId={_otherMachineId}");
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    [Fact]
    public async Task List_InaccessibleHiddenInactiveAndUnassignedMachines_Return400()
    {
        var unknown = await _client.GetAsync($"/api/v1/machine-parameter-photos?machineId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("Machine not found.", await unknown.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var hidden = await _client.GetAsync($"/api/v1/machine-parameter-photos?machineId={_hiddenMachineId}");
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);

        var inactive = await _client.GetAsync($"/api/v1/machine-parameter-photos?machineId={_inactiveMachineId}");
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);

        var unassigned = await _client.GetAsync($"/api/v1/machine-parameter-photos?machineId={_unassignedMachineId}");
        Assert.Equal(HttpStatusCode.BadRequest, unassigned.StatusCode);
    }

    [Fact]
    public async Task Upload_InvalidFiles_Return400()
    {
        await GrantManagerAsync();

        var unsupported = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateFileForm(_visibleMachineId, "image/gif", "photo.gif", new byte[] { 1, 2, 3, 4 }));
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Contains("Invalid file type.", await unsupported.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var empty = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateFileForm(_visibleMachineId, "image/jpeg", "empty.jpg", Array.Empty<byte>()));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("Uploaded files cannot be empty.", await empty.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var tooMany = new MultipartFormDataContent
        {
            { new StringContent(_visibleMachineId.ToString()), "machineId" }
        };
        for (var i = 0; i < 11; i++)
        {
            var file = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            tooMany.Add(file, "files", $"photo-{i}.jpg");
        }

        var tooManyResponse = await _client.PostAsync("/api/v1/machine-parameter-photos", tooMany);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyResponse.StatusCode);
        Assert.Contains(
            "A maximum of 10 files is allowed.",
            await tooManyResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        Assert.Empty(_storage.UploadedUrls);
    }

    [Fact]
    public async Task Upload_InaccessibleHiddenAndInactiveMachines_Return400()
    {
        await GrantManagerAsync();

        var inaccessible = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateJpegForm(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, inaccessible.StatusCode);

        var hidden = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateJpegForm(_hiddenMachineId));
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);

        var inactive = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateJpegForm(_inactiveMachineId));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);

        Assert.Empty(_storage.UploadedUrls);
    }

    [Fact]
    public async Task PostAndDelete_WithoutManager_Return403()
    {
        var post = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_visibleMachineId));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

        var delete = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/{_photoId}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task OtherTenantManagerAssignment_DoesNotGrantWriteAccess()
    {
        _dbContext.MachineParameterPhotoManagers.Add(new MachineParameterPhotoManager
        {
            Id = Guid.NewGuid(),
            TenantId = _otherTenantId,
            UserId = _userId,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var post = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_visibleMachineId));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task AssignedManager_CanUploadAndDelete()
    {
        await GrantManagerAsync();

        var upload = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_visibleMachineId));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var created = await upload.Content.ReadFromJsonAsync<List<MachineParameterPhotoDto>>();
        Assert.NotNull(created);
        Assert.Single(created);
        Assert.Equal(2, created[0].SortOrder);
        Assert.Single(_storage.UploadedUrls);

        var hiddenUpload = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_hiddenMachineId));
        Assert.Equal(HttpStatusCode.BadRequest, hiddenUpload.StatusCode);

        var otherUpload = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_otherMachineId));
        Assert.Equal(HttpStatusCode.BadRequest, otherUpload.StatusCode);

        var delete = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/{created[0].Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Contains(_storage.UploadedUrls[0], _storage.DeletedUrls);

        var missing = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var otherDelete = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/{_otherPhotoId}");
        Assert.Equal(HttpStatusCode.NotFound, otherDelete.StatusCode);
    }

    private async Task GrantManagerAsync()
    {
        _dbContext.MachineParameterPhotoManagers.Add(new MachineParameterPhotoManager
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            UserId = _userId,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
    }

    private void Authenticate(Guid userId, Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateToken(
            new[]
            {
                new Claim("user_id", userId.ToString()),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("role_id", "1"),
                new Claim(ClaimTypes.Email, "photo@example.com"),
                new Claim(ClaimTypes.Name, "photouser"),
                new Claim("display_name", "Photo User")
            },
            DateTime.UtcNow.AddHours(8));
        _client.DefaultRequestHeaders.Remove("Cookie");
        _client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookie.Name}={token}");
    }

    private static MultipartFormDataContent CreateJpegForm(Guid machineId) =>
        CreateFileForm(machineId, "image/jpeg", "photo.jpg", new byte[] { 1, 2, 3, 4 });

    private static MultipartFormDataContent CreateFileForm(
        Guid machineId,
        string contentType,
        string fileName,
        byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(machineId.ToString()), "machineId");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "files", fileName);
        return content;
    }
}
