import { apiFetch } from '../api/apiClient';
import type {
  AnnualPlanDto,
  MaintenanceTaskDto,
  TechnicianDto,
  PlanType,
} from '../types/annualPlans';

export async function getAnnualPlan(
  year: number,
  type: PlanType
): Promise<AnnualPlanDto | null> {
  try {
    return await apiFetch<AnnualPlanDto>(
      `/api/v1/annual-plans?year=${year}&type=${type}`
    );
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    if (message.includes('HTTP 404')) {
      return null;
    }
    throw err;
  }
}

export async function saveAnnualPlan(body: unknown): Promise<{ id: string }> {
  return apiFetch<{ id: string }>('/api/v1/annual-plans', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function getMaintenanceTasks(): Promise<MaintenanceTaskDto[]> {
  return apiFetch<MaintenanceTaskDto[]>('/api/v1/annual-plans/tasks');
}

export async function getTechnicians(): Promise<TechnicianDto[]> {
  return apiFetch<TechnicianDto[]>('/api/v1/annual-plans/technicians');
}

