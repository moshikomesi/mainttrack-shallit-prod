export interface CreateTreatmentRequest {
  machineId: string;
  treatmentDate: string; // ISO date (yyyy-MM-dd)
  machineComponentId: string;
  description: string;
  technician: string;
  nextDueDate?: string | null;
}

export interface TreatmentDto {
  id: string;
  machineId?: string | null;
  machineName?: string | null;
  /** Resolved from the machine's array assignment at read time; not stored on the treatment. */
  arrayId?: string | null;
  treatmentDate: string;
  machineComponentId?: string | null;
  machineComponentNameKey?: string | null;
  /** Legacy field for treatments created before machine-component selection. */
  maintenanceTypeId?: string | null;
  maintenanceTypeName?: string | null;
  description: string;
  technician: string;
  nextDueDate?: string | null;
  createdByUserId?: string | null;
}

export type TreatmentsReportScreenProps = {
  onBack: () => void;
  onSubmit: () => void;
  userRoleId: number;
};
