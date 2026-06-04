using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Forklifts;

public interface IForkliftReportService
{
    Task<Guid> CreateAsync(CreateForkliftReportRequest request, CancellationToken ct);

    Task<IReadOnlyList<ForkliftReportDto>> GetAsync(int pageNumber, int pageSize, CancellationToken ct);

    Task<ForkliftReportDto?> GetByIdAsync(Guid id, CancellationToken ct);
}
