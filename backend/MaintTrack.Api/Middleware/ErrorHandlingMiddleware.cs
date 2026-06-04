using System.Net;
using System.Text.Json;

namespace MaintTrack.Api.Middleware;

/// <summary>
/// Global error handling middleware that converts unhandled exceptions
/// into safe JSON responses.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var traceId = context.TraceIdentifier;

            _logger.LogError(ex, "Unhandled exception for request {TraceId}", traceId);

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("The response has already started, the error handling middleware will not be executed.");
                throw;
            }

            var (statusCode, errorMessage) = ex switch
            {
                UnauthorizedAccessException => (HttpStatusCode.Forbidden, ex.Message),
                InvalidOperationException => (HttpStatusCode.BadRequest, ex.Message),
                KeyNotFoundException => (HttpStatusCode.NotFound, ex.Message),
                _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
            };

            context.Response.Clear();
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            var payload = new
            {
                error = errorMessage,
                traceId
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
        }
    }
}

