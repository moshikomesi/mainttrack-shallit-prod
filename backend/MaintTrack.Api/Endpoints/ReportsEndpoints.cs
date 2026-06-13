using MaintTrack.Application.Reports.ForkliftReports;
using MaintTrack.Application.Reports.MaintenanceTasks;
using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace MaintTrack.Api.Endpoints;

public static class ReportsEndpoints
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/reports/forklifts", async (
                [AsParameters] ForkliftReportsQuery query,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IForkliftReportsQueryService service,
                CancellationToken ct) =>
            {
                if (currentUserContext.RoleId < 3)
                    return Results.Forbid();

                var result = await service.GetAsync(query, ct);
                return Results.Ok(result);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetForkliftReportsOverview")
            .WithOpenApi();

        app.MapGet("/api/reports/maintenance-tasks", async (
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceTasksReportService service,
                CancellationToken ct) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var result = await service.GetAsync(ct);
                return Results.Ok(result);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceTasksReport")
            .WithOpenApi();

        return app;
    }
}
