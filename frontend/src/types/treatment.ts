export interface CreateTreatmentRequest {
  machineId: string;
  treatmentDate: string; // ISO date (yyyy-MM-dd)
  maintenanceTypeId: string;
  description: string;
  technician: string;
  nextDueDate?: string | null;
}

export interface TreatmentDto {
  id: string;
  machineId?: string | null;
  machineName?: string | null;
  treatmentDate: string;
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
