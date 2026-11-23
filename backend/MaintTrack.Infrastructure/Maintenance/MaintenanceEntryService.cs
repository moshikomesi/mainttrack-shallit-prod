using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Maintenance;
using MaintTrack.Application.Translations;
using MaintTrack.Domain.Audit;
using MaintTrack.Domain.Clearance;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Translations;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaintTrack.Infrastructure.Maintenance;

public sealed class MaintenanceEntryService : IMaintenanceEntryService
{
    private const int MaximumAdditionalImages = 2;
    private const string V1SentinelLfLine = MaintenanceDescriptionText.V1Sentinel + "\n";
    private const string V2SentinelLfLine = MaintenanceDescriptionText.V2Sentinel + "\n";
    private const string V1SentinelCrLfLine = MaintenanceDescriptionText.V1Sentinel + "\r\n";
    private const string V2SentinelCrLfLine = MaintenanceDescriptionText.V2Sentinel + "\r\n";

    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<MaintenanceEntryService> _logger;
    private readonly ITranslationRepository? _translations;
    private readonly bool _enqueueEnabled;

    public MaintenanceEntryService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IFileStorageService fileStorageService,
        ILogger<MaintenanceEntryService> logger,
        IOptions<TranslationWorkerOptions>? translationOptions = null,
        ITranslationRepository? translations = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _translations = translations;
        _enqueueEnabled = translationOptions?.Value.EnqueueEnabled == true;
    }

    public async Task<Guid> CreateAsync(CreateMaintenanceEntryRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var additionalImageUrls = request.AdditionalImageUrls ?? Array.Empty<string>();

        if (additionalImageUrls.Count > MaximumAdditionalImages)
            throw new InvalidOperationException("A maximum of two additional images is allowed.");

        if (additionalImageUrls.Count > 0 && string.IsNullOrWhiteSpace(request.ImageUrl))
            throw new InvalidOperationException("A primary image is required when additional images are provided.");

        var tenantId = _tenantContext.TenantId.Value;
        var userId = _currentUserContext.UserId;

        var machineExists = await _dbContext.Machines
            .AnyAsync(m => m.Id == request.MachineId && m.TenantId == tenantId, ct);
        if (!machineExists)
            throw new InvalidOperationException("Machine not found.");

        if (request.MaintenanceTypeId == Guid.Empty)
            throw new InvalidOperationException("Maintenance type is required.");

        var (description, maintenanceTypeId) = await ResolveDescriptionForTypeAsync(
            request.MaintenanceTypeId,
            request.Description,
            ct);

        var entryId = Guid.NewGuid();
        var entry = new MaintenanceEntry
        {
            Id = entryId,
            TenantId = tenantId,
            MachineId = request.MachineId,
            Date = request.Date,
            MaintenanceTypeId = maintenanceTypeId,
            Description = description,
            ImageUrl = request.ImageUrl,
            SparePartsUsed = request.SparePartsUsed,
            // Create payload has no employee name; always use authenticated user.
            EmployeeName = _currentUserContext.DisplayName,
            WorkHours = request.WorkHours,
            IsSafeToOperate = request.IsSafeToOperate,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        for (var index = 0; index < additionalImageUrls.Count; index++)
        {
            var imageUrl = additionalImageUrls[index];
            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new InvalidOperationException("Additional image URLs cannot be empty.");

            entry.AdditionalImages.Add(new MaintenanceEntryImage
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ImageUrl = imageUrl,
                SortOrder = index + 1,
                CreatedAt = entry.CreatedAt
            });
        }

        await _dbContext.MaintenanceEntries.AddAsync(entry, ct);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Action = "maintenance_created",
            EntityName = "MaintenanceEntry",
            EntityId = entryId,
            CreatedAt = entry.CreatedAt
        }, ct);

        // Saved in the same SaveChanges (one transaction) as the entry: neither exists without the other.
        await _dbContext.ClearanceItems.AddAsync(new ClearanceItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceType = ClearanceSourceType.MaintenanceEntry,
            MaintenanceEntryId = entryId,
            Status = ClearanceStatus.Pending,
            SubmittedByUserId = userId,
            SubmittedAt = entry.CreatedAt,
            CreatedAt = entry.CreatedAt
        }, ct);

        await _dbContext.SaveChangesAsync(ct);

        try
        {
            // The report is committed; a cancelled request must not drop its translation jobs.
            await EnqueueDescriptionTranslationsAsync(entryId, entry.Description);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to enqueue translations for maintenance entry {MaintenanceEntryId}. The report was saved.",
                entryId);
        }

        return entryId;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
            throw new InvalidOperationException("Tenant not resolved.");

        var entryExists = await _dbContext.MaintenanceEntries
            .AsNoTracking()
            .AnyAsync(e => e.Id == id && e.TenantId == tenantId, ct);
        if (!entryExists || _currentUserContext.RoleId != (int)UserRole.SuperAdmin)
            throw new UnauthorizedAccessException("You are not allowed to delete this maintenance entry.");

        // Business rule: maintenance reports are never deleted (hard, soft or cascading).
        throw new InvalidOperationException("Maintenance reports cannot be deleted.");
    }

    public async Task<IReadOnlyList<MaintenanceEntryDto>> GetAsync(GetMaintenanceEntriesRequest request, CancellationToken ct)
    {
        var query = from e in _dbContext.MaintenanceEntries.AsNoTracking()
                    join m in _dbContext.Machines.AsNoTracking() on e.MachineId equals m.Id
                    join mt in _dbContext.MaintenanceTypes.AsNoTracking() on e.MaintenanceTypeId equals mt.Id into mtGroup
                    from mt in mtGroup.DefaultIfEmpty()
                    select new { Entry = e, Machine = m, Type = mt };

        if (request.MachineId.HasValue)
            query = query.Where(x => x.Entry.MachineId == request.MachineId.Value);

        // MaintenanceEntry.Date is DateOnly (calendar day). Inclusive start with half-open end:
        // FromDate <= Date < ToDate+1 day covers the full selected calendar range without timezones.
        if (request.FromDate.HasValue)
            query = query.Where(x => x.Entry.Date >= request.FromDate.Value);
        if (request.ToDate.HasValue)
        {
            var exclusiveEnd = request.ToDate.Value.AddDays(1);
            query = query.Where(x => x.Entry.Date < exclusiveEnd);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            // The sentinel line is a format marker, not report content; match only the text after it.
            query = query.Where(x =>
                EF.Functions.ILike(
                    x.Entry.Description == MaintenanceDescriptionText.V1Sentinel
                        || x.Entry.Description == MaintenanceDescriptionText.V2Sentinel
                        ? string.Empty
                        : x.Entry.Description.StartsWith(V1SentinelCrLfLine)
                            || x.Entry.Description.StartsWith(V2SentinelCrLfLine)
                            ? x.Entry.Description.Substring(V1SentinelCrLfLine.Length)
                            : x.Entry.Description.StartsWith(V1SentinelLfLine)
                                || x.Entry.Description.StartsWith(V2SentinelLfLine)
                                ? x.Entry.Description.Substring(V1SentinelLfLine.Length)
                                : x.Entry.Description,
                    $"%{search}%") ||
                EF.Functions.ILike(x.Machine.Name, $"%{search}%") ||
                (x.Type != null && EF.Functions.ILike(x.Type.Code, $"%{search}%")));
        }

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

        // Newest maintenance occurrence first; CreatedAt then Id stabilize same-day ties.
        var list = await query
            .OrderByDescending(x => x.Entry.Date)
            .ThenByDescending(x => x.Entry.CreatedAt)
            .ThenByDescending(x => x.Entry.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MaintenanceEntryDto(
                x.Entry.Id,
                x.Entry.MachineId,
                x.Machine.ArrayId,
                x.Entry.Date,
                x.Entry.MaintenanceTypeId,
                x.Type != null ? x.Type.Code : null,
                x.Entry.Description,
                x.Entry.ImageUrl,
                x.Entry.SparePartsUsed,
                x.Entry.EmployeeName,
                x.Entry.WorkHours,
                x.Entry.IsSafeToOperate,
                x.Entry.CreatedByUserId,
                x.Entry.CreatedAt,
                x.Entry.AdditionalImages
                    .OrderBy(image => image.SortOrder)
                    .Select(image => new MaintenanceEntryImageDto(
                        image.Id,
                        image.ImageUrl,
                        image.SortOrder))
                    .ToList()))
            .ToListAsync(ct);

        return list;
    }

    public async Task<MaintenanceEntryDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var dto = await (from e in _dbContext.MaintenanceEntries.AsNoTracking()
                         join m in _dbContext.Machines.AsNoTracking() on e.MachineId equals m.Id
                         join mt in _dbContext.MaintenanceTypes.AsNoTracking() on e.MaintenanceTypeId equals mt.Id into mtGroup
                         from mt in mtGroup.DefaultIfEmpty()
                         where e.Id == id
                         select new MaintenanceEntryDto(
                             e.Id,
                             e.MachineId,
                             m.ArrayId,
                             e.Date,
                             e.MaintenanceTypeId,
                             mt != null ? mt.Code : null,
                             e.Description,
                             e.ImageUrl,
                             e.SparePartsUsed,
                             e.EmployeeName,
                             e.WorkHours,
                             e.IsSafeToOperate,
                             e.CreatedByUserId,
                             e.CreatedAt,
                             e.AdditionalImages
                                 .OrderBy(image => image.SortOrder)
                                 .Select(image => new MaintenanceEntryImageDto(
                                     image.Id,
                                     image.ImageUrl,
                                     image.SortOrder))
                                 .ToList()))
            .FirstOrDefaultAsync(ct);

        return dto;
    }

    public async Task<MaintenanceTranslationsResponse?> GetTranslationsAsync(Guid id, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return null;

        var entry = await GetByIdAsync(id, ct);
        if (entry is null)
            return null;

        var parts = MaintenanceDescriptionText.Extract(entry.Description);
        if (!_enqueueEnabled || _translations is null || !parts.HasTranslatableNarrative)
            return MaintenanceTranslationsResponse.WithoutRecords(parts.Narrative);

        var rows = await _translations.GetNarrativeTranslationsAsync(
            id,
            TranslatableFields.MaintenanceEntry,
            TranslatableFields.Description,
            SourceTextHasher.Compute(parts.Narrative),
            ct);

        return MaintenanceTranslationsResponse.FromRows(parts.Narrative, rows);
    }

    internal async Task EnqueueDescriptionTranslationsAsync(Guid entryId, string? description)
    {
        if (!_enqueueEnabled || _translations is null)
            return;

        var parts = MaintenanceDescriptionText.Extract(description);
        if (!parts.HasTranslatableNarrative)
            return;

        var requests = TranslationLanguages.Supported
            .Select(language => new EnqueueTranslationRequest(
                TranslatableFields.MaintenanceEntry,
                entryId,
                TranslatableFields.Description,
                parts.Narrative,
                language))
            .ToArray();

        await _translations.EnqueueAllAsync(requests, CancellationToken.None);
    }

    /// <summary>
    /// Validates type belongs to tenant and is active; enforces description rules for code <c>other</c>.
    /// </summary>
    private async Task<(string Description, Guid MaintenanceTypeId)> ResolveDescriptionForTypeAsync(
        Guid maintenanceTypeId,
        string? description,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var tenantId = _tenantContext.TenantId.Value;

        var type = await _dbContext.MaintenanceTypes.AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.Id == maintenanceTypeId && t.TenantId == tenantId && t.IsActive,
                ct);
        if (type is null)
            throw new InvalidOperationException("Maintenance type not found or inactive.");

        var isOther = string.Equals(type.Code, "other", StringComparison.OrdinalIgnoreCase);
        if (isOther)
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException(
                    "Description is required when maintenance type code is \"other\".");
            return (description.Trim(), maintenanceTypeId);
        }

        if (!string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException(
                "Description must be empty unless maintenance type code is \"other\".");

        return (string.Empty, maintenanceTypeId);
    }
}
