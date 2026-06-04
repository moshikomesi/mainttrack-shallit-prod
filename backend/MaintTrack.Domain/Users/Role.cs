namespace MaintTrack.Domain.Users;

/// <summary>
/// Role reference entity used for RBAC.
/// </summary>
public sealed class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

