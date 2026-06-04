using MaintTrack.Application.Machines;
using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;

namespace MaintTrack.Api.Endpoints;

public static class MachinesEndpoints
{
    public static IEndpointRouteBuilder MapMachines(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/machines", async (
                IMachineService machineService,
                CancellationToken cancellationToken) =>
            {
                var machines = await machineService.GetAllAsync(cancellationToken);
                return Results.Ok(machines);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMachines")
            .WithOpenApi();

        app.MapPost("/api/v1/machines", async (
                CreateMachineRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                IMachineService machineService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 2)
                    return Results.Forbid();

                var id = await machineService.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/v1/machines/{id}", new { id });
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("CreateMachine")
            .WithOpenApi();

        return app;
    }
}

