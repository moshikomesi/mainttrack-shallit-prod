using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Hierarchy;
using MaintTrack.Application.MorningRoundV2;
using MaintTrack.Domain.MorningRoundV2;
using MaintTrack.Infrastructure.Hierarchy;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MorningRoundV2;

public sealed class MorningRoundV2Service : IMorningRoundV2Service
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public MorningRoundV2Service(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
    }

    public async Task<IReadOnlyList<MorningRoundV2ArrayDto>> GetChecklistAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        var arrays = await _dbContext.Arrays
            .AsNoTracking()
            .Where(a => a.IsActive && a.IsMorningRoundEnabled && a.TenantId == tenantId)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.NameKey)
            .ToListAsync(ct);

        var machines = await _dbContext.Machines
            .AsNoTracking()
            .Where(m => m.IsActive && m.TenantId == tenantId)
            .OrderBy(m => m.Name)
            .Select(m => new HierarchyService.MachineHierarchyRow(m.Id, m.Name, m.ArrayId))
            .ToListAsync(ct);

        await transaction.CommitAsync(ct);

        var hierarchy = HierarchyService.BuildHierarchy(arrays, machines);

        return hierarchy
            .Select(group => new MorningRoundV2ArrayDto(
                group.ArrayId,
                group.NameKey,
                group.Machines
                    .Select(m => new MorningRoundV2MachineDto(m.Id, m.Name))
                    .ToList()))
            .ToList();
    }

    public async Task<SubmitMorningRoundV2Response> SubmitAsync(
        SubmitMorningRoundV2Request request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("At least one checklist item is required.");
        }

        var submittedAt = request.Timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.Timestamp, DateTimeKind.Utc)
            : request.Timestamp.ToUniversalTime();

        var reportDate = DateOnly.FromDateTime(submittedAt);

        var machineIds = request.Items.Select(i => i.MachineId).Distinct().ToList();
        var validMachineIds = await _dbContext.Machines
            .AsNoTracking()
            .Where(m => m.IsActive && m.TenantId == tenantId && machineIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (validMachineIds.Count != machineIds.Count)
        {
            throw new ArgumentException("One or more machine IDs are invalid or inactive.");
        }

        var normalizedItems = request.Items
            .Select(item =>
            {
                var status = item.Status?.Trim().ToLowerInvariant();
                if (status is not ("ok" or "fail"))
                {
                    throw new ArgumentException($"Invalid status '{item.Status}' for machine {item.MachineId}.");
                }

                return new StoredMorningRoundV2Item(
                    item.MachineId,
                    status,
                    string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim());
            })
            .ToList();

        var itemsJson = JsonSerializer.Serialize(normalizedItems, JsonOptions);
        var now = DateTime.UtcNow;

        var existing = await _dbContext.MorningRoundV2Submissions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ReportDate == reportDate, ct);

        if (existing is null)
        {
            existing = new MorningRoundV2Submission
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ReportDate = reportDate,
                SubmittedAt = submittedAt,
                SubmittedByUserId = _currentUserContext.UserId,
                ItemsJson = itemsJson,
                CreatedAt = now
            };

            await _dbContext.MorningRoundV2Submissions.AddAsync(existing, ct);
        }
        else
        {
            existing.SubmittedAt = submittedAt;
            existing.SubmittedByUserId = _currentUserContext.UserId;
            existing.ItemsJson = itemsJson;
            existing.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);

        return new SubmitMorningRoundV2Response(existing.Id, existing.ReportDate, existing.SubmittedAt);
    }

    public async Task<MorningRoundV2ReportDto?> GetReportByIdAsync(Guid reportId, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        var submission = await _dbContext.MorningRoundV2Submissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == reportId && s.TenantId == tenantId, ct);

        return submission is null ? null : await BuildReportDtoAsync(submission, tenantId, ct);
    }

    public async Task<MorningRoundV2ReportDto?> GetReportByDateAsync(DateOnly date, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        var submission = await _dbContext.MorningRoundV2Submissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ReportDate == date && s.TenantId == tenantId, ct);

        return submission is null ? null : await BuildReportDtoAsync(submission, tenantId, ct);
    }

    public async Task<IReadOnlyList<MorningRoundV2ReportSummaryDto>> ListReportsAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        var submissions = await _dbContext.MorningRoundV2Submissions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.ReportDate)
            .ThenByDescending(s => s.SubmittedAt)
            .ToListAsync(ct);

        if (submissions.Count == 0)
        {
            return Array.Empty<MorningRoundV2ReportSummaryDto>();
        }

        var userIds = submissions.Select(s => s.SubmittedByUserId).Distinct().ToList();
        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);

        var usersById = users.ToDictionary(u => u.Id, u => u.DisplayName);

        return submissions
            .Select(submission =>
            {
                usersById.TryGetValue(submission.SubmittedByUserId, out var displayName);
                return new MorningRoundV2ReportSummaryDto(
                    submission.Id,
                    submission.ReportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    submission.SubmittedAt,
                    new MorningRoundV2ReportSubmittedByDto(
                        submission.SubmittedByUserId,
                        displayName ?? string.Empty));
            })
            .ToList();
    }

    private async Task<MorningRoundV2ReportDto> BuildReportDtoAsync(
        MorningRoundV2Submission submission,
        Guid tenantId,
        CancellationToken ct)
    {
        var storedItems = JsonSerializer.Deserialize<List<StoredMorningRoundV2Item>>(submission.ItemsJson, JsonOptions)
            ?? new List<StoredMorningRoundV2Item>();

        var itemByMachineId = storedItems
            .GroupBy(i => i.MachineId)
            .ToDictionary(g => g.Key, g => g.Last());

        var machineIds = itemByMachineId.Keys.ToList();

        var arrays = await _dbContext.Arrays
            .AsNoTracking()
            .Where(a => a.IsActive && a.IsMorningRoundEnabled && a.TenantId == tenantId)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.NameKey)
            .ToListAsync(ct);

        var machinesFromDb = machineIds.Count == 0
            ? new List<HierarchyService.MachineHierarchyRow>()
            : await _dbContext.Machines
                .AsNoTracking()
                .Where(m => m.TenantId == tenantId && machineIds.Contains(m.Id))
                .Select(m => new HierarchyService.MachineHierarchyRow(m.Id, m.Name, m.ArrayId))
                .ToListAsync(ct);

        var machinesById = machinesFromDb.ToDictionary(m => m.Id);
        var hierarchyMachines = new List<HierarchyService.MachineHierarchyRow>(machineIds.Count);

        foreach (var machineId in machineIds)
        {
            if (machinesById.TryGetValue(machineId, out var machine))
            {
                hierarchyMachines.Add(machine);
            }
            else
            {
                hierarchyMachines.Add(new HierarchyService.MachineHierarchyRow(
                    machineId,
                    "machine.unknown",
                    null));
            }
        }

        hierarchyMachines = hierarchyMachines
            .OrderBy(m => m.Name)
            .ToList();

        var hierarchy = HierarchyService.BuildHierarchy(arrays, hierarchyMachines);

        var reportArrays = hierarchy
            .Select(group => new MorningRoundV2ReportArrayDto(
                group.ArrayId,
                group.NameKey,
                group.Machines
                    .Select(machine =>
                    {
                        itemByMachineId.TryGetValue(machine.Id, out var item);
                        return new MorningRoundV2ReportMachineDto(
                            machine.Id,
                            machine.Name,
                            item?.Status,
                            item?.Notes);
                    })
                    .ToList()))
            .ToList();

        var submitter = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == submission.SubmittedByUserId && u.TenantId == tenantId)
            .Select(u => new { u.Id, u.DisplayName })
            .FirstOrDefaultAsync(ct);

        var submittedBy = new MorningRoundV2ReportSubmittedByDto(
            submission.SubmittedByUserId,
            submitter?.DisplayName ?? string.Empty);

        var reportDate = submission.ReportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return new MorningRoundV2ReportDto(
            submission.Id,
            reportDate,
            submission.SubmittedAt,
            submittedBy,
            reportArrays);
    }

    private sealed record StoredMorningRoundV2Item(
        Guid MachineId,
        string Status,
        string? Notes);
}
