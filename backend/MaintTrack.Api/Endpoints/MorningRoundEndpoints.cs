using MaintTrack.Application.MorningRound;
using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MorningRoundEndpoints
{
    public static IEndpointRouteBuilder MapMorningRoundEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/morning-round", async (
                DateOnly? fromDate,
                DateOnly? toDate,
                Guid? performedByUserId,
                int? pageNumber,
                int? pageSize,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMorningRoundService morningRoundService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var request = new GetMorningRoundsRequest
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    PerformedByUserId = performedByUserId,
                    PageNumber = pageNumber ?? 1,
                    PageSize = pageSize ?? 20
                };
                var list = await morningRoundService.GetAsync(request, cancellationToken);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMorningRounds")
            .WithOpenApi();

        app.MapGet("/api/morning-round/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMorningRoundService morningRoundService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var dto = await morningRoundService.GetByIdAsync(id, cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMorningRoundById")
            .WithOpenApi();

        app.MapPost("/api/morning-round", async (
                CreateMorningRoundRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMorningRoundService morningRoundService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var id = await morningRoundService.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/morning-round/{id}", new { id });
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("CreateMorningRound")
            .WithOpenApi();

        app.MapGet("/api/morning-round/template", async (
                [FromServices] IMorningRoundTemplateService templateService,
                [FromServices] ICurrentUserContext currentUserContext,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var template = await templateService.GetTemplateAsync(cancellationToken);
                return Results.Ok(template);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMorningRoundTemplate")
            .WithOpenApi();

        return app;
    }
}
