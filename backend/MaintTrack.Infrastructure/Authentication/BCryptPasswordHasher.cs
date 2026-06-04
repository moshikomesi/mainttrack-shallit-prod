using MaintTrack.Application.Authentication;

namespace MaintTrack.Infrastructure.Authentication;

/// <summary>
/// BCrypt implementation of password hashing and verification.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch
        {
            // Invalid hash format or other BCrypt errors
            return false;
        }
    }
}
