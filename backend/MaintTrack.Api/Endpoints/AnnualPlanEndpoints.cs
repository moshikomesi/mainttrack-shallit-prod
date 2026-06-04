using MaintTrack.Application.AnnualPlans;
using MaintTrack.Domain.AnnualPlans;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Api.Endpoints;

public static class AnnualPlanEndpoints
{
    public static IEndpointRouteBuilder MapAnnualPlanEndpoints(this IEndpointRouteBuilder app)
    {
        // Create or update annual plan
        app.MapPost("/api/v1/annual-plans", async (
                CreateAnnualPlanRequest request,
                [FromServices] IAnnualPlanService service,
                CancellationToken ct) =>
            {
                var id = await service.CreateOrUpdateAsync(request, ct);
                return Results.Ok(new { id });
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("CreateOrUpdateAnnualPlan")
            .WithOpenApi();

        // Get plans by year/type or list
        app.MapGet("/api/v1/annual-plans", async (
                int? year,
                PlanType? type,
                [FromServices] IAnnualPlanService service,
                CancellationToken ct) =>
            {
                if (year.HasValue && type.HasValue)
                {
                    var plan = await service.GetByYearAsync(year.Value, type.Value, ct);
                    return Results.Ok(plan);
                }

                var list = await service.GetListAsync(year, type, ct);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetAnnualPlans")
            .WithOpenApi();

        // Get maintenance tasks for annual plans
        app.MapGet("/api/v1/annual-plans/tasks", async (
                [FromServices] MaintTrackDbContext db,
                CancellationToken ct) =>
            {
                var tasks = await db.MaintenanceTasks
                    .AsNoTracking()
                    .OrderBy(t => t.Type)
                    .ThenBy(t => t.OrderIndex)
                    .Select(t => new
                    {
                        t.Id,
                        t.Type,
                        t.TranslationKey,
                        t.OrderIndex
                    })
                    .ToListAsync(ct);

                return Results.Ok(tasks);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetAnnualPlanTasks")
            .WithOpenApi();

        // Get technicians for annual plans
        app.MapGet("/api/v1/annual-plans/technicians", async (
                [FromServices] MaintTrackDbContext db,
                CancellationToken ct) =>
            {
                var technicians = await db.Technicians
                    .AsNoTracking()
                    .OrderBy(t => t.TranslationKey)
                    .Select(t => new
                    {
                        t.Id,
                        t.TranslationKey
                    })
                    .ToListAsync(ct);

                return Results.Ok(technicians);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetAnnualPlanTechnicians")
            .WithOpenApi();

        return app;
    }
}

