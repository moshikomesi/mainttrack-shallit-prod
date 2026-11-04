using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MaintTrack.Api.Auth;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Authentication;
using MaintTrack.Application.Clearance;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.MorningRoundV2;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Tenants;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Tests.MachineParameterPhotos;
using MaintTrack.Tests.MorningRoundV2;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MaintTrack.Tests.Clearance;

public sealed class ClearanceApiIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RecordingFileStorageService _storage = new();
    private readonly MutableTimeProvider _clock = new(TestBusinessCalendar.MorningOf(19));
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private MaintTrackDbContext _dbContext = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _approverId = Guid.NewGuid();
    private readonly Guid _workerId = Guid.NewGuid();
    private readonly Guid _superAdminId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _otherWorkerId = Guid.NewGuid();
    private readonly Guid _machineId = Guid.NewGuid();
    private readonly Guid _otherMachineId = Guid.NewGuid();
    private readonly Guid _entryId = Guid.NewGuid();
    private readonly Guid _legacyEntryId = Guid.NewGuid();
    private readonly Guid _otherEntryId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _otherItemId = Guid.NewGuid();

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
                    { "Jwt:Key", "TestSecretKeyForJwtTokenGeneration12345678901234567890" }
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<MaintTrackDbContext>));
                services.RemoveAll(typeof(MaintTrackDbContext));
                services.AddDbContext<MaintTrackDbContext>((_, options) => options.UseSqlite(_connection));

                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService>(_storage);

                services.RemoveAll<IBusinessCalendar>();
                services.AddSingleton<IBusinessCalendar>(TestBusinessCalendar.Create(_clock));
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var scope = _factory.Services.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<MaintTrackDbContext>();
        await _dbContext.Database.EnsureCreatedAsync();

        _dbContext.Tenants.AddRange(
            new Tenant { Id = _tenantId, Name = "Clearance Factory", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow },
            new Tenant { Id = _otherTenantId, Name = "Other Factory", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });

        _dbContext.Roles.AddRange(
            new Role { Id = (int)UserRole.Worker, Name = "Worker" },
            new Role { Id = (int)UserRole.Manager, Name = "Manager" },
            new Role { Id = (int)UserRole.SuperAdmin, Name = "SuperAdmin" });

        _dbContext.Users.AddRange(
            CreateUser(_approverId, "approver", UserRole.Worker),
            CreateUser(_workerId, "worker", UserRole.Worker),
            CreateUser(_superAdminId, "admin", UserRole.SuperAdmin),
            CreateUser(_managerId, "manager", UserRole.Manager),
            CreateUser(_otherWorkerId, "other-worker", UserRole.Worker));

        _dbContext.ClearanceApprovers.Add(new ClearanceApprover
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            UserId = _approverId,
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.Machines.AddRange(
            new Machine { Id = _machineId, TenantId = _tenantId, Name = "machine.local", Code = "L1", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Machine { Id = _otherMachineId, TenantId = _otherTenantId, Name = "machine.other", Code = "O1", IsActive = true, CreatedAt = DateTime.UtcNow });

        _dbContext.MaintenanceEntries.AddRange(
            CreateEntry(_entryId, _tenantId, _machineId),
            CreateEntry(_legacyEntryId, _tenantId, _machineId),
            CreateEntry(_otherEntryId, _otherTenantId, _otherMachineId));

        _dbContext.ClearanceItems.AddRange(
            CreateItem(_itemId, _tenantId, _entryId),
            CreateItem(_otherItemId, _otherTenantId, _otherEntryId));

        await _dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetQueue_WithoutAuth_Returns401()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/clearance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQueue_AsAssignedApprover_ReturnsTenantItemsWithCanApprove()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.GetAsync("/api/v1/clearance?status=pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClearanceQueueResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body!.CanApprove);
        Assert.Equal(1, body.TotalCount);
        var item = Assert.Single(body.Items);
        Assert.Equal(_itemId, item.Id);
        Assert.Equal(ClearanceSourceType.MaintenanceEntry, item.SourceType);
        Assert.Equal(_entryId, item.MaintenanceEntryId);
    }

    [Fact]
    public async Task GetQueue_ClientSuppliedTenantId_IsIgnored()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.GetAsync($"/api/v1/clearance?tenantId={_otherTenantId}&machineId={_otherMachineId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClearanceQueueResponse>(JsonOptions);
        Assert.Empty(body!.Items);
        Assert.Equal(0, body.TotalCount);
    }

    [Fact]
    public async Task GetQueue_AsUnassignedWorker_Returns403()
    {
        Authenticate(_workerId, _tenantId, UserRole.Worker);

        var response = await _client.GetAsync("/api/v1/clearance");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetQueue_ForgedSuperAdminRoleClaim_Returns403()
    {
        Authenticate(_workerId, _tenantId, UserRole.SuperAdmin);

        var response = await _client.GetAsync("/api/v1/clearance");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetQueue_UserTokenForAnotherTenant_Returns403()
    {
        Authenticate(_approverId, _otherTenantId, UserRole.Worker);

        var response = await _client.GetAsync("/api/v1/clearance");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetQueue_AsUnassignedSuperAdmin_ReadsButCannotApprove()
    {
        Authenticate(_superAdminId, _tenantId, UserRole.SuperAdmin);

        var response = await _client.GetAsync("/api/v1/clearance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClearanceQueueResponse>(JsonOptions);
        Assert.False(body!.CanApprove);
        Assert.Single(body.Items);
    }

    [Fact]
    public async Task GetQueue_InvalidStatus_Returns400()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.GetAsync("/api/v1/clearance?status=deleted");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Invalid status", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteMaintenance_AsSuperAdmin_IsRejectedForLegacyAndClearanceLinkedReports(bool linked)
    {
        Authenticate(_superAdminId, _tenantId, UserRole.SuperAdmin);
        var entryId = linked ? _entryId : _legacyEntryId;

        var response = await _client.DeleteAsync($"/api/maintenance/{entryId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Maintenance reports cannot be deleted.", await response.Content.ReadAsStringAsync());
        Assert.True(await _dbContext.MaintenanceEntries.IgnoreQueryFilters().AnyAsync(e => e.Id == entryId));
        Assert.Empty(_storage.DeletedUrls);
    }

    [Theory]
    [InlineData(UserRole.Worker)]
    [InlineData(UserRole.Manager)]
    public async Task DeleteMaintenance_AsNonSuperAdmin_Returns403(UserRole role)
    {
        Authenticate(_workerId, _tenantId, role);

        var response = await _client.DeleteAsync($"/api/maintenance/{_legacyEntryId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await _dbContext.MaintenanceEntries.IgnoreQueryFilters().AnyAsync(e => e.Id == _legacyEntryId));
    }

    [Fact]
    public async Task Decisions_WithoutAuth_Return401()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/v1/clearance/{_itemId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync($"/api/v1/clearance/{_itemId}/reject", new { note = "x" })).StatusCode);
        await AssertItemPendingAsync(_itemId);
    }

    [Fact]
    public async Task Approve_AsAssignedApprover_Returns200WithDecision()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.PostAsync($"/api/v1/clearance/{_itemId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClearanceDecisionDto>(JsonOptions);
        Assert.Equal(_itemId, body!.Id);
        Assert.Equal(ClearanceStatus.Approved, body.Status);
        Assert.Equal(_approverId, body.DecidedByUserId);
        Assert.Null(body.DecisionNote);

        var stored = await LoadItemAsync(_itemId);
        Assert.Equal(ClearanceStatus.Approved, stored.Status);
        Assert.Equal(_approverId, stored.DecidedByUserId);
    }

    [Fact]
    public async Task BySource_ApprovedItemCarriesApproverIdentityAndTimeForTheDeclaration()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);
        var sourceUrl = $"/api/v1/clearance/source/maintenance_entry/{_entryId}";

        var pending = await _client.GetFromJsonAsync<ClearanceSourceItemResponse>(sourceUrl, JsonOptions);
        Assert.Equal(ClearanceStatus.Pending, pending!.Item.Status);
        Assert.Null(pending.Item.DecidedByUserId);
        Assert.Null(pending.Item.DecidedAt);

        var approve = await _client.PostAsync($"/api/v1/clearance/{_itemId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var decision = await approve.Content.ReadFromJsonAsync<ClearanceDecisionDto>(JsonOptions);

        var approved = await _client.GetFromJsonAsync<ClearanceSourceItemResponse>(sourceUrl, JsonOptions);
        Assert.Equal(ClearanceStatus.Approved, approved!.Item.Status);
        Assert.Equal(_approverId, approved.Item.DecidedByUserId);
        Assert.False(string.IsNullOrWhiteSpace(approved.Item.DecidedByName));
        Assert.Equal(decision!.DecidedAt, approved.Item.DecidedAt);
        var audit = Assert.Single(await _dbContext.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityId == _itemId).ToListAsync());
        Assert.Equal(("clearance_approved", _approverId), (audit.Action, audit.UserId));
    }

    [Fact]
    public async Task Reject_WithNote_Returns200AndPersistsNote()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.PostAsJsonAsync($"/api/v1/clearance/{_itemId}/reject", new { note = "Replace the seal" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClearanceDecisionDto>(JsonOptions);
        Assert.Equal(ClearanceStatus.Rejected, body!.Status);
        Assert.Equal("Replace the seal", body.DecisionNote);
        Assert.Equal("Replace the seal", (await LoadItemAsync(_itemId)).DecisionNote);
    }

    [Fact]
    public async Task Reject_WithoutBody_Returns200WithNullNote()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.PostAsync($"/api/v1/clearance/{_itemId}/reject", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await LoadItemAsync(_itemId);
        Assert.Equal(ClearanceStatus.Rejected, stored.Status);
        Assert.Null(stored.DecisionNote);
    }

    [Fact]
    public async Task Reject_NoteTooLong_Returns400AndItemStaysPending()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/clearance/{_itemId}/reject", new { note = new string('n', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertItemPendingAsync(_itemId);
    }

    [Fact]
    public async Task Decide_AlreadyDecided_Returns409AndKeepsFirstDecision()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/v1/clearance/{_itemId}/approve", null)).StatusCode);

        var approveAgain = await _client.PostAsync($"/api/v1/clearance/{_itemId}/approve", null);
        var reject = await _client.PostAsJsonAsync($"/api/v1/clearance/{_itemId}/reject", new { note = "late" });

        Assert.Equal(HttpStatusCode.Conflict, approveAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Contains("already been decided", await reject.Content.ReadAsStringAsync());
        var stored = await LoadItemAsync(_itemId);
        Assert.Equal(ClearanceStatus.Approved, stored.Status);
        Assert.Null(stored.DecisionNote);
    }

    [Theory]
    [InlineData(UserRole.Worker)]
    [InlineData(UserRole.SuperAdmin)]
    public async Task Decide_AsUnassignedUser_Returns403(UserRole role)
    {
        var userId = role == UserRole.SuperAdmin ? _superAdminId : _workerId;
        Authenticate(userId, _tenantId, role);

        var approve = await _client.PostAsync($"/api/v1/clearance/{_itemId}/approve", null);
        var reject = await _client.PostAsJsonAsync($"/api/v1/clearance/{_itemId}/reject", new { note = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
        await AssertItemPendingAsync(_itemId);
    }

    [Fact]
    public async Task RevokedApprover_SameToken_IsForbiddenOnNextRequestAndLosesAccess()
    {
        var approveTargetId = Guid.NewGuid();
        var rejectTargetId = Guid.NewGuid();
        var approveEntryId = Guid.NewGuid();
        var rejectEntryId = Guid.NewGuid();
        _dbContext.MaintenanceEntries.AddRange(
            CreateEntry(approveEntryId, _tenantId, _machineId),
            CreateEntry(rejectEntryId, _tenantId, _machineId));
        _dbContext.ClearanceItems.AddRange(
            CreateItem(approveTargetId, _tenantId, approveEntryId),
            CreateItem(rejectTargetId, _tenantId, rejectEntryId));
        await _dbContext.SaveChangesAsync();

        Authenticate(_approverId, _tenantId, UserRole.Worker);
        var tokenCookie = _client.DefaultRequestHeaders.GetValues("Cookie").Single();

        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/v1/clearance/{_itemId}/approve", null)).StatusCode);
        Assert.Equal(ClearanceStatus.Approved, (await LoadItemAsync(_itemId)).Status);

        var removed = await _dbContext.ClearanceApprovers
            .Where(a => a.TenantId == _tenantId && a.UserId == _approverId)
            .ExecuteDeleteAsync();
        Assert.Equal(1, removed);

        var approve = await _client.PostAsync($"/api/v1/clearance/{approveTargetId}/approve", null);
        var reject = await _client.PostAsJsonAsync($"/api/v1/clearance/{rejectTargetId}/reject", new { note = "x" });
        var access = await _client.GetFromJsonAsync<ClearanceAccessDto>("/api/v1/clearance/access", JsonOptions);

        Assert.Equal(tokenCookie, _client.DefaultRequestHeaders.GetValues("Cookie").Single());
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
        await AssertItemPendingAsync(approveTargetId);
        await AssertItemPendingAsync(rejectTargetId);
        Assert.Null((await LoadItemAsync(rejectTargetId)).DecisionNote);
        Assert.False(await _dbContext.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.EntityId == approveTargetId || a.EntityId == rejectTargetId));
        Assert.Equal(new ClearanceAccessDto(CanView: false, CanApprove: false), access);
    }

    [Fact]
    public async Task Decide_ItemOfAnotherTenant_Returns404LikeMissingItem()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var foreign = await _client.PostAsJsonAsync(
            $"/api/v1/clearance/{_otherItemId}/reject", new { note = "x", tenantId = _otherTenantId });
        var missing = await _client.PostAsync($"/api/v1/clearance/{Guid.NewGuid()}/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        await AssertItemPendingAsync(_otherItemId);
    }

    [Fact]
    public async Task Decide_InvalidIdFormat_Returns404()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);

        var response = await _client.PostAsync("/api/v1/clearance/not-a-guid/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_DoesNotChangeIsSafeToOperateAndDeletionStaysBlocked()
    {
        Authenticate(_approverId, _tenantId, UserRole.Worker);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/v1/clearance/{_itemId}/approve", null)).StatusCode);

        _dbContext.ChangeTracker.Clear();
        var entry = await _dbContext.MaintenanceEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == _entryId);
        Assert.True(entry.IsSafeToOperate);

        Authenticate(_superAdminId, _tenantId, UserRole.SuperAdmin);
        var delete = await _client.DeleteAsync($"/api/maintenance/{_entryId}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.True(await _dbContext.MaintenanceEntries.IgnoreQueryFilters().AnyAsync(e => e.Id == _entryId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ClearanceStatus.Pending)]
    [InlineData(ClearanceStatus.Approved)]
    [InlineData(ClearanceStatus.Rejected)]
    public async Task EditMaintenanceReport_IsRejectedForEveryUser_WhateverTheClearanceStatus(string? status)
    {
        var entryId = status is null ? _legacyEntryId : _entryId;
        await _dbContext.MaintenanceEntries.IgnoreQueryFilters()
            .Where(e => e.Id == entryId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedByUserId, _workerId));
        if (status is ClearanceStatus.Approved or ClearanceStatus.Rejected)
        {
            await _dbContext.ClearanceItems.IgnoreQueryFilters()
                .Where(c => c.Id == _itemId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.DecidedByUserId, (Guid?)_approverId)
                    .SetProperty(c => c.DecidedAt, (DateTime?)new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(c => c.DecisionNote, "Replace the seal"));
        }
        var typeId = await AddMaintenanceTypeAsync("other");
        var entryBefore = await LoadEntryAsync(entryId);
        var itemBefore = await LoadItemAsync(_itemId);
        var body = new
        {
            maintenanceTypeId = typeId,
            description = "edited",
            imageUrl = entryBefore.ImageUrl,
            sparePartsUsed = "seal",
            employeeName = "Someone Else",
            workHours = 9,
            isSafeToOperate = !entryBefore.IsSafeToOperate
        };

        var editors = new (string Who, Guid UserId, UserRole Role)[]
        {
            ("creator", _workerId, UserRole.Worker),
            ("manager", _managerId, UserRole.Manager),
            ("super admin", _superAdminId, UserRole.SuperAdmin),
            ("assigned approver", _approverId, UserRole.Worker),
            ("unassigned worker", _otherWorkerId, UserRole.Worker)
        };
        foreach (var (who, userId, role) in editors)
        {
            Authenticate(userId, _tenantId, role);
            var response = await _client.PutAsJsonAsync($"/api/maintenance/{entryId}", body);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{who} got {(int)response.StatusCode}");
            Assert.Contains("cannot be edited", await response.Content.ReadAsStringAsync());
        }

        Assert.Equivalent(entryBefore, await LoadEntryAsync(entryId), strict: true);
        Assert.Equivalent(itemBefore, await LoadItemAsync(_itemId), strict: true);
        Assert.False(await _dbContext.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "maintenance_updated"));

        if (status == ClearanceStatus.Pending)
        {
            Authenticate(_approverId, _tenantId, UserRole.Worker);
            Assert.Equal(HttpStatusCode.OK,
                (await _client.PostAsync($"/api/v1/clearance/{_itemId}/reject", JsonContent.Create(new { note = "Still leaking" }))).StatusCode);
            Assert.Equal(ClearanceStatus.Rejected, (await LoadItemAsync(_itemId)).Status);
            Assert.Equivalent(entryBefore, await LoadEntryAsync(entryId), strict: true);
        }
    }

    [Fact]
    public async Task EditMaintenanceReport_DirectRequestsCannotBypassTheRule()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync($"/api/maintenance/{_entryId}", new { description = "edited" })).StatusCode);

        Authenticate(_superAdminId, _tenantId, UserRole.SuperAdmin);
        var foreign = await _client.PutAsJsonAsync($"/api/maintenance/{_otherEntryId}", new { description = "edited" });
        var missing = await _client.PutAsJsonAsync($"/api/maintenance/{Guid.NewGuid()}", new { description = "edited" });
        var malformed = await _client.PutAsync($"/api/maintenance/{_entryId}",
            new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        var patch = await _client.PatchAsync($"/api/maintenance/{_entryId}", JsonContent.Create(new { description = "edited" }));

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(await foreign.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, patch.StatusCode);
        foreach (var id in new[] { _entryId, _otherEntryId })
        {
            var entry = await LoadEntryAsync(id);
            Assert.Equal("fault", entry.Description);
            Assert.Null(entry.UpdatedAt);
        }
    }

    [Theory]
    [InlineData("approver", true, true)]
    [InlineData("admin", true, false)]
    [InlineData("worker", false, false)]
    public async Task GetAccess_ReflectsDatabaseAssignment(string who, bool canView, bool canApprove)
    {
        var (userId, role) = who switch
        {
            "approver" => (_approverId, UserRole.Worker),
            "admin" => (_superAdminId, UserRole.SuperAdmin),
            _ => (_workerId, UserRole.Worker)
        };
        Authenticate(userId, _tenantId, role);

        var response = await _client.GetAsync("/api/v1/clearance/access");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClearanceAccessDto>(JsonOptions);
        Assert.Equal(new ClearanceAccessDto(canView, canApprove), body);
    }

    [Fact]
    public async Task GetAccess_And_GetBySource_WithoutAuth_Return401()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/clearance/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/v1/clearance/source/maintenance_entry/{_entryId}")).StatusCode);
    }

    [Fact]
    public async Task GetBySource_ReturnsItemForTenantReportAnd404OtherwiseWithoutLeaking()
    {
        Authenticate(_workerId, _tenantId, UserRole.Worker);

        var found = await _client.GetAsync($"/api/v1/clearance/source/maintenance_entry/{_entryId}");
        var noItem = await _client.GetAsync($"/api/v1/clearance/source/maintenance_entry/{_legacyEntryId}");
        var foreign = await _client.GetAsync($"/api/v1/clearance/source/maintenance_entry/{_otherEntryId}");
        var invalidType = await _client.GetAsync($"/api/v1/clearance/source/treatment/{_entryId}");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var body = await found.Content.ReadFromJsonAsync<ClearanceSourceItemResponse>(JsonOptions);
        Assert.Equal(_itemId, body!.Item.Id);
        Assert.False(body.CanApprove);
        Assert.Equal(HttpStatusCode.NotFound, noItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(await noItem.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, invalidType.StatusCode);
    }

    [Fact]
    public async Task SubmittingMaintenanceReport_ThroughApi_AddsOnePendingQueueItem()
    {
        var typeId = Guid.NewGuid();
        _dbContext.MaintenanceTypes.Add(new MaintenanceType
        {
            Id = typeId, TenantId = _tenantId, Code = "lubrication", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        Authenticate(_workerId, _tenantId, UserRole.Worker);

        var created = await _client.PostAsJsonAsync("/api/maintenance", new
        {
            machineId = _machineId,
            date = "2026-10-20",
            maintenanceTypeId = typeId,
            workHours = 1,
            isSafeToOperate = false
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entryId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Authenticate(_approverId, _tenantId, UserRole.Worker);
        var queue = await (await _client.GetAsync("/api/v1/clearance?status=pending"))
            .Content.ReadFromJsonAsync<ClearanceQueueResponse>(JsonOptions);
        var item = Assert.Single(queue!.Items, i => i.MaintenanceEntryId == entryId);
        Assert.Equal(ClearanceStatus.Pending, item.Status);
        Assert.Equal(_machineId, item.MachineId);
    }

    [Fact]
    public async Task SubmittingMorningRound_ThroughApi_CreatesItemOnlyForAbnormalFindings()
    {
        var secondMachine = Guid.NewGuid();
        _dbContext.Machines.Add(new Machine
        {
            Id = secondMachine, TenantId = _tenantId, Name = "machine.second", Code = "L2", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        Authenticate(_workerId, _tenantId, UserRole.Worker);

        _clock.UtcNow = TestBusinessCalendar.MorningOf(19);
        var normal = await _client.PostAsJsonAsync("/api/v2/morning-round/submit", new
        {
            timestamp = "2026-10-19T06:00:00Z",
            items = new[] { new { machineId = _machineId, status = "ok", notes = (string?)null } }
        });
        _clock.UtcNow = TestBusinessCalendar.MorningOf(20);
        var abnormal = await _client.PostAsJsonAsync("/api/v2/morning-round/submit", new
        {
            timestamp = "2026-10-20T06:00:00Z",
            items = new[]
            {
                new { machineId = _machineId, status = "fail", notes = (string?)"oil leak" },
                new { machineId = secondMachine, status = "fail", notes = (string?)"belt worn" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        Assert.Equal(HttpStatusCode.OK, abnormal.StatusCode);
        var abnormalId = (await abnormal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Authenticate(_approverId, _tenantId, UserRole.Worker);
        var queue = await (await _client.GetAsync("/api/v1/clearance?status=pending&pageSize=100"))
            .Content.ReadFromJsonAsync<ClearanceQueueResponse>(JsonOptions);
        var roundItems = queue!.Items.Where(i => i.SourceType == ClearanceSourceType.MorningRoundV2).ToList();
        var item = Assert.Single(roundItems);
        Assert.Equal(abnormalId, item.MorningRoundV2SubmissionId);
        Assert.Equal(new[] { "belt worn", "oil leak" }, item.Findings.Select(f => f.Notes).OrderBy(n => n));

        var bySecondMachine = await (await _client.GetAsync($"/api/v1/clearance?machineId={secondMachine}"))
            .Content.ReadFromJsonAsync<ClearanceQueueResponse>(JsonOptions);
        Assert.Equal(item.Id, Assert.Single(bySecondMachine!.Items).Id);
    }

    [Fact]
    public async Task SubmittingMorningRoundTwiceOnTheSameDay_Returns409_AndKeepsFirstSubmissionAndDecision()
    {
        _clock.UtcNow = TestBusinessCalendar.MorningOf(20);
        Authenticate(_workerId, _tenantId, UserRole.Worker);
        var first = await _client.PostAsJsonAsync("/api/v2/morning-round/submit", new
        {
            timestamp = "2026-10-20T03:00:00Z",
            items = new[] { new { machineId = _machineId, status = "fail", notes = (string?)"oil leak" } }
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var submissionId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var itemId = (await LoadMorningRoundItemAsync(submissionId)).Id;
        Authenticate(_approverId, _tenantId, UserRole.Worker);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/v1/clearance/{itemId}/approve", null)).StatusCode);
        var submissionBefore = await LoadSubmissionAsync(submissionId);
        var itemBefore = await LoadItemAsync(itemId);

        _clock.UtcNow = _clock.UtcNow.AddHours(8);
        var duplicates = new List<HttpResponseMessage>();
        foreach (var (userId, role, timestamp) in new[]
                 {
                     (_superAdminId, UserRole.SuperAdmin, "2026-10-20T11:00:00Z"),
                     (_otherWorkerId, UserRole.Worker, "2026-10-21T06:00:00Z"),
                     (_workerId, UserRole.Worker, "2026-10-19T06:00:00Z")
                 })
        {
            Authenticate(userId, _tenantId, role);
            duplicates.Add(await _client.PostAsJsonAsync("/api/v2/morning-round/submit", new
            {
                timestamp,
                items = new[] { new { machineId = _machineId, status = "ok", notes = (string?)"all good" } }
            }));
        }

        Assert.All(duplicates, d => Assert.Equal(HttpStatusCode.Conflict, d.StatusCode));
        Assert.Contains("already been submitted for 2026-10-20", await duplicates[0].Content.ReadAsStringAsync());
        Assert.Equivalent(submissionBefore, await LoadSubmissionAsync(submissionId), strict: true);
        Assert.Equivalent(itemBefore, await LoadItemAsync(itemId), strict: true);
        Assert.Equal(1, await _dbContext.MorningRoundV2Submissions.IgnoreQueryFilters().CountAsync(s => s.TenantId == _tenantId));
        Assert.Equal(1, await _dbContext.ClearanceItems.IgnoreQueryFilters()
            .CountAsync(c => c.SourceType == ClearanceSourceType.MorningRoundV2));
    }

    [Fact]
    public async Task SubmittingMorningRound_OnTheNextBusinessDay_IsAllowedAgain()
    {
        Authenticate(_workerId, _tenantId, UserRole.Worker);
        var body = new
        {
            timestamp = "2026-10-20T03:00:00Z",
            items = new[] { new { machineId = _machineId, status = "ok", notes = (string?)null } }
        };

        _clock.UtcNow = TestBusinessCalendar.MorningOf(20);
        var day20 = await _client.PostAsJsonAsync("/api/v2/morning-round/submit", body);
        _clock.UtcNow = new DateTimeOffset(2026, 10, 20, 21, 5, 0, TimeSpan.Zero);
        var day21 = await _client.PostAsJsonAsync("/api/v2/morning-round/submit", body);

        Assert.Equal(HttpStatusCode.OK, day20.StatusCode);
        Assert.Equal(HttpStatusCode.OK, day21.StatusCode);
        Assert.Equal("2026-10-21", (await day21.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reportDate").GetString());
    }

    private async Task<MaintenanceEntry> LoadEntryAsync(Guid id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.MaintenanceEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private async Task<MorningRoundV2Submission> LoadSubmissionAsync(Guid id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.MorningRoundV2Submissions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task<ClearanceItem> LoadMorningRoundItemAsync(Guid submissionId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.ClearanceItems.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.MorningRoundV2SubmissionId == submissionId);
    }

    private async Task<Guid> AddMaintenanceTypeAsync(string code)
    {
        var typeId = Guid.NewGuid();
        _dbContext.MaintenanceTypes.Add(new MaintenanceType
        {
            Id = typeId, TenantId = _tenantId, Code = code, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        return typeId;
    }

    private async Task<ClearanceItem> LoadItemAsync(Guid id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.ClearanceItems.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task AssertItemPendingAsync(Guid id)
    {
        var stored = await LoadItemAsync(id);
        Assert.Equal(ClearanceStatus.Pending, stored.Status);
        Assert.Null(stored.DecidedByUserId);
        Assert.Null(stored.DecidedAt);
    }

    private User CreateUser(Guid id, string username, UserRole role) => new()
    {
        Id = id,
        TenantId = _tenantId,
        Username = username,
        Email = $"{username}@example.com",
        PasswordHash = "hash",
        DisplayName = username,
        RoleId = (int)role,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };


    private static MaintenanceEntry CreateEntry(Guid id, Guid tenantId, Guid machineId) => new()
    {
        Id = id,
        TenantId = tenantId,
        MachineId = machineId,
        Date = new DateOnly(2026, 10, 15),
        Description = "fault",
        ImageUrl = $"/uploads/{id:N}.jpg",
        EmployeeName = "Technician",
        WorkHours = 1,
        IsSafeToOperate = true,
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow
    };

    private static ClearanceItem CreateItem(Guid id, Guid tenantId, Guid entryId) => new()
    {
        Id = id,
        TenantId = tenantId,
        SourceType = ClearanceSourceType.MaintenanceEntry,
        MaintenanceEntryId = entryId,
        Status = ClearanceStatus.Pending,
        SubmittedByUserId = Guid.NewGuid(),
        SubmittedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private void Authenticate(Guid userId, Guid tenantId, UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateToken(
            new[]
            {
                new Claim("user_id", userId.ToString()),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("role_id", ((int)role).ToString()),
                new Claim(ClaimTypes.Email, "clearance@example.com"),
                new Claim(ClaimTypes.Name, "clearance"),
                new Claim("display_name", "Clearance User")
            },
            DateTime.UtcNow.AddHours(8));
        _client.DefaultRequestHeaders.Remove("Cookie");
        _client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookie.Name}={token}");
    }
}
