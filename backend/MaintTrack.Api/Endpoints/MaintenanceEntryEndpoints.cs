using MaintTrack.Application.Maintenance;
using MaintTrack.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MaintTrack.Api.Endpoints;

public static class MaintenanceEntryEndpoints
{
    private const long MaximumFileSize = 10L * 1024 * 1024;
    private const long MaximumAggregateFileSize = 30L * 1024 * 1024;
    private const int MaximumAdditionalFiles = 2;
    private const int MaximumTotalFiles = 3;

    private static readonly HashSet<string> AllowedImageContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

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

                var (request, uploadedImageUrls) = await ReadCreateMaintenanceRequestAsync(
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
                    await DeleteUploadedFilesAsync(fileStorageService, uploadedImageUrls);
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

    private static async Task<(CreateMaintenanceEntryRequest? Request, IReadOnlyList<string> UploadedImageUrls)> ReadCreateMaintenanceRequestAsync(
        HttpRequest httpRequest,
        ICurrentUserContext currentUserContext,
        IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            var jsonRequest = await httpRequest.ReadFromJsonAsync<CreateMaintenanceEntryRequest>(cancellationToken);
            return (jsonRequest, Array.Empty<string>());
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken);
        var imageUrl = ReadOptional(form, "imageUrl");
        var primaryFiles = form.Files
            .Where(candidate => string.Equals(candidate.Name, "file", StringComparison.Ordinal))
            .ToArray();
        var additionalFiles = form.Files
            .Where(candidate => string.Equals(candidate.Name, "additionalFiles", StringComparison.Ordinal))
            .ToArray();

        if (primaryFiles.Length > 1)
            throw new InvalidOperationException("Only one primary file is allowed.");

        if (additionalFiles.Length > MaximumAdditionalFiles)
            throw new InvalidOperationException("A maximum of two additional files is allowed.");

        var allFiles = primaryFiles.Concat(additionalFiles).ToArray();
        if (allFiles.Length > MaximumTotalFiles)
            throw new InvalidOperationException("A maximum of three files is allowed.");

        foreach (var candidate in allFiles)
        {
            if (candidate.Length <= 0)
                throw new InvalidOperationException("Uploaded files cannot be empty.");

            if (candidate.Length > MaximumFileSize)
                throw new InvalidOperationException("File is too large. Maximum size is 10 MiB.");

            if (string.IsNullOrWhiteSpace(candidate.ContentType) ||
                !AllowedImageContentTypes.Contains(candidate.ContentType))
                throw new InvalidOperationException("Invalid file type.");
        }

        if (allFiles.Sum(candidate => candidate.Length) > MaximumAggregateFileSize)
            throw new InvalidOperationException("Combined file size exceeds 30 MiB.");

        if (additionalFiles.Length > 0 &&
            primaryFiles.Length == 0 &&
            string.IsNullOrWhiteSpace(imageUrl))
            throw new InvalidOperationException("A primary image is required when additional images are provided.");

        var parsedRequest = new CreateMaintenanceEntryRequest
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

        var uploadedImageUrls = new List<string>(allFiles.Length);
        var additionalImageUrls = new List<string>(additionalFiles.Length);

        try
        {
            if (primaryFiles.Length == 1)
            {
                imageUrl = await UploadAsync(
                    primaryFiles[0],
                    currentUserContext,
                    fileStorageService,
                    cancellationToken);
                uploadedImageUrls.Add(imageUrl);
            }

            foreach (var additionalFile in additionalFiles)
            {
                var additionalImageUrl = await UploadAsync(
                    additionalFile,
                    currentUserContext,
                    fileStorageService,
                    cancellationToken);
                uploadedImageUrls.Add(additionalImageUrl);
                additionalImageUrls.Add(additionalImageUrl);
            }
        }
        catch
        {
            await DeleteUploadedFilesAsync(fileStorageService, uploadedImageUrls);
            throw;
        }

        var request = new CreateMaintenanceEntryRequest
        {
            MachineId = parsedRequest.MachineId,
            Date = parsedRequest.Date,
            MaintenanceTypeId = parsedRequest.MaintenanceTypeId,
            Description = parsedRequest.Description,
            ImageUrl = imageUrl,
            AdditionalImageUrls = additionalImageUrls,
            SparePartsUsed = parsedRequest.SparePartsUsed,
            WorkHours = parsedRequest.WorkHours,
            IsSafeToOperate = parsedRequest.IsSafeToOperate
        };

        return (request, uploadedImageUrls);
    }

    private static async Task<string> UploadAsync(
        IFormFile file,
        ICurrentUserContext currentUserContext,
        IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return await fileStorageService.UploadAsync(
            stream,
            file.FileName,
            file.ContentType,
            currentUserContext.TenantId.ToString(),
            cancellationToken);
    }

    private static async Task DeleteUploadedFilesAsync(
        IFileStorageService fileStorageService,
        IEnumerable<string> uploadedImageUrls)
    {
        foreach (var uploadedImageUrl in uploadedImageUrls.Reverse())
        {
            try
            {
                await fileStorageService.DeleteAsync(uploadedImageUrl, CancellationToken.None);
            }
            catch
            {
                // Preserve the original upload/database exception while attempting every cleanup.
            }
        }
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
