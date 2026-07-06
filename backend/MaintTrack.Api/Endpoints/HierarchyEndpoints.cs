using MaintTrack.Application.Hierarchy;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class HierarchyEndpoints
{
    public static IEndpointRouteBuilder MapHierarchyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/hierarchy", async (
                IHierarchyService hierarchyService,
                CancellationToken cancellationToken) =>
            {
                var hierarchy = await hierarchyService.GetHierarchyAsync(cancellationToken);
                return Results.Ok(hierarchy);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetHierarchy")
            .WithOpenApi();

        return app;
    }
}
