using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/uploads")
            .RequireRateLimiting("api");

        group.MapPost("/", async (
                IFormFile? file,
                [FromServices] IFileStorageService fileStorageService,
                [FromServices] ICurrentUserContext currentUserContext,
                CancellationToken cancellationToken) =>
            {
                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "File is required." });
                }

                if (file.Length > 5L * 1024 * 1024)
                {
                    return Results.BadRequest(new { error = "File is too large. Maximum size is 5MB." });
                }

                if (string.IsNullOrWhiteSpace(file.ContentType) ||
                    file.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                {
                    return Results.BadRequest(new { error = "Invalid file type." });
                }

                await using var stream = file.OpenReadStream();

                var url = await fileStorageService.UploadAsync(
                    stream,
                    file.FileName,
                    file.ContentType,
                    currentUserContext.TenantId.ToString(),
                    cancellationToken);

                return Results.Ok(new { url });
            })
            .DisableAntiforgery()
            .RequireAuthorization()
            .WithName("UploadFile")
            .WithOpenApi();

        return app;
    }
}
