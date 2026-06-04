namespace MaintTrack.Application.Forklifts;

public sealed class UpdateForkliftRequest
{
    public string LicenseNumber { get; init; } = string.Empty;

    public string? Description { get; init; }
}
