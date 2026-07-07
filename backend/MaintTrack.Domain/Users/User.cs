using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Users;

/// <summary>
/// Application user belonging to a tenant.
/// </summary>
public class User : TenantEntity
{
    public int RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public string Email { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Per-user feature flag: when true, this user is routed to Morning Round V2
    /// instead of the legacy Morning Round screen. Defaults to false so new
    /// features are opt-in per user before a wider rollout.
    /// </summary>
    public bool EnableNewMorningRound { get; set; }

    /// <summary>
    /// Per-user feature flag: when true, this user is routed to the new
    /// hierarchical Maintenance Log instead of the legacy Maintenance Log
    /// screen. Defaults to false so new features are opt-in per user before
    /// a wider rollout.
    /// </summary>
    public bool EnableNewMaintenanceLog { get; set; }
}

