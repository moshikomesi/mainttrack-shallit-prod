using System;

namespace MaintTrack.Application.MorningRound;

public sealed record MorningRoundTemplateItemDto(
    Guid Id,
    string TranslationKey,
    int Order
);

