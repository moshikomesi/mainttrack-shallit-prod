using MaintTrack.Domain.Users;

namespace MaintTrack.Application.Authentication;

/// <summary>
/// Service for handling user authentication and login.
/// </summary>
public interface ILoginService
{
    /// <summary>
    /// Authenticates a user by username/email and password.
    /// </summary>
    /// <param name="usernameOrEmail">The username or email address.</param>
    /// <param name="password">The plain-text password.</param>
    /// <returns>The authenticated user if credentials are valid; otherwise, null.</returns>
    Task<User?> AuthenticateAsync(string usernameOrEmail, string password);
}
