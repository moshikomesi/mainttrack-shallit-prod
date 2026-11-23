using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MachineParameterPhotos;

/// <summary>
/// A parameter in the Parameter Photos catalog. Photos attach here,
/// not to shared maintenance machines. Maps to <c>parameter_photo_parameters</c>.
/// </summary>
public sealed class ParameterPhotoParameter : TenantEntity
{
    public Guid GroupId { get; set; }

    public ParameterPhotoGroup? Group { get; set; }

    public string NameKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
