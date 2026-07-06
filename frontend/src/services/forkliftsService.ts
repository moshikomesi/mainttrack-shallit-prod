import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type { Forklift } from '../types/forklift';

export type CreateForkliftRequest = {
  licenseNumber: string;
  description?: string;
};

export async function getForklifts(): Promise<Forklift[]> {
  return apiFetch<Forklift[]>(endpoints.forklifts);
}

export async function createForklift(body: CreateForkliftRequest): Promise<{ id: string }> {
  return apiFetch(endpoints.forklifts, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      licenseNumber: body.licenseNumber,
      description: body.description,
    }),
  });
}
