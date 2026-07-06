import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';

export type HierarchyMachine = {
  id: string;
  name: string;
};

export type HierarchyArray = {
  arrayId: string | null;
  nameKey: string;
  machines: HierarchyMachine[];
};

export async function getHierarchy(): Promise<HierarchyArray[]> {
  return apiFetch<HierarchyArray[]>(endpoints.hierarchy);
}
