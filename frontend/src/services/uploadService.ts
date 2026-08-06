export const MAINTENANCE_IMAGE_MAX_BYTES = 5 * 1024 * 1024;
export const MAINTENANCE_LOG_IMAGE_MAX_BYTES = 10 * 1024 * 1024;
export const MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES = 2;
export const MAINTENANCE_LOG_MAX_TOTAL_IMAGES = 1 + MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES;

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

export function validateMaintenanceImageFile(
  file: File,
  maxBytes = MAINTENANCE_IMAGE_MAX_BYTES
): void {
  if (file.size > maxBytes) {
    throw new MaintenanceImageValidationError(
      maxBytes === MAINTENANCE_LOG_IMAGE_MAX_BYTES
        ? 'validation.maintenanceImageTooLarge'
        : 'validation.maintenanceTaskImageTooLarge'
    );
  }
  if (!file.type || !ALLOWED_MIME.includes(file.type)) {
    throw new MaintenanceImageValidationError('validation.maintenanceImageType');
  }
}
