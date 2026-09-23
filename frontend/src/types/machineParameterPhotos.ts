import type { HierarchyArray } from '../services/hierarchyService';

export type { HierarchyArray, HierarchyMachine } from '../services/hierarchyService';

export type MachineParameterPhotosHierarchy = {
  arrays: HierarchyArray[];
  canManage: boolean;
};

export type MachineParameterPhoto = {
  id: string;
  machineId: string;
  imageUrl: string;
  sortOrder: number;
  createdByUserId: string;
  createdAt: string;
};
