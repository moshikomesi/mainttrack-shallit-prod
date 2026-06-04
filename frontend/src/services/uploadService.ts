import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';

export type UploadResponse = {
  url: string;
};

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

export class UploadUserError extends Error {
  readonly translationKey: string;
  readonly aborted: boolean;

  constructor(
    translationKey: string,
    options?: { aborted?: boolean; cause?: unknown }
  ) {
    super(translationKey, options?.cause ? { cause: options.cause } : undefined);
    this.name = 'UploadUserError';
    this.translationKey = translationKey;
    this.aborted = options?.aborted ?? false;
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function isAbortError(e: unknown): boolean {
  if (e instanceof DOMException && e.name === 'AbortError') return true;
  if (e instanceof Error && e.name === 'AbortError') return true;
  return false;
}

function isUploadResponse(value: unknown): value is UploadResponse {
  if (value === null || typeof value !== 'object') return false;
  const url = (value as UploadResponse).url;
  return typeof url === 'string' && url.trim().length > 0;
}

export function validateMaintenanceImageFile(file: File): void {
  if (file.size > MAINTENANCE_IMAGE_MAX_BYTES) {
    throw new MaintenanceImageValidationError('validation.maintenanceImageTooLarge');
  }
  if (!file.type || !ALLOWED_MIME.includes(file.type)) {
    throw new MaintenanceImageValidationError('validation.maintenanceImageType');
  }
}

function shouldRetryUploadFailure(
  error: unknown,
  attemptIndex: number,
  maxAttempts: number
): boolean {
  if (attemptIndex >= maxAttempts - 1) return false;
  if (error instanceof MaintenanceImageValidationError) return false;
  if (error instanceof UploadUserError && error.aborted) return false;
  if (isAbortError(error)) return false;
  if (error instanceof UploadUserError && error.translationKey === 'messages.uploadCancelled') {
    return false;
  }
  return true;
}

export async function uploadFile(
  file: File,
  signal?: AbortSignal
): Promise<string> {
  validateMaintenanceImageFile(file);

  if (signal?.aborted) {
    throw new UploadUserError('messages.uploadCancelled', { aborted: true });
  }

  const formData = new FormData();
  formData.append('file', file);

  try {
    const data = await apiFetch<unknown>(endpoints.uploads, {
      method: 'POST',
      body: formData,
      signal,
    });

    if (!isUploadResponse(data)) {
      throw new UploadUserError('messages.uploadFailed');
    }

    return data.url.trim();
  } catch (e) {
    if (signal?.aborted || isAbortError(e)) {
      throw new UploadUserError('messages.uploadCancelled', { aborted: true, cause: e });
    }
    if (e instanceof MaintenanceImageValidationError) throw e;
    if (e instanceof UploadUserError) throw e;
    console.error('[uploadFile]', e);
    throw new UploadUserError('messages.uploadFailed', { cause: e });
  }
}

export async function uploadFileWithRetry(
  file: File,
  signal?: AbortSignal,
  extraRetries = 2
): Promise<string> {
  validateMaintenanceImageFile(file);

  const maxAttempts = 1 + extraRetries;
  let lastError: unknown;

  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    if (signal?.aborted) {
      throw new UploadUserError('messages.uploadCancelled', { aborted: true });
    }

    try {
      return await uploadFile(file, signal);
    } catch (e) {
      lastError = e;
      if (!shouldRetryUploadFailure(e, attempt, maxAttempts)) {
        throw e;
      }
      await sleep(350 * (attempt + 1));
    }
  }

  console.error('[uploadFileWithRetry] exhausted retries', lastError);
  if (lastError instanceof Error) {
    throw new UploadUserError('messages.uploadFailed', { cause: lastError });
  }
  throw new UploadUserError('messages.uploadFailed');
}
