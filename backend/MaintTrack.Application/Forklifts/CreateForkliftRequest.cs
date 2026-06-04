namespace MaintTrack.Application.Forklifts;

public sealed class CreateForkliftRequest
{
    public string LicenseNumber { get; init; } = string.Empty;

    public string? Description { get; init; }
}
