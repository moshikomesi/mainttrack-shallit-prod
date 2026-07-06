import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';

export type MachineComponentOption = {
  id: string;
  code: string;
  nameKey: string;
  sortOrder: number;
};

export async function getMachineComponents(machineId: string): Promise<MachineComponentOption[]> {
  return apiFetch<MachineComponentOption[]>(endpoints.maintenanceLogV2MachineComponents(machineId));
}
