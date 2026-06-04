using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Reports.ForkliftReports;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Reports;

/// <summary>
/// Read-model query service for the Forklift Reports Overview screen.
/// Uses LINQ projection only (no Include). Two queries: reports, expiring inspections.
/// </summary>
public sealed class ForkliftReportsQueryService : IForkliftReportsQueryService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public ForkliftReportsQueryService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<ForkliftReportsResponse> GetAsync(ForkliftReportsQuery query, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return new ForkliftReportsResponse(Array.Empty<ForkliftReportListItemDto>(), Array.Empty<ExpiringInspectionDto>());

        var reports = await GetReportsAsync(query, ct);
        var expiringInspections = query.InspectionExpiringSoon == true
        ? await GetExpiringInspectionsAsync(query, ct)
        : Array.Empty<ExpiringInspectionDto>();

        return new ForkliftReportsResponse(reports, expiringInspections);
    }

    private async Task<IReadOnlyList<ForkliftReportListItemDto>> GetReportsAsync(
        ForkliftReportsQuery query,
        CancellationToken ct)
    {
        var baseQuery = _dbContext.ForkliftReports
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.ForkliftNumber))
            baseQuery = baseQuery.Where(r => r.Forklift != null && r.Forklift.LicenseNumber.Contains(query.ForkliftNumber));

        if (!string.IsNullOrWhiteSpace(query.Search))
            baseQuery = baseQuery.Where(r =>
                r.Treatments.Any(t => t.Description.Contains(query.Search)) ||
                r.Faults.Any(f => f.Description.Contains(query.Search)));

        if (query.FromDate.HasValue)
            baseQuery = baseQuery.Where(r => r.ReportDate >= query.FromDate.Value);

        if (query.ToDate.HasValue)
            baseQuery = baseQuery.Where(r => r.ReportDate <= query.ToDate.Value);

        if (query.Type == "treatments")
            baseQuery = baseQuery.Where(r => r.Treatments.Any());

        if (query.Type == "faults")
            baseQuery = baseQuery.Where(r => r.Faults.Any());

        var pageNumber = query.PageNumber is > 0 ? query.PageNumber.Value : 1;
        var pageSize = query.PageSize is > 0 ? query.PageSize.Value : 20;

        // Step 1 — Get latest report per forklift (correlated subquery; no anonymous-type join)
        var latestReports = await baseQuery
            .GroupBy(r => new { r.ForkliftId, r.Forklift!.LicenseNumber })
            .Select(g => new
            {
                ForkliftId = g.Key.ForkliftId,
                LicenseNumber = g.Key.LicenseNumber,
                LastReportDate = g.Max(r => r.ReportDate),
                ReportId = g
                    .OrderByDescending(r => r.ReportDate)
                    .Select(r => r.Id)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        if (latestReports.Count == 0)
            return Array.Empty<ForkliftReportListItemDto>();

        var reportIds = latestReports.Select(x => x.ReportId).ToList();

        // Step 2 — Load treatment counts for those report IDs only
        var treatmentCounts = await _dbContext.ForkliftTreatments
            .AsNoTracking()
            .Where(t => reportIds.Contains(t.ReportId))
            .GroupBy(t => t.ReportId)
            .Select(g => new { ReportId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ReportId, x => x.Count, ct);

        // Step 3 — Load fault counts for those report IDs only
        var faultCounts = await _dbContext.ForkliftFaults
            .AsNoTracking()
            .Where(f => reportIds.Contains(f.ReportId))
            .GroupBy(f => f.ReportId)
            .Select(g => new { ReportId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ReportId, x => x.Count, ct);

        // Step 4 — Build DTOs in memory, then sort and paginate
        var result = latestReports
            .Select(r => new ForkliftReportListItemDto(
                r.ReportId,
                r.ForkliftId,
                r.LicenseNumber,
                r.LastReportDate,
                treatmentCounts.GetValueOrDefault(r.ReportId),
                faultCounts.GetValueOrDefault(r.ReportId)
            ))
            .OrderBy(x => x.ForkliftNumber)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return result;
    }

    private async Task<IReadOnlyList<ExpiringInspectionDto>> GetExpiringInspectionsAsync(
        ForkliftReportsQuery query,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var expiryDays = query.ExpiryDays ?? 30;
        var thresholdUtc = DateTime.UtcNow.Date.AddDays(expiryDays);
        var list = await _dbContext.Forklifts
            .AsNoTracking()
            .Where(f =>
                f.InspectionExpiryDate != null &&
                f.InspectionExpiryDate.Value <= thresholdUtc)
            .Select(f => new ExpiringInspectionDto(
                f.Id,
                f.LicenseNumber,
                DateOnly.FromDateTime(f.InspectionExpiryDate!.Value),
                DateOnly.FromDateTime(f.InspectionExpiryDate!.Value).DayNumber - today.DayNumber
            ))
            .ToListAsync(ct);

        return list;
    }
}
