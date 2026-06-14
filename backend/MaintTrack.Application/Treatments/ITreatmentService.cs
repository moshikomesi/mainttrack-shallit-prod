using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Treatments;

public interface ITreatmentService
{
    Task<Guid> CreateAsync(CreateTreatmentRequest request, CancellationToken ct);

    Task<IEnumerable<TreatmentDto>> GetAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int pageNumber,
        int pageSize,
        CancellationToken ct);

    Task<TreatmentDto?> GetByIdAsync(Guid id, CancellationToken ct);
}
