using MaintTrack.Application.Maintenance;
using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MaintenanceEntryEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEntryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/maintenance", async (
                Guid? machineId,
                DateOnly? fromDate,
                DateOnly? toDate,
                string? search,
                int? pageNumber,
                int? pageSize,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var request = new GetMaintenanceEntriesRequest
                {
                    MachineId = machineId,
                    FromDate = fromDate,
                    ToDate = toDate,
                    Search = search,
                    PageNumber = pageNumber is > 0 ? pageNumber.Value : 1,
                    PageSize = pageSize is > 0 ? pageSize.Value : 20
                };

                var list = await maintenanceEntryService.GetAsync(request, cancellationToken);
                return Results.Ok(list);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceEntries")
            .WithOpenApi();

        app.MapGet("/api/maintenance/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var dto = await maintenanceEntryService.GetByIdAsync(id, cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("GetMaintenanceEntryById")
            .WithOpenApi();

        app.MapPost("/api/maintenance", async (
                HttpRequest httpRequest,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IFileStorageService fileStorageService,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                var (request, uploadedImageUrl) = await ReadCreateMaintenanceRequestAsync(
                    httpRequest,
                    currentUserContext,
                    fileStorageService,
                    cancellationToken);

                if (request is null)
                    return Results.BadRequest(new { error = "Invalid maintenance request." });

                Guid id;
                try
                {
                    id = await maintenanceEntryService.CreateAsync(request, cancellationToken);
                }
                catch
                {
                    if (!string.IsNullOrWhiteSpace(uploadedImageUrl))
                    {
                        await fileStorageService.DeleteAsync(uploadedImageUrl, CancellationToken.None);
                    }

                    throw;
                }

                return Results.Created($"/api/maintenance/{id}", new { id });
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithName("CreateMaintenanceEntry")
            .WithOpenApi();

        app.MapPut("/api/maintenance/{id:guid}", async (
                Guid id,
                UpdateMaintenanceEntryRequest request,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                try
                {
                    await maintenanceEntryService.UpdateAsync(id, request, cancellationToken);
                    return Results.NoContent();
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("UpdateMaintenanceEntry")
            .WithOpenApi();

        app.MapDelete("/api/maintenance/{id:guid}", async (
                Guid id,
                [FromServices] ICurrentUserContext currentUserContext,
                [FromServices] IMaintenanceEntryService maintenanceEntryService,
                CancellationToken cancellationToken) =>
            {
                if (currentUserContext.RoleId < 1)
                    return Results.Forbid();

                try
                {
                    await maintenanceEntryService.DeleteAsync(id, cancellationToken);
                    return Results.NoContent();
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .RequireRateLimiting("api")
            .RequireAuthorization()
            .WithName("DeleteMaintenanceEntry")
            .WithOpenApi();

        return app;
    }

    private static async Task<(CreateMaintenanceEntryRequest? Request, string? UploadedImageUrl)> ReadCreateMaintenanceRequestAsync(
        HttpRequest httpRequest,
        ICurrentUserContext currentUserContext,
        IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            var jsonRequest = await httpRequest.ReadFromJsonAsync<CreateMaintenanceEntryRequest>(cancellationToken);
            return (jsonRequest, null);
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken);
        var imageUrl = ReadOptional(form, "imageUrl");
        string? uploadedImageUrl = null;
        var file = form.Files.GetFile("file");

        if (file is { Length: > 0 })
        {
            if (file.Length > 5L * 1024 * 1024)
                throw new InvalidOperationException("File is too large. Maximum size is 5MB.");

            if (string.IsNullOrWhiteSpace(file.ContentType) ||
                file.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                throw new InvalidOperationException("Invalid file type.");

            await using var stream = file.OpenReadStream();
            uploadedImageUrl = await fileStorageService.UploadAsync(
                stream,
                file.FileName,
                file.ContentType,
                currentUserContext.TenantId.ToString(),
                cancellationToken);
            imageUrl = uploadedImageUrl;
        }

        var request = new CreateMaintenanceEntryRequest
        {
            MachineId = ReadGuid(form, "machineId") ?? Guid.Empty,
            Date = DateOnly.Parse(ReadRequired(form, "date")),
            MaintenanceTypeId = ReadGuid(form, "maintenanceTypeId") ?? Guid.Empty,
            Description = ReadOptional(form, "description"),
            ImageUrl = imageUrl,
            SparePartsUsed = ReadOptional(form, "sparePartsUsed"),
            WorkHours = decimal.Parse(ReadRequired(form, "workHours")),
            IsSafeToOperate = bool.Parse(ReadRequired(form, "isSafeToOperate"))
        };

        return (request, uploadedImageUrl);
    }

    private static string ReadRequired(IFormCollection form, string key)
    {
        var value = ReadOptional(form, key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} is required.");

        return value;
    }

    private static string? ReadOptional(IFormCollection form, string key)
    {
        var value = form[key].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Guid? ReadGuid(IFormCollection form, string key)
    {
        var value = ReadOptional(form, key);
        return string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
    }
}

