
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Maintenance;
using MaintTrack.Domain.Audit;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Maintenance;

public sealed class MaintenanceEntryService : IMaintenanceEntryService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IFileStorageService _fileStorageService;

    public MaintenanceEntryService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IFileStorageService fileStorageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
    }

    public async Task<Guid> CreateAsync(CreateMaintenanceEntryRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

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

        await _dbContext.SaveChangesAsync(ct);
        return entryId;
    }

    public async Task UpdateAsync(Guid id, UpdateMaintenanceEntryRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var entry = await _dbContext.MaintenanceEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            throw new InvalidOperationException("Maintenance entry not found.");

        if (entry.CreatedByUserId != _currentUserContext.UserId &&
            _currentUserContext.RoleId != (int)UserRole.SuperAdmin)
            throw new UnauthorizedAccessException("You are not allowed to update this maintenance entry.");

        if (request.MaintenanceTypeId == Guid.Empty)
            throw new InvalidOperationException("Maintenance type is required.");

        var (description, maintenanceTypeId) = await ResolveDescriptionForTypeAsync(
            request.MaintenanceTypeId,
            request.Description,
            ct);

        entry.MaintenanceTypeId = maintenanceTypeId;
        entry.Description = description;
        entry.ImageUrl = request.ImageUrl;
        entry.SparePartsUsed = request.SparePartsUsed;
        entry.EmployeeName = request.EmployeeName;
        entry.WorkHours = request.WorkHours;
        entry.IsSafeToOperate = request.IsSafeToOperate;
        entry.UpdatedAt = DateTime.UtcNow;

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = entry.TenantId,
            UserId = _currentUserContext.UserId,
            Action = "maintenance_updated",
            EntityName = "MaintenanceEntry",
            EntityId = entry.Id,
            CreatedAt = DateTime.UtcNow
        }, ct);

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var entry = await _dbContext.MaintenanceEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null || _currentUserContext.RoleId != (int)UserRole.SuperAdmin)
            throw new UnauthorizedAccessException("You are not allowed to delete this maintenance entry.");

        if (!string.IsNullOrWhiteSpace(entry.ImageUrl))
            await _fileStorageService.DeleteAsync(entry.ImageUrl, ct);

        _dbContext.MaintenanceEntries.Remove(entry);
        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = entry.TenantId,
            UserId = _currentUserContext.UserId,
            Action = "maintenance_deleted",
            EntityName = "MaintenanceEntry",
            EntityId = entry.Id,
            CreatedAt = DateTime.UtcNow
        }, ct);
        await _dbContext.SaveChangesAsync(ct);
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
        if (request.FromDate.HasValue)
            query = query.Where(x => x.Entry.Date >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.Entry.Date <= request.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.Entry.Description, $"%{search}%") ||
                EF.Functions.ILike(x.Machine.Name, $"%{search}%") ||
                (x.Type != null && EF.Functions.ILike(x.Type.Code, $"%{search}%")));
        }

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

        var list = await query
            .OrderByDescending(x => x.Entry.Date)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MaintenanceEntryDto(
                x.Entry.Id,
                x.Entry.MachineId,
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
                x.Entry.CreatedAt))
            .ToListAsync(ct);

        return list;
    }

    public async Task<MaintenanceEntryDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var dto = await (from e in _dbContext.MaintenanceEntries.AsNoTracking()
                         join mt in _dbContext.MaintenanceTypes.AsNoTracking() on e.MaintenanceTypeId equals mt.Id into mtGroup
                         from mt in mtGroup.DefaultIfEmpty()
                         where e.Id == id
                         select new MaintenanceEntryDto(
                             e.Id,
                             e.MachineId,
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
                             e.CreatedAt))
            .FirstOrDefaultAsync(ct);

        return dto;
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

    public async Task MigrateBase64ImagesAsync(CancellationToken ct)
    {
        var entries = await _dbContext.MaintenanceEntries
            .Where(x => x.ImageUrl != null && x.ImageUrl.StartsWith("data:image"))
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            try
            {
                var base64 = entry.ImageUrl;

                var commaIndex = base64.IndexOf(',');
                var data = base64.Substring(commaIndex + 1);

                var bytes = Convert.FromBase64String(data);

                using var stream = new MemoryStream(bytes);

                var contentType = base64.Contains("png") ? "image/png" : "image/jpeg";

                var fileName = $"{entry.Id}.jpg";

                var url = await _fileStorageService.UploadAsync(
                    stream,
                    fileName,
                    contentType,
                    entry.TenantId.ToString(),
                    ct);

                entry.ImageUrl = url;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed for entry {entry.Id}: {ex.Message}");
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

}
