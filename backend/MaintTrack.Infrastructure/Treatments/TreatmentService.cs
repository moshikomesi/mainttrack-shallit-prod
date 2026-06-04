using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Treatments;
using MaintTrack.Domain.Audit;
using MaintTrack.Domain.Treatments;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Treatments;

public sealed class TreatmentService : ITreatmentService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public TreatmentService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
    }

    public async Task<Guid> CreateAsync(CreateTreatmentRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        if (request.Cost < 0)
            throw new InvalidOperationException("Cost must be greater than or equal to zero.");

        var tenantId = _tenantContext.TenantId.Value;
        var userId = _currentUserContext.UserId;
        var now = DateTime.UtcNow;

        var id = Guid.NewGuid();
        var treatment = new Treatment
        {
            Id = id,
            TenantId = tenantId,
            EquipmentType = request.EquipmentType,
            TreatmentDate = request.TreatmentDate,
            TreatmentType = request.TreatmentType,
            Description = request.Description,
            Technician = request.Technician,
            Cost = request.Cost,
            NextDueDate = request.NextDueDate,
            CreatedAt = now
        };

        await _dbContext.Treatments.AddAsync(treatment, ct);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Action = "treatment_created",
            EntityName = "Treatment",
            EntityId = id,
            CreatedAt = now
        }, ct);

        await _dbContext.SaveChangesAsync(ct);
        return id;
    }

    public async Task<IEnumerable<TreatmentDto>> GetAsync(
        EquipmentType? equipmentType,
        DateOnly? fromDate,
        DateOnly? toDate,
        int pageNumber,
        int pageSize,
        CancellationToken ct)
    {
        var p = pageNumber > 0 ? pageNumber : 1;
        var s = pageSize > 0 ? pageSize : 20;

        var query = _dbContext.Treatments.AsNoTracking();

        if (equipmentType.HasValue)
            query = query.Where(t => t.EquipmentType == equipmentType.Value);
        if (fromDate.HasValue)
            query = query.Where(t => t.TreatmentDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(t => t.TreatmentDate <= toDate.Value);

        var list = await query
            .OrderByDescending(t => t.TreatmentDate)
            .Skip((p - 1) * s)
            .Take(s)
            .Select(t => new TreatmentDto(
                t.Id,
                t.EquipmentType,
                t.TreatmentDate,
                t.TreatmentType,
                t.Description,
                t.Technician,
                t.Cost,
                t.NextDueDate))
            .ToListAsync(ct);

        return list;
    }

    public async Task<TreatmentDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var dto = await _dbContext.Treatments
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TreatmentDto(
                t.Id,
                t.EquipmentType,
                t.TreatmentDate,
                t.TreatmentType,
                t.Description,
                t.Technician,
                t.Cost,
                t.NextDueDate))
            .FirstOrDefaultAsync(ct);

        return dto;
    }
}
