using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MaintenanceTasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MaintenanceTasksController
{
    public static IEndpointRouteBuilder MapMaintenanceTasksController(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/maintenance-tasks", async (
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceTasksService maintenanceTasksService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var tasks = await maintenanceTasksService.GetAsync(cancellationToken);
                return Results.Ok(tasks);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceTasks")
            .WithOpenApi();

        app.MapPost("/api/maintenance-tasks", async (
                IFormFile? file,
                [FromForm] string? description,
                [FromForm] bool isConfirmed,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IFileStorageService fileStorageService,
                [FromServices] IMaintenanceTasksService maintenanceTasksService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { error = "File is required." });

                if (file.Length > 5L * 1024 * 1024)
                    return Results.BadRequest(new { error = "File is too large. Maximum size is 5MB." });

                if (string.IsNullOrWhiteSpace(file.ContentType) ||
                    file.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                    return Results.BadRequest(new { error = "Invalid file type." });

                if (!isConfirmed)
                    return Results.BadRequest(new { error = "Confirmation is required." });

                await using var stream = file.OpenReadStream();
                var imageUrl = await fileStorageService.UploadAsync(
                    stream,
                    file.FileName,
                    file.ContentType,
                    currentUserContext.TenantId.ToString(),
                    cancellationToken);

                var request = new CreateMaintenanceTaskRequest
                {
                    ImageUrl = imageUrl,
                    Description = description,
                    IsConfirmed = isConfirmed
                };

                Guid id;
                try
                {
                    id = await maintenanceTasksService.CreateAsync(request, cancellationToken);
                }
                catch
                {
                    await fileStorageService.DeleteAsync(imageUrl, CancellationToken.None);
                    throw;
                }

                return Results.Created($"/api/maintenance-tasks/{id}", new { id });
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithName("CreateMaintenanceTask")
            .WithOpenApi();

        return app;
    }
}
