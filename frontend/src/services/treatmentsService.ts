import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type { CreateTreatmentRequest, TreatmentDto } from '../types/treatment';

export interface GetTreatmentsParams {
  fromDate?: string;
  toDate?: string;
  search?: string;
  pageNumber?: number;
  pageSize?: number;
}

function buildSearchParams(params?: GetTreatmentsParams): string {
  if (!params) return '';
  const search = new URLSearchParams();
  if (params.fromDate != null) search.set('fromDate', params.fromDate);
  if (params.toDate != null) search.set('toDate', params.toDate);
  if (params.search != null) search.set('search', params.search);
  if (params.pageNumber != null) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize != null) search.set('pageSize', String(params.pageSize));
  const q = search.toString();
  return q ? `?${q}` : '';
}

export async function getTreatments(params?: GetTreatmentsParams): Promise<TreatmentDto[]> {
  return apiFetch(`${endpoints.treatments}${buildSearchParams(params)}`);
}

export async function getTreatmentById(id: string): Promise<TreatmentDto> {
  return apiFetch(`${endpoints.treatments}/${id}`);
}

export async function createTreatment(body: CreateTreatmentRequest): Promise<{ id: string }> {
  return apiFetch(endpoints.treatments, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}
