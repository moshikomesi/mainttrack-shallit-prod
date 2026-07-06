using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MorningRoundV2;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MorningRoundV2Endpoints
{
    public static IEndpointRouteBuilder MapMorningRoundV2Endpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v2/morning-round/report/{id:guid}", async (
                Guid id,
                IMorningRoundV2Service morningRoundV2Service,
                ICurrentUserContext currentUserContext,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                {
                    return Results.Forbid();
                }

                var report = await morningRoundV2Service.GetReportByIdAsync(id, cancellationToken);
                return report is null ? Results.NotFound() : Results.Ok(report);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMorningRoundV2ReportById")
            .WithOpenApi();

        app.MapGet("/api/v2/morning-round/report", async (
                string? date,
                IMorningRoundV2Service morningRoundV2Service,
                ICurrentUserContext currentUserContext,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                {
                    return Results.Forbid();
                }

                if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, out var reportDate))
                {
                    return Results.BadRequest(new { error = "Query parameter 'date' is required (YYYY-MM-DD)." });
                }

                var report = await morningRoundV2Service.GetReportByDateAsync(reportDate, cancellationToken);
                return report is null ? Results.NotFound() : Results.Ok(report);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMorningRoundV2ReportByDate")
            .WithOpenApi();

        app.MapGet("/api/v2/morning-round/reports", async (
                IMorningRoundV2Service morningRoundV2Service,
                ICurrentUserContext currentUserContext,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                {
                    return Results.Forbid();
                }

                var reports = await morningRoundV2Service.ListReportsAsync(cancellationToken);
                return Results.Ok(reports);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("ListMorningRoundV2Reports")
            .WithOpenApi();

        app.MapGet("/api/v2/morning-round", async (
                IMorningRoundV2Service morningRoundV2Service,
                ICurrentUserContext currentUserContext,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                {
                    return Results.Forbid();
                }

                var checklist = await morningRoundV2Service.GetChecklistAsync(cancellationToken);
                return Results.Ok(checklist);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMorningRoundV2Checklist")
            .WithOpenApi();

        app.MapPost("/api/v2/morning-round/submit", async (
                SubmitMorningRoundV2Request request,
                IMorningRoundV2Service morningRoundV2Service,
                ICurrentUserContext currentUserContext,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                {
                    return Results.Forbid();
                }

                try
                {
                    var result = await morningRoundV2Service.SubmitAsync(request, cancellationToken);
                    return Results.Ok(result);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("SubmitMorningRoundV2")
            .WithOpenApi();

        return app;
    }
}
