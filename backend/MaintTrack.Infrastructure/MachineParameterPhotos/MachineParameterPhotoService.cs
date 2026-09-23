using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.Hierarchy;
using MaintTrack.Application.MachineParameterPhotos;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.MachineParameterPhotos;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MaintTrack.Infrastructure.MachineParameterPhotos;

public sealed class MachineParameterPhotoService : IMachineParameterPhotoService
{
    public const long MaximumFileSizeBytes = 10L * 1024 * 1024;
    public const long MaximumAggregateFileSizeBytes = 41_943_040;
    public const int MaximumFilesPerRequest = 10;

    private static readonly HashSet<string> AllowedImageContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IHierarchyService _hierarchyService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<MachineParameterPhotoService> _logger;

    public MachineParameterPhotoService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IHierarchyService hierarchyService,
        IFileStorageService fileStorageService,
        ILogger<MachineParameterPhotoService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        _hierarchyService = hierarchyService ?? throw new ArgumentNullException(nameof(hierarchyService));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MachineParameterPhotoHierarchyDto> GetHierarchyAsync(CancellationToken ct)
    {
        EnsureTenantResolved();

        var hierarchy = await _hierarchyService.GetHierarchyAsync(ct);
        var hiddenArrayIds = await GetHiddenArrayIdsAsync(ct);

        var arrays = hierarchy
            .Where(group => group.ArrayId is { } arrayId && !hiddenArrayIds.Contains(arrayId))
            .ToList();

        return new MachineParameterPhotoHierarchyDto(arrays, await IsManagerAsync(ct));
    }

    public async Task<IReadOnlyList<MachineParameterPhotoDto>> GetByMachineIdAsync(
        Guid machineId,
        CancellationToken ct)
    {
        EnsureTenantResolved();
        await EnsureMachineAccessibleAsync(machineId, ct);

        var photos = await _dbContext.MachineParameterPhotos
            .AsNoTracking()
            .Where(photo => photo.MachineId == machineId)
            .OrderBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .ToListAsync(ct);

        return photos.Select(MapPhoto).ToList();
    }

    public async Task<IReadOnlyList<MachineParameterPhotoDto>> UploadAsync(
        Guid machineId,
        IReadOnlyList<MachineParameterPhotoUploadFile> files,
        CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        await EnsureCanManageAsync(ct);
        ValidateFiles(files);
        await EnsureMachineAccessibleAsync(machineId, ct);

        var uploadedUrls = new List<string>(files.Count);
        try
        {
            foreach (var file in files)
            {
                if (file.Stream.CanSeek)
                {
                    file.Stream.Position = 0;
                }

                var url = await _fileStorageService.UploadAsync(
                    file.Stream,
                    file.FileName,
                    file.ContentType,
                    tenantId.ToString(),
                    ct);
                uploadedUrls.Add(url);
            }

            var persisted = await PersistUploadedPhotosAsync(machineId, uploadedUrls, ct);
            return persisted;
        }
        catch
        {
            await DeleteUploadedFilesAsync(uploadedUrls);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        EnsureTenantResolved();
        await EnsureCanManageAsync(ct);

        var photo = await _dbContext.MachineParameterPhotos
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        if (photo is null)
        {
            throw new KeyNotFoundException("Photo not found.");
        }

        try
        {
            await EnsureMachineAccessibleAsync(photo.MachineId, ct);
        }
        catch (InvalidOperationException)
        {
            throw new KeyNotFoundException("Photo not found.");
        }

        var imageUrl = photo.ImageUrl;
        _dbContext.MachineParameterPhotos.Remove(photo);
        await _dbContext.SaveChangesAsync(ct);

        try
        {
            await _fileStorageService.DeleteAsync(imageUrl, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Machine parameter photo {PhotoId} was deleted, but image {ImageUrl} could not be removed from storage.",
                id,
                imageUrl);
        }
    }

    private async Task<IReadOnlyList<MachineParameterPhotoDto>> PersistUploadedPhotosAsync(
        Guid machineId,
        IReadOnlyList<string> uploadedUrls,
        CancellationToken ct)
    {
        // SortOrder is max(existing)+1..n in one SaveChanges. There is no row lock.
        // Concurrent uploads to the same machine can collide on unique
        // (machine_id, sort_order). One retry re-reads the max and reassigns.
        DbUpdateException? lastConflict = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var nextSortOrder = await _dbContext.MachineParameterPhotos
                .Where(photo => photo.MachineId == machineId)
                .Select(photo => (int?)photo.SortOrder)
                .MaxAsync(ct) ?? 0;

            var createdAt = DateTime.UtcNow;
            var photos = new List<MachineParameterPhoto>(uploadedUrls.Count);
            for (var index = 0; index < uploadedUrls.Count; index++)
            {
                photos.Add(new MachineParameterPhoto
                {
                    Id = Guid.NewGuid(),
                    TenantId = _currentUserContext.TenantId,
                    MachineId = machineId,
                    ImageUrl = uploadedUrls[index],
                    SortOrder = nextSortOrder + index + 1,
                    CreatedByUserId = _currentUserContext.UserId,
                    CreatedAt = createdAt
                });
            }

            await _dbContext.MachineParameterPhotos.AddRangeAsync(photos, ct);

            try
            {
                await _dbContext.SaveChangesAsync(ct);
                return photos.Select(MapPhoto).ToList();
            }
            catch (DbUpdateException ex) when (attempt == 0)
            {
                lastConflict = ex;
                foreach (var photo in photos)
                {
                    _dbContext.Entry(photo).State = EntityState.Detached;
                }
            }
        }

        throw lastConflict!;
    }

