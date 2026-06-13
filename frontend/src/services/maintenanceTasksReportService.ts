import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type { MaintenanceTaskReportDto } from '../types/maintenanceTaskReport';

export async function getMaintenanceTasksReport(): Promise<MaintenanceTaskReportDto[]> {
  return apiFetch(endpoints.maintenanceTasksReport);
}
