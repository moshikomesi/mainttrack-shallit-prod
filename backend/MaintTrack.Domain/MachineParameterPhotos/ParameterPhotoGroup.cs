using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MachineParameterPhotos;

/// <summary>
/// A group in the Parameter Photos catalog. Independent of shared arrays.
/// Maps to <c>parameter_photo_groups</c>.
/// </summary>
public sealed class ParameterPhotoGroup : TenantEntity
{
    public string NameKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ParameterPhotoParameter> Parameters { get; set; } = new List<ParameterPhotoParameter>();
}
