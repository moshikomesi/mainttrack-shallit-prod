using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MachineParameterPhotos;
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
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<MachineParameterPhotoService> _logger;

    /// <summary>
    /// Test hook. Production leaves this null. Assignment awaits it after the
    /// unlinked photo has been read and before the conditional update.
    /// </summary>
    public static Func<CancellationToken, Task>? BeforeAssignmentPersistAsync { get; set; }

    public MachineParameterPhotoService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IFileStorageService fileStorageService,
        ILogger<MachineParameterPhotoService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ParameterPhotoCatalogDto> GetCatalogAsync(CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        var linkResult = await ParameterPhotoCatalogSeeder.EnsureAsync(_dbContext, tenantId, ct);
        if (linkResult is not null &&
            ParameterPhotoLegacyLinker.ShouldLogAmbiguousNames(tenantId, linkResult.AmbiguousMachineNames))
        {
            _logger.LogWarning(
                ParameterPhotoLegacyLinker.AmbiguousNamesMessage,
                tenantId,
                string.Join(", ", linkResult.AmbiguousMachineNames));
        }

        var excludedParameterIds = await _dbContext.ParameterPhotoParameterExclusions
            .AsNoTracking()
            .Where(exclusion => exclusion.TenantId == tenantId)
            .Select(exclusion => exclusion.ParameterId)
            .ToListAsync(ct);
        var excluded = excludedParameterIds.ToHashSet();

        var groups = await _dbContext.ParameterPhotoGroups
            .AsNoTracking()
            .Include(group => group.Parameters)
            .Where(group => group.IsActive && group.TenantId == tenantId)
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.NameKey)
            .ToListAsync(ct);

        var visibleGroups = groups
            .Select(group => new ParameterPhotoGroupDto(
                group.Id,
                group.NameKey,
                group.SortOrder,
                group.Parameters
                    .Where(parameter =>
                        parameter.IsActive &&
                        parameter.TenantId == tenantId &&
                        !excluded.Contains(parameter.Id))
                    .OrderBy(parameter => parameter.SortOrder)
                    .ThenBy(parameter => parameter.NameKey)
                    .Select(parameter => new ParameterPhotoParameterDto(
                        parameter.Id,
                        parameter.NameKey,
                        parameter.SortOrder))
                    .ToList()))
            .Where(group => group.Parameters.Count > 0)
            .ToList();

        return new ParameterPhotoCatalogDto(visibleGroups, await IsManagerAsync(ct));
    }

    public async Task<IReadOnlyList<MachineParameterPhotoDto>> GetByParameterIdAsync(
        Guid parameterId,
        CancellationToken ct)
    {
        EnsureTenantResolved();
        await EnsureParameterAccessibleAsync(parameterId, ct);

        var photos = await _dbContext.MachineParameterPhotos
            .AsNoTracking()
            .Where(photo => photo.ParameterId == parameterId)
            .OrderBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .ToListAsync(ct);

        return photos.Select(MapPhoto).ToList();
    }

    public async Task<IReadOnlyList<LegacyParameterPhotoDto>> GetLegacyPhotosAsync(CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        await EnsureCanManageAsync(ct);

        var photos = await _dbContext.MachineParameterPhotos
            .AsNoTracking()
            .Where(photo =>
                photo.TenantId == tenantId &&
                photo.ParameterId == null &&
                photo.MachineId != null)
            .OrderBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .ToListAsync(ct);

        var machineIds = photos.Select(photo => photo.MachineId!.Value).Distinct().ToList();
        var machines = await _dbContext.Machines
            .AsNoTracking()
            .Where(machine => machine.TenantId == tenantId && machineIds.Contains(machine.Id))
            .ToDictionaryAsync(machine => machine.Id, ct);

        return photos
            .Select(photo =>
            {
                machines.TryGetValue(photo.MachineId!.Value, out var machine);
                return new LegacyParameterPhotoDto(
                    photo.Id,
                    photo.MachineId!.Value,
                    machine?.Name ?? string.Empty,
                    machine?.Code ?? string.Empty,
                    photo.ImageUrl,
                    photo.SortOrder,
                    photo.CreatedByUserId,
                    photo.CreatedAt);
            })
            .ToList();
    }

    public async Task<MachineParameterPhotoDto> AssignLegacyPhotoAsync(
        Guid photoId,
        Guid parameterId,
        CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        await EnsureCanManageAsync(ct);
        await EnsureParameterAccessibleAsync(parameterId, ct);

        var photo = await _dbContext.MachineParameterPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == photoId && item.TenantId == tenantId, ct);
        if (photo is null || photo.ParameterId is not null || photo.MachineId is not Guid machineId)
        {
            throw new KeyNotFoundException("Photo not found.");
        }

        var imageUrl = photo.ImageUrl;
        var sortOrder = photo.SortOrder;
        var sortTaken = await _dbContext.MachineParameterPhotos
            .AsNoTracking()
            .AnyAsync(
                item => item.ParameterId == parameterId && item.SortOrder == photo.SortOrder && item.Id != photo.Id,
                ct);
        if (sortTaken)
        {
            var maxSortOrder = await _dbContext.MachineParameterPhotos
                .AsNoTracking()
                .Where(item => item.ParameterId == parameterId)
                .Select(item => (int?)item.SortOrder)
                .MaxAsync(ct) ?? 0;
            sortOrder = maxSortOrder + 1;
        }

        if (BeforeAssignmentPersistAsync is Func<CancellationToken, Task> beforePersist)
        {
            await beforePersist(ct);
        }

        // One UPDATE conditioned on parameter_id still being null. A concurrent
        // assignment of this same photo changes that column and matches zero rows.
        var updated = await _dbContext.MachineParameterPhotos
            .Where(item =>
                item.Id == photoId &&
                item.TenantId == tenantId &&
                item.ParameterId == null &&
                item.MachineId == machineId &&
                item.ImageUrl == imageUrl)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.ParameterId, parameterId)
                    .SetProperty(item => item.SortOrder, sortOrder),
                ct);
        if (updated != 1)
        {
            throw new KeyNotFoundException("Photo not found.");
        }

        var stored = await _dbContext.MachineParameterPhotos
            .AsNoTracking()
            .SingleAsync(item => item.Id == photoId && item.TenantId == tenantId, ct);
        if (stored.MachineId != machineId ||
            stored.ImageUrl != imageUrl ||
            stored.ParameterId != parameterId ||
            stored.SortOrder != sortOrder)
        {
            throw new InvalidOperationException("Legacy photo identity changed during assignment.");
        }

        return MapPhoto(stored);
    }

    public async Task DeleteLegacyPhotoAsync(Guid photoId, CancellationToken ct)
    {
        EnsureTenantResolved();
        await EnsureCanManageAsync(ct);

        var photo = await FindUnlinkedLegacyPhotoAsync(photoId, ct);
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
                "Legacy parameter photo {PhotoId} was deleted, but image {ImageUrl} could not be removed from storage.",
                photoId,
                imageUrl);
        }
    }

    public async Task<IReadOnlyList<MachineParameterPhotoDto>> UploadAsync(
        Guid parameterId,
        IReadOnlyList<MachineParameterPhotoUploadFile> files,
        CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        await EnsureCanManageAsync(ct);
        ValidateFiles(files);
        await EnsureParameterAccessibleAsync(parameterId, ct);

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

            var persisted = await PersistUploadedPhotosAsync(parameterId, uploadedUrls, ct);
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
        if (photo is null || photo.ParameterId is not Guid parameterId)
        {
            throw new KeyNotFoundException("Photo not found.");
        }

        try
        {
            await EnsureParameterAccessibleAsync(parameterId, ct);
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
        Guid parameterId,
        IReadOnlyList<string> uploadedUrls,
        CancellationToken ct)
    {
        // SortOrder is max(existing)+1..n in one SaveChanges. There is no row lock.
        // Concurrent uploads to the same parameter can collide on unique
        // (parameter_id, sort_order). One retry re-reads the max and reassigns.
        DbUpdateException? lastConflict = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var nextSortOrder = await _dbContext.MachineParameterPhotos
                .Where(photo => photo.ParameterId == parameterId)
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
                    ParameterId = parameterId,
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

    private async Task EnsureParameterAccessibleAsync(Guid parameterId, CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();

        var parameter = await _dbContext.ParameterPhotoParameters
            .AsNoTracking()
            .Include(item => item.Group)
            .FirstOrDefaultAsync(
                item => item.Id == parameterId && item.TenantId == tenantId && item.IsActive,
                ct);

        if (parameter?.Group is null ||
            parameter.Group.TenantId != tenantId ||
            !parameter.Group.IsActive)
        {
            throw new InvalidOperationException("Parameter not found.");
        }

        var excluded = await _dbContext.ParameterPhotoParameterExclusions
            .AsNoTracking()
            .AnyAsync(exclusion => exclusion.TenantId == tenantId && exclusion.ParameterId == parameterId, ct);
        if (excluded)
        {
            throw new InvalidOperationException("Parameter not found.");
        }
    }

    private async Task<MachineParameterPhoto> FindUnlinkedLegacyPhotoAsync(Guid photoId, CancellationToken ct)
    {
        var tenantId = EnsureTenantResolved();
        var photo = await _dbContext.MachineParameterPhotos
            .FirstOrDefaultAsync(item => item.Id == photoId && item.TenantId == tenantId, ct);

        if (photo is null || photo.ParameterId is not null || photo.MachineId is null)
        {
            throw new KeyNotFoundException("Photo not found.");
        }

        return photo;
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
            photo.ParameterId ?? throw new InvalidOperationException("Photo is not linked to a parameter."),
            photo.ImageUrl,
            photo.SortOrder,
            photo.CreatedByUserId,
            photo.CreatedAt);
}
