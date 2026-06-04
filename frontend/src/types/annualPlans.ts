export type PlanType = 'Preventive' | 'Summer';

export interface SummerExecutionDto {
  plannedStart?: string | null;
  requiredDays?: number | null;
  plannedFinish?: string | null;
  description?: string | null;
  actualStart?: string | null;
  actualFinish?: string | null;
  workers: string[];
}

export interface AnnualPlanItemDto {
  taskKey: string;
  dates: string[];
  execution?: SummerExecutionDto | null;
}

export interface AnnualPlanDto {
  id: string;
  year: number;
  type: PlanType;
  items: AnnualPlanItemDto[];
}

export interface MaintenanceTaskDto {
  id: string;
  type: PlanType;
  translationKey: string;
  orderIndex: number;
}

export interface TechnicianDto {
  id: string;
  translationKey: string;
}

export type UiPlanType = 'preventive' | 'summer';

export type MonthData = {
  lubricationIntake: string[];
  lubricationConveyors: string[];
  coolingService: string[];
  tempSensorInspection: string[];
};

export type SummerMaintenanceRow = {
  id: string;
  machine: string;
  plannedStart: string;
  requiredDays: string;
  plannedFinish: string;
  plannedWork: string;
  actualStart: string;
  actualFinish: string;
  performedBy: string[];
};

export type AnnualPlanExecution = {
  plannedStart?: string | null;
  requiredDays?: number | null;
  plannedFinish?: string | null;
  description?: string | null;
  actualStart?: string | null;
  actualFinish?: string | null;
  workerIds: string[];
};

export type AnnualPlanItem = {
  taskId: string;
  dates: string[];
  execution?: AnnualPlanExecution | null;
};

export type SaveAnnualPlanRequest = {
  year: number;
  /**
   * API expects numeric type: 0 = Preventive, 1 = Summer
   */
  type: number;
  items: AnnualPlanItem[];
};

