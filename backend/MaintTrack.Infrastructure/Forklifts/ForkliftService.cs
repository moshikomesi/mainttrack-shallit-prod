using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Forklifts;
using MaintTrack.Domain.Forklifts;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Forklifts;

/// <summary>
/// Infrastructure implementation of <see cref="IForkliftService" /> backed by EF Core.
/// </summary>
public sealed class ForkliftService : IForkliftService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public ForkliftService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<Guid> CreateAsync(CreateForkliftRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var now = DateTime.UtcNow;
        var forklift = new Forklift
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId.Value,
            LicenseNumber = request.LicenseNumber,
            Description = request.Description,
            CreatedAt = now
        };

        await _dbContext.Forklifts.AddAsync(forklift, ct);
        await _dbContext.SaveChangesAsync(ct);
        return forklift.Id;
    }

    public async Task<IReadOnlyList<ForkliftDto>> GetAsync(CancellationToken ct)
    {
        var list = await _dbContext.Forklifts
            .AsNoTracking()
            .OrderBy(f => f.LicenseNumber)
            .Select(f => new ForkliftDto(
                f.Id,
                f.TenantId,
                f.LicenseNumber,
                f.Description,
                f.CreatedAt,
                f.UpdatedAt,
                f.LastInspectionDate,
                f.InspectionExpiryDate))
            .ToListAsync(ct);

        return list;
    }

    public async Task<ForkliftDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var dto = await _dbContext.Forklifts
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new ForkliftDto(
                f.Id,
                f.TenantId,
                f.LicenseNumber,
                f.Description,
                f.CreatedAt,
                f.UpdatedAt,
                f.LastInspectionDate,
                f.InspectionExpiryDate))
            .FirstOrDefaultAsync(ct);

        return dto;
    }

    public async Task UpdateAsync(Guid id, UpdateForkliftRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var forklift = await _dbContext.Forklifts.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (forklift is null)
            throw new InvalidOperationException("Forklift not found.");

        forklift.LicenseNumber = request.LicenseNumber;
        forklift.Description = request.Description;
        forklift.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
    }
}
