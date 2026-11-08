using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MaintTrack.Application.Clearance;
using MaintTrack.Domain.Clearance;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Tests.Clearance;

/// <summary>
/// A user signed in through the real API pipeline, plus a pending Clearance item of their tenant.
/// <paramref name="Authenticate"/> sets the user's auth cookie once; the scenarios reuse it
/// unchanged across the revocation.
/// </summary>
public sealed record ClearanceRevocationSetup(
    HttpClient Client,
    MaintTrackDbContext Db,
    Action Authenticate,
    Guid TenantId,
    Guid UserId,
    Guid EntryId,
    Guid ItemId);

/// <summary>
/// Grant-revocation scenarios shared by the SQLite and PostgreSQL API tests: grants are deleted
/// directly in the database while the same token stays in use.
/// </summary>
public static class ClearanceGrantRevocationScenarios
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string QueueUrl = "/api/v1/clearance";
    private const string AccessUrl = "/api/v1/clearance/access";

    public static async Task RevokingViewerGrant_RemovesAllClearanceAccessOnNextRequest(ClearanceRevocationSetup s)
    {
        var sourceUrl = SourceUrl(s);
        s.Authenticate();
        var cookie = CurrentCookie(s.Client);

        var queue = await GetOkAsync<ClearanceQueueResponse>(s.Client, QueueUrl);
        Assert.False(queue.CanApprove);
        Assert.Contains(queue.Items, item => item.Id == s.ItemId);
        var source = await GetOkAsync<ClearanceSourceItemResponse>(s.Client, sourceUrl);
        Assert.Equal(s.ItemId, source.Item.Id);
        Assert.False(source.CanApprove);
        Assert.Equal(new ClearanceAccessDto(CanView: true, CanApprove: false), await GetOkAsync<ClearanceAccessDto>(s.Client, AccessUrl));

        Assert.Equal(1, await s.Db.ClearanceViewers.IgnoreQueryFilters()
            .Where(v => v.TenantId == s.TenantId && v.UserId == s.UserId)
            .ExecuteDeleteAsync());

        var queueAfter = await s.Client.GetAsync(QueueUrl);
        var sourceAfter = await s.Client.GetAsync(sourceUrl);
        var accessAfter = await GetOkAsync<ClearanceAccessDto>(s.Client, AccessUrl);

        Assert.Equal(cookie, CurrentCookie(s.Client));
        foreach (var response in new[] { queueAfter, sourceAfter })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(new ClearanceAccessDto(CanView: false, CanApprove: false), accessAfter);
        Assert.True(await s.Db.Users.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(u => u.Id == s.UserId && u.TenantId == s.TenantId && u.IsActive));
    }

    public static async Task RevokingApproverGrant_KeepsViewAccessButForbidsDecisions(ClearanceRevocationSetup s)
    {
        var sourceUrl = SourceUrl(s);
        s.Authenticate();
        var cookie = CurrentCookie(s.Client);

        var queue = await GetOkAsync<ClearanceQueueResponse>(s.Client, QueueUrl);
        Assert.True(queue.CanApprove);
        Assert.Contains(queue.Items, item => item.Id == s.ItemId);
        var source = await GetOkAsync<ClearanceSourceItemResponse>(s.Client, sourceUrl);
        Assert.Equal(s.ItemId, source.Item.Id);
        Assert.True(source.CanApprove);
        Assert.Equal(new ClearanceAccessDto(CanView: true, CanApprove: true), await GetOkAsync<ClearanceAccessDto>(s.Client, AccessUrl));

        var itemBefore = await LoadItemStateAsync(s);
        var auditCountBefore = await s.Db.AuditLogs.IgnoreQueryFilters().CountAsync();

        Assert.Equal(1, await s.Db.ClearanceApprovers.IgnoreQueryFilters()
            .Where(a => a.TenantId == s.TenantId && a.UserId == s.UserId)
            .ExecuteDeleteAsync());
        Assert.Equal(1, await s.Db.ClearanceViewers.IgnoreQueryFilters()
            .CountAsync(v => v.TenantId == s.TenantId && v.UserId == s.UserId));

        var queueAfter = await GetOkAsync<ClearanceQueueResponse>(s.Client, QueueUrl);
        var sourceAfter = await GetOkAsync<ClearanceSourceItemResponse>(s.Client, sourceUrl);
        var accessAfter = await GetOkAsync<ClearanceAccessDto>(s.Client, AccessUrl);
        var approve = await s.Client.PostAsync($"/api/v1/clearance/{s.ItemId}/approve", null);
        var reject = await s.Client.PostAsJsonAsync($"/api/v1/clearance/{s.ItemId}/reject", new { note = "revoked" });

        Assert.Equal(cookie, CurrentCookie(s.Client));
        Assert.False(queueAfter.CanApprove);
        Assert.Contains(queueAfter.Items, item => item.Id == s.ItemId);
        Assert.Equal(s.ItemId, sourceAfter.Item.Id);
        Assert.False(sourceAfter.CanApprove);
        Assert.Equal(new ClearanceAccessDto(CanView: true, CanApprove: false), accessAfter);
        foreach (var response in new[] { approve, reject })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }

        var itemAfter = await LoadItemStateAsync(s);
        Assert.Equal(ClearanceStatus.Pending, itemAfter.Status);
        Assert.Equal(itemBefore, itemAfter);
        Assert.Equal(auditCountBefore, await s.Db.AuditLogs.IgnoreQueryFilters().CountAsync());
        Assert.False(await s.Db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EntityId == s.ItemId));
    }

    private static string SourceUrl(ClearanceRevocationSetup s) =>
        $"/api/v1/clearance/source/{ClearanceSourceType.MaintenanceEntry}/{s.EntryId}";

    private static string CurrentCookie(HttpClient client) =>
        client.DefaultRequestHeaders.GetValues("Cookie").Single();

    private static async Task<T> GetOkAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static Task<ItemState> LoadItemStateAsync(ClearanceRevocationSetup s) =>
        s.Db.ClearanceItems.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == s.ItemId)
            .Select(c => new ItemState(c.Status, c.DecidedByUserId, c.DecidedAt, c.DecisionNote, c.UpdatedAt))
            .SingleAsync();

    private sealed record ItemState(string Status, Guid? DecidedByUserId, DateTime? DecidedAt, string? DecisionNote, DateTime? UpdatedAt);
}
