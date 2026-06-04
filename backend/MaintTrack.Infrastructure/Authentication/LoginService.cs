using MaintTrack.Application.Authentication;
using MaintTrack.Domain.Users;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Authentication;

/// <summary>
/// Implementation of login/authentication service.
/// </summary>
public sealed class LoginService : ILoginService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public LoginService(MaintTrackDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<User?> AuthenticateAsync(string usernameOrEmail, string password)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        // Find user by username or email (case-insensitive), only if active.
        // Note: We temporarily disable the tenant filter for login since we don't have tenant context yet.
        var user = await _dbContext.Users
            .IgnoreQueryFilters() // Temporarily disable tenant filter for login
            .Include(u => u.Role)
            .AsNoTracking()
            .Where(u => u.IsActive &&
                       (EF.Functions.ILike(u.Username, usernameOrEmail) ||
                        EF.Functions.ILike(u.Email, usernameOrEmail)))
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return null;
        }

        // Verify password
        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return user;
    }
}
