import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type {
  MachineParameterPhoto,
  MachineParameterPhotosHierarchy,
} from '../types/machineParameterPhotos';

export async function getMachineParameterPhotosHierarchy(): Promise<MachineParameterPhotosHierarchy> {
  return apiFetch<MachineParameterPhotosHierarchy>(endpoints.machineParameterPhotosHierarchy);
}

export async function getMachineParameterPhotos(
  machineId: string
): Promise<MachineParameterPhoto[]> {
  const params = new URLSearchParams({ machineId });
  return apiFetch<MachineParameterPhoto[]>(`${endpoints.machineParameterPhotos}?${params.toString()}`);
}

export async function uploadMachineParameterPhotos(
  machineId: string,
  files: File[]
): Promise<MachineParameterPhoto[]> {
  const formData = new FormData();
  formData.append('machineId', machineId);
  files.forEach((file) => {
    formData.append('files', file);
  });

  return apiFetch<MachineParameterPhoto[]>(endpoints.machineParameterPhotos, {
    method: 'POST',
    body: formData,
  });
}

export async function deleteMachineParameterPhoto(photoId: string): Promise<void> {
  return apiFetch<void>(endpoints.machineParameterPhoto(photoId), {
    method: 'DELETE',
  });
}
