using MaintTrack.Application.Treatments;
using MaintTrack.Application.Abstractions;
using MaintTrack.Domain.Treatments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class TreatmentEndpoints
{
    public static IEndpointRouteBuilder MapTreatmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/treatments", async (
                EquipmentType? equipmentType,
                DateOnly? fromDate,
                DateOnly? toDate,
                int? pageNumber,
                int? pageSize,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] ITreatmentService treatmentService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 3)
                    return Results.Forbid();

                var p = pageNumber is > 0 ? pageNumber.Value : 1;
                var s = pageSize is > 0 ? pageSize.Value : 20;
                var list = await treatmentService.GetAsync(equipmentType, fromDate, toDate, p, s, cancellationToken);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetTreatments")
            .WithOpenApi();

        app.MapGet("/api/v1/treatments/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] ITreatmentService treatmentService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 3)
                    return Results.Forbid();

                var dto = await treatmentService.GetByIdAsync(id, cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetTreatmentById")
            .WithOpenApi();

        app.MapPost("/api/v1/treatments", async (
                CreateTreatmentRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] ITreatmentService treatmentService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 3)
                    return Results.Forbid();

                if (request.Cost < 0)
                    return Results.BadRequest(new { error = "Cost must be greater than or equal to zero." });

                var id = await treatmentService.CreateAsync(request, cancellationToken);

                return Results.Created($"/api/v1/treatments/{id}", new { id });
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("CreateTreatment")
            .WithOpenApi();

        return app;
    }
}