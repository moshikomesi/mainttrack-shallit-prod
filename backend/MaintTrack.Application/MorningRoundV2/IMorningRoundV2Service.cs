using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.MorningRoundV2;

public interface IMorningRoundV2Service
{
    Task<IReadOnlyList<MorningRoundV2ArrayDto>> GetChecklistAsync(CancellationToken ct);

    Task<SubmitMorningRoundV2Response> SubmitAsync(SubmitMorningRoundV2Request request, CancellationToken ct);

    Task<MorningRoundV2ReportDto?> GetReportByIdAsync(Guid reportId, CancellationToken ct);

    Task<MorningRoundV2ReportDto?> GetReportByDateAsync(DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<MorningRoundV2ReportSummaryDto>> ListReportsAsync(CancellationToken ct);
}
