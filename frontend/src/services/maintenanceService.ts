import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type {
  MaintenanceEntryDto,
  CreateMaintenanceEntryRequest,
  UpdateMaintenanceEntryRequest,
} from '../types/maintenance';

export interface GetMaintenanceParams {
  machineId?: string;
  fromDate?: string;
  toDate?: string;
  search?: string;
  pageNumber?: number;
  pageSize?: number;
}

function buildSearchParams(params?: GetMaintenanceParams): string {
  if (!params) return '';
  const search = new URLSearchParams();
  if (params.machineId != null) search.set('machineId', params.machineId);
  if (params.fromDate != null) search.set('fromDate', params.fromDate);
  if (params.toDate != null) search.set('toDate', params.toDate);
  if (params.search != null) search.set('search', params.search);
  if (params.pageNumber != null) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize != null) search.set('pageSize', String(params.pageSize));
  const q = search.toString();
  return q ? `?${q}` : '';
}

export async function getMaintenance(
  params?: GetMaintenanceParams
): Promise<MaintenanceEntryDto[]> {
  return apiFetch(`${endpoints.maintenance}${buildSearchParams(params)}`);
}

export async function getMaintenanceById(id: string): Promise<MaintenanceEntryDto> {
  return apiFetch(`${endpoints.maintenance}/${id}`);
}

export async function createMaintenance(
  body: CreateMaintenanceEntryRequest
): Promise<MaintenanceEntryDto> {
  return apiFetch(endpoints.maintenance, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function updateMaintenance(
  id: string,
  body: UpdateMaintenanceEntryRequest
): Promise<MaintenanceEntryDto> {
  return apiFetch(`${endpoints.maintenance}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function deleteMaintenance(id: string): Promise<void> {
  return apiFetch(`${endpoints.maintenance}/${id}`, {
    method: 'DELETE',
  });
}
