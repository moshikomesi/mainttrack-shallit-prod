namespace MaintTrack.Api.Auth;

/// <summary>
/// Request payload for the login endpoint.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// Username or email address (case-insensitive).
    /// </summary>
    public string UsernameOrEmail { get; set; } = string.Empty;

    /// <summary>
    /// Backward compatibility: accepts "username" property as well.
    /// </summary>
    public string? Username
    {
        get => null;
        set
        {
            if (value != null && string.IsNullOrWhiteSpace(UsernameOrEmail))
            {
                UsernameOrEmail = value;
            }
        }
    }

    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Response payload for the login endpoint (JWT is set via HttpOnly cookie).
/// </summary>
public sealed class LoginResponse
{
    public DateTime ExpiresAtUtc { get; init; }

    public UserInfo User { get; init; } = null!;
}

/// <summary>
/// Current authenticated user (GET /api/v1/auth/me).
/// </summary>
public sealed class CurrentUserResponse
{
    public Guid UserId { get; init; }

    public Guid TenantId { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public int RoleId { get; init; }

    /// <summary>
    /// Per-user feature flag. When true, the frontend routes this user to
    /// Morning Round V2 instead of the legacy Morning Round screen.
    /// </summary>
    public bool EnableNewMorningRound { get; init; }

    /// <summary>
    /// Per-user feature flag. When true, the frontend routes this user to
    /// the new hierarchical Maintenance Log instead of the legacy screen.
    /// </summary>
    public bool EnableNewMaintenanceLog { get; init; }
}

/// <summary>
/// User information included in login response.
/// </summary>
public sealed class UserInfo
{
    public Guid UserId { get; init; }

    public Guid TenantId { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public int RoleId { get; init; }

    /// <summary>
    /// Per-user feature flag. When true, the frontend routes this user to
    /// Morning Round V2 instead of the legacy Morning Round screen.
    /// </summary>
    public bool EnableNewMorningRound { get; init; }

    /// <summary>
    /// Per-user feature flag. When true, the frontend routes this user to
    /// the new hierarchical Maintenance Log instead of the legacy screen.
    /// </summary>
    public bool EnableNewMaintenanceLog { get; init; }
}

