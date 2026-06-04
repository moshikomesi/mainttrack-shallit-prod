using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.AnnualPlans;

public sealed class Technician : TenantEntity
{
    public string TranslationKey { get; set; } = string.Empty;
}

