using Microsoft.AspNetCore.Http;

namespace MaintTrack.Api.Auth;

/// <summary>
/// HttpOnly JWT auth cookie settings.
/// </summary>
public static class AuthCookie
{
    public const string Name = "mainttrack_access_token";

    public static CookieOptions CreateOptions(bool isHttps, DateTime expiresAtUtc)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAtUtc,
            IsEssential = true,
        };
    }

    public static void Clear(HttpResponse response, bool isHttps)
    {
        response.Cookies.Delete(Name, CreateOptions(isHttps, DateTime.UnixEpoch));
    }
}
