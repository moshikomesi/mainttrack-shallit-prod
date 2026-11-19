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
using MaintTrack.Infrastructure.MachineParameterPhotos;
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
    private readonly Guid _sharedMachineId = Guid.NewGuid();
    private readonly Guid _photoId = Guid.NewGuid();
    private readonly Guid _otherPhotoId = Guid.NewGuid();
    private Guid _hopperParameterId;
    private Guid _inactiveParameterId;
    private Guid _otherParameterId;

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

        var sharedArrayId = Guid.NewGuid();
        _dbContext.Arrays.Add(new WorkGroup
        {
            Id = sharedArrayId,
            TenantId = _tenantId,
            NameKey = "array.washing_system",
            SortOrder = 1,
            IsActive = true,
            IsMorningRoundEnabled = true,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.Machines.Add(new Machine
        {
            Id = _sharedMachineId,
            TenantId = _tenantId,
            Name = "machine.largeHopper",
            Code = "H1",
            ArrayId = sharedArrayId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        await ParameterPhotoCatalogSeeder.EnsureAsync(_dbContext, _tenantId, CancellationToken.None);
        await ParameterPhotoCatalogSeeder.EnsureAsync(_dbContext, _otherTenantId, CancellationToken.None);

        _hopperParameterId = await _dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .Where(parameter => parameter.TenantId == _tenantId && parameter.NameKey == ParameterPhotoCatalog.HopperKey)
            .Select(parameter => parameter.Id)
            .SingleAsync();
        var washingGroupId = await _dbContext.ParameterPhotoGroups
            .IgnoreQueryFilters()
            .Where(group => group.TenantId == _tenantId && group.NameKey == ParameterPhotoCatalog.WashingSystemGroupKey)
            .Select(group => group.Id)
            .SingleAsync();
        _inactiveParameterId = Guid.NewGuid();
        _dbContext.ParameterPhotoParameters.Add(new ParameterPhotoParameter
        {
            Id = _inactiveParameterId,
            TenantId = _tenantId,
            GroupId = washingGroupId,
            NameKey = "parameterPhoto.parameter.inactive",
            SortOrder = 99,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });
        _otherParameterId = await _dbContext.ParameterPhotoParameters
            .IgnoreQueryFilters()
            .Where(parameter => parameter.TenantId == _otherTenantId && parameter.NameKey == ParameterPhotoCatalog.HopperKey)
            .Select(parameter => parameter.Id)
            .SingleAsync();

        _dbContext.MachineParameterPhotos.AddRange(
            new MachineParameterPhoto
            {
                Id = _photoId,
                TenantId = _tenantId,
                ParameterId = _hopperParameterId,
                MachineId = _sharedMachineId,
                ImageUrl = "/uploads/visible.jpg",
                SortOrder = 1,
                CreatedByUserId = _userId,
                CreatedAt = DateTime.UtcNow
            },
            new MachineParameterPhoto
            {
                Id = _otherPhotoId,
                TenantId = _otherTenantId,
                ParameterId = _otherParameterId,
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/machine-parameter-photos/legacy")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/machine-parameter-photos?parameterId={_hopperParameterId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/machine-parameter-photos", new MultipartFormDataContent())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/v1/machine-parameter-photos/{_photoId}")).StatusCode);
    }

    [Fact]
    public async Task FeatureCatalog_ReturnsIndependentGroups_AndNotSharedMachines()
    {
        var response = await _client.GetAsync("/api/v1/machine-parameter-photos/hierarchy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("canManage").GetBoolean());

        var groups = document.RootElement.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(
            new[]
            {
                ParameterPhotoCatalog.WashingSystemGroupKey,
                ParameterPhotoCatalog.CoolingSystemGroupKey,
                ParameterPhotoCatalog.PackingHouseGroupKey
            },
            groups.Select(group => group.GetProperty("nameKey").GetString()).ToArray());

        var washingParameters = groups[0].GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("nameKey").GetString())
            .ToArray();
        Assert.Equal(
            new[] { ParameterPhotoCatalog.HopperKey, ParameterPhotoCatalog.CoolingSystemComputerKey },
            washingParameters);
        Assert.Contains(
            groups[1].GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("nameKey").GetString() == ParameterPhotoCatalog.CompressorRoomsKey);
        Assert.Contains(
            groups[2].GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("nameKey").GetString() == ParameterPhotoCatalog.BagPullersUnderViscoseKey);
        Assert.DoesNotContain(
            groups.SelectMany(group => group.GetProperty("parameters").EnumerateArray()),
            parameter => parameter.GetProperty("nameKey").GetString() == "machine.largeHopper");
        Assert.DoesNotContain(
            groups.SelectMany(group => group.GetProperty("parameters").EnumerateArray()),
            parameter => parameter.GetProperty("id").GetGuid() == _sharedMachineId);
    }

    [Fact]
    public async Task SharedHierarchyMachinesAndMorningRound_StillIncludeSharedMachines()
    {
        var shared = await _client.GetFromJsonAsync<List<HierarchyArrayDto>>("/api/v1/hierarchy");
        Assert.NotNull(shared);
        Assert.Contains(shared.SelectMany(group => group.Machines), machine => machine.Id == _sharedMachineId);

        var machines = await _client.GetAsync("/api/v1/machines");
        Assert.Equal(HttpStatusCode.OK, machines.StatusCode);
        using (var document = JsonDocument.Parse(await machines.Content.ReadAsStringAsync()))
        {
            Assert.Contains(
                document.RootElement.EnumerateArray(),
                machine => machine.GetProperty("id").GetGuid() == _sharedMachineId);
        }

        var morningRound = await _client.GetAsync("/api/v2/morning-round");
        Assert.Equal(HttpStatusCode.OK, morningRound.StatusCode);
        using (var document = JsonDocument.Parse(await morningRound.Content.ReadAsStringAsync()))
        {
            Assert.Contains(
                document.RootElement.EnumerateArray().SelectMany(array => array.GetProperty("machines").EnumerateArray()),
                machine => machine.GetProperty("id").GetGuid() == _sharedMachineId);
        }
    }

    [Fact]
    public async Task List_RequiresParameterId_AndReturnsLinkedPhotos()
    {
        var missing = await _client.GetAsync("/api/v1/machine-parameter-photos");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var response = await _client.GetAsync($"/api/v1/machine-parameter-photos?parameterId={_hopperParameterId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var photos = await response.Content.ReadFromJsonAsync<List<MachineParameterPhotoDto>>();
        Assert.NotNull(photos);
        var photo = Assert.Single(photos);
        Assert.Equal(_photoId, photo.Id);
        Assert.Equal(_hopperParameterId, photo.ParameterId);
        Assert.Equal("/uploads/visible.jpg", photo.ImageUrl);
    }

    [Fact]
    public async Task List_DoesNotExposeInactiveOrOtherTenantParameters()
    {
        var inactive = await _client.GetAsync($"/api/v1/machine-parameter-photos?parameterId={_inactiveParameterId}");
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);

        var other = await _client.GetAsync($"/api/v1/machine-parameter-photos?parameterId={_otherParameterId}");
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);

        var unknown = await _client.GetAsync($"/api/v1/machine-parameter-photos?parameterId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("Parameter not found.", await unknown.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_InvalidFiles_Return400()
    {
        await GrantManagerAsync();

        var unsupported = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateFileForm(_hopperParameterId, "image/gif", "photo.gif", new byte[] { 1, 2, 3, 4 }));
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Contains("Invalid file type.", await unsupported.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var empty = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateFileForm(_hopperParameterId, "image/jpeg", "empty.jpg", Array.Empty<byte>()));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("Uploaded files cannot be empty.", await empty.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var tooMany = new MultipartFormDataContent
        {
            { new StringContent(_hopperParameterId.ToString()), "parameterId" }
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
    public async Task Upload_InaccessibleParameters_Return400()
    {
        await GrantManagerAsync();

        var inaccessible = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateJpegForm(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, inaccessible.StatusCode);

        var inactive = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateJpegForm(_inactiveParameterId));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);

        var other = await _client.PostAsync(
            "/api/v1/machine-parameter-photos",
            CreateJpegForm(_otherParameterId));
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);

        Assert.Empty(_storage.UploadedUrls);
    }

    [Fact]
    public async Task PostAndDelete_WithoutManager_Return403()
    {
        var post = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_hopperParameterId));
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

        var post = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_hopperParameterId));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task AssignedManager_CanUploadAndDelete_AndCannotCrossTenants()
    {
        await GrantManagerAsync();

        var upload = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_hopperParameterId));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var created = await upload.Content.ReadFromJsonAsync<List<MachineParameterPhotoDto>>();
        Assert.NotNull(created);
        Assert.Single(created);
        Assert.Equal(2, created[0].SortOrder);
        Assert.Equal(_hopperParameterId, created[0].ParameterId);
        Assert.Single(_storage.UploadedUrls);

        var otherUpload = await _client.PostAsync("/api/v1/machine-parameter-photos", CreateJpegForm(_otherParameterId));
        Assert.Equal(HttpStatusCode.BadRequest, otherUpload.StatusCode);

        var delete = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/{created[0].Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Contains(_storage.UploadedUrls[0], _storage.DeletedUrls);

        var missing = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var otherDelete = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/{_otherPhotoId}");
        Assert.Equal(HttpStatusCode.NotFound, otherDelete.StatusCode);

        _dbContext.ChangeTracker.Clear();
        var legacy = await _dbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(item => item.Id == _photoId);
        Assert.Equal("/uploads/visible.jpg", legacy.ImageUrl);
        Assert.Equal(_sharedMachineId, legacy.MachineId);
    }

    [Fact]
    public async Task LegacyRecovery_IsManagerAndTenantScoped_AndDoesNotAssignHopper()
    {
        var legacyPhotoId = Guid.NewGuid();
        _dbContext.MachineParameterPhotos.Add(new MachineParameterPhoto
        {
            Id = legacyPhotoId,
            TenantId = _tenantId,
            MachineId = _sharedMachineId,
            ImageUrl = "/uploads/first-hopper.jpg",
            SortOrder = 4,
            CreatedByUserId = _userId,
            CreatedAt = DateTime.UtcNow
        });
        var otherMachineId = Guid.NewGuid();
        _dbContext.Machines.Add(new Machine
        {
            Id = otherMachineId,
            TenantId = _otherTenantId,
            Name = "machine.largeHopper",
            Code = "HX",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        var otherLegacyId = Guid.NewGuid();
        _dbContext.MachineParameterPhotos.Add(new MachineParameterPhoto
        {
            Id = otherLegacyId,
            TenantId = _otherTenantId,
            MachineId = otherMachineId,
            ImageUrl = "/uploads/other-legacy.jpg",
            SortOrder = 1,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var forbidden = await _client.GetAsync("/api/v1/machine-parameter-photos/legacy");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await GrantManagerAsync();
        var response = await _client.GetAsync("/api/v1/machine-parameter-photos/legacy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var legacyPhotos = await response.Content.ReadFromJsonAsync<List<LegacyParameterPhotoDto>>();
        var recovered = Assert.Single(legacyPhotos!);
        Assert.Equal(legacyPhotoId, recovered.Id);
        Assert.Equal(_sharedMachineId, recovered.MachineId);
        Assert.Equal("machine.largeHopper", recovered.MachineName);
        Assert.Equal("/uploads/first-hopper.jpg", recovered.ImageUrl);

        var hopperPhotos = await _client.GetFromJsonAsync<List<MachineParameterPhotoDto>>(
            $"/api/v1/machine-parameter-photos?parameterId={_hopperParameterId}");
        Assert.DoesNotContain(hopperPhotos!, photo => photo.Id == legacyPhotoId);

        var coolingParameterId = await _dbContext.ParameterPhotoParameters
            .Where(parameter =>
                parameter.TenantId == _tenantId &&
                parameter.NameKey == ParameterPhotoCatalog.CoolingSystemComputerKey)
            .Select(parameter => parameter.Id)
            .SingleAsync();
        var assign = await _client.PostAsJsonAsync(
            $"/api/v1/machine-parameter-photos/legacy/{legacyPhotoId}/assign",
            new { parameterId = coolingParameterId });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(photo => photo.Id == legacyPhotoId);
        Assert.Equal(coolingParameterId, stored.ParameterId);
        Assert.Equal(_sharedMachineId, stored.MachineId);
        Assert.Equal("/uploads/first-hopper.jpg", stored.ImageUrl);
        Assert.Equal(1, await _dbContext.MachineParameterPhotos.IgnoreQueryFilters().CountAsync(photo => photo.ParameterId == _hopperParameterId));
    }

    [Fact]
    public async Task LegacyAssignAndDelete_WithoutManager_Return403()
    {
        var assign = await _client.PostAsJsonAsync(
            $"/api/v1/machine-parameter-photos/legacy/{_photoId}/assign",
            new { parameterId = _hopperParameterId });
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);

        var delete = await _client.DeleteAsync($"/api/v1/machine-parameter-photos/legacy/{_photoId}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(photo => photo.Id == _photoId);
        Assert.Equal(_hopperParameterId, stored.ParameterId);
        Assert.Equal(_sharedMachineId, stored.MachineId);
        Assert.Equal("/uploads/visible.jpg", stored.ImageUrl);
        Assert.Empty(_storage.DeletedUrls);
    }

    [Fact]
    public async Task LegacyAssign_AlreadyLinkedPhoto_ReturnsNotFound()
    {
        await GrantManagerAsync();
        var coolingParameterId = await CoolingParameterIdAsync();

        var assign = await _client.PostAsJsonAsync(
            $"/api/v1/machine-parameter-photos/legacy/{_photoId}/assign",
            new { parameterId = coolingParameterId });
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(photo => photo.Id == _photoId);
        Assert.Equal(_hopperParameterId, stored.ParameterId);
        Assert.Equal(_sharedMachineId, stored.MachineId);
        Assert.Equal("/uploads/visible.jpg", stored.ImageUrl);
    }

    [Fact]
    public async Task LegacyAssign_OtherTenantPhoto_ReturnsNotFound()
    {
        await GrantManagerAsync();
        var otherMachineId = Guid.NewGuid();
        _dbContext.Machines.Add(new Machine
        {
            Id = otherMachineId,
            TenantId = _otherTenantId,
            Name = "machine.largeHopper",
            Code = "HX",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        var otherLegacyId = Guid.NewGuid();
        _dbContext.MachineParameterPhotos.Add(new MachineParameterPhoto
        {
            Id = otherLegacyId,
            TenantId = _otherTenantId,
            MachineId = otherMachineId,
            ImageUrl = "/uploads/other-tenant-legacy.jpg",
            SortOrder = 2,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        var coolingParameterId = await CoolingParameterIdAsync();

        var assign = await _client.PostAsJsonAsync(
            $"/api/v1/machine-parameter-photos/legacy/{otherLegacyId}/assign",
            new { parameterId = coolingParameterId });
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.MachineParameterPhotos.IgnoreQueryFilters().SingleAsync(photo => photo.Id == otherLegacyId);
        Assert.Null(stored.ParameterId);
        Assert.Equal(otherMachineId, stored.MachineId);
        Assert.Equal("/uploads/other-tenant-legacy.jpg", stored.ImageUrl);
    }

    private async Task<Guid> CoolingParameterIdAsync() =>
        await _dbContext.ParameterPhotoParameters
            .Where(parameter =>
                parameter.TenantId == _tenantId &&
                parameter.NameKey == ParameterPhotoCatalog.CoolingSystemComputerKey)
            .Select(parameter => parameter.Id)
            .SingleAsync();

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

    private static MultipartFormDataContent CreateJpegForm(Guid parameterId) =>
        CreateFileForm(parameterId, "image/jpeg", "photo.jpg", new byte[] { 1, 2, 3, 4 });

    private static MultipartFormDataContent CreateFileForm(
        Guid parameterId,
        string contentType,
        string fileName,
        byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(parameterId.ToString()), "parameterId");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "files", fileName);
        return content;
    }
}
