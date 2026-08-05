export interface MaintenanceImageDto {
  id: string;
  imageUrl: string;
  sortOrder: number;
}

export interface MaintenanceEntryDto {
  id: string;
  machineId: string;
  /** Resolved from the machine's array assignment at read time; not stored on the entry. */
  arrayId?: string | null;
  date: string;
  maintenanceTypeId?: string | null;
  maintenanceTypeCode?: string | null;
  description: string;
  imageUrl?: string | null;
  additionalImages: MaintenanceImageDto[];
  sparePartsUsed?: string | null;
  employeeName: string;
  workHours: number;
  isSafeToOperate: boolean;
  createdByUserId: string;
  createdAt: string;
}

export interface CreateMaintenanceEntryRequest {
  machineId: string;
  date: string;
  maintenanceTypeId: string;
  description?: string;
  imageUrl?: string | null;
  sparePartsUsed?: string | null;
  workHours: number;
  isSafeToOperate: boolean;
}

export interface UpdateMaintenanceEntryRequest {
  maintenanceTypeId: string;
  description?: string | null;
  imageUrl?: string | null;
  sparePartsUsed?: string | null;
  employeeName: string;
  workHours: number;
  isSafeToOperate: boolean;
}

export type MaintenanceLogEntry = {
  id: string;
  date: string;
  machine: string;
  maintenanceTypeId: string;
  /** Mirrors selected type's `code` from API (e.g. `other`); used for validation and payload. */
  maintenanceTypeCode: string;
  fault: string;
  spareParts: string;
  workHours: string;
  /** Local file chosen by the user; uploaded on submit. */
  photoFile?: File;
  /** Object URL for preview only; never sent to the API. */
  photoPreviewUrl?: string;
  /** Optional local images selected after the primary image (maximum two). */
  additionalPhotoFiles?: File[];
  /** Object URLs matching additionalPhotoFiles by index. */
  additionalPhotoPreviewUrls?: string[];
  /**
   * Server URL after a successful upload. Used to skip re-upload when create failed
   * or the user retries; never a blob/data URL.
   */
  uploadedImageUrl?: string | null;
};

export type MaintenanceLogScreenProps = {
  onBack: () => void;
  onSubmit: () => void;
};

