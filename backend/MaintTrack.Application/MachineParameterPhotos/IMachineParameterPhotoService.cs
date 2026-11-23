using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.MachineParameterPhotos;

public interface IMachineParameterPhotoService
{
    Task<ParameterPhotoCatalogDto> GetCatalogAsync(CancellationToken ct);

    Task<IReadOnlyList<MachineParameterPhotoDto>> GetByParameterIdAsync(Guid parameterId, CancellationToken ct);

    Task<IReadOnlyList<LegacyParameterPhotoDto>> GetLegacyPhotosAsync(CancellationToken ct);

    Task<MachineParameterPhotoDto> AssignLegacyPhotoAsync(Guid photoId, Guid parameterId, CancellationToken ct);

    Task DeleteLegacyPhotoAsync(Guid photoId, CancellationToken ct);

    Task<IReadOnlyList<MachineParameterPhotoDto>> UploadAsync(
        Guid parameterId,
        IReadOnlyList<MachineParameterPhotoUploadFile> files,
        CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
