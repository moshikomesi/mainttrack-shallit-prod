using MaintTrack.Application.Abstractions;

namespace MaintTrack.Api.Middleware;

/// <summary>
/// Extracts the tenant_id from the authenticated user's JWT claims
/// and stores it in the scoped <see cref="ITenantContext" />.
/// </summary>
public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var user = context.User;

        if (user?.Identity is { IsAuthenticated: true })
        {
            var tenantClaim = user.FindFirst("tenant_id");

            if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
            {
                tenantContext.TenantId = tenantId;
            }
            else if (tenantClaim != null)
            {
                _logger.LogWarning("Invalid tenant_id claim value: {Value}", tenantClaim.Value);
            }
        }

        await _next(context);
    }
}

