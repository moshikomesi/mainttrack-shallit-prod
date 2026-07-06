using System;
using System.Collections.Generic;

namespace MaintTrack.Application.MorningRoundV2;

public sealed record MorningRoundV2MachineDto(
    Guid Id,
    string NameKey);

public sealed record MorningRoundV2ArrayDto(
    Guid? ArrayId,
    string NameKey,
    IReadOnlyList<MorningRoundV2MachineDto> Machines);

public sealed record SubmitMorningRoundV2Request(
    DateTime Timestamp,
    IReadOnlyList<SubmitMorningRoundV2ItemRequest> Items);

public sealed record SubmitMorningRoundV2ItemRequest(
    Guid MachineId,
    string Status,
    string? Notes);

public sealed record SubmitMorningRoundV2Response(
    Guid Id,
    DateOnly ReportDate,
    DateTime SubmittedAt);

public sealed record MorningRoundV2ReportSubmittedByDto(
    Guid UserId,
    string FullName);

public sealed record MorningRoundV2ReportMachineDto(
    Guid MachineId,
    string NameKey,
    string? Status,
    string? Notes);

public sealed record MorningRoundV2ReportArrayDto(
    Guid? ArrayId,
    string NameKey,
    IReadOnlyList<MorningRoundV2ReportMachineDto> Machines);

public sealed record MorningRoundV2ReportDto(
    Guid ReportId,
    DateTime Date,
    DateTime SubmittedAt,
    MorningRoundV2ReportSubmittedByDto SubmittedBy,
    IReadOnlyList<MorningRoundV2ReportArrayDto> Arrays);

public sealed record MorningRoundV2ReportSummaryDto(
    Guid ReportId,
    DateTime Date,
    DateTime SubmittedAt,
    MorningRoundV2ReportSubmittedByDto SubmittedBy);
