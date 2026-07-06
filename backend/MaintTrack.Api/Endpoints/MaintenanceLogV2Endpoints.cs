using System;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MachineComponents;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MaintenanceLogV2Endpoints
{
    public static IEndpointRouteBuilder MapMaintenanceLogV2Endpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v2/maintenance-log/machines/{machineId:guid}/components", async (
                Guid machineId,
                ICurrentUserContext currentUserContext,
                IMachineComponentService machineComponentService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                try
                {
                    var components = await machineComponentService.GetByMachineIdAsync(
                        machineId,
                        cancellationToken);
                    return Results.Ok(components);
                }
                catch (InvalidOperationException ex) when (ex.Message == "Machine not found.")
                {
                    return Results.NotFound();
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceLogV2MachineComponents")
            .WithOpenApi();

        return app;
    }
}
