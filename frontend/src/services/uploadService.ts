export const MAINTENANCE_IMAGE_MAX_BYTES = 5 * 1024 * 1024;

export const MAINTENANCE_IMAGE_TYPES = [
  'image/jpeg',
  'image/png',
  'image/webp',
] as const;

const ALLOWED_MIME = MAINTENANCE_IMAGE_TYPES as readonly string[];

export class MaintenanceImageValidationError extends Error {
  readonly translationKey: string;

  constructor(translationKey: string) {
    super(translationKey);
    this.name = 'MaintenanceImageValidationError';
    this.translationKey = translationKey;
  }
}

export function validateMaintenanceImageFile(file: File): void {
  if (file.size > MAINTENANCE_IMAGE_MAX_BYTES) {
    throw new MaintenanceImageValidationError('validation.maintenanceImageTooLarge');
  }
  if (!file.type || !ALLOWED_MIME.includes(file.type)) {
    throw new MaintenanceImageValidationError('validation.maintenanceImageType');
  }
}
