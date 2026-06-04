import { apiFetch } from '../api/apiClient';

export interface MachineDto {
  id: string;
  name: string;
}

export async function getMachines(): Promise<MachineDto[]> {
  return apiFetch<MachineDto[]>('/v1/machines');
}

