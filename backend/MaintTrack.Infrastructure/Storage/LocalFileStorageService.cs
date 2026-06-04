using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;

namespace MaintTrack.Infrastructure.Storage;

public sealed class LocalFileStorageService : IFileStorageService
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public async Task<string> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string tenantId,
        CancellationToken ct)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidOperationException("Invalid file type.");
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("Tenant identifier is required.");
        }

        var safeTenantId = tenantId.Replace("..", string.Empty).Replace("/", string.Empty).Replace("\\", string.Empty);

        var rootPath = "/mnt/app";

        var uploadsRoot = Path.Combine(rootPath, "uploads", safeTenantId);

        if (!Directory.Exists(uploadsRoot))
        {
            Directory.CreateDirectory(uploadsRoot);
        }

        var extension = GetExtensionFromContentType(contentType);

        var generatedName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(uploadsRoot, generatedName);

        await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            await stream.CopyToAsync(fileStream, ct);
        }

        var relativeUrl = $"/uploads/{safeTenantId}/{generatedName}";
        return relativeUrl;
    }

    public Task DeleteAsync(string fileUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return Task.CompletedTask;
        }

        var rootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

        const string prefix = "/uploads/";
        if (!fileUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var relativePath = fileUrl.TrimStart('/');

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var uploadsRoot = Path.GetFullPath(Path.Combine(rootPath, "uploads"));

        if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private static string GetExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("Unsupported file type.")
        };
    }
}
