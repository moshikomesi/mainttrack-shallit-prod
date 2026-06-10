import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type { CreateMaintenanceTaskRequest, MaintenanceTaskDto } from '../types/maintenanceTask';

export async function getMaintenanceTasks(): Promise<MaintenanceTaskDto[]> {
  return apiFetch(endpoints.maintenanceTasks);
}

export async function createMaintenanceTask(
  body: CreateMaintenanceTaskRequest,
  signal?: AbortSignal
): Promise<{ id: string }> {
  const formData = new FormData();
  formData.append('file', body.file);
  formData.append('isConfirmed', String(body.isConfirmed));
  if (body.description?.trim()) {
    formData.append('description', body.description.trim());
  }

  return apiFetch(endpoints.maintenanceTasks, {
    method: 'POST',
    body: formData,
    signal,
  });
}
