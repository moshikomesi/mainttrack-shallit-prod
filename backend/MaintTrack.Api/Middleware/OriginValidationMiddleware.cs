namespace MaintTrack.Api.Middleware;

/// <summary>
/// Rejects cross-site mutating requests that do not originate from an allowed frontend origin.
/// Complements SameSite cookies and CORS for CSRF defense in depth.
/// </summary>
public sealed class OriginValidationMiddleware
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    };

    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowedOrigins;
    private readonly bool _allowMissingOriginInDevelopment;
    private readonly ILogger<OriginValidationMiddleware> _logger;

    public OriginValidationMiddleware(
        RequestDelegate next,
        IReadOnlyList<string> allowedOrigins,
        IHostEnvironment environment,
        ILogger<OriginValidationMiddleware> logger)
    {
        _next = next;
        _allowedOrigins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
        _allowMissingOriginInDevelopment = environment.IsDevelopment();
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!MutatingMethods.Contains(context.Request.Method) || IsExemptPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!TryGetAllowedOrigin(context, out var origin))
        {
            if (_allowMissingOriginInDevelopment)
            {
                await _next(context);
                return;
            }

            _logger.LogWarning("Blocked mutating request without Origin/Referer: {Method} {Path}",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!_allowedOrigins.Contains(origin))
        {
            _logger.LogWarning("Blocked request from disallowed origin {Origin}: {Method} {Path}",
                origin, context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    private static bool IsExemptPath(PathString path)
    {
        if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool TryGetAllowedOrigin(HttpContext context, out string origin)
    {
        origin = string.Empty;

        if (context.Request.Headers.TryGetValue("Origin", out var originValues))
        {
            var value = originValues.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                origin = value;
                return true;
            }
        }

        if (context.Request.Headers.TryGetValue("Referer", out var refererValues))
        {
            var referer = refererValues.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                origin = $"{refererUri.Scheme}://{refererUri.Authority}";
                return true;
            }
        }

        return false;
    }
}
