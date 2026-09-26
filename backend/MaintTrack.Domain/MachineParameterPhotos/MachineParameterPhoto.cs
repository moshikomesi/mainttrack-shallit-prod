using MaintTrack.Domain.Common;
using MaintTrack.Domain.Machines;

namespace MaintTrack.Domain.MachineParameterPhotos;

/// <summary>
/// A photo documenting machine parameters/settings.
/// Maps to the <c>machine_parameter_photos</c> table.
/// </summary>
public sealed class MachineParameterPhoto : TenantEntity
{
    public Guid MachineId { get; set; }

    public Machine? Machine { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public Guid CreatedByUserId { get; set; }
}
