using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MachineParameterPhotos;

/// <summary>
/// Hides one catalog parameter from Parameter Photos.
/// A row means hidden. This is the feature's own visibility record and does not
/// change shared machine, maintenance, or morning-round visibility.
/// </summary>
public sealed class ParameterPhotoParameterExclusion : TenantEntity
{
    public Guid ParameterId { get; set; }

    public ParameterPhotoParameter? Parameter { get; set; }
}
