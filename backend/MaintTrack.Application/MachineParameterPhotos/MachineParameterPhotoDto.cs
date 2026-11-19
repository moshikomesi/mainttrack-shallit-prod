using System;
using System.Collections.Generic;
using System.IO;

namespace MaintTrack.Application.MachineParameterPhotos;

public sealed record MachineParameterPhotoDto(
    Guid Id,
    Guid ParameterId,
    string ImageUrl,
    int SortOrder,
    Guid CreatedByUserId,
    DateTime CreatedAt);

public sealed record ParameterPhotoParameterDto(
    Guid Id,
    string NameKey,
    int SortOrder);

public sealed record ParameterPhotoGroupDto(
    Guid Id,
    string NameKey,
    int SortOrder,
    IReadOnlyList<ParameterPhotoParameterDto> Parameters);

public sealed record ParameterPhotoCatalogDto(
    IReadOnlyList<ParameterPhotoGroupDto> Groups,
    bool CanManage);

public sealed record LegacyParameterPhotoDto(
    Guid Id,
    Guid MachineId,
    string MachineName,
    string MachineCode,
    string ImageUrl,
    int SortOrder,
    Guid CreatedByUserId,
    DateTime CreatedAt);

public sealed record MachineParameterPhotoUploadFile(
    Stream Stream,
    string FileName,
    string ContentType,
    long Length);
