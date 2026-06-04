export type TreatmentType = number;

export interface CreateTreatmentRequest {
  equipmentType: number;
  treatmentDate: string; // ISO date (yyyy-MM-dd)
  treatmentType: TreatmentType;
  description: string;
  technician: string;
  cost: number;
  nextDueDate?: string | null;
}

export interface TreatmentDto {
  id: string;
  equipmentType: number;
  treatmentDate: string;
  treatmentType: number;
  description: string;
  technician: string;
  cost: number;
  nextDueDate?: string | null;
}

export type TreatmentsReportScreenProps = {
  onBack: () => void;
  onSubmit: () => void;
  userRoleId: number;
};

export type UiEquipmentKey = 'airCompressor' | 'coolingSystem';

export const equipmentMap: Record<UiEquipmentKey, number> = {
  airCompressor: 0,
  coolingSystem: 1,
};

export type UiTreatmentTypeKey = 'preventive' | 'repair';

export const treatmentTypeMap: Record<UiTreatmentTypeKey, number> = {
  preventive: 0,
  repair: 1,
};

