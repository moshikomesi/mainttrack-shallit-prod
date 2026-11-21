export type ParameterPhotoParameter = {
  id: string;
  nameKey: string;
  sortOrder: number;
};

export type ParameterPhotoGroup = {
  id: string;
  nameKey: string;
  sortOrder: number;
  parameters: ParameterPhotoParameter[];
};

export type MachineParameterPhotosHierarchy = {
  groups: ParameterPhotoGroup[];
  canManage: boolean;
  unresolvedPhotoCount: number;
};

export type MachineParameterPhoto = {
  id: string;
  parameterId: string;
  imageUrl: string;
  sortOrder: number;
  createdByUserId: string;
  createdAt: string;
};

export type LegacyParameterPhoto = {
  id: string;
  machineId: string;
  machineName: string;
  machineCode: string;
  imageUrl: string;
  sortOrder: number;
  createdByUserId: string;
  createdAt: string;
};
