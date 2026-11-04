using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Clearance;
using MaintTrack.Domain.Audit;
using MaintTrack.Domain.Clearance;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Clearance;

public sealed class ClearanceService : IClearanceService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxDecisionNoteLength = 2000;
    private const string UnknownMachineName = "machine.unknown";
    private const string ApprovedAuditAction = "clearance_approved";
    private const string RejectedAuditAction = "clearance_rejected";

    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public ClearanceService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
    }

    public async Task<bool> IsCurrentUserApproverAsync(CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        var access = await GetCurrentUserAccessAsync(tenantId, ct);
        return access?.IsApprover ?? false;
    }

    public async Task<ClearanceAccessDto> GetAccessAsync(CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        var access = await GetCurrentUserAccessAsync(tenantId, ct);
        return new ClearanceAccessDto(access?.CanView ?? false, access?.IsApprover ?? false);
    }

    public async Task<ClearanceQueueResponse> GetQueueAsync(ClearanceQueueQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tenantId = EnsureTenantResolved();
        var access = await EnsureCanViewAsync(tenantId, ct);
        var status = NormalizeStatus(query.Status);

        var pageNumber = query.PageNumber is > 0 ? query.PageNumber.Value : 1;
        var pageSize = query.PageSize is > 0 ? Math.Min(query.PageSize.Value, MaxPageSize) : DefaultPageSize;

        var items = _dbContext.ClearanceItems.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (status is not null)
        {
            items = items.Where(c => c.Status == status);
        }

        var rows = BuildRows(tenantId, items);

        if (query.ArrayId is not null || query.MachineId is not null)
        {
            // Morning Round findings live in items_json, so matching submissions are resolved
            // in memory (one submission per tenant per day) and then filtered by id in SQL.
            var matchingSubmissionIds = await FindMorningRoundSubmissionsMatchingAsync(
                tenantId, items, query.ArrayId, query.MachineId, ct);

            var arrayId = query.ArrayId;
            var machineId = query.MachineId;
            rows = rows.Where(r =>
                (r.SourceType == ClearanceSourceType.MaintenanceEntry
                    && (arrayId == null || r.ArrayId == arrayId)
                    && (machineId == null || r.MachineId == machineId))
                || (r.SourceType == ClearanceSourceType.MorningRoundV2
                    && matchingSubmissionIds.Contains(r.MorningRoundV2SubmissionId!.Value)));
        }

        var totalCount = await rows.CountAsync(ct);

        var page = await rows
            .OrderByDescending(r => r.SubmittedAt)
            .ThenByDescending(r => r.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = await ToDtosAsync(tenantId, page, ct);
        return new ClearanceQueueResponse(dtos, pageNumber, pageSize, totalCount, access.IsApprover);
    }

    public async Task<ClearanceSourceItemResponse?> GetBySourceAsync(string sourceType, Guid sourceId, CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();

        // Checked before the lookup so users without a grant learn nothing about the report.
        var access = await EnsureCanViewAsync(tenantId, ct);

        var normalizedType = sourceType?.Trim().ToLowerInvariant() switch
        {
            ClearanceSourceType.MaintenanceEntry => ClearanceSourceType.MaintenanceEntry,
            ClearanceSourceType.MorningRoundV2 => ClearanceSourceType.MorningRoundV2,
            _ => throw new ArgumentException(
                $"Invalid source type. Allowed values: {ClearanceSourceType.MaintenanceEntry}, {ClearanceSourceType.MorningRoundV2}.",
                nameof(sourceType))
        };

        var items = _dbContext.ClearanceItems.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.SourceType == normalizedType);
        items = normalizedType == ClearanceSourceType.MaintenanceEntry
            ? items.Where(c => c.MaintenanceEntryId == sourceId)
            : items.Where(c => c.MorningRoundV2SubmissionId == sourceId);

        var row = await BuildRows(tenantId, items).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var dto = (await ToDtosAsync(tenantId, new List<QueueRow> { row }, ct))[0];
        return new ClearanceSourceItemResponse(dto, access.IsApprover);
    }

    /// <summary>
    /// Joins items to their source report, machine and users with explicit tenant predicates,
    /// so an item whose source (or maintenance machine) lives in another tenant is never returned.
    /// </summary>
    private IQueryable<QueueRow> BuildRows(Guid tenantId, IQueryable<ClearanceItem> items)
    {
        var entries = _dbContext.MaintenanceEntries.AsNoTracking().Where(e => e.TenantId == tenantId);
        var submissions = _dbContext.MorningRoundV2Submissions.AsNoTracking().Where(s => s.TenantId == tenantId);
        var machines = _dbContext.Machines.AsNoTracking().Where(m => m.TenantId == tenantId);
        var types = _dbContext.MaintenanceTypes.AsNoTracking().Where(t => t.TenantId == tenantId);
        var users = _dbContext.Users.AsNoTracking().Where(u => u.TenantId == tenantId);

        return
            from c in items
            join e in entries on c.MaintenanceEntryId equals (Guid?)e.Id into entryGroup
            from e in entryGroup.DefaultIfEmpty()
            join s in submissions on c.MorningRoundV2SubmissionId equals (Guid?)s.Id into submissionGroup
            from s in submissionGroup.DefaultIfEmpty()
            join m in machines on (Guid?)e.MachineId equals (Guid?)m.Id into machineGroup
            from m in machineGroup.DefaultIfEmpty()
            join t in types on e.MaintenanceTypeId equals (Guid?)t.Id into typeGroup
            from t in typeGroup.DefaultIfEmpty()
            join su in users on c.SubmittedByUserId equals su.Id into submitterGroup
            from su in submitterGroup.DefaultIfEmpty()
            join du in users on c.DecidedByUserId equals (Guid?)du.Id into deciderGroup
            from du in deciderGroup.DefaultIfEmpty()
            where (c.SourceType == ClearanceSourceType.MaintenanceEntry && e != null && m != null)
                || (c.SourceType == ClearanceSourceType.MorningRoundV2 && s != null)
            select new QueueRow
            {
                Id = c.Id,
                SourceType = c.SourceType,
                MaintenanceEntryId = c.MaintenanceEntryId,
                MorningRoundV2SubmissionId = c.MorningRoundV2SubmissionId,
                MachineId = m == null ? (Guid?)null : m.Id,
                MachineName = m == null ? null : m.Name,
                ArrayId = m == null ? null : m.ArrayId,
                ReportDate = c.SourceType == ClearanceSourceType.MaintenanceEntry ? e!.Date : s!.ReportDate,
                Description = e == null ? null : e.Description,
                MaintenanceTypeCode = t == null ? null : t.Code,
                ItemsJson = s == null ? null : s.ItemsJson,
                Status = c.Status,
                SubmittedByUserId = c.SubmittedByUserId,
                SubmittedByName = su == null ? null : su.DisplayName,
                SubmittedAt = c.SubmittedAt,
                DecidedByUserId = c.DecidedByUserId,
                DecidedByName = du == null ? null : du.DisplayName,
                DecidedAt = c.DecidedAt,
                DecisionNote = c.DecisionNote
            };
    }

    private async Task<List<Guid>> FindMorningRoundSubmissionsMatchingAsync(
        Guid tenantId,
        IQueryable<ClearanceItem> items,
        Guid? arrayId,
        Guid? machineId,
        CancellationToken ct)
    {
        var candidates = await (
                from c in items
                where c.SourceType == ClearanceSourceType.MorningRoundV2
                join s in _dbContext.MorningRoundV2Submissions.AsNoTracking().Where(s => s.TenantId == tenantId)
                    on c.MorningRoundV2SubmissionId equals (Guid?)s.Id
                select new { s.Id, s.ItemsJson })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return new List<Guid>();
        }

        var findingsBySubmission = candidates.ToDictionary(c => c.Id, c => MorningRoundV2Findings.Abnormal(c.ItemsJson));
        var machineArrays = await LoadMachinesAsync(
            tenantId, findingsBySubmission.Values.SelectMany(f => f).Select(f => f.MachineId), ct);

        return findingsBySubmission
            .Where(pair => pair.Value.Any(finding =>
                machineArrays.TryGetValue(finding.MachineId, out var machine)
                && (machineId == null || finding.MachineId == machineId)
                && (arrayId == null || machine.ArrayId == arrayId)))
            .Select(pair => pair.Key)
            .ToList();
    }

    private async Task<List<ClearanceItemDto>> ToDtosAsync(Guid tenantId, List<QueueRow> rows, CancellationToken ct)
    {
        var findingsByItem = rows
            .Where(r => r.SourceType == ClearanceSourceType.MorningRoundV2)
            .ToDictionary(r => r.Id, r => MorningRoundV2Findings.Abnormal(r.ItemsJson));
        var machines = await LoadMachinesAsync(
            tenantId, findingsByItem.Values.SelectMany(f => f).Select(f => f.MachineId), ct);

        return rows
            .Select(r =>
            {
                IReadOnlyList<ClearanceFindingDto> findings = findingsByItem.TryGetValue(r.Id, out var abnormal)
                    ? abnormal
                        .Select(f => machines.TryGetValue(f.MachineId, out var machine)
                            ? new ClearanceFindingDto(f.MachineId, machine.Name, machine.ArrayId, f.Notes)
                            : new ClearanceFindingDto(f.MachineId, UnknownMachineName, null, f.Notes))
                        .ToList()
                    : Array.Empty<ClearanceFindingDto>();

                return new ClearanceItemDto(
                    r.Id,
                    r.SourceType,
                    r.MaintenanceEntryId,
                    r.MorningRoundV2SubmissionId,
                    r.MachineId,
                    r.MachineName,
                    r.ArrayId,
                    r.ReportDate,
                    r.Description,
                    r.MaintenanceTypeCode,
                    findings,
                    r.Status,
                    r.SubmittedByUserId,
                    r.SubmittedByName,
                    r.SubmittedAt,
                    r.DecidedByUserId,
                    r.DecidedByName,
                    r.DecidedAt,
                    r.DecisionNote);
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, (string Name, Guid? ArrayId)>> LoadMachinesAsync(
        Guid tenantId,
        IEnumerable<Guid> machineIds,
        CancellationToken ct)
    {
        var ids = machineIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, (string Name, Guid? ArrayId)>();
        }

        var machines = await _dbContext.Machines
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.ArrayId })
            .ToListAsync(ct);

        return machines.ToDictionary(m => m.Id, m => (m.Name, m.ArrayId));
    }

    /// <summary>
    /// Clearance information (queue, status, decision details) is visible only to active users with a
    /// clearance_viewers or clearance_approvers assignment in this tenant. Roles never grant it.
    /// </summary>
    private async Task<CurrentUserAccess> EnsureCanViewAsync(Guid tenantId, CancellationToken ct)
    {
        var access = await GetCurrentUserAccessAsync(tenantId, ct);
        if (access is null || !access.CanView)
        {
            throw new UnauthorizedAccessException("You are not allowed to view Clearance information.");
        }

        return access;
    }

    public Task<ClearanceDecisionDto> ApproveAsync(Guid id, CancellationToken ct) =>
        DecideAsync(id, ClearanceStatus.Approved, note: null, ct);

    public Task<ClearanceDecisionDto> RejectAsync(Guid id, string? note, CancellationToken ct) =>
        DecideAsync(id, ClearanceStatus.Rejected, NormalizeDecisionNote(note), ct);

    private async Task<ClearanceDecisionDto> DecideAsync(Guid id, string decision, string? note, CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        var userId = _currentUserContext.UserId;

        // Checked before touching the item so non-approvers learn nothing about item existence.
        await EnsureActiveApproverAsync(tenantId, ct);

        var decidedAt = DateTime.UtcNow;
        var currentApprover = ClearanceApproverRules.ActiveApprovers(_dbContext, tenantId)
            .Where(u => u.Id == userId);

        await using (var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            // One conditional UPDATE is the only pending -> final transition. Concurrent requests
            // serialize on the row and the loser matches zero rows. The approver predicate is
            // repeated so an assignment removed after the pre-check cannot still decide.
            var updated = await TenantOwnedItems(tenantId)
                .Where(c => c.Id == id && c.Status == ClearanceStatus.Pending)
                .Where(_ => currentApprover.Any())
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Status, decision)
                    .SetProperty(c => c.DecidedByUserId, (Guid?)userId)
                    .SetProperty(c => c.DecidedAt, (DateTime?)decidedAt)
                    .SetProperty(c => c.DecisionNote, note)
                    .SetProperty(c => c.UpdatedAt, (DateTime?)decidedAt), ct);

            if (updated == 1)
            {
                await _dbContext.AuditLogs.AddAsync(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    Action = decision == ClearanceStatus.Approved ? ApprovedAuditAction : RejectedAuditAction,
                    EntityName = nameof(ClearanceItem),
                    EntityId = id,
                    CreatedAt = decidedAt
                }, ct);
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new ClearanceDecisionDto(id, decision, userId, decidedAt, note);
            }

            await transaction.RollbackAsync(ct);
        }

        await EnsureActiveApproverAsync(tenantId, ct);

        var existsInTenant = await TenantOwnedItems(tenantId).AsNoTracking().AnyAsync(c => c.Id == id, ct);
        if (existsInTenant)
        {
            throw new ClearanceDecisionConflictException();
        }

        throw new KeyNotFoundException("Clearance item not found.");
    }

    /// <summary>
    /// Items of this tenant whose source report (and, for maintenance entries, its machine) also
    /// belongs to this tenant; the same visibility rule the queue applies through its joins.
    /// </summary>
    private IQueryable<ClearanceItem> TenantOwnedItems(Guid tenantId) =>
        _dbContext.ClearanceItems.Where(c => c.TenantId == tenantId
            && ((c.SourceType == ClearanceSourceType.MaintenanceEntry
                    && _dbContext.MaintenanceEntries.Any(e => e.Id == c.MaintenanceEntryId
                        && e.TenantId == tenantId
                        && _dbContext.Machines.Any(m => m.Id == e.MachineId && m.TenantId == tenantId)))
                || (c.SourceType == ClearanceSourceType.MorningRoundV2
                    && _dbContext.MorningRoundV2Submissions.Any(s => s.Id == c.MorningRoundV2SubmissionId
                        && s.TenantId == tenantId))));

    private async Task EnsureActiveApproverAsync(Guid tenantId, CancellationToken ct)
    {
        if (!await ClearanceApproverRules.IsActiveApproverAsync(_dbContext, tenantId, _currentUserContext.UserId, ct))
        {
            throw new UnauthorizedAccessException("You are not allowed to decide Clearance items.");
        }
    }

    private static string? NormalizeDecisionNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxDecisionNoteLength)
        {
            throw new ArgumentException(
                $"Note must be at most {MaxDecisionNoteLength} characters.",
                nameof(note));
        }

        return trimmed;
    }

    private Guid EnsureTenantResolved()
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        return tenantId;
    }

    /// <summary>
    /// Reads the current user from the database on every call: the user must exist in this
    /// tenant and be active. Approver and viewer status each require an assignment row in the same tenant.
    /// </summary>
    private async Task<CurrentUserAccess?> GetCurrentUserAccessAsync(Guid tenantId, CancellationToken ct)
    {
        var userId = _currentUserContext.UserId;
        var activeApprovers = ClearanceApproverRules.ActiveApprovers(_dbContext, tenantId);
        var viewers = _dbContext.ClearanceViewers.Where(v => v.TenantId == tenantId);

        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == tenantId && u.IsActive)
            .Select(u => new CurrentUserAccess(
                activeApprovers.Any(a => a.Id == u.Id),
                viewers.Any(v => v.UserId == u.Id)))
            .FirstOrDefaultAsync(ct);
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized switch
        {
            ClearanceStatus.Pending or ClearanceStatus.Approved or ClearanceStatus.Rejected => normalized,
            _ => throw new ArgumentException(
                $"Invalid status. Allowed values: {ClearanceStatus.Pending}, {ClearanceStatus.Approved}, {ClearanceStatus.Rejected}.",
                nameof(status))
        };
    }

    /// <summary>Approvers can see what they decide on; viewers can see without deciding.</summary>
    private sealed record CurrentUserAccess(bool IsApprover, bool IsViewer)
    {
        public bool CanView => IsApprover || IsViewer;
    }

    private sealed class QueueRow
    {
        public Guid Id { get; init; }
        public string SourceType { get; init; } = string.Empty;
        public Guid? MaintenanceEntryId { get; init; }
        public Guid? MorningRoundV2SubmissionId { get; init; }
        public Guid? MachineId { get; init; }
        public string? MachineName { get; init; }
        public Guid? ArrayId { get; init; }
        public DateOnly ReportDate { get; init; }
        public string? Description { get; init; }
        public string? MaintenanceTypeCode { get; init; }
        public string? ItemsJson { get; init; }
        public string Status { get; init; } = string.Empty;
        public Guid SubmittedByUserId { get; init; }
        public string? SubmittedByName { get; init; }
        public DateTime SubmittedAt { get; init; }
        public Guid? DecidedByUserId { get; init; }
        public string? DecidedByName { get; init; }
        public DateTime? DecidedAt { get; init; }
        public string? DecisionNote { get; init; }
    }
}
