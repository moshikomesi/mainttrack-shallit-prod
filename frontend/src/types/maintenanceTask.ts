export interface MaintenanceTaskDto {
  id: string;
  taskDate: string;
  imageUrl: string;
  description?: string | null;
  isConfirmed: boolean;
  createdByUserId: string;
  tenantId: string;
  createdAt: string;
}

export interface CreateMaintenanceTaskRequest {
  file: File;
  description?: string | null;
  isConfirmed: boolean;
}
