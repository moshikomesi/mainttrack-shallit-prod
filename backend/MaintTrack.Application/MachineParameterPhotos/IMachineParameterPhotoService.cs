using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.MachineParameterPhotos;

public interface IMachineParameterPhotoService
{
    Task<MachineParameterPhotoHierarchyDto> GetHierarchyAsync(CancellationToken ct);

    Task<IReadOnlyList<MachineParameterPhotoDto>> GetByMachineIdAsync(Guid machineId, CancellationToken ct);

    Task<IReadOnlyList<MachineParameterPhotoDto>> UploadAsync(
        Guid machineId,
        IReadOnlyList<MachineParameterPhotoUploadFile> files,
        CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
