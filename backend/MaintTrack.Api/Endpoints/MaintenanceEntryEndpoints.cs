using MaintTrack.Application.Maintenance;
using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MaintenanceEntryEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEntryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/maintenance", async (
                Guid? machineId,
                DateOnly? fromDate,
                DateOnly? toDate,
                string? search,
                int? pageNumber,
                int? pageSize,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var request = new GetMaintenanceEntriesRequest
                {
                    MachineId = machineId,
                    FromDate = fromDate,
                    ToDate = toDate,
                    Search = search,
                    PageNumber = pageNumber is > 0 ? pageNumber.Value : 1,
                    PageSize = pageSize is > 0 ? pageSize.Value : 20
                };

                var list = await maintenanceEntryService.GetAsync(request, cancellationToken);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceEntries")
            .WithOpenApi();

        app.MapGet("/api/maintenance/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var dto = await maintenanceEntryService.GetByIdAsync(id, cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceEntryById")
            .WithOpenApi();

        app.MapPost("/api/maintenance", async (
                CreateMaintenanceEntryRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var id = await maintenanceEntryService.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/maintenance/{id}", new { id });
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("CreateMaintenanceEntry")
            .WithOpenApi();

        app.MapPut("/api/maintenance/{id:guid}", async (
                Guid id,
                UpdateMaintenanceEntryRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                try
                {
                    await maintenanceEntryService.UpdateAsync(id, request, cancellationToken);
                    return Results.NoContent();
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("UpdateMaintenanceEntry")
            .WithOpenApi();

        app.MapDelete("/api/maintenance/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                try
                {
                    await maintenanceEntryService.DeleteAsync(id, cancellationToken);
                    return Results.NoContent();
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("DeleteMaintenanceEntry")
            .WithOpenApi();

        return app;
    }
}

