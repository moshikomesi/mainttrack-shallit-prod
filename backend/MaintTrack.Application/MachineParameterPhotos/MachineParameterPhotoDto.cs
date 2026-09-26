using System;
using System.Collections.Generic;
using System.IO;
using MaintTrack.Application.Hierarchy;

namespace MaintTrack.Application.MachineParameterPhotos;

public sealed record MachineParameterPhotoDto(
    Guid Id,
    Guid MachineId,
    string ImageUrl,
    int SortOrder,
    Guid CreatedByUserId,
    DateTime CreatedAt);

public sealed record MachineParameterPhotoHierarchyDto(
    IReadOnlyList<HierarchyArrayDto> Arrays,
    bool CanManage);

public sealed record MachineParameterPhotoUploadFile(
    Stream Stream,
    string FileName,
    string ContentType,
    long Length);
