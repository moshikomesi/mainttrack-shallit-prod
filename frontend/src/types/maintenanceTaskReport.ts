export interface MaintenanceTaskReportDto {
  id: string;
  taskDate: string;
  imageUrl: string;
  description?: string | null;
  isConfirmed: boolean;
  createdByUserId: string;
  createdByUserName: string;
  tenantId: string;
  createdAt: string;
}
