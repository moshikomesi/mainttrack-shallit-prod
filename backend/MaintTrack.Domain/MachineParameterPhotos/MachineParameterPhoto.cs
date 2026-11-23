using MaintTrack.Domain.Common;
using MaintTrack.Domain.Machines;

namespace MaintTrack.Domain.MachineParameterPhotos;

/// <summary>
/// A photo documenting parameter settings.
/// Maps to the <c>machine_parameter_photos</c> table.
/// <see cref="ParameterId"/> is the catalog association. <see cref="MachineId"/>
/// is retained for photos that were stored against a shared machine.
/// </summary>
public sealed class MachineParameterPhoto : TenantEntity
{
    public Guid? MachineId { get; set; }

    public Machine? Machine { get; set; }

    public Guid? ParameterId { get; set; }

    public ParameterPhotoParameter? Parameter { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public Guid CreatedByUserId { get; set; }
}
