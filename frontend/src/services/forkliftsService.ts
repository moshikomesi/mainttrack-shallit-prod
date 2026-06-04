import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type { Forklift } from '../types/forklift';

export async function getForklifts(): Promise<Forklift[]> {
  return apiFetch<Forklift[]>(endpoints.forklifts);
}

export async function createForklift(body: Record<string, unknown>): Promise<unknown> {
  return apiFetch(endpoints.forklifts, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}
