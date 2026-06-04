using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Forklifts;
using MaintTrack.Domain.Audit;
using MaintTrack.Domain.Forklifts;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Forklifts;

/// <summary>
/// Infrastructure implementation of <see cref="IForkliftReportService" /> backed by EF Core.
/// </summary>
public sealed class ForkliftReportService : IForkliftReportService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public ForkliftReportService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
    }

    public async Task<Guid> CreateAsync(CreateForkliftReportRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        foreach (var fault in request.Faults)
        {
            if (fault.RepairCost < 0)
                throw new InvalidOperationException("RepairCost must be greater than or equal to zero.");
        }

        if (request.Inspections != null && request.Inspections.Any())
        {
            var inspection = request.Inspections.First();
            if (inspection.ExpiryDate <= inspection.TestDate)
                throw new InvalidOperationException("Inspection expiry date must be after test date.");

            var forklift = await _dbContext.Forklifts.FirstOrDefaultAsync(f => f.Id == request.ForkliftId, ct);
            if (forklift is null)
                throw new InvalidOperationException("Forklift not found.");

            var testDateUtc = DateTime.SpecifyKind(inspection.TestDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var expiryDateUtc = DateTime.SpecifyKind(inspection.ExpiryDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            forklift.LastInspectionDate = testDateUtc;
            forklift.InspectionExpiryDate = expiryDateUtc;
            forklift.InspectionUpdatedAt = DateTime.UtcNow;
        }

        var tenantId = _tenantContext.TenantId.Value;
        var userId = _currentUserContext.UserId;
        var now = DateTime.UtcNow;
        var reportId = Guid.NewGuid();

        var report = new ForkliftReport
        {
            Id = reportId,
            TenantId = tenantId,
            ForkliftId = request.ForkliftId,
            ReportDate = request.ReportDate,
            CreatedByUserId = userId,
            CreatedAt = now
        };

        foreach (var item in request.Treatments)
        {
            report.Treatments.Add(new ForkliftTreatment
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                Date = item.Date,
                Description = item.Description,
                Technician = item.Technician
            });
        }

        foreach (var item in request.Faults)
        {
            report.Faults.Add(new ForkliftFault
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                FaultType = item.FaultType,
                Description = item.Description,
                RepairCost = item.RepairCost
            });
        }

        // Inspections are not stored as report rows; they update the forklift's current certification state only.

        await _dbContext.ForkliftReports.AddAsync(report, ct);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Action = "forklift_report_created",
            EntityName = "ForkliftReport",
            EntityId = reportId,
            CreatedAt = now
        }, ct);

        await _dbContext.SaveChangesAsync(ct);
        return reportId;
    }

    public async Task<IReadOnlyList<ForkliftReportDto>> GetAsync(int pageNumber, int pageSize, CancellationToken ct)
    {
        var p = pageNumber > 0 ? pageNumber : 1;
        var s = pageSize > 0 ? pageSize : 20;

        var list = await _dbContext.ForkliftReports
            .AsNoTracking()
            .OrderByDescending(r => r.ReportDate)
            .Skip((p - 1) * s)
            .Take(s)
            .Select(r => new ForkliftReportDto(
                r.Id,
                r.TenantId,
                r.ForkliftId,
                r.ReportDate,
                r.CreatedByUserId,
                r.CreatedAt,
                r.Treatments
                    .Select(t => new ForkliftTreatmentDto(t.Id, t.ReportId, t.Date, t.Description, t.Technician))
                    .ToList(),
                r.Faults
                    .Select(f => new ForkliftFaultDto(f.Id, f.ReportId, f.FaultType, f.Description, f.RepairCost))
                    .ToList(),
                r.Inspections
                    .Select(i => new ForkliftInspectionDto(i.Id, i.ReportId, i.TestDate, i.ExpiryDate))
                    .ToList()))
            .ToListAsync(ct);

        return list;
    }

    public async Task<ForkliftReportDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var dto = await _dbContext.ForkliftReports
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new ForkliftReportDto(
                r.Id,
                r.TenantId,
                r.ForkliftId,
                r.ReportDate,
                r.CreatedByUserId,
                r.CreatedAt,
                r.Treatments
                    .Select(t => new ForkliftTreatmentDto(t.Id, t.ReportId, t.Date, t.Description, t.Technician))
                    .ToList(),
                r.Faults
                    .Select(f => new ForkliftFaultDto(f.Id, f.ReportId, f.FaultType, f.Description, f.RepairCost))
                    .ToList(),
                r.Inspections
                    .Select(i => new ForkliftInspectionDto(i.Id, i.ReportId, i.TestDate, i.ExpiryDate))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return dto;
    }
}
