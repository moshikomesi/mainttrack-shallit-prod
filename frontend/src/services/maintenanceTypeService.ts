import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';

export interface MaintenanceTypeDto {
  id: string;
  code: string;
}

export async function getMaintenanceTypes(): Promise<MaintenanceTypeDto[]> {
  return apiFetch(endpoints.maintenanceTypes);
}
