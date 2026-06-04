using System.Security.Claims;

namespace MaintTrack.Application.Authentication;

/// <summary>
/// Service for generating JWT tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a JWT token with the specified claims.
    /// </summary>
    /// <param name="claims">The claims to include in the token.</param>
    /// <param name="expiresAtUtc">The UTC expiration time for the token.</param>
    /// <returns>The generated JWT token string.</returns>
    string GenerateToken(IEnumerable<Claim> claims, DateTime expiresAtUtc);
}