    private async Task EnsureMachineAccessibleAsync(Guid machineId, CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();

        var machine = await _dbContext.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == machineId && item.TenantId == tenantId && item.IsActive,
                ct);

        if (machine is null || machine.ArrayId is not { } arrayId)
        {
            throw new InvalidOperationException("Machine not found.");
        }

        var array = await _dbContext.Arrays
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == arrayId && item.TenantId == tenantId && item.IsActive,
                ct);

        if (array is null)
        {
            throw new InvalidOperationException("Machine not found.");
        }

        var hiddenArrayIds = await GetHiddenArrayIdsAsync(ct);
        if (hiddenArrayIds.Contains(array.Id))
        {
            throw new InvalidOperationException("Machine not found.");
        }
    }

    private async Task<HashSet<Guid>> GetHiddenArrayIdsAsync(CancellationToken ct)
    {
        var hidden = await _dbContext.ArrayFeatureVisibilities
            .AsNoTracking()
            .Where(row =>
                row.FeatureKey == ArrayFeatureVisibility.MachineParameterPhotosFeatureKey &&
                !row.IsVisible)
            .Select(row => row.ArrayId)
            .ToListAsync(ct);

        return hidden.ToHashSet();
    }

    private async Task EnsureCanManageAsync(CancellationToken ct)
    {
        if (!await IsManagerAsync(ct))
        {
            throw new UnauthorizedAccessException("You are not allowed to manage machine parameter photos.");
        }
    }

    private Task<bool> IsManagerAsync(CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        var userId = _currentUserContext.UserId;

        return _dbContext.MachineParameterPhotoManagers
            .AsNoTracking()
            .AnyAsync(row => row.TenantId == tenantId && row.UserId == userId, ct);
    }

    private Guid EnsureTenantResolved()
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Tenant not resolved.");
        }

        return tenantId;
    }

    private static void ValidateFiles(IReadOnlyList<MachineParameterPhotoUploadFile> files)
    {
        if (files is null || files.Count == 0)
        {
            throw new InvalidOperationException("At least one file is required.");
        }

        if (files.Count > MaximumFilesPerRequest)
        {
            throw new InvalidOperationException("A maximum of 10 files is allowed.");
        }

        foreach (var file in files)
        {
            if (file.Length <= 0)
            {
                throw new InvalidOperationException("Uploaded files cannot be empty.");
            }

            if (file.Length > MaximumFileSizeBytes)
            {
                throw new InvalidOperationException("File is too large. Maximum size is 10 MiB.");
            }

            if (string.IsNullOrWhiteSpace(file.ContentType) ||
                !AllowedImageContentTypes.Contains(file.ContentType))
            {
                throw new InvalidOperationException("Invalid file type.");
            }
        }

        if (files.Sum(file => file.Length) > MaximumAggregateFileSizeBytes)
        {
            throw new InvalidOperationException("Combined file size exceeds the request limit.");
        }
    }

    private async Task DeleteUploadedFilesAsync(IEnumerable<string> uploadedImageUrls)
    {
        foreach (var uploadedImageUrl in uploadedImageUrls.Reverse())
        {
            try
            {
                await _fileStorageService.DeleteAsync(uploadedImageUrl, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to clean up uploaded machine parameter photo {ImageUrl} after a failed request.",
                    uploadedImageUrl);
            }
        }
    }

    private static MachineParameterPhotoDto MapPhoto(MachineParameterPhoto photo) =>
        new(
            photo.Id,
            photo.MachineId,
            photo.ImageUrl,
            photo.SortOrder,
            photo.CreatedByUserId,
            photo.CreatedAt);
}
