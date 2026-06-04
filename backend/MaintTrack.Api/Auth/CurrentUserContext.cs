using System.Security.Claims;
using MaintTrack.Application.Abstractions;
using MaintTrack.Domain.Users;

namespace MaintTrack.Api.Auth;

public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpContext HttpContext =>
        _httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("No HttpContext available.");

    public Guid UserId => GetGuidClaim("user_id");

    public Guid TenantId => GetGuidClaim("tenant_id");

    public int RoleId => GetRoleId();

    public UserRole Role => GetRole();

    public string DisplayName => GetDisplayName();

    private Guid GetGuidClaim(string claimType)
    {
        var claim = HttpContext.User?.FindFirst(claimType);

        if (claim is null || !Guid.TryParse(claim.Value, out var value))
        {
            throw new InvalidOperationException($"Required claim {claimType} is missing or invalid.");
        }

        return value;
    }

    private UserRole GetRole()
    {
        // Prefer role_id for internal backend checks.
        // Keep enum projection for backward compatibility with existing consumers.
        if (!Enum.IsDefined(typeof(UserRole), RoleId))
        {
            throw new InvalidOperationException("Required claim 'role_id' is missing or invalid.");
        }

        return (UserRole)RoleId;
    }

    private int GetRoleId()
    {
        var roleIdClaim = HttpContext.User?.FindFirst("role_id");
        if (roleIdClaim is null || !int.TryParse(roleIdClaim.Value, out var roleId))
            throw new InvalidOperationException("Required claim 'role_id' is missing or invalid.");

        if (!Enum.IsDefined(typeof(UserRole), roleId))
            throw new InvalidOperationException("Required claim 'role_id' is missing or invalid.");

        return roleId;
    }

    private string GetDisplayName()
    {
        var displayName = HttpContext.User?.FindFirst("display_name")?.Value?.Trim();
        if (!string.IsNullOrEmpty(displayName))
            return displayName;

        var name = HttpContext.User?.FindFirst(ClaimTypes.Name)?.Value?.Trim();
        if (!string.IsNullOrEmpty(name))
            return name;

        throw new InvalidOperationException("Required claim 'display_name' or name is missing or invalid.");
    }

}