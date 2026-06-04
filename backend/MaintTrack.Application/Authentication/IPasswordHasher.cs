namespace MaintTrack.Application.Authentication;

/// <summary>
/// Service for password hashing and verification.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Verifies a plain-text password against a stored hash.
    /// </summary>
    /// <param name="password">The plain-text password to verify.</param>
    /// <param name="passwordHash">The stored password hash.</param>
    /// <returns>True if the password matches the hash; otherwise, false.</returns>
    bool Verify(string password, string passwordHash);
}
