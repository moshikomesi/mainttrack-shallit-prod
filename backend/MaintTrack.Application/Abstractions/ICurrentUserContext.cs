using MaintTrack.Domain.Users;

namespace MaintTrack.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid UserId { get; }

    Guid TenantId { get; }

    int RoleId { get; }

    UserRole Role { get; }

    /// <summary>User-facing name from JWT (display name, or username when display name is unset).</summary>
    string DisplayName { get; }
}
