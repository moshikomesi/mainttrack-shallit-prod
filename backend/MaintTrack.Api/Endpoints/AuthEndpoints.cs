using System.Security.Claims;
using MaintTrack.Api.Auth;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Authentication;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/login", async (
                LoginRequest request,
                HttpContext httpContext,
                ILoginService loginService,
                IJwtTokenService jwtTokenService) =>
            {
                var usernameOrEmail = !string.IsNullOrWhiteSpace(request.UsernameOrEmail)
                    ? request.UsernameOrEmail
                    : request.Username ?? string.Empty;

                if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.Problem(
                        title: "Invalid credentials",
                        detail: "Username/email and password are required.",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var user = await loginService.AuthenticateAsync(usernameOrEmail, request.Password);

                if (user == null)
                {
                    return Results.Problem(
                        title: "Invalid credentials",
                        detail: "The provided credentials are incorrect.",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var expiresAtUtc = DateTime.UtcNow.AddHours(8);
                var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.Username
                    : user.DisplayName.Trim();

                var claims = new[]
                {
                    new Claim("user_id", user.Id.ToString()),
                    new Claim("tenant_id", user.TenantId.ToString()),
                    new Claim("role_id", user.RoleId.ToString()),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim("display_name", displayName)
                };

                var token = jwtTokenService.GenerateToken(claims, expiresAtUtc);

                httpContext.Response.Cookies.Append(
                    AuthCookie.Name,
                    token,
                    AuthCookie.CreateOptions(httpContext.Request.IsHttps, expiresAtUtc));

                var response = new LoginResponse
                {
                    ExpiresAtUtc = expiresAtUtc,
                    User = new UserInfo
                    {
                        UserId = user.Id,
                        TenantId = user.TenantId,
                        Username = user.Username,
                        Email = user.Email,
                        DisplayName = user.DisplayName,
                        Role = user.Role.Name,
                        RoleId = user.RoleId
                    }
                };

                return Results.Ok(response);
            })
            .RequireRateLimiting("login")
            .WithName("Login")
            .WithOpenApi();

        app.MapPost("/api/v1/auth/logout", (HttpContext httpContext) =>
            {
                AuthCookie.Clear(httpContext.Response, httpContext.Request.IsHttps);
                return Results.NoContent();
            })
            .WithName("Logout")
            .WithOpenApi();

        app.MapGet("/api/v1/auth/me", (ICurrentUserContext currentUser, ClaimsPrincipal principal) =>
            {
                if (principal.Identity?.IsAuthenticated != true)
                {
                    return Results.Unauthorized();
                }

                var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
                var username = principal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

                return Results.Ok(new CurrentUserResponse
                {
                    UserId = currentUser.UserId,
                    TenantId = currentUser.TenantId,
                    Username = username,
                    Email = email,
                    DisplayName = currentUser.DisplayName,
                    Role = currentUser.Role.ToString(),
                    RoleId = currentUser.RoleId
                });
            })
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithOpenApi();

        app.MapGet("/api/v1/auth/secure-test", () =>
            {
                return Results.Ok(new { message = "You are authenticated!" });
            })
            .RequireRateLimiting("login")
            .RequireAuthorization()
            .WithName("SecureTest")
            .WithOpenApi();

        return app;
    }
}
