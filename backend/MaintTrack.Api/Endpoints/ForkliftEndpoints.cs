using MaintTrack.Application.Forklifts;
using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class ForkliftEndpoints
{
    public static IEndpointRouteBuilder MapForkliftEndpoints(this IEndpointRouteBuilder app)
    {
        // Forklift (asset) CRUD
        app.MapPost("/api/v1/forklifts", async (
                CreateForkliftRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftService forkliftService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 2)
                    return Results.Forbid();

                try
                {
                    var id = await forkliftService.CreateAsync(request, cancellationToken);
                    return Results.Created($"/api/v1/forklifts/{id}", new { id });
                }
                catch (InvalidOperationException ex) when (
                    ex.Message is "License number is required."
                    or "Forklift license number already exists.")
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("CreateForklift")
            .WithOpenApi();

        app.MapGet("/api/v1/forklifts", async (
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftService forkliftService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 2)
                    return Results.Forbid();

                var list = await forkliftService.GetAsync(cancellationToken);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetForklifts")
            .WithOpenApi();

        app.MapGet("/api/v1/forklifts/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftService forkliftService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 2)
                    return Results.Forbid();

                var dto = await forkliftService.GetByIdAsync(id, cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetForkliftById")
            .WithOpenApi();

        app.MapPut("/api/v1/forklifts/{id:guid}", async (
                Guid id,
                UpdateForkliftRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftService forkliftService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 2)
                    return Results.Forbid();

                try
                {
                    await forkliftService.UpdateAsync(id, request, cancellationToken);
                    return Results.NoContent();
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound();
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("UpdateForklift")
            .WithOpenApi();

        // Forklift reports
        app.MapPost("/api/v1/forklift", async (
                CreateForkliftReportRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftReportService forkliftReportService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    if (currentUserContext.RoleId < 2)
                        return Results.Forbid();

                    var id = await forkliftReportService.CreateAsync(request, cancellationToken);
                    return Results.Created($"/api/v1/forklift/{id}", new { id });
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("RepairCost") || ex.Message.Contains("ExpiryDate") || ex.Message.Contains("Inspection") || ex.Message.Contains("Forklift not found"))
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("CreateForkliftReport")
            .WithOpenApi();

        app.MapGet("/api/v1/forklift", async (
                int? pageNumber,
                int? pageSize,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftReportService forkliftReportService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 2)
                    return Results.Forbid();

                var p = pageNumber is > 0 ? pageNumber.Value : 1;
                var s = pageSize is > 0 ? pageSize.Value : 20;
                var list = await forkliftReportService.GetAsync(p, s, cancellationToken);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetForkliftReports")
            .WithOpenApi();

        app.MapGet("/api/v1/forklift/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftReportService forkliftReportService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 2)
                    return Results.Forbid();

                var dto = await forkliftReportService.GetByIdAsync(id, cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetForkliftReportById")
            .WithOpenApi();

        return app;
    }
}
