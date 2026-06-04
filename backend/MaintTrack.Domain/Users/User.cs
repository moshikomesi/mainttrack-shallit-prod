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
}

