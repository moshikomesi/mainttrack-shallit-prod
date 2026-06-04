using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.MorningRound;

/// <summary>
/// Tenant-specific Morning Round checklist template item.
/// Labels are translated on the frontend using TranslationKey.
/// </summary>
public class MorningRoundTemplateItem : TenantEntity
{
    public string TranslationKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

