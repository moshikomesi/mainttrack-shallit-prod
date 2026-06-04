namespace MaintTrack.Application.Machines;

public sealed class CreateMachineRequest
{
    public string Name { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string? Description { get; init; }
}

