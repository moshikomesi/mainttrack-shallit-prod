using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Maintenance;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MaintenanceTypeEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceTypeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/maintenance-types", async (
                ICurrentUserContext currentUserContext,
                IMaintenanceTypeService maintenanceTypeService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var list = await maintenanceTypeService.GetAllAsync(cancellationToken);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceTypes")
            .WithOpenApi();

        return app;
    }
}
