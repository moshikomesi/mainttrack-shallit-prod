using System;
using MaintTrack.Domain.Common;
using MaintTrack.Domain.MachineComponents;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.Users;

namespace MaintTrack.Domain.Treatments;

public class Treatment : TenantEntity
{
    public Guid? MachineId { get; set; }

    public Machine? Machine { get; set; }

    public EquipmentType EquipmentType { get; set; }

    public DateOnly TreatmentDate { get; set; }

    public Guid? MachineComponentId { get; set; }

    public MachineComponent? MachineComponent { get; set; }

    /// <summary>Legacy field for treatments created before machine-component selection.</summary>
    public Guid? MaintenanceTypeId { get; set; }

    public MaintenanceType? MaintenanceType { get; set; }

    public TreatmentType TreatmentType { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Technician { get; set; } = string.Empty;

    public decimal Cost { get; set; }

    public DateOnly? NextDueDate { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }
}
