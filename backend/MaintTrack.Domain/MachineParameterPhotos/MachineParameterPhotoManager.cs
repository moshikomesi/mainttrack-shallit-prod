using MaintTrack.Domain.Common;
using MaintTrack.Domain.Users;

namespace MaintTrack.Domain.MachineParameterPhotos;

/// <summary>
/// Feature-specific assignment: a tenant user who may add/edit/delete
/// machine parameter photos. Maps to <c>machine_parameter_photo_managers</c>.
/// </summary>
public sealed class MachineParameterPhotoManager : TenantEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }
}
