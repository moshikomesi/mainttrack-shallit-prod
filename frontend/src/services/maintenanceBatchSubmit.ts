import type { CreateMaintenanceEntryRequest, MaintenanceLogEntry } from '../types/maintenance';
import { createMaintenance, createMaintenanceWithFile } from './maintenanceService';

export type PerRowSubmitResult =
  | { rowId: string; status: 'success' }
  | { rowId: string; status: 'upload_failed'; translationKey: string }
  | {
      rowId: string;
      status: 'create_failed';
      translationKey: string;
      uploadedImageUrl: string | null;
    }
  | { rowId: string; status: 'aborted'; translationKey: string };

export type SubmitMaintenanceRowsResult = {
  results: PerRowSubmitResult[];
};

function isAbortLike(signal: AbortSignal | undefined, e: unknown): boolean {
  if (signal?.aborted) return true;
  if (e instanceof DOMException && e.name === 'AbortError') return true;
  if (e instanceof Error && e.name === 'AbortError') return true;
  return false;
}

function buildPayload(
  row: MaintenanceLogEntry,
  imageUrl: string | null
): CreateMaintenanceEntryRequest {
  const isOther = row.maintenanceTypeCode === 'other';
  const base: CreateMaintenanceEntryRequest = {
    machineId: row.machine,
    date: row.date,
    maintenanceTypeId: row.maintenanceTypeId,
    sparePartsUsed: null,
    workHours: Number(row.workHours ?? 0),
    isSafeToOperate: true,
    imageUrl,
  };
  if (isOther) {
    return { ...base, description: row.fault.trim() };
  }
  return { ...base };
}

function resolveImageUrlForRow(
  row: MaintenanceLogEntry,
  signal?: AbortSignal
): { ok: true; url: string | null } | { ok: false; translationKey: string } {
  if (signal?.aborted) {
    return { ok: false, translationKey: 'messages.uploadCancelled' };
  }

  if (row.uploadedImageUrl) {
    return { ok: true, url: row.uploadedImageUrl };
  }

  if (!row.photoFile) {
    return { ok: true, url: null };
  }

  return { ok: true, url: null };
}

/**
 * Upload (if needed) then create maintenance for one row. Does not throw.
 */
export async function submitOneMaintenanceRow(
  row: MaintenanceLogEntry,
  signal?: AbortSignal
): Promise<PerRowSubmitResult> {
  const resolved = resolveImageUrlForRow(row, signal);
  if (!resolved.ok) {
    if (resolved.translationKey === 'messages.uploadCancelled') {
      return { rowId: row.id, status: 'aborted', translationKey: resolved.translationKey };
    }
    return { rowId: row.id, status: 'upload_failed', translationKey: resolved.translationKey };
  }

  if (signal?.aborted) {
    return { rowId: row.id, status: 'aborted', translationKey: 'messages.uploadCancelled' };
  }

  const payload = buildPayload(row, resolved.url);

  try {
    if (row.photoFile || (row.additionalPhotoFiles?.length ?? 0) > 0) {
      await createMaintenanceWithFile(payload, row.photoFile, row.additionalPhotoFiles, signal);
    } else {
      await createMaintenance(payload);
    }
  } catch (e) {
    if (isAbortLike(signal, e)) {
      return { rowId: row.id, status: 'aborted', translationKey: 'messages.uploadCancelled' };
    }
    console.error('[submitOneMaintenanceRow] createMaintenance', row.id, e);
    return {
      rowId: row.id,
      status: 'create_failed',
      translationKey: 'messages.someEntriesFailedToSave',
      uploadedImageUrl: resolved.url,
    };
  }

  return { rowId: row.id, status: 'success' };
}

/**
 * Runs all rows in parallel. Each row's upload + create is isolated; outcomes are always returned.
 */
export async function submitMaintenanceRows(
  rows: MaintenanceLogEntry[],
  signal?: AbortSignal
): Promise<SubmitMaintenanceRowsResult> {
  const settled = await Promise.allSettled(
    rows.map((row) => submitOneMaintenanceRow(row, signal))
  );

  const results: PerRowSubmitResult[] = settled.map((outcome, i) => {
    const row = rows[i];
    if (outcome.status === 'fulfilled') {
      return outcome.value;
    }
    console.error('[submitMaintenanceRows] unexpected rejection', row?.id, outcome.reason);
    return {
      rowId: row.id,
      status: 'upload_failed',
      translationKey: 'messages.someEntriesFailedToSave',
    };
  });

  return { results };
}

/**
 * Drops successful rows; for create-failed rows with an uploaded URL, clears local file state and stores URL for retry.
 */
export function applyMaintenanceSubmitOutcomes(
  currentEntries: MaintenanceLogEntry[],
  results: PerRowSubmitResult[]
): MaintenanceLogEntry[] {
  const resultById = new Map(results.map((r) => [r.rowId, r]));

  currentEntries.forEach((entry) => {
    if (resultById.get(entry.id)?.status !== 'success') return;
    if (entry.photoPreviewUrl) URL.revokeObjectURL(entry.photoPreviewUrl);
    entry.additionalPhotoPreviewUrls?.forEach((url) => URL.revokeObjectURL(url));
  });

  return currentEntries
    .filter((entry) => resultById.get(entry.id)?.status !== 'success')
    .map((entry) => {
      const r = resultById.get(entry.id);
      if (r?.status === 'create_failed' && r.uploadedImageUrl) {
        if (entry.photoPreviewUrl) {
          URL.revokeObjectURL(entry.photoPreviewUrl);
        }
        return {
          ...entry,
          uploadedImageUrl: r.uploadedImageUrl,
          photoFile: undefined,
          photoPreviewUrl: undefined,
        };
      }
      return entry;
    });
}

/**
 * When every row failed (excluding pure-abort outcomes), pick one i18n key for the toast.
 */
export function getFullFailureToastKey(results: PerRowSubmitResult[]): string {
  const actionable = results.filter((r) => r.status !== 'aborted');
  if (actionable.length === 0) {
    return 'messages.uploadCancelled';
  }
  const keys = actionable.map((r) =>
    r.status === 'upload_failed' || r.status === 'create_failed'
      ? r.translationKey
      : 'messages.failedToSaveMaintenanceEntries'
  );
  const allSame = keys.every((k) => k === keys[0]);
  return allSame ? keys[0] : 'messages.failedToSaveMaintenanceEntries';
}
