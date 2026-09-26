using MaintTrack.Application.MachineParameterPhotos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MachineParameterPhotoEndpoints
{
    public static IEndpointRouteBuilder MapMachineParameterPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/machine-parameter-photos/hierarchy", async (
                IMachineParameterPhotoService photoService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var hierarchy = await photoService.GetHierarchyAsync(cancellationToken);
                    return Results.Ok(hierarchy);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMachineParameterPhotoHierarchy")
            .WithOpenApi();

        app.MapGet("/api/v1/machine-parameter-photos", async (
                Guid? machineId,
                IMachineParameterPhotoService photoService,
                CancellationToken cancellationToken) =>
            {
                if (machineId is null || machineId == Guid.Empty)
                {
                    return Results.BadRequest(new { error = "machineId is required." });
                }

                try
                {
                    var photos = await photoService.GetByMachineIdAsync(machineId.Value, cancellationToken);
                    return Results.Ok(photos);
                }
                catch (InvalidOperationException ex)
                {
                    // Covers inaccessible / cross-tenant / hidden / inactive / unassigned machines.
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMachineParameterPhotos")
            .WithOpenApi();

        app.MapPost("/api/v1/machine-parameter-photos", async (
                HttpRequest httpRequest,
                IMachineParameterPhotoService photoService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var (machineId, files) = await ReadUploadRequestAsync(httpRequest, cancellationToken);
                    var photos = await photoService.UploadAsync(machineId, files, cancellationToken);
                    return Results.Created("/api/v1/machine-parameter-photos", photos);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
                catch (InvalidOperationException ex)
                {
                    // Multipart/request parsing + service validation/access failures
                    // (empty/unsupported/oversized/too many files, inaccessible machines, etc.).
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithName("UploadMachineParameterPhotos")
            .WithOpenApi();

        app.MapDelete("/api/v1/machine-parameter-photos/{id:guid}", async (
                Guid id,
                IMachineParameterPhotoService photoService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await photoService.DeleteAsync(id, cancellationToken);
                    return Results.NoContent();
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("DeleteMachineParameterPhoto")
            .WithOpenApi();

        return app;
    }

    private static async Task<(Guid MachineId, IReadOnlyList<MachineParameterPhotoUploadFile> Files)> ReadUploadRequestAsync(
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            throw new InvalidOperationException("multipart/form-data is required.");
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken);
        var machineIdValue = form["machineId"].ToString();
        if (string.IsNullOrWhiteSpace(machineIdValue) || !Guid.TryParse(machineIdValue, out var machineId) ||
            machineId == Guid.Empty)
        {
            throw new InvalidOperationException("machineId is required.");
        }

        var files = form.Files
            .Where(file => string.Equals(file.Name, "files", StringComparison.Ordinal))
            .Select(file => new MachineParameterPhotoUploadFile(
                file.OpenReadStream(),
                file.FileName,
                file.ContentType,
                file.Length))
            .ToList();

        return (machineId, files);
    }
}
