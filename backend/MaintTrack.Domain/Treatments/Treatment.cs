using System;
using MaintTrack.Domain.Common;

namespace MaintTrack.Domain.Treatments;

public class Treatment : TenantEntity
{
    public EquipmentType EquipmentType { get; set; }

    public DateOnly TreatmentDate { get; set; }

    public TreatmentType TreatmentType { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Technician { get; set; } = string.Empty;

    public decimal Cost { get; set; }

    public DateOnly? NextDueDate { get; set; }
}
