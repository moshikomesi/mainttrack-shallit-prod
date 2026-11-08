using MaintTrack.Application.Clearance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class ClearanceEndpoints
{
    public static IEndpointRouteBuilder MapClearanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/clearance", async (
                string? status,
                Guid? arrayId,
                Guid? machineId,
                int? pageNumber,
                int? pageSize,
                IClearanceService clearanceService,
                CancellationToken cancellationToken) =>
            {
                var query = new ClearanceQueueQuery
                {
                    Status = status,
                    ArrayId = arrayId,
                    MachineId = machineId,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                try
                {
                    var response = await clearanceService.GetQueueAsync(query, cancellationToken);
                    return Results.Ok(response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetClearanceQueue")
            .WithOpenApi();

        app.MapGet("/api/v1/clearance/access", async (
                IClearanceService clearanceService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await clearanceService.GetAccessAsync(cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetClearanceAccess")
            .WithOpenApi();

        app.MapGet("/api/v1/clearance/source/{sourceType}/{sourceId:guid}", async (
                string sourceType,
                Guid sourceId,
                IClearanceService clearanceService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var response = await clearanceService.GetBySourceAsync(sourceType, sourceId, cancellationToken);
                    return response is null
                        ? Results.NotFound(new { error = "This report has no Clearance item." })
                        : Results.Ok(response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetClearanceItemBySource")
            .WithOpenApi();

        app.MapPost("/api/v1/clearance/{id:guid}/approve", (
                Guid id,
                IClearanceService clearanceService,
                CancellationToken cancellationToken) =>
                DecideAsync(() => clearanceService.ApproveAsync(id, cancellationToken)))
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("ApproveClearanceItem")
            .WithOpenApi();

        app.MapPost("/api/v1/clearance/{id:guid}/reject", (
                Guid id,
                [FromBody] ClearanceRejectRequest? request,
                IClearanceService clearanceService,
                CancellationToken cancellationToken) =>
                DecideAsync(() => clearanceService.RejectAsync(id, request?.Note, cancellationToken)))
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("RejectClearanceItem")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> DecideAsync(Func<Task<ClearanceDecisionDto>> decide)
    {
        try
        {
            return Results.Ok(await decide());
        }
        catch (ClearanceDecisionConflictException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
